using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace AnimeScrollApp
{
    public partial class MainWindow : Window
    {
        private static readonly HttpClient client = new HttpClient();
        private Random random = new Random();
        private bool isLoading = false;
        private int animeCount = 0;
        private double windowHeight;
        private int scrollCounter = 0;
        private float scrollStartPos = 0;
        private bool isDragging = false;
        private bool canLoadMore = true;
        private bool isLoadingAreaVisible = false;
        
        // Couleur d'accent pour l'identité visuelle
        private static readonly Color AccentColor = Color.FromRgb(0xE9, 0x45, 0x60);

        public MainWindow()
        {
            InitializeComponent();
            windowHeight = this.Height;

            // Centrer la fenêtre sur l'écran
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            
            // Rendre la fenêtre déplaçable
            this.MouseLeftButtonDown += Window_MouseLeftButtonDown;

            // Désactiver le scrolling au clavier
            MainScrollViewer.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Up || e.Key == Key.Down || 
                    e.Key == Key.PageUp || e.Key == Key.PageDown ||
                    e.Key == Key.Home || e.Key == Key.End)
                {
                    e.Handled = true;
                }
            };

            // Charger les premiers animes en arrière-plan (sans indicateur)
            _ = LoadAnimesAsync(8, false);
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Permettre de déplacer la fenêtre seulement si on ne clique pas sur le contenu interactif
            if (e.OriginalSource is Grid || e.OriginalSource is Border)
            {
                if (!isDragging)
                {
                    this.DragMove();
                }
            }
        }

        private async Task LoadAnimesAsync(int count, bool showLoading = false)
        {
            if (isLoading) return;

            isLoading = true;
            canLoadMore = false;

            // Afficher l'indicateur de chargement dans la zone de chargement
            if (showLoading && LoadingArea != null)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    LoadingArea.Visibility = Visibility.Visible;
                    isLoadingAreaVisible = true;
                });
            }

            // Charger en arrière-plan sans bloquer l'UI
            var tasks = new Task[count];
            for (int i = 0; i < count; i++)
            {
                tasks[i] = LoadSingleAnimeAsync();
            }

            await Task.WhenAll(tasks);

            // Attendre un peu pour que l'utilisateur voie le message de chargement
            if (showLoading)
            {
                await Task.Delay(500);
            }

            // Cacher l'indicateur de chargement et revenir à la position normale
            await Dispatcher.InvokeAsync(() =>
            {
                if (LoadingArea != null)
                {
                    LoadingArea.Visibility = Visibility.Collapsed;
                    isLoadingAreaVisible = false;
                }
                
                // Revenir au dernier anime chargé
                if (showLoading && scrollCounter >= animeCount - 1)
                {
                    scrollCounter = Math.Max(0, animeCount - 1);
                    ScrollToOffsetSmooth(scrollCounter * windowHeight);
                }
            });

            isLoading = false;
            canLoadMore = true;
        }

        private async Task LoadSingleAnimeAsync()
        {
            try
            {
                int randomPage = random.Next(1, 80);
                int rand = random.Next(0, 100);
                string mediaFilter;

                if (rand < 45)
                {
                    // Populaires terminés (safe)
                    mediaFilter = @"
        media(
            type: ANIME,
            status: FINISHED,
            averageScore_greater: 0,
            episodes_greater: 0,
            sort: POPULARITY_DESC
        )";
                }
                else if (rand < 65)
                {
                    // Bien notés (moins mainstream)
                    mediaFilter = @"
        media(
            type: ANIME,
            status: FINISHED,
            averageScore_greater: 0,
            episodes_greater: 0,
            sort: SCORE_DESC
        )";
                }
                else if (rand < 80)
                {
                    // ⭐ Peu connus mais qualitatifs
                    mediaFilter = @"
        media(
            type: ANIME,
            status: FINISHED,
            averageScore_greater: 70,
            episodes_greater: 0,
            popularity_lesser: 20000,
            sort: SCORE_DESC
        )";
                }
                else if (rand < 95)
                {
                    // En cours de diffusion
                    mediaFilter = @"
        media(
            type: ANIME,
            status: RELEASING,
            averageScore_greater: 0,
            sort: TRENDING_DESC
        )";
                }
                else
                {
                    // À venir (assumé sans score/épisodes)
                    mediaFilter = @"
        media(
            type: ANIME,
            status: NOT_YET_RELEASED,
            sort: POPULARITY_DESC
        )";
                }

                var query = $@"
                query ($page: Int) {{
                    Page(page: $page, perPage: 1) {{
                        {mediaFilter} {{
                            id
                            title {{
                                romaji
                                english
                            }}
                            coverImage {{
                                extraLarge
                                large
                            }}
                            bannerImage
                            averageScore
                            genres
                            episodes
                            description
                            season
                            seasonYear
                            status
                        }}
                    }}
                }}";

                var request = new
                {
                    query = query,
                    variables = new { page = randomPage }
                };

                var json = Newtonsoft.Json.JsonConvert.SerializeObject(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync("https://graphql.anilist.co", content);

                if (!response.IsSuccessStatusCode) return;

                var jsonResponse = await response.Content.ReadAsStringAsync();
                JObject data = JObject.Parse(jsonResponse);

                var page = data["data"]?["Page"];
                if (page == null) return;

                var mediaArray = page["media"];
                if (mediaArray == null || !mediaArray.HasValues) return;

                var media = mediaArray[0];
                if (media == null) return;

                // Dispatcher pour ajouter à l'UI thread
                await Dispatcher.InvokeAsync(() =>
                {
                    CreateFullScreenAnimeCard(media);
                    animeCount++;
                    AnimeCounter.Text = animeCount + " animes";
                });
            }
            catch (Exception)
            {
                // Ignorer les erreurs
            }
        }

        private void CreateFullScreenAnimeCard(JToken media)
        {
            try
            {
                var titleObj = media["title"];
                string titleRomaji = titleObj?["romaji"]?.ToString();
                string titleEnglish = titleObj?["english"]?.ToString();
                string displayTitle = !string.IsNullOrEmpty(titleEnglish) ? titleEnglish : titleRomaji;

                var coverObj = media["coverImage"];
                string imageUrl = coverObj?["extraLarge"]?.ToString() ?? coverObj?["large"]?.ToString();
                string score = string.IsNullOrWhiteSpace(media["averageScore"]?.ToString()) ? "N/A" : media["averageScore"].ToString();
                string episodes = string.IsNullOrWhiteSpace(media["episodes"]?.ToString()) ? "N/A" : media["episodes"].ToString();
                string description = media["description"]?.ToString() ?? "";
                string season = media["season"]?.ToString() ?? "";
                string year = media["seasonYear"]?.ToString() ?? "";
                string status = media["status"]?.ToString() ?? "";

                description = System.Text.RegularExpressions.Regex.Replace(description, "<.*?>", string.Empty);
                string fullDescription = description;
                string shortDescription = description.Length > 200 ? description.Substring(0, 200) + "..." : description;

                string genres = "";
                var genresArray = media["genres"];
                if (genresArray != null && genresArray.HasValues)
                {
                    for (int i = 0; i < Math.Min(3, genresArray.Count()); i++)
                    {
                        genres += genresArray[i].ToString() + " • ";
                    }
                    genres = genres.TrimEnd(' ', '•');
                }

                if (string.IsNullOrEmpty(imageUrl) || string.IsNullOrEmpty(displayTitle))
                    return;

                Grid fullScreenCard = new Grid
                {
                    Height = windowHeight,
                    Background = Brushes.Black
                };

                // --- IMAGE DE FOND FLOUTÉE ---
                Image bgImage = new Image
                {
                    Stretch = Stretch.UniformToFill,
                    Opacity = 0.4
                };

                BitmapImage bgBitmap = new BitmapImage();
                bgBitmap.BeginInit();
                bgBitmap.UriSource = new Uri(imageUrl);
                bgBitmap.CacheOption = BitmapCacheOption.OnLoad;
                bgBitmap.EndInit();
                bgImage.Source = bgBitmap;

                BlurEffect blurEffect = new BlurEffect { Radius = 20 };
                bgImage.Effect = blurEffect;

                fullScreenCard.Children.Add(bgImage);

                // --- OVERLAY DÉGRADÉ ---
                Border gradientOverlay = new Border
                {
                    Background = new LinearGradientBrush
                    {
                        StartPoint = new Point(0.5, 0),
                        EndPoint = new Point(0.5, 1),
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop(Color.FromArgb(100, 0, 0, 0), 0),
                            new GradientStop(Color.FromArgb(200, 0, 0, 0), 1)
                        }
                    }
                };
                fullScreenCard.Children.Add(gradientOverlay);

                Grid contentGrid = new Grid();
                contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                BitmapImage mainBitmap = new BitmapImage();
                mainBitmap.BeginInit();
                mainBitmap.UriSource = new Uri(imageUrl);
                mainBitmap.CacheOption = BitmapCacheOption.OnLoad;
                mainBitmap.EndInit();

                Border imageCard = new Border
                {
                    Name = "ImageCard",
                    Width = 280,
                    Height = 400,
                    CornerRadius = new CornerRadius(8),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Effect = new DropShadowEffect
                    {
                        Color = Colors.Black,
                        Direction = 270,
                        ShadowDepth = 10,
                        BlurRadius = 20,
                        Opacity = 0.8
                    },
                    Background = new ImageBrush
                    {
                        ImageSource = mainBitmap,
                        Stretch = Stretch.UniformToFill
                    },
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform = new ScaleTransform(1, 1)
                };

                // Animation au hover sur l'image
                imageCard.MouseEnter += (s, e) =>
                {
                    ScaleTransform scale = imageCard.RenderTransform as ScaleTransform;
                    DoubleAnimation scaleUpX = new DoubleAnimation(1.05, TimeSpan.FromMilliseconds(200));
                    DoubleAnimation scaleUpY = new DoubleAnimation(1.05, TimeSpan.FromMilliseconds(200));
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleUpX);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleUpY);
                };

                imageCard.MouseLeave += (s, e) =>
                {
                    ScaleTransform scale = imageCard.RenderTransform as ScaleTransform;
                    DoubleAnimation scaleDownX = new DoubleAnimation(1, TimeSpan.FromMilliseconds(200));
                    DoubleAnimation scaleDownY = new DoubleAnimation(1, TimeSpan.FromMilliseconds(200));
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleDownX);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleDownY);
                };

                Grid.SetRow(imageCard, 0);
                contentGrid.Children.Add(imageCard);


                StackPanel infoPanel = new StackPanel
                {
                    Name = "InfoPanel",
                    Margin = new Thickness(20, 0, 20, 30),
                    VerticalAlignment = VerticalAlignment.Bottom
                };

                TextBlock titleBlock = new TextBlock
                {
                    Name = "TitleBlock",
                    Text = displayTitle,
                    FontSize = 28,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 10),
                    Effect = new DropShadowEffect
                    {
                        Color = Colors.Black,
                        Direction = 270,
                        ShadowDepth = 2,
                        BlurRadius = 8
                    }
                };
                infoPanel.Children.Add(titleBlock);

                if (!string.IsNullOrEmpty(season) && !string.IsNullOrEmpty(year))
                {
                    StackPanel seasonPanel = new StackPanel
                    {
                        Name = "SeasonPanel",
                        Orientation = Orientation.Horizontal,
                        Margin = new Thickness(0, 0, 0, 5)
                    };

                    if (!string.IsNullOrEmpty(status))
                    {
                        Ellipse statusDot = CreateStatusIndicator(status);
                        if (statusDot != null)
                        {
                            seasonPanel.Children.Add(statusDot);
                        }
                    }

                    TextBlock seasonBlock = new TextBlock
                    {
                        Text = season + " " + year,
                        FontSize = 14,
                        Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    seasonPanel.Children.Add(seasonBlock);

                    infoPanel.Children.Add(seasonPanel);
                }

                if (!string.IsNullOrEmpty(genres))
                {
                    TextBlock genresBlock = new TextBlock
                    {
                        Name = "GenresBlock",
                        Text = genres,
                        FontSize = 14,
                        Foreground = new SolidColorBrush(AccentColor),
                        Margin = new Thickness(0, 0, 0, 10),
                        FontWeight = FontWeights.SemiBold
                    };
                    infoPanel.Children.Add(genresBlock);
                }

                StackPanel statsPanel = new StackPanel
                {
                    Name = "StatsPanel",
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 0, 0, 10)
                };

                Border scoreBadge = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(255, 215, 0)),
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(10, 5, 10, 5),
                    Margin = new Thickness(0, 0, 10, 0)
                };

                TextBlock scoreText = new TextBlock
                {
                    Text = "★ " + score,
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.Black
                };
                scoreBadge.Child = scoreText;
                statsPanel.Children.Add(scoreBadge);

                Border episodesBadge = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(15, 52, 96)),
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(10, 5, 10, 5)
                };

                TextBlock episodesText = new TextBlock
                {
                    Text = "📺 " + episodes + " EP",
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White
                };
                episodesBadge.Child = episodesText;
                statsPanel.Children.Add(episodesBadge);

                infoPanel.Children.Add(statsPanel);

                if (!string.IsNullOrEmpty(description))
                {
                    // Calculer la hauteur max pour la description (basée sur la hauteur de fenêtre)
                    double maxDescriptionHeight = 150;
                    bool needsScroll = description.Length > 300;
                    
                    Border descriptionBorder = new Border
                    {
                        Name = "DescriptionBorder"
                    };
                    
                    if (needsScroll)
                    {
                        ScrollViewer descScrollViewer = new ScrollViewer
                        {
                            MaxHeight = maxDescriptionHeight,
                            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                            Padding = new Thickness(0, 0, 5, 0)
                        };

                        TextBlock descBlock = new TextBlock
                        {
                            Name = "DescriptionBlock",
                            Text = shortDescription,
                            FontSize = 14,
                            Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                            TextWrapping = TextWrapping.Wrap,
                            LineHeight = 20,
                            Tag = new { Short = shortDescription, Full = fullDescription, IsExpanded = false, ScrollViewer = descScrollViewer }
                        };

                        descScrollViewer.Content = descBlock;
                        descriptionBorder.Child = descScrollViewer;
                    }
                    else
                    {
                        TextBlock descBlock = new TextBlock
                        {
                            Name = "DescriptionBlock",
                            Text = shortDescription,
                            FontSize = 14,
                            Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                            TextWrapping = TextWrapping.Wrap,
                            LineHeight = 20,
                            Tag = new { Short = shortDescription, Full = fullDescription, IsExpanded = false, ScrollViewer = (ScrollViewer)null }
                        };
                        descriptionBorder.Child = descBlock;
                    }

                    infoPanel.Children.Add(descriptionBorder);

                    // Ajouter le bouton "voir plus" si la description est tronquée
                    if (description.Length > 200)
                    {
                        TextBlock expandButton = new TextBlock
                        {
                            Text = "Voir plus...",
                            FontSize = 14,
                            Foreground = new SolidColorBrush(AccentColor),
                            Margin = new Thickness(0, 5, 0, 0),
                            Cursor = Cursors.Hand,
                            FontWeight = FontWeights.SemiBold,
                            HorizontalAlignment = HorizontalAlignment.Left
                        };

                        expandButton.MouseLeftButtonDown += (s, e) =>
                        {
                            e.Handled = true;
                            
                            // Trouver le TextBlock de description
                            TextBlock descBlock = FindDescriptionBlock(descriptionBorder);
                            if (descBlock == null) return;

                            var tag = (dynamic)descBlock.Tag;
                            bool isExpanded = tag.IsExpanded;

                            if (!isExpanded)
                            {
                                // Expand
                                AnimateDescriptionExpansion(contentGrid, imageCard, infoPanel, titleBlock, descBlock, expandButton, fullDescription, needsScroll, maxDescriptionHeight);
                                descBlock.Tag = new { Short = shortDescription, Full = fullDescription, IsExpanded = true, ScrollViewer = tag.ScrollViewer };
                                expandButton.Text = "Voir moins";
                            }
                            else
                            {
                                // Collapse
                                AnimateDescriptionCollapse(contentGrid, imageCard, infoPanel, titleBlock, descBlock, expandButton, shortDescription);
                                descBlock.Tag = new { Short = shortDescription, Full = fullDescription, IsExpanded = false, ScrollViewer = tag.ScrollViewer };
                                expandButton.Text = "Voir plus...";
                            }
                        };

                        infoPanel.Children.Add(expandButton);
                    }
                }

                Grid.SetRow(infoPanel, 1);
                contentGrid.Children.Add(infoPanel);

                fullScreenCard.Children.Add(contentGrid);
                AnimeContainer.Children.Add(fullScreenCard);
            }
            catch (Exception)
            {
                // Ignorer
            }
        }

        private TextBlock FindDescriptionBlock(Border descriptionBorder)
        {
            if (descriptionBorder.Child is TextBlock tb)
                return tb;
            if (descriptionBorder.Child is ScrollViewer sv && sv.Content is TextBlock tb2)
                return tb2;
            return null;
        }

        private void AnimateDescriptionExpansion(Grid contentGrid, Border imageCard, StackPanel infoPanel, 
            TextBlock titleBlock, TextBlock descBlock, TextBlock expandButton, string fullDescription, 
            bool needsScroll, double maxScrollHeight)
        {
            // Calculer les nouvelles tailles en fonction de la longueur de la description
            double descriptionLength = fullDescription.Length;
            double imageTargetHeight = Math.Max(200, 400 - (descriptionLength / 8)); // Min 200, max 400
            double imageTargetWidth = imageTargetHeight * 0.7; // Ratio d'aspect
            double titleTargetSize = Math.Max(18, 28 - (descriptionLength / 100)); // Min 18, max 28

            // Réduire l'image proportionnellement
            DoubleAnimation imageShrink = new DoubleAnimation(imageTargetWidth, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            imageCard.BeginAnimation(Border.WidthProperty, imageShrink);

            DoubleAnimation imageHeightShrink = new DoubleAnimation(imageTargetHeight, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            imageCard.BeginAnimation(Border.HeightProperty, imageHeightShrink);

            // Déplacer l'image vers le haut
            imageCard.VerticalAlignment = VerticalAlignment.Top;
            imageCard.Margin = new Thickness(0, 20, 0, 0);

            // Réduire la taille du titre
            DoubleAnimation titleShrink = new DoubleAnimation(titleTargetSize, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            titleBlock.BeginAnimation(TextBlock.FontSizeProperty, titleShrink);

            // Afficher la description complète
            descBlock.Text = fullDescription;
            
            // Si on a un ScrollViewer, s'assurer qu'il est configuré correctement
            var tag = (dynamic)descBlock.Tag;
            if (tag.ScrollViewer != null)
            {
                ScrollViewer sv = tag.ScrollViewer;
                sv.MaxHeight = maxScrollHeight;
                sv.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            }
        }

        private void AnimateDescriptionCollapse(Grid contentGrid, Border imageCard, StackPanel infoPanel, 
            TextBlock titleBlock, TextBlock descBlock, TextBlock expandButton, string shortDescription)
        {
            // Restaurer l'image
            DoubleAnimation imageGrow = new DoubleAnimation(280, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            imageCard.BeginAnimation(Border.WidthProperty, imageGrow);

            DoubleAnimation imageHeightGrow = new DoubleAnimation(400, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            imageCard.BeginAnimation(Border.HeightProperty, imageHeightGrow);

            // Recentrer l'image
            imageCard.VerticalAlignment = VerticalAlignment.Center;
            imageCard.Margin = new Thickness(0);

            // Restaurer la taille du titre
            DoubleAnimation titleGrow = new DoubleAnimation(28, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            titleBlock.BeginAnimation(TextBlock.FontSizeProperty, titleGrow);

            // Afficher la description courte
            descBlock.Text = shortDescription;
            
            // Si on a un ScrollViewer, réinitialiser le scroll
            var tag = (dynamic)descBlock.Tag;
            if (tag.ScrollViewer != null)
            {
                ScrollViewer sv = tag.ScrollViewer;
                sv.ScrollToTop();
            }
        }

        private Ellipse CreateStatusIndicator(string status)
        {
            Color dotColor;
            Color glowColor;

            switch (status.ToUpper())
            {
                case "RELEASING":
                    dotColor = Color.FromRgb(0, 230, 0);
                    glowColor = Color.FromRgb(0, 255, 0);
                    break;
                case "NOT_YET_RELEASED":
                    dotColor = Color.FromRgb(230, 0, 0);
                    glowColor = Color.FromRgb(255, 0, 0);
                    break;
                default:
                    return null;
            }

            Ellipse statusDot = new Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = new SolidColorBrush(dotColor),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Effect = new DropShadowEffect
                {
                    Color = glowColor,
                    Direction = 0,
                    ShadowDepth = 0,
                    BlurRadius = 8,
                    Opacity = 1
                }
            };

            DoubleAnimation glowAnimation = new DoubleAnimation
            {
                From = 0.5,
                To = 1.0,
                Duration = TimeSpan.FromSeconds(1),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };

            statusDot.Effect.BeginAnimation(DropShadowEffect.OpacityProperty, glowAnimation);

            return statusDot;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            e.Handled = true;
        }

        private void ScrollToOffsetSmooth(double offset)
        {
            DoubleAnimation animation = new DoubleAnimation
            {
                From = MainScrollViewer.VerticalOffset,
                To = offset,
                Duration = TimeSpan.FromMilliseconds(400),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };

            Storyboard storyboard = new Storyboard();
            storyboard.Children.Add(animation);

            Storyboard.SetTarget(animation, MainScrollViewer);
            Storyboard.SetTargetProperty(animation, new PropertyPath(ScrollViewerBehavior.VerticalOffsetProperty));

            storyboard.Begin();

            // Précharger plus intelligemment : charger quand il reste 5 animes ou moins
            if (canLoadMore && !isLoading && animeCount - scrollCounter <= 5)
            {
                _ = LoadAnimesAsync(8, false);
            }
        }

        private void AnimeContainer_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && isDragging)
            {
                double currentPos = e.GetPosition(this).Y;
                double delta = scrollStartPos - currentPos;
                double newOffset = scrollCounter * windowHeight + delta;

                newOffset = Math.Max(0, newOffset);

                // Permettre de scroll un peu au-delà pour voir la zone de chargement
                double maxOffset = Math.Max(0, (animeCount - 1) * windowHeight);
                
                // Si on est au dernier anime et qu'on peut charger plus
                if (scrollCounter >= animeCount - 1 && canLoadMore && !isLoading)
                {
                    // Permettre de scroll jusqu'à 200px de plus pour révéler la zone de chargement
                    double extraScroll = Math.Min(200, Math.Max(0, delta));
                    newOffset = maxOffset + extraScroll;
                    
                    // Afficher la zone de chargement si on scroll assez
                    if (extraScroll > 50 && LoadingArea != null && !isLoadingAreaVisible)
                    {
                        LoadingArea.Visibility = Visibility.Visible;
                        isLoadingAreaVisible = true;
                    }
                }
                else
                {
                    newOffset = Math.Min(newOffset, maxOffset);
                }

                MainScrollViewer.ScrollToVerticalOffset(newOffset);
            }
        }

        private void AnimeContainer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            scrollStartPos = (float)e.GetPosition(this).Y;
            isDragging = true;
            AnimeContainer.CaptureMouse();
        }

        private void AnimeContainer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!isDragging) return;

            isDragging = false;
            AnimeContainer.ReleaseMouseCapture();

            double currentPos = e.GetPosition(this).Y;
            double delta = scrollStartPos - currentPos;
            double scrollRatio = delta / windowHeight;

            int previousScrollCounter = scrollCounter;

            if (scrollRatio >= 0.5)
            {
                if (scrollCounter < animeCount - 1)
                {
                    scrollCounter++;
                }
                else if (scrollCounter == animeCount - 1 && canLoadMore && !isLoading)
                {
                    // Si on tire vers le bas au dernier anime, déclencher le chargement
                    if (delta > 50)
                    {
                        _ = LoadAnimesAsync(8, true);
                    }
                }
            }
            else if (scrollRatio <= -0.5)
            {
                scrollCounter = Math.Max(0, scrollCounter - 1);
            }

            // Si on a juste révélé la zone de chargement sans aller assez loin
            if (isLoadingAreaVisible && !isLoading)
            {
                LoadingArea.Visibility = Visibility.Collapsed;
                isLoadingAreaVisible = false;
            }

            scrollCounter = Math.Min(scrollCounter, Math.Max(0, animeCount - 1));

            ScrollToOffsetSmooth(scrollCounter * windowHeight);
        }
    }

    public static class ScrollViewerBehavior
    {
        public static readonly DependencyProperty VerticalOffsetProperty =
            DependencyProperty.RegisterAttached(
                "VerticalOffset",
                typeof(double),
                typeof(ScrollViewerBehavior),
                new UIPropertyMetadata(0.0, OnVerticalOffsetChanged));

        public static void SetVerticalOffset(DependencyObject target, double value)
        {
            target.SetValue(VerticalOffsetProperty, value);
        }

        public static double GetVerticalOffset(DependencyObject target)
        {
            return (double)target.GetValue(VerticalOffsetProperty);
        }

        private static void OnVerticalOffsetChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
        {
            ScrollViewer scrollViewer = target as ScrollViewer;
            if (scrollViewer != null)
            {
                scrollViewer.ScrollToVerticalOffset((double)e.NewValue);
            }
        }
    }
}