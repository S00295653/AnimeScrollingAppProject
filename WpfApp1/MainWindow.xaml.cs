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

        public MainWindow()
        {
            InitializeComponent();
            windowHeight = this.Height;

            // Rendre la fenêtre déplaçable
            this.MouseLeftButtonDown += Window_MouseLeftButtonDown;

            // Charger les premiers animes en arrière-plan (sans indicateur)
            _ = LoadAnimesAsync(5, false);
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Permettre de déplacer la fenêtre seulement si on ne clique pas sur le contenu
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

            // Afficher l'indicateur de chargement seulement si demandé (style Instagram)
            if (showLoading)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    if (LoadingIndicator != null)
                    {
                        LoadingIndicator.Visibility = Visibility.Visible;
                    }
                });
            }

            // Charger en arrière-plan sans bloquer l'UI
            var tasks = new Task[count];
            for (int i = 0; i < count; i++)
            {
                tasks[i] = LoadSingleAnimeAsync();
            }

            await Task.WhenAll(tasks);

            // Cacher l'indicateur de chargement
            await Dispatcher.InvokeAsync(() =>
            {
                if (LoadingIndicator != null)
                {
                    LoadingIndicator.Visibility = Visibility.Collapsed;
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

                // Pour charger uniquement les animes RELEASING ou NOT_YET_RELEASED, utiliser :
                // mediaFilter = "media(type: ANIME, sort: POPULARITY_DESC, status_in: [RELEASING, NOT_YET_RELEASED])";

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
                if (description.Length > 200)
                {
                    description = description.Substring(0, 200) + "...";
                }

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

                Border imageBorder = new Border
                {
                    Width = 280,
                    Height = 400,
                    CornerRadius = new CornerRadius(15),
                    ClipToBounds = true,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Effect = new DropShadowEffect
                    {
                        Color = Colors.Black,
                        Direction = 270,
                        ShadowDepth = 10,
                        BlurRadius = 20,
                        Opacity = 0.8
                    }
                };

                Image mainImage = new Image
                {
                    Stretch = Stretch.UniformToFill,
                };

                BitmapImage mainBitmap = new BitmapImage();
                mainBitmap.BeginInit();
                mainBitmap.UriSource = new Uri(imageUrl);
                mainBitmap.CacheOption = BitmapCacheOption.OnLoad;
                mainBitmap.EndInit();
                mainImage.Source = mainBitmap;

                imageBorder.Child = mainImage;
                Grid.SetRow(imageBorder, 0);
                contentGrid.Children.Add(imageBorder);

                StackPanel infoPanel = new StackPanel
                {
                    Margin = new Thickness(20, 0, 20, 30),
                    VerticalAlignment = VerticalAlignment.Bottom
                };

                TextBlock titleBlock = new TextBlock
                {
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
                        Orientation = Orientation.Horizontal,
                        Margin = new Thickness(0, 0, 0, 5)
                    };

                    // Pastille de statut avec effet brillant (seulement pour RELEASING et NOT_YET_RELEASED)
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
                        Text = genres,
                        FontSize = 14,
                        Foreground = new SolidColorBrush(Color.FromRgb(233, 69, 96)),
                        Margin = new Thickness(0, 0, 0, 10),
                        FontWeight = FontWeights.SemiBold
                    };
                    infoPanel.Children.Add(genresBlock);
                }

                StackPanel statsPanel = new StackPanel
                {
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
                    TextBlock descBlock = new TextBlock
                    {
                        Text = description,
                        FontSize = 14,
                        Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                        TextWrapping = TextWrapping.Wrap,
                        LineHeight = 20
                    };
                    infoPanel.Children.Add(descBlock);
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

        private Ellipse CreateStatusIndicator(string status)
        {
            Color dotColor;
            Color glowColor;

            // Déterminer la couleur selon le statut (seulement RELEASING et NOT_YET_RELEASED)
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
                    // Ne pas afficher d'ellipse pour les autres statuts
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

            // Animation de brillance
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
            // Animation fluide
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

            // Charger plus d'animes si on approche de la fin (style Instagram)
            // Si on est proche de la fin et qu'on peut charger, on charge en arrière-plan
            if (canLoadMore && animeCount - scrollCounter <= 3 && animeCount - scrollCounter > 0)
            {
                _ = LoadAnimesAsync(3, false); // Chargement discret en arrière-plan
            }
        }

        private void AnimeContainer_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && isDragging)
            {
                double currentPos = e.GetPosition(this).Y;
                double delta = scrollStartPos - currentPos;
                double newOffset = scrollCounter * windowHeight + delta;

                // Limiter le scroll pour ne pas aller en négatif
                newOffset = Math.Max(0, newOffset);

                // Limiter le scroll pour ne pas dépasser le contenu disponible
                double maxOffset = Math.Max(0, (animeCount - 1) * windowHeight);
                newOffset = Math.Min(newOffset, maxOffset);

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

            // Déterminer la direction et si on doit changer de carte
            if (scrollRatio >= 0.5)
            {
                // Scroll vers le bas - vérifier qu'il y a du contenu disponible
                if (scrollCounter < animeCount - 1)
                {
                    // Il y a encore des animes chargés à afficher
                    scrollCounter++;
                }
                else if (scrollCounter == animeCount - 1 && canLoadMore && !isLoading)
                {
                    // On est à la toute fin, essayer de charger plus avec indicateur visible
                    scrollCounter++;
                    _ = LoadAnimesAsync(5, true); // Afficher le loading (style Instagram)
                }
                // Sinon on ne fait rien - on reste sur la dernière carte disponible
            }
            else if (scrollRatio <= -0.5)
            {
                // Scroll vers le haut
                scrollCounter = Math.Max(0, scrollCounter - 1);
            }

            // Limiter scrollCounter au maximum disponible
            scrollCounter = Math.Min(scrollCounter, Math.Max(0, animeCount - 1));

            // Animer vers la position finale
            ScrollToOffsetSmooth(scrollCounter * windowHeight);
        }
    }

    // Helper class pour animer le ScrollViewer
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