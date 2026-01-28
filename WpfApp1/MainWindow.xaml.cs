using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace AnimeScrollApp
{
    public partial class MainWindow : Window
    {
        private static readonly HttpClient client = new HttpClient();
        private Random random = new Random();
        private bool isLoading = false;
        private int animeCount = 0;
        private double windowHeight;

        public MainWindow()
        {
            InitializeComponent();
            windowHeight = this.Height;

            // Charger les premiers animes
            LoadAnimes(3);
        }

        private async void LoadAnimes(int count)
        {
            if (isLoading) return;

            isLoading = true;
            LoadingIndicator.Visibility = Visibility.Visible;

            for (int i = 0; i < count; i++)
            {
                try
                {
                    int randomPage = random.Next(1, 500);

                    var query = @"
                    query ($page: Int) {
                        Page(page: $page, perPage: 1) {
                            media(type: ANIME, sort: POPULARITY_DESC) {
                                id
                                title {
                                    romaji
                                    english
                                }
                                coverImage {
                                    extraLarge
                                    large
                                }
                                bannerImage
                                averageScore
                                genres
                                episodes
                                description
                                season
                                seasonYear
                            }
                        }
                    }";

                    var request = new
                    {
                        query = query,
                        variables = new { page = randomPage }
                    };

                    var json = Newtonsoft.Json.JsonConvert.SerializeObject(request);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    var response = await client.PostAsync("https://graphql.anilist.co", content);

                    if (!response.IsSuccessStatusCode) continue;

                    var jsonResponse = await response.Content.ReadAsStringAsync();
                    JObject data = JObject.Parse(jsonResponse);

                    var page = data["data"]?["Page"];
                    if (page == null) continue;

                    var mediaArray = page["media"];
                    if (mediaArray == null || !mediaArray.HasValues) continue;

                    var media = mediaArray[0];
                    if (media == null) continue;

                    CreateFullScreenAnimeCard(media);
                    animeCount++;
                    AnimeCounter.Text = animeCount + " animes";
                }
                catch (Exception)
                {
                    continue;
                }
            }

            LoadingIndicator.Visibility = Visibility.Collapsed;
            isLoading = false;
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

                string score = media["averageScore"]?.ToString() ?? "N/A";
                string episodes = media["episodes"]?.ToString() ?? "?";
                string description = media["description"]?.ToString() ?? "";
                string season = media["season"]?.ToString() ?? "";
                string year = media["seasonYear"]?.ToString() ?? "";

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
                    Stretch = Stretch.UniformToFill
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
                    TextBlock seasonBlock = new TextBlock
                    {
                        Text = season + " " + year,
                        FontSize = 14,
                        Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                        Margin = new Thickness(0, 0, 0, 5)
                    };
                    infoPanel.Children.Add(seasonBlock);
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
                    Text = "⭐ " + score,
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

        private void MainScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            var scrollViewer = sender as ScrollViewer;

            if (scrollViewer != null && scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight * 0.8)
            {
                if (!isLoading)
                {
                    LoadAnimes(2);
                }
            }
        }

        private void MainScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var scrollViewer = sender as ScrollViewer;
            if (scrollViewer != null)
            {
                double offset = scrollViewer.VerticalOffset - (e.Delta * 2);
                scrollViewer.ScrollToVerticalOffset(offset);
                e.Handled = true;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void AnimeContainer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            
        }

        private void AnimeContainer_MouseEnter(object sender, MouseEventArgs e)
        {
      
        }

        private void AnimeContainer_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                System.Windows.Point position = e.GetPosition(this);
                double pX = position.X;
                double pY = position.Y;

                Console.WriteLine($"Mouse clicked at X: {pX}, Y: {pY}");
            }
        }
    }
}