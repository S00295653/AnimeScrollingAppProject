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

        // --- CONSTANTES ---
        private const double IMAGE_HEIGHT_NORMAL = 480;
        private const double IMAGE_HEIGHT_MIN = 180;
        private const double UI_MARGINS_NORMAL = 80; // Marges autour de l'image et du bas

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

            // Charger les premiers animes
            _ = LoadAnimesAsync(5, false);
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
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

            if (showLoading && LoadingArea != null)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    LoadingArea.Visibility = Visibility.Visible;
                    isLoadingAreaVisible = true;
                });
            }

            var tasks = new Task[count];
            for (int i = 0; i < count; i++)
            {
                tasks[i] = LoadSingleAnimeAsync();
            }

            await Task.WhenAll(tasks);

            if (showLoading)
            {
                await Task.Delay(500);
            }

            await Dispatcher.InvokeAsync(() =>
            {
                if (LoadingArea != null)
                {
                    LoadingArea.Visibility = Visibility.Collapsed;
                    isLoadingAreaVisible = false;
                }

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
                    mediaFilter = "media(type: ANIME, status: FINISHED, averageScore_greater: 0, episodes_greater: 0, sort: POPULARITY_DESC)";
                else if (rand < 65)
                    mediaFilter = "media(type: ANIME, status: FINISHED, averageScore_greater: 0, episodes_greater: 0, sort: SCORE_DESC)";
                else if (rand < 80)
                    mediaFilter = "media(type: ANIME, status: FINISHED, averageScore_greater: 70, episodes_greater: 0, popularity_lesser: 20000, sort: SCORE_DESC)";
                else if (rand < 95)
                    mediaFilter = "media(type: ANIME, status: RELEASING, averageScore_greater: 0, sort: TRENDING_DESC)";
                else
                    mediaFilter = "media(type: ANIME, status: NOT_YET_RELEASED, sort: POPULARITY_DESC)";

                var query = $@"
                query ($page: Int) {{
                    Page(page: $page, perPage: 1) {{
                        {mediaFilter} {{
                            id
                            title {{ romaji english }}
                            coverImage {{ extraLarge large }}
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

                var request = new { query = query, variables = new { page = randomPage } };
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

                await Dispatcher.InvokeAsync(() =>
                {
                    CreateFullScreenAnimeCard(media);
                    animeCount++;
                    AnimeCounter.Text = animeCount + " animes";
                });
            }
            catch (Exception) { }
        }

        private void CreateFullScreenAnimeCard(JToken media)
        {
            try
            {
                // Extraction des données
                var titleObj = media["title"];
                string displayTitle = (!string.IsNullOrEmpty(titleObj?["english"]?.ToString())) ? titleObj["english"].ToString() : titleObj?["romaji"]?.ToString();

                var coverObj = media["coverImage"];
                string imageUrl = coverObj?["extraLarge"]?.ToString() ?? coverObj?["large"]?.ToString();

                string score = media["averageScore"]?.ToString() ?? "N/A";
                string episodes = media["episodes"]?.ToString() ?? "N/A";
                string description = media["description"]?.ToString() ?? "";
                string season = media["season"]?.ToString() ?? "";
                string year = media["seasonYear"]?.ToString() ?? "";
                string status = media["status"]?.ToString() ?? "";

                // Nettoyage description HTML
                description = System.Text.RegularExpressions.Regex.Replace(description, "<.*?>", string.Empty);
                string fullDescription = description;

                // Genres
                string genres = "";
                var genresArray = media["genres"];
                if (genresArray != null && genresArray.HasValues)
                {
                    for (int i = 0; i < Math.Min(3, genresArray.Count()); i++)
                        genres += genresArray[i].ToString() + " • ";
                    genres = genres.TrimEnd(' ', '•');
                }

                if (string.IsNullOrEmpty(imageUrl) || string.IsNullOrEmpty(displayTitle)) return;

                // Structure de la carte
                Grid fullScreenCard = new Grid { Height = windowHeight, Background = Brushes.Black };

                // Background Flou
                Image bgImage = new Image { Stretch = Stretch.UniformToFill, Opacity = 0.4 };
                bgImage.Source = new BitmapImage(new Uri(imageUrl));
                bgImage.Effect = new BlurEffect { Radius = 20 };
                fullScreenCard.Children.Add(bgImage);

                // Overlay sombre
                Border gradientOverlay = new Border
                {
                    Background = new LinearGradientBrush
                    {
                        StartPoint = new Point(0.5, 0),
                        EndPoint = new Point(0.5, 1),
                        GradientStops = new GradientStopCollection {
                            new GradientStop(Color.FromArgb(100, 0, 0, 0), 0),
                            new GradientStop(Color.FromArgb(200, 0, 0, 0), 1)
                        }
                    }
                };
                fullScreenCard.Children.Add(gradientOverlay);

                // Grille principale
                Grid contentGrid = new Grid();
                contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Image
                contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Espace
                contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Info

                // Image Principale
                Border imageCard = new Border
                {
                    Width = 320,
                    Height = IMAGE_HEIGHT_NORMAL,
                    CornerRadius = new CornerRadius(8),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 20, 0, 20),
                    Effect = new DropShadowEffect { Color = Colors.Black, Direction = 270, ShadowDepth = 10, BlurRadius = 20, Opacity = 0.8 },
                    Background = new ImageBrush { ImageSource = new BitmapImage(new Uri(imageUrl)), Stretch = Stretch.UniformToFill },
                    RenderTransform = new ScaleTransform(1, 1),
                    RenderTransformOrigin = new Point(0.5, 0.5)
                };

                // Hover Effect
                bool isHovering = false;
                imageCard.MouseEnter += (s, e) => {
                    if (!isDragging)
                    {
                        isHovering = true;
                        ((ScaleTransform)imageCard.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.05, TimeSpan.FromMilliseconds(200)));
                        ((ScaleTransform)imageCard.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.05, TimeSpan.FromMilliseconds(200)));
                    }
                };
                imageCard.MouseLeave += (s, e) => {
                    isHovering = false;
                    ((ScaleTransform)imageCard.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)));
                    ((ScaleTransform)imageCard.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)));
                };
                imageCard.PreviewMouseLeftButtonDown += (s, e) => {
                    if (isHovering)
                    {
                        ScaleTransform scale = imageCard.RenderTransform as ScaleTransform;
                        scale.ScaleX = 1.05; scale.ScaleY = 1.05;
                    }
                };

                Grid.SetRow(imageCard, 0);
                contentGrid.Children.Add(imageCard);

                // --- INFO PANEL ---
                StackPanel infoPanel = new StackPanel { Margin = new Thickness(30, 20, 30, 50), VerticalAlignment = VerticalAlignment.Bottom };
                Grid.SetRow(infoPanel, 2);

                // Titre avec taille adaptative
                TextBlock titleBlock = new TextBlock
                {
                    Text = displayTitle,
                    FontSize = 28,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 10),
                    Effect = new DropShadowEffect { Color = Colors.Black, ShadowDepth = 2 }
                };

                // Ajuster la taille du titre si trop long
                if (displayTitle.Length > 50)
                {
                    titleBlock.FontSize = 22;
                    titleBlock.MaxHeight = 70; // Limite à environ 3 lignes
                }
                else if (displayTitle.Length > 35)
                {
                    titleBlock.FontSize = 24;
                }

                infoPanel.Children.Add(titleBlock);

                // Saison
                if (!string.IsNullOrEmpty(season) && !string.IsNullOrEmpty(year))
                {
                    StackPanel seasonPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 5) };
                    Ellipse statusDot = CreateStatusIndicator(status);
                    if (statusDot != null) seasonPanel.Children.Add(statusDot);
                    seasonPanel.Children.Add(new TextBlock { Text = season + " " + year, FontSize = 14, Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)) });
                    infoPanel.Children.Add(seasonPanel);
                }

                // Genres
                if (!string.IsNullOrEmpty(genres))
                {
                    infoPanel.Children.Add(new TextBlock { Text = genres, FontSize = 14, Foreground = new SolidColorBrush(Color.FromRgb(233, 69, 96)), Margin = new Thickness(0, 0, 0, 10), FontWeight = FontWeights.SemiBold });
                }

                // Stats (Score / Ep)
                StackPanel statsPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };

                Border scoreBadge = new Border { Background = new SolidColorBrush(Color.FromRgb(255, 215, 0)), CornerRadius = new CornerRadius(5), Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 10, 0) };
                scoreBadge.Child = new TextBlock { Text = "★ " + score, FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Brushes.Black };
                statsPanel.Children.Add(scoreBadge);

                Border epBadge = new Border { Background = new SolidColorBrush(Color.FromRgb(15, 52, 96)), CornerRadius = new CornerRadius(5), Padding = new Thickness(10, 5, 10, 5) };
                epBadge.Child = new TextBlock { Text = "📺 " + episodes + " EP", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Brushes.White };
                statsPanel.Children.Add(epBadge);

                infoPanel.Children.Add(statsPanel);

                // --- DESCRIPTION INTELLIGENTE ---
                if (!string.IsNullOrEmpty(description))
                {
                    CreateSmartDescription(infoPanel, imageCard, titleBlock, statsPanel, fullDescription);
                }

                contentGrid.Children.Add(infoPanel);
                fullScreenCard.Children.Add(contentGrid);
                AnimeContainer.Children.Add(fullScreenCard);
            }
            catch (Exception) { }
        }

        private void CreateSmartDescription(StackPanel infoPanel, Border imageCard, TextBlock titleBlock, StackPanel statsPanel, string fullDescription)
        {
            StackPanel descriptionContainer = new StackPanel();

            // 1. Calcul précis de l'espace disponible
            // Pour cela, on doit estimer la hauteur que prennent déjà le titre et les stats.
            // On fait une mesure théorique.

            // Mesure Titre
            TextBlock measureTitle = new TextBlock { Text = titleBlock.Text, FontSize = titleBlock.FontSize, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, Width = 390 };
            measureTitle.Measure(new Size(390, double.PositiveInfinity));
            double titleHeight = measureTitle.DesiredSize.Height;

            // Hauteur estimée des autres éléments (Saison, Genres, Stats, Marges)
            double otherElementsHeight = 120; // Augmenté pour inclure les marges du bas

            // Espace occupé par l'UI hors description
            double uiOccupied = titleHeight + otherElementsHeight + UI_MARGINS_NORMAL;

            // Espace restant sous l'image normale
            double availableSpaceForDesc = windowHeight - IMAGE_HEIGHT_NORMAL - uiOccupied;

            // Si le calcul donne un truc négatif ou très petit (ex: petit écran), on force un minimum pour l'affichage court
            if (availableSpaceForDesc < 60) availableSpaceForDesc = 60; // Au moins 3 lignes

            // 2. Mesure de la description
            TextBlock measureDesc = new TextBlock { Text = fullDescription, FontSize = 14, TextWrapping = TextWrapping.Wrap, Width = 390, LineHeight = 20 };
            measureDesc.Measure(new Size(390, double.PositiveInfinity));
            double fullDescHeight = measureDesc.DesiredSize.Height;

            // 3. Condition stricte : Si le texte dépasse l'espace dispo, on coupe.
            bool needsExpansion = fullDescHeight > availableSpaceForDesc;

            // 4. Description Courte
            TextBlock descBlockShort = new TextBlock
            {
                Text = fullDescription,
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 20,
                // Si besoin d'expansion, on force la hauteur max calculée
                MaxHeight = needsExpansion ? availableSpaceForDesc : double.PositiveInfinity,
                TextTrimming = needsExpansion ? TextTrimming.CharacterEllipsis : TextTrimming.None
            };
            descriptionContainer.Children.Add(descBlockShort);

            if (needsExpansion)
            {
                // Bouton Voir Plus
                TextBlock expandButton = new TextBlock
                {
                    Text = "Read More",
                    FontSize = 14,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 149, 237)),
                    Margin = new Thickness(0, 8, 0, 0),
                    Cursor = Cursors.Hand,
                    FontWeight = FontWeights.Bold
                };

                // ScrollViewer (caché)
                ScrollViewer descScrollViewer = new ScrollViewer
                {
                    MaxHeight = 0,
                    Opacity = 0,
                    Visibility = Visibility.Collapsed,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                    PanningMode = PanningMode.VerticalOnly
                };

                // Gestion du Drag sur le texte
                Point startPoint = new Point();
                bool scrolling = false;
                descScrollViewer.PreviewMouseLeftButtonDown += (s, e) => { startPoint = e.GetPosition(descScrollViewer); scrolling = true; descScrollViewer.CaptureMouse(); };
                descScrollViewer.PreviewMouseMove += (s, e) => {
                    if (scrolling)
                    {
                        double delta = startPoint.Y - e.GetPosition(descScrollViewer).Y;
                        descScrollViewer.ScrollToVerticalOffset(descScrollViewer.VerticalOffset + delta);
                        startPoint = e.GetPosition(descScrollViewer);
                    }
                };
                descScrollViewer.PreviewMouseLeftButtonUp += (s, e) => { scrolling = false; descScrollViewer.ReleaseMouseCapture(); };

                // Contenu complet + Voir Moins
                StackPanel scrollContent = new StackPanel();
                scrollContent.Children.Add(new TextBlock
                {
                    Text = fullDescription,
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 20
                });

                TextBlock collapseButton = new TextBlock
                {
                    Text = "See Less",
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 149, 237)),
                    Margin = new Thickness(0, 15, 0, 10),
                    Cursor = Cursors.Hand,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                collapseButton.MouseLeftButtonDown += (s, e) => {
                    e.Handled = true;
                    AnimateDescriptionCollapse(imageCard, titleBlock, descBlockShort, descScrollViewer, statsPanel);
                };
                scrollContent.Children.Add(collapseButton);
                descScrollViewer.Content = scrollContent;

                descriptionContainer.Children.Add(descScrollViewer);
                descriptionContainer.Children.Add(expandButton);

                // Clic Voir Plus
                expandButton.MouseLeftButtonDown += (s, e) => {
                    e.Handled = true;
                    AnimateDescriptionExpansion(imageCard, titleBlock, descBlockShort, descScrollViewer, expandButton, statsPanel, fullDescHeight);
                };
            }

            infoPanel.Children.Add(descriptionContainer);
        }

        private void AnimateDescriptionExpansion(Border imageCard, TextBlock titleBlock, TextBlock descBlockShort,
            ScrollViewer descScrollViewer, TextBlock expandButton, StackPanel statsPanel, double fullTextHeight)
        {
            var duration = TimeSpan.FromMilliseconds(500);
            var easing = new QuarticEase { EasingMode = EasingMode.EaseInOut };

            // 1. Calcul mathématique de l'espace idéal
            // Espace total dispo = Hauteur fenêtre - TitreReduit(approx 60) - Stats(35) - Marges(70 au lieu de 50)
            double uiOverheadExpanded = 165; // Augmenté pour plus de marge en bas
            double totalAvailableHeight = windowHeight - uiOverheadExpanded;

            // Quelle taille prend l'image si on affiche tout le texte ?
            double idealImageHeight = totalAvailableHeight - fullTextHeight;

            double targetImageHeight;
            double targetDescHeight;

            if (idealImageHeight >= IMAGE_HEIGHT_NORMAL)
            {
                // CAS 1: Y'a plein de place (texte moyen) -> Image Max, Texte entier
                targetImageHeight = IMAGE_HEIGHT_NORMAL;
                targetDescHeight = fullTextHeight;
            }
            else if (idealImageHeight >= IMAGE_HEIGHT_MIN)
            {
                // CAS 2: On réduit l'image pour que le texte rentre pile poil (Pas de vide !)
                targetImageHeight = idealImageHeight;
                targetDescHeight = fullTextHeight;
            }
            else
            {
                // CAS 3: Manque de place même avec image min -> Image Min, Scroll
                targetImageHeight = IMAGE_HEIGHT_MIN;
                targetDescHeight = totalAvailableHeight - IMAGE_HEIGHT_MIN;
            }

            double targetImageWidth = targetImageHeight * 0.7; // Ratio

            // Animations
            DoubleAnimation fadeBtn = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
            expandButton.BeginAnimation(TextBlock.OpacityProperty, fadeBtn);
            expandButton.IsHitTestVisible = false;

            // Image
            imageCard.VerticalAlignment = VerticalAlignment.Top;
            imageCard.BeginAnimation(Border.WidthProperty, new DoubleAnimation(imageCard.Width, targetImageWidth, duration) { EasingFunction = easing });
            imageCard.BeginAnimation(Border.HeightProperty, new DoubleAnimation(imageCard.Height, targetImageHeight, duration) { EasingFunction = easing });
            imageCard.BeginAnimation(Border.MarginProperty, new ThicknessAnimation(imageCard.Margin, new Thickness(0, 10, 0, 5), duration) { EasingFunction = easing });

            // Titre & Stats - adapter la taille du titre
            double targetTitleSize = titleBlock.Text.Length > 50 ? 16 : (titleBlock.Text.Length > 35 ? 18 : 20);
            titleBlock.BeginAnimation(TextBlock.FontSizeProperty, new DoubleAnimation(titleBlock.FontSize, targetTitleSize, duration) { EasingFunction = easing });
            titleBlock.BeginAnimation(TextBlock.MarginProperty, new ThicknessAnimation(titleBlock.Margin, new Thickness(0, 0, 0, 5), duration) { EasingFunction = easing });
            statsPanel.BeginAnimation(StackPanel.MarginProperty, new ThicknessAnimation(statsPanel.Margin, new Thickness(0, 0, 0, 5), duration) { EasingFunction = easing });

            // Texte
            descBlockShort.BeginAnimation(TextBlock.OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200)));

            descScrollViewer.Visibility = Visibility.Visible;
            descScrollViewer.Opacity = 0;
            descScrollViewer.BeginAnimation(ScrollViewer.MaxHeightProperty, new DoubleAnimation(0, targetDescHeight, duration) { EasingFunction = easing });
            descScrollViewer.BeginAnimation(ScrollViewer.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)) { BeginTime = TimeSpan.FromMilliseconds(200) });

            // Cleanup
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = duration };
            timer.Tick += (s, e) => { expandButton.Visibility = Visibility.Collapsed; descBlockShort.Visibility = Visibility.Collapsed; timer.Stop(); };
            timer.Start();
        }

        private void AnimateDescriptionCollapse(Border imageCard, TextBlock titleBlock, TextBlock descBlockShort, ScrollViewer descScrollViewer, StackPanel statsPanel)
        {
            var duration = TimeSpan.FromMilliseconds(500);
            var easing = new QuarticEase { EasingMode = EasingMode.EaseInOut };

            // Cacher scroll
            descScrollViewer.BeginAnimation(ScrollViewer.MaxHeightProperty, new DoubleAnimation(descScrollViewer.MaxHeight, 0, TimeSpan.FromMilliseconds(300)) { EasingFunction = easing });
            descScrollViewer.BeginAnimation(ScrollViewer.OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300)) { EasingFunction = easing });

            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            t.Tick += (s, e) => {
                descScrollViewer.Visibility = Visibility.Collapsed;
                descScrollViewer.ScrollToVerticalOffset(0);

                // Restaurer UI
                imageCard.VerticalAlignment = VerticalAlignment.Center;
                imageCard.BeginAnimation(Border.WidthProperty, new DoubleAnimation(imageCard.Width, 320, duration) { EasingFunction = easing });
                imageCard.BeginAnimation(Border.HeightProperty, new DoubleAnimation(imageCard.Height, IMAGE_HEIGHT_NORMAL, duration) { EasingFunction = easing });
                imageCard.BeginAnimation(Border.MarginProperty, new ThicknessAnimation(imageCard.Margin, new Thickness(0, 20, 0, 20), duration) { EasingFunction = easing });

                // Restaurer la taille originale du titre
                double originalTitleSize = titleBlock.Text.Length > 50 ? 22 : (titleBlock.Text.Length > 35 ? 24 : 28);
                titleBlock.BeginAnimation(TextBlock.FontSizeProperty, new DoubleAnimation(titleBlock.FontSize, originalTitleSize, duration) { EasingFunction = easing });
                titleBlock.BeginAnimation(TextBlock.MarginProperty, new ThicknessAnimation(titleBlock.Margin, new Thickness(0, 0, 0, 10), duration) { EasingFunction = easing });
                statsPanel.BeginAnimation(StackPanel.MarginProperty, new ThicknessAnimation(statsPanel.Margin, new Thickness(0, 0, 0, 10), duration) { EasingFunction = easing });

                StackPanel p = (StackPanel)descScrollViewer.Parent;
                TextBlock btn = p.Children.OfType<TextBlock>().FirstOrDefault(x => x.Text == "Read More");
                if (btn != null) { btn.Visibility = Visibility.Visible; btn.BeginAnimation(TextBlock.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))); btn.IsHitTestVisible = true; }

                descBlockShort.Visibility = Visibility.Visible;
                descBlockShort.BeginAnimation(TextBlock.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));
                t.Stop();
            };
            t.Start();
        }

        private Ellipse CreateStatusIndicator(string status)
        {
            Color c, g;
            if (status == "RELEASING") { c = Color.FromRgb(0, 230, 0); g = Color.FromRgb(0, 255, 0); }
            else if (status == "NOT_YET_RELEASED") { c = Color.FromRgb(230, 0, 0); g = Color.FromRgb(255, 0, 0); }
            else return null;

            Ellipse e = new Ellipse { Width = 10, Height = 10, Fill = new SolidColorBrush(c), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Effect = new DropShadowEffect { Color = g, BlurRadius = 8, Opacity = 1 } };
            DoubleAnimation a = new DoubleAnimation { From = 0.5, To = 1.0, Duration = TimeSpan.FromSeconds(1), AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            e.Effect.BeginAnimation(DropShadowEffect.OpacityProperty, a);
            return e;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => this.Close();
        private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e) => e.Handled = true;

        private void ScrollToOffsetSmooth(double offset)
        {
            DoubleAnimation a = new DoubleAnimation { From = MainScrollViewer.VerticalOffset, To = offset, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
            Storyboard s = new Storyboard(); s.Children.Add(a); Storyboard.SetTarget(a, MainScrollViewer); Storyboard.SetTargetProperty(a, new PropertyPath(ScrollViewerBehavior.VerticalOffsetProperty)); s.Begin();
            if (canLoadMore && animeCount - scrollCounter <= 3 && animeCount - scrollCounter > 0) _ = LoadAnimesAsync(3, false);
        }

        private void AnimeContainer_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && isDragging)
            {
                double delta = scrollStartPos - e.GetPosition(this).Y;
                double off = Math.Max(0, scrollCounter * windowHeight + delta);
                if (scrollCounter >= animeCount - 1 && canLoadMore && !isLoading && delta > 50 && !isLoadingAreaVisible) { LoadingArea.Visibility = Visibility.Visible; isLoadingAreaVisible = true; }
                MainScrollViewer.ScrollToVerticalOffset(Math.Min(off, Math.Max(0, (animeCount - 1) * windowHeight) + (scrollCounter >= animeCount - 1 ? 200 : 0)));
            }
        }
        private void AnimeContainer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { scrollStartPos = (float)e.GetPosition(this).Y; isDragging = true; AnimeContainer.CaptureMouse(); }
        private void AnimeContainer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!isDragging) return; isDragging = false; AnimeContainer.ReleaseMouseCapture();
            double r = (scrollStartPos - e.GetPosition(this).Y) / windowHeight;
            if (r >= 0.5) { if (scrollCounter < animeCount - 1) scrollCounter++; else if (canLoadMore && !isLoading) _ = LoadAnimesAsync(5, true); }
            else if (r <= -0.5) scrollCounter = Math.Max(0, scrollCounter - 1);
            if (isLoadingAreaVisible && !isLoading) { LoadingArea.Visibility = Visibility.Collapsed; isLoadingAreaVisible = false; }
            ScrollToOffsetSmooth(Math.Min(scrollCounter, Math.Max(0, animeCount - 1)) * windowHeight);
        }
    }

    public static class ScrollViewerBehavior
    {
        public static readonly DependencyProperty VerticalOffsetProperty = DependencyProperty.RegisterAttached("VerticalOffset", typeof(double), typeof(ScrollViewerBehavior), new UIPropertyMetadata(0.0, (d, e) => ((ScrollViewer)d).ScrollToVerticalOffset((double)e.NewValue)));
        public static void SetVerticalOffset(DependencyObject t, double v) => t.SetValue(VerticalOffsetProperty, v);
        public static double GetVerticalOffset(DependencyObject t) => (double)t.GetValue(VerticalOffsetProperty);
    }
}