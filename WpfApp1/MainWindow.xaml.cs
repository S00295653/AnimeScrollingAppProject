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
        private enum SwipeMode
        {
            Normal,
            DescriptionScrolling
        }

        private SwipeMode currentSwipeMode = SwipeMode.Normal;
        private ScrollViewer currentDescScrollViewer = null;
        private const double DESC_SCROLL_BUFFER = 40; // px avant d'autoriser le swipe carte

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

        // ===== 🖐️ CORRECTION 3 : Variable de seuil de scroll ajustable =====
        private const double SCROLL_THRESHOLD_NORMAL = 0.5;      // 50% de l'écran (défaut)
        private const double SCROLL_THRESHOLD_EXPANDED = 0.35;   // 35% quand description ouverte (plus dur)
        // ===== FIN CORRECTION 3 =====

        private const double IMAGE_HEIGHT_NORMAL = 480;
        private const double IMAGE_HEIGHT_MIN = 180;
        private const double UI_MARGINS_NORMAL = 80;

        public MainWindow()
        {
            InitializeComponent();
            windowHeight = this.Height;

            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            this.MouseLeftButtonDown += Window_MouseLeftButtonDown;

            MainScrollViewer.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Up || e.Key == Key.Down ||
                    e.Key == Key.PageUp || e.Key == Key.PageDown ||
                    e.Key == Key.Home || e.Key == Key.End)
                {
                    e.Handled = true;
                }
            };

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
                            nextAiringEpisode {{ episode }}
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
                var titleObj = media["title"];
                string displayTitle = (!string.IsNullOrEmpty(titleObj?["english"]?.ToString())) ? titleObj["english"].ToString() : titleObj?["romaji"]?.ToString();

                var coverObj = media["coverImage"];
                string imageUrl = coverObj?["extraLarge"]?.ToString() ?? coverObj?["large"]?.ToString();

                string score = media["averageScore"] != null && media["averageScore"].Type != JTokenType.Null ? media["averageScore"].ToString() : "N/A";
                string description = media["description"]?.ToString() ?? "";
                string season = media["season"]?.ToString() ?? "";
                string year = media["seasonYear"]?.ToString() ?? "";
                string status = media["status"]?.ToString() ?? "";

                string epDisplay = "N/A";

                if (status == "RELEASING")
                {
                    int? totalEpisodes = media["episodes"]?.Type == JTokenType.Null ? null : (int?)media["episodes"];
                    int? nextAiring = media["nextAiringEpisode"]?["episode"]?.Type == JTokenType.Null ? null : (int?)media["nextAiringEpisode"]["episode"];

                    if (nextAiring.HasValue)
                    {
                        int releasedEpisodes = nextAiring.Value - 1;

                        if (totalEpisodes.HasValue)
                        {
                            epDisplay = $"{releasedEpisodes}/{totalEpisodes.Value}";
                        }
                        else
                        {
                            epDisplay = $"{releasedEpisodes}+";
                        }
                    }
                    else if (totalEpisodes.HasValue)
                    {
                        epDisplay = totalEpisodes.Value.ToString();
                    }
                }
                else if (status == "FINISHED")
                {
                    if (media["episodes"] != null && media["episodes"].Type != JTokenType.Null)
                    {
                        epDisplay = media["episodes"].ToString();
                    }
                }
                else if (status == "NOT_YET_RELEASED")
                {
                    if (media["episodes"] != null && media["episodes"].Type != JTokenType.Null)
                    {
                        epDisplay = media["episodes"].ToString();
                    }
                }

                description = System.Text.RegularExpressions.Regex.Replace(description, "<.*?>", string.Empty);
                string fullDescription = description;

                string genres = "";
                var genresArray = media["genres"];
                if (genresArray != null && genresArray.HasValues)
                {
                    for (int i = 0; i < Math.Min(3, genresArray.Count()); i++)
                        genres += genresArray[i].ToString() + " • ";
                    genres = genres.TrimEnd(' ', '•');
                }

                if (string.IsNullOrEmpty(imageUrl) || string.IsNullOrEmpty(displayTitle)) return;

                Grid fullScreenCard = new Grid { Height = windowHeight, Background = Brushes.Black };

                Image bgImage = new Image { Stretch = Stretch.UniformToFill, Opacity = 0.4 };
                bgImage.Source = new BitmapImage(new Uri(imageUrl));
                bgImage.Effect = new BlurEffect { Radius = 20 };
                fullScreenCard.Children.Add(bgImage);

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

                Grid contentGrid = new Grid();
                contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

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

                StackPanel infoPanel = new StackPanel { Margin = new Thickness(30, 20, 30, 50), VerticalAlignment = VerticalAlignment.Bottom };
                Grid.SetRow(infoPanel, 2);

                TextBlock titleBlock = new TextBlock
                {
                    Text = displayTitle,
                    FontSize = 28,
                    Width = 390,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 10)
                };

                TextBlock measureBlock = new TextBlock { Text = displayTitle, FontSize = 28, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, Width = 390 };
                measureBlock.Measure(new Size(390, double.PositiveInfinity));
                int lines = (int)Math.Ceiling(measureBlock.DesiredSize.Height / 35);

                if (lines > 3)
                {
                    titleBlock.FontSize = 16;
                    titleBlock.MaxHeight = 70;
                }
                else if (lines > 2)
                {
                    titleBlock.FontSize = 20;
                    titleBlock.MaxHeight = 70;
                }
                else if (lines > 1)
                {
                    titleBlock.FontSize = 24;
                    titleBlock.MaxHeight = 70;
                }

                infoPanel.Children.Add(titleBlock);

                if (!string.IsNullOrEmpty(season) && !string.IsNullOrEmpty(year))
                {
                    StackPanel seasonPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 5) };
                    Ellipse statusDot = CreateStatusIndicator(status);
                    if (statusDot != null) seasonPanel.Children.Add(statusDot);
                    seasonPanel.Children.Add(new TextBlock { Text = season + " " + year, FontSize = 14, Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)) });
                    infoPanel.Children.Add(seasonPanel);
                }

                if (!string.IsNullOrEmpty(genres))
                {
                    infoPanel.Children.Add(new TextBlock { Text = genres, FontSize = 14, Foreground = new SolidColorBrush(Color.FromRgb(233, 69, 96)), Margin = new Thickness(0, 0, 0, 10), FontWeight = FontWeights.SemiBold });
                }

                StackPanel statsPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 15) };

                Border scoreBadge = new Border { Background = new SolidColorBrush(Color.FromRgb(255, 215, 0)), CornerRadius = new CornerRadius(5), Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 10, 0) };
                scoreBadge.Child = new TextBlock { Text = "★ " + score, FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Brushes.Black };
                statsPanel.Children.Add(scoreBadge);

                Border epBadge = new Border { Background = new SolidColorBrush(Color.FromRgb(15, 52, 96)), CornerRadius = new CornerRadius(5), Padding = new Thickness(10, 5, 10, 5) };
                epBadge.Child = new TextBlock { Text = "📺 " + epDisplay + " EP", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Brushes.White };
                statsPanel.Children.Add(epBadge);

                infoPanel.Children.Add(statsPanel);

                if (!string.IsNullOrEmpty(description))
                {
                    CreateSmartDescription(infoPanel, imageCard, titleBlock, statsPanel, fullDescription, fullScreenCard);
                }

                contentGrid.Children.Add(infoPanel);
                fullScreenCard.Children.Add(contentGrid);
                AnimeContainer.Children.Add(fullScreenCard);
            }
            catch (Exception) { }
        }

        // ===== 🐞 CORRECTION 1 : Tag "IsExpanded" pour tracking de l'état =====
        private void CreateSmartDescription(StackPanel infoPanel, Border imageCard, TextBlock titleBlock,
    StackPanel statsPanel, string fullDescription, Grid fullScreenCard)
        {
            StackPanel descriptionContainer = new StackPanel();
            fullScreenCard.Tag = false; // false = fermée, true = ouverte

            // === MESURE DES HAUTEURS ===
            TextBlock measureTitle = new TextBlock
            {
                Text = titleBlock.Text,
                FontSize = titleBlock.FontSize,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
                Width = 390
            };
            measureTitle.Measure(new Size(390, double.PositiveInfinity));
            double titleHeight = measureTitle.DesiredSize.Height;

            double otherElementsHeight = 120;
            double uiOccupied = titleHeight + otherElementsHeight + UI_MARGINS_NORMAL;
            double availableSpaceForDesc = windowHeight - IMAGE_HEIGHT_NORMAL - uiOccupied;
            if (availableSpaceForDesc < 60) availableSpaceForDesc = 60;

            TextBlock measureDesc = new TextBlock
            {
                Text = fullDescription,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Width = 390,
                LineHeight = 20
            };
            measureDesc.Measure(new Size(390, double.PositiveInfinity));
            double fullDescHeight = measureDesc.DesiredSize.Height;

            bool needsExpansion = fullDescHeight > availableSpaceForDesc;

            // === WRAPPER POUR TEXTE COURT (avec Height contrôlée) ===
            Border descShortWrapper = new Border
            {
                ClipToBounds = true  // CRUCIAL : coupe le contenu qui dépasse
            };

            TextBlock descBlockShort = new TextBlock
            {
                Text = fullDescription,
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 20,
                MaxHeight = needsExpansion ? availableSpaceForDesc : double.PositiveInfinity,
                TextTrimming = needsExpansion ? TextTrimming.CharacterEllipsis : TextTrimming.None
            };

            descShortWrapper.Child = descBlockShort;
            descriptionContainer.Children.Add(descShortWrapper);

            // Mesurer la hauteur réelle du texte court
            descShortWrapper.Measure(new Size(390, double.PositiveInfinity));
            double shortDescHeight = descShortWrapper.DesiredSize.Height;
            descShortWrapper.Height = shortDescHeight; // Fixer la hauteur initiale

            if (needsExpansion)
            {
                // === BOUTON READ MORE ===
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

                // === WRAPPER POUR TEXTE LONG (avec Height contrôlée) ===
                Border descLongWrapper = new Border
                {
                    Height = 0,  // Commence à 0
                    ClipToBounds = true
                };

                ScrollViewer descScrollViewer = new ScrollViewer
                {
                    Opacity = 0,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                    PanningMode = PanningMode.VerticalOnly
                };

                // === GESTION DU SCROLL DANS LA DESCRIPTION ===
                Point startPoint = new Point();
                bool scrolling = false;
                double accumulatedDelta = 0;

                descScrollViewer.PreviewMouseLeftButtonDown += (s, e) => {
                    startPoint = e.GetPosition(descScrollViewer);
                    scrolling = true;
                    accumulatedDelta = 0;
                    descScrollViewer.CaptureMouse();
                };

                descScrollViewer.PreviewMouseMove += (s, e) => {
                    if (scrolling)
                    {
                        double delta = startPoint.Y - e.GetPosition(descScrollViewer).Y;
                        double currentOffset = descScrollViewer.VerticalOffset;
                        double maxOffset = descScrollViewer.ScrollableHeight;

                        // Mise à jour de l'état du scroll (pour le swipe)
                        if (currentDescScrollViewer == descScrollViewer)
                        {
                            UpdateDescriptionScrollState();
                        }

                        if (currentOffset <= 0 && delta < 0)
                        {
                            accumulatedDelta += delta;
                            if (Math.Abs(accumulatedDelta) > DESC_SCROLL_BUFFER)
                            {
                                scrolling = false;
                                descScrollViewer.ReleaseMouseCapture();
                                currentSwipeMode = SwipeMode.Normal;
                                currentDescScrollViewer = null;

                                if (scrollCounter > 0)
                                {
                                    scrollCounter--;
                                    ScrollToOffsetSmooth(scrollCounter * windowHeight);
                                }
                            }
                        }
                        else if (currentOffset >= maxOffset && delta > 0)
                        {
                            accumulatedDelta += delta;
                            if (accumulatedDelta > DESC_SCROLL_BUFFER)
                            {
                                scrolling = false;
                                descScrollViewer.ReleaseMouseCapture();
                                currentSwipeMode = SwipeMode.Normal;
                                currentDescScrollViewer = null;

                                if (scrollCounter < animeCount - 1)
                                {
                                    scrollCounter++;
                                    ScrollToOffsetSmooth(scrollCounter * windowHeight);
                                }
                                else if (canLoadMore && !isLoading)
                                {
                                    scrollCounter++;
                                    _ = LoadAnimesAsync(5, true);
                                    ScrollToOffsetSmooth(scrollCounter * windowHeight);
                                }
                            }
                        }
                        else
                        {
                            accumulatedDelta = 0;
                            descScrollViewer.ScrollToVerticalOffset(currentOffset + delta);
                        }

                        startPoint = e.GetPosition(descScrollViewer);
                    }
                };

                descScrollViewer.PreviewMouseLeftButtonUp += (s, e) => {
                    scrolling = false;
                    accumulatedDelta = 0;
                    descScrollViewer.ReleaseMouseCapture();
                };

                // === CONTENU DU SCROLLVIEWER ===
                StackPanel scrollContent = new StackPanel();
                scrollContent.Children.Add(new TextBlock
                {
                    Text = fullDescription,
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 20
                });

                // === BOUTON SEE LESS ===
                TextBlock collapseButton = new TextBlock
                {
                    Text = "See Less",
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 149, 237)),
                    Margin = new Thickness(0, 15, 0, 10),
                    Cursor = Cursors.Hand,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    IsHitTestVisible = true,
                    Background = Brushes.Transparent
                };

                collapseButton.PreviewMouseDown += (s, e) => {
                    if (e.ChangedButton == MouseButton.Left)
                    {
                        e.Handled = true;
                        fullScreenCard.Tag = false;
                        AnimateDescriptionCollapse(imageCard, titleBlock, descShortWrapper, descLongWrapper,
                            descBlockShort, descScrollViewer, statsPanel, expandButton, shortDescHeight);
                    }
                };

                scrollContent.Children.Add(collapseButton);
                descScrollViewer.Content = scrollContent;
                descLongWrapper.Child = descScrollViewer;

                descriptionContainer.Children.Add(descLongWrapper);
                descriptionContainer.Children.Add(expandButton);

                // === HANDLER READ MORE ===
                expandButton.MouseLeftButtonDown += (s, e) => {
                    e.Handled = true;
                    fullScreenCard.Tag = true;
                    currentSwipeMode = SwipeMode.DescriptionScrolling;

                    AnimateDescriptionExpansion(imageCard, titleBlock, descShortWrapper, descLongWrapper,
                        descBlockShort, descScrollViewer, expandButton, statsPanel, fullDescHeight, shortDescHeight);
                };
            }

            infoPanel.Children.Add(descriptionContainer);
        }

        // ===== 🎞️ CORRECTION 2 : Animation Read More synchronisée et sans snap =====
        private void AnimateDescriptionExpansion(Border imageCard, TextBlock titleBlock,
    Border descShortWrapper, Border descLongWrapper, TextBlock descBlockShort,
    ScrollViewer descScrollViewer, TextBlock expandButton, StackPanel statsPanel,
    double fullTextHeight, double shortDescHeight)
        {
            var duration = TimeSpan.FromMilliseconds(650);
            var easing = new QuarticEase { EasingMode = EasingMode.EaseInOut };

            // === CALCUL DES DIMENSIONS ===
            double uiOverheadExpanded = 165;
            double totalAvailableHeight = windowHeight - uiOverheadExpanded;
            double idealImageHeight = totalAvailableHeight - fullTextHeight;

            double targetImageHeight;
            double targetDescHeight;

            if (idealImageHeight >= IMAGE_HEIGHT_NORMAL)
            {
                targetImageHeight = IMAGE_HEIGHT_NORMAL;
                targetDescHeight = fullTextHeight;
            }
            else if (idealImageHeight >= IMAGE_HEIGHT_MIN)
            {
                targetImageHeight = idealImageHeight;
                targetDescHeight = fullTextHeight;
            }
            else
            {
                targetImageHeight = IMAGE_HEIGHT_MIN;
                targetDescHeight = totalAvailableHeight - IMAGE_HEIGHT_MIN;
            }

            double targetImageWidth = targetImageHeight * 0.7;

            // === ANIMATIONS SYNCHRONISÉES ===

            // 1. Fade out du bouton Read More
            DoubleAnimation fadeBtn = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
            expandButton.BeginAnimation(TextBlock.OpacityProperty, fadeBtn);
            expandButton.IsHitTestVisible = false;

            // 2. Positionner l'image
            imageCard.VerticalAlignment = VerticalAlignment.Top;

            // 3. Animation de l'image
            DoubleAnimation widthAnim = new DoubleAnimation(imageCard.Width, targetImageWidth, duration)
            { EasingFunction = easing };
            DoubleAnimation heightAnim = new DoubleAnimation(imageCard.Height, targetImageHeight, duration)
            { EasingFunction = easing };
            ThicknessAnimation marginAnim = new ThicknessAnimation(imageCard.Margin,
                new Thickness(0, 10, 0, 5), duration)
            { EasingFunction = easing };

            imageCard.BeginAnimation(Border.WidthProperty, widthAnim);
            imageCard.BeginAnimation(Border.HeightProperty, heightAnim);
            imageCard.BeginAnimation(Border.MarginProperty, marginAnim);

            // 4. Animation du titre et des stats
            double targetTitleSize = titleBlock.Text.Length > 50 ? 16 :
                                    (titleBlock.Text.Length > 35 ? 18 : 20);
            titleBlock.BeginAnimation(TextBlock.FontSizeProperty,
                new DoubleAnimation(titleBlock.FontSize, targetTitleSize, duration) { EasingFunction = easing });
            titleBlock.BeginAnimation(TextBlock.MarginProperty,
                new ThicknessAnimation(titleBlock.Margin, new Thickness(0, 0, 0, 5), duration)
                { EasingFunction = easing });
            statsPanel.BeginAnimation(StackPanel.MarginProperty,
                new ThicknessAnimation(statsPanel.Margin, new Thickness(0, 0, 0, 15), duration)
                { EasingFunction = easing });

            // 5. ⭐ ANIMATION DES WRAPPERS (la clé de la solution)

            // Réduire le wrapper court de sa hauteur à 0
            DoubleAnimation shortWrapperAnim = new DoubleAnimation(shortDescHeight, 0, duration)
            { EasingFunction = easing };
            descShortWrapper.BeginAnimation(Border.HeightProperty, shortWrapperAnim);

            // Fade out du texte court
            DoubleAnimation shortOpacityAnim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
            descBlockShort.BeginAnimation(TextBlock.OpacityProperty, shortOpacityAnim);

            // Agrandir le wrapper long de 0 à targetDescHeight
            DoubleAnimation longWrapperAnim = new DoubleAnimation(0, targetDescHeight, duration)
            { EasingFunction = easing };
            descLongWrapper.BeginAnimation(Border.HeightProperty, longWrapperAnim);

            // Fade in du ScrollViewer
            DoubleAnimation longOpacityAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400))
            { BeginTime = TimeSpan.FromMilliseconds(200) };
            descScrollViewer.BeginAnimation(ScrollViewer.OpacityProperty, longOpacityAnim);

            // === FINALISATION ===
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = duration };
            timer.Tick += (s, e) => {
                // Cacher définitivement les éléments non utilisés SANS changer Visibility
                expandButton.Visibility = Visibility.Collapsed;

                // Fixer les valeurs finales (arrêter les animations)
                descShortWrapper.BeginAnimation(Border.HeightProperty, null);
                descShortWrapper.Height = 0;  // ← Hauteur = 0, pas Collapsed

                descLongWrapper.BeginAnimation(Border.HeightProperty, null);
                descLongWrapper.Height = targetDescHeight;

                // Enregistrer la référence au ScrollViewer actif
                currentDescScrollViewer = descScrollViewer;
                UpdateDescriptionScrollState();

                timer.Stop();
            };
            timer.Start();
        }
        // ===== FIN CORRECTION 2 =====

        // ===== 🐞 CORRECTION 1 BIS : Fix complet du "See Less" avec animation fluide =====
        private void AnimateDescriptionCollapse(Border imageCard, TextBlock titleBlock,
    Border descShortWrapper, Border descLongWrapper, TextBlock descBlockShort,
    ScrollViewer descScrollViewer, StackPanel statsPanel, TextBlock expandButton,
    double shortDescHeight)
        {
            var duration = TimeSpan.FromMilliseconds(600);
            var easing = new QuarticEase { EasingMode = EasingMode.EaseInOut };

            // Phase 1 : Animation de fermeture du wrapper long
            DoubleAnimation longWrapperAnim = new DoubleAnimation(descLongWrapper.Height, 0,
                TimeSpan.FromMilliseconds(350))
            { EasingFunction = easing };
            DoubleAnimation longOpacityAnim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300))
            { EasingFunction = easing };

            descLongWrapper.BeginAnimation(Border.HeightProperty, longWrapperAnim);
            descScrollViewer.BeginAnimation(ScrollViewer.OpacityProperty, longOpacityAnim);

            var timer = new System.Windows.Threading.DispatcherTimer
            { Interval = TimeSpan.FromMilliseconds(350) };

            // Capturer les valeurs actuelles
            double currentImageWidth = imageCard.ActualWidth;
            double currentImageHeight = imageCard.ActualHeight;
            Thickness currentImageMargin = imageCard.Margin;
            double currentTitleSize = titleBlock.FontSize;
            Thickness currentTitleMargin = titleBlock.Margin;
            Thickness currentStatsMargin = statsPanel.Margin;

            timer.Tick += (s, e) => {
                // Phase 2 : Préparer le retour
                descScrollViewer.ScrollToVerticalOffset(0);

                // Fixer la hauteur du wrapper long à 0
                descLongWrapper.BeginAnimation(Border.HeightProperty, null);
                descLongWrapper.Height = 0;

                // Repositionner l'image
                imageCard.VerticalAlignment = VerticalAlignment.Center;

                // Animation de restauration de l'image
                DoubleAnimation restoreWidthAnim = new DoubleAnimation(currentImageWidth, 320, duration)
                { EasingFunction = easing };
                DoubleAnimation restoreHeightAnim = new DoubleAnimation(currentImageHeight,
                    IMAGE_HEIGHT_NORMAL, duration)
                { EasingFunction = easing };
                ThicknessAnimation restoreMarginAnim = new ThicknessAnimation(currentImageMargin,
                    new Thickness(0, 20, 0, 20), duration)
                { EasingFunction = easing };

                imageCard.BeginAnimation(Border.WidthProperty, restoreWidthAnim);
                imageCard.BeginAnimation(Border.HeightProperty, restoreHeightAnim);
                imageCard.BeginAnimation(Border.MarginProperty, restoreMarginAnim);

                // Animation de restauration du titre et des stats
                double originalTitleSize = titleBlock.Text.Length > 50 ? 22 :
                                          (titleBlock.Text.Length > 35 ? 24 : 28);
                titleBlock.BeginAnimation(TextBlock.FontSizeProperty,
                    new DoubleAnimation(currentTitleSize, originalTitleSize, duration)
                    { EasingFunction = easing });
                titleBlock.BeginAnimation(TextBlock.MarginProperty,
                    new ThicknessAnimation(currentTitleMargin, new Thickness(0, 0, 0, 10), duration)
                    { EasingFunction = easing });
                statsPanel.BeginAnimation(StackPanel.MarginProperty,
                    new ThicknessAnimation(currentStatsMargin, new Thickness(0, 0, 0, 15), duration)
                    { EasingFunction = easing });

                // ⭐ Restaurer le wrapper court (de 0 à sa hauteur)
                DoubleAnimation shortWrapperAnim = new DoubleAnimation(0, shortDescHeight, duration)
                { EasingFunction = easing };
                descShortWrapper.BeginAnimation(Border.HeightProperty, shortWrapperAnim);

                // Fade in du texte court
                descBlockShort.BeginAnimation(TextBlock.OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));

                // Réafficher le bouton Read More
                expandButton.Visibility = Visibility.Visible;
                expandButton.BeginAnimation(TextBlock.OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));
                expandButton.IsHitTestVisible = true;

                // Réinitialiser l'état du swipe
                currentDescScrollViewer = null;
                currentSwipeMode = SwipeMode.Normal;
                UpdateDescriptionScrollState();

                timer.Stop();
            };
            timer.Start();
        }
        // ===== FIN CORRECTION 1 BIS =====

        private Ellipse CreateStatusIndicator(string status)
        {
            Color c, g;
            if (status == "RELEASING") { c = Color.FromRgb(0, 230, 0); g = Color.FromRgb(0, 255, 0); }
            else if (status == "NOT_YET_RELEASED") { c = Color.FromRgb(230, 0, 0); g = Color.FromRgb(255, 0, 0); }
            else return null;

            Ellipse e = new Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = new SolidColorBrush(c),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Effect = new DropShadowEffect
                {
                    Color = g,
                    BlurRadius = 8,
                    Opacity = 1,
                    ShadowDepth = 0,
                    Direction = 0
                }
            };
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

                // Afficher la zone de loading seulement si on scroll assez loin
                if (scrollCounter >= animeCount - 1 && canLoadMore && !isLoading && delta > 50 && !isLoadingAreaVisible)
                {
                    LoadingArea.Visibility = Visibility.Visible;
                    isLoadingAreaVisible = true;
                }

                MainScrollViewer.ScrollToVerticalOffset(Math.Min(off, Math.Max(0, (animeCount - 1) * windowHeight) + (scrollCounter >= animeCount - 1 ? 400 : 0)));
            }
        }

        private void AnimeContainer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            scrollStartPos = (float)e.GetPosition(this).Y;
            isDragging = true;
            AnimeContainer.CaptureMouse();
        }

        // ===== 🖐️ CORRECTION 3 BIS + 5 : Seuil adaptatif + animation de retour toujours fluide =====
        private void AnimeContainer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!isDragging) return;
            isDragging = false;
            AnimeContainer.ReleaseMouseCapture();

            double swipeDistance = scrollStartPos - e.GetPosition(this).Y;

            // Déterminer si la description actuelle est ouverte
            bool isCurrentDescriptionExpanded = false;
            if (scrollCounter >= 0 && scrollCounter < AnimeContainer.Children.Count)
            {
                var currentCard = AnimeContainer.Children[scrollCounter] as Grid;
                if (currentCard != null && currentCard.Tag is bool expanded)
                {
                    isCurrentDescriptionExpanded = expanded;
                }
            }

            // Utiliser le seuil adaptatif
            double threshold = isCurrentDescriptionExpanded ? SCROLL_THRESHOLD_EXPANDED : SCROLL_THRESHOLD_NORMAL;
            double r = swipeDistance / windowHeight;

            if (r >= threshold)
            {
                if (scrollCounter < animeCount - 1)
                    scrollCounter++;
                else if (canLoadMore && !isLoading)
                    _ = LoadAnimesAsync(5, true);
            }
            else if (r <= -threshold)
                scrollCounter = Math.Max(0, scrollCounter - 1);

            // === FIX CORRECTION 5 : Animation de retour TOUJOURS fluide ===
            // Si la zone de loading est visible, toujours animer le retour
            if (isLoadingAreaVisible && !isLoading)
            {
                LoadingArea.Visibility = Visibility.Collapsed;
                isLoadingAreaVisible = false;
            }

            // Animation fluide dans TOUS les cas (scroll partiel ou complet)
            ScrollToOffsetSmooth(Math.Min(scrollCounter, Math.Max(0, animeCount - 1)) * windowHeight);
        }
        // ===== FIN CORRECTION 3 BIS + 5 =====

        private void UpdateDescriptionScrollState()
        {
            if (currentDescScrollViewer == null)
            {
                currentSwipeMode = SwipeMode.Normal;
                return;
            }

            double offset = currentDescScrollViewer.VerticalOffset;
            double max = currentDescScrollViewer.ScrollableHeight;

            // Tant que le texte peut encore scroller → on bloque le swipe carte
            if (offset > 0 && offset < max)
            {
                currentSwipeMode = SwipeMode.DescriptionScrolling;
            }
            else
            {
                // On est tout en haut ou tout en bas → swipe carte autorisé au prochain geste
                currentSwipeMode = SwipeMode.Normal;
            }
        }

    }

    public static class ScrollViewerBehavior
    {
        public static readonly DependencyProperty VerticalOffsetProperty = DependencyProperty.RegisterAttached("VerticalOffset", typeof(double), typeof(ScrollViewerBehavior), new UIPropertyMetadata(0.0, (d, e) => ((ScrollViewer)d).ScrollToVerticalOffset((double)e.NewValue)));
        public static void SetVerticalOffset(DependencyObject t, double v) => t.SetValue(VerticalOffsetProperty, v);
        public static double GetVerticalOffset(DependencyObject t) => (double)t.GetValue(VerticalOffsetProperty);
    }
}
