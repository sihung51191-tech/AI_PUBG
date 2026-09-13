using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Aimmy2.Theme;
using Other;

namespace Aimmy2.Controls
{
    public partial class AboutMenuControl : UserControl
    {
        private MainWindow? _mainWindow;
        private bool _isInitialized;
        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(5) };

        private FontFamily? _fontFamily;

        // Credits data - easy to add/remove people
        private static readonly (string name, string role, string? github)[] CoreTeam =
        {
            ("Babyhamsta", "Logic AI", "Babyhamsta"),
            ("MarsQQ", "Thiết kế", "MarsInsanity"),
            ("Taylor", "Tối ưu hóa", "TaylorIsBlue")
        };

        private static readonly (string name, string? github, bool highlighted)[] Contributors =
        {
            ("Whoswhip", "whoswhip", true),
            ("Camilia2o7", "Camilia2o7", true),
            ("Shall0e", null, false),
            ("Wisethef0x", null, false),
            ("HakaCat", null, false),
            ("Themida", null, false),
            ("Ninja", null, false)
        };

        // Public properties for MainWindow access
        public Label AboutSpecsControl => AboutSpecs;
        public ScrollViewer AboutMenuScrollViewer => AboutMenu;

        public AboutMenuControl()
        {
            InitializeComponent();
            Loaded += (_, _) => global::Other.UiLanguage.RefreshTree(this);
        }

        public void Initialize(MainWindow mainWindow)
        {
            if (_isInitialized) return;

            _mainWindow = mainWindow;
            _isInitialized = true;

            _fontFamily = Application.Current.TryFindResource("Atkinson Hyperlegible") as FontFamily
                ?? new FontFamily("Segoe UI"); // Fallback font

            LoadCoreTeam();
            LoadContributors();
        }

        private void LoadCoreTeam()
        {
            CoreTeamPanel.Children.Clear();

            foreach (var (name, role, github) in CoreTeam)
            {
                var panel = CreateCoreTeamMember(name, role, github);
                CoreTeamPanel.Children.Add(panel);
            }
        }

        private StackPanel CreateCoreTeamMember(string name, string role, string? github)
        {
            var panel = new StackPanel
            {
                Margin = new Thickness(8, 0, 8, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            // Avatar container
            var avatarBorder = new Border
            {
                Width = 48,
                Height = 48,
                CornerRadius = new CornerRadius(24),
                Margin = new Thickness(0, 0, 0, 8),
                ClipToBounds = true
            };
            avatarBorder.SetResourceReference(Border.BackgroundProperty, "ThemePrimaryAction");

            // Fallback text (first letter)
            var fallbackText = new TextBlock
            {
                Text = name[0].ToString().ToUpper(),
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            fallbackText.SetResourceReference(TextBlock.ForegroundProperty, "ThemePrimaryActionForeground");
            avatarBorder.Child = fallbackText;

            // Try to load GitHub avatar
            if (!string.IsNullOrEmpty(github))
            {
                LoadGitHubAvatar(github, avatarBorder, fallbackText);
            }

            panel.Children.Add(avatarBorder);

            // Name
            var nameText = new TextBlock
            {
                Text = name,
                FontFamily = _fontFamily,
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            nameText.SetResourceReference(TextBlock.ForegroundProperty, "ThemeTextPrimary");

            // Make clickable if has GitHub
            if (!string.IsNullOrEmpty(github))
            {
                nameText.Cursor = Cursors.Hand;
                nameText.MouseEnter += (s, e) => nameText.TextDecorations = TextDecorations.Underline;
                nameText.MouseLeave += (s, e) => nameText.TextDecorations = null;
                nameText.MouseLeftButtonUp += (s, e) => OpenGitHubProfile(github);
                avatarBorder.Cursor = Cursors.Hand;
                avatarBorder.MouseLeftButtonUp += (s, e) => OpenGitHubProfile(github);
            }

            panel.Children.Add(nameText);

            // Role
            var roleText = new TextBlock
            {
                Text = role,
                FontFamily = _fontFamily,
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            roleText.SetResourceReference(TextBlock.ForegroundProperty, "ThemeTextSecondary");
            panel.Children.Add(roleText);

            return panel;
        }

        private async void LoadGitHubAvatar(string username, Border avatarBorder, TextBlock fallbackText)
        {
            try
            {
                var imageUrl = $"https://github.com/{username}.png?size=96";
                var response = await _httpClient.GetAsync(imageUrl);

                if (response.IsSuccessStatusCode)
                {
                    var imageData = await response.Content.ReadAsByteArrayAsync();

                    await Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            var bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.StreamSource = new System.IO.MemoryStream(imageData);
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.EndInit();
                            bitmap.Freeze();

                            var image = new Ellipse
                            {
                                Width = 48,
                                Height = 48,
                                Fill = new ImageBrush(bitmap)
                                {
                                    Stretch = Stretch.UniformToFill
                                }
                            };

                            avatarBorder.Background = Brushes.Transparent;
                            avatarBorder.Child = image;
                        }
                        catch
                        {
                            // Keep fallback text on error
                        }
                    });
                }
            }
            catch
            {
                // Keep fallback text on error
            }
        }

        private void LoadContributors()
        {
            HighlightedContributorsPanel.Children.Clear();
            ContributorsPanel.Children.Clear();

            foreach (var (name, github, highlighted) in Contributors)
            {
                var chip = CreateContributorChip(name, github, highlighted);

                if (highlighted)
                    HighlightedContributorsPanel.Children.Add(chip);
                else
                    ContributorsPanel.Children.Add(chip);
            }
        }

        private Border CreateContributorChip(string name, string? github, bool highlighted)
        {
            var border = new Border
            {
                CornerRadius = new CornerRadius(highlighted ? 12 : 10),
                Padding = new Thickness(highlighted ? 12 : 10, highlighted ? 6 : 5, highlighted ? 12 : 10, highlighted ? 6 : 5),
                Margin = new Thickness(highlighted ? 4 : 3),
                BorderThickness = new Thickness(1)
            };
            border.SetResourceReference(Border.BackgroundProperty, highlighted ? "ThemePrimaryAction" : "ThemeSurfaceElevated");
            border.SetResourceReference(Border.BorderBrushProperty, "ThemeOutline");

            var text = new TextBlock
            {
                Text = name,
                FontFamily = _fontFamily,
                FontSize = highlighted ? 11 : 10,
            };
            text.SetResourceReference(TextBlock.ForegroundProperty, highlighted ? "ThemePrimaryActionForeground" : "ThemeTextPrimary");

            border.Child = text;

            // Make clickable if has GitHub
            if (!string.IsNullOrEmpty(github))
            {
                border.Cursor = Cursors.Hand;
                border.MouseEnter += (s, e) =>
                {
                    border.SetResourceReference(Border.BackgroundProperty, highlighted ? "ThemeSecondaryAction" : "ThemeSurfaceRaised");
                    text.SetResourceReference(TextBlock.ForegroundProperty, highlighted ? "ThemeSecondaryActionForeground" : "ThemeTextPrimary");
                };
                border.MouseLeave += (s, e) =>
                {
                    border.SetResourceReference(Border.BackgroundProperty, highlighted ? "ThemePrimaryAction" : "ThemeSurfaceElevated");
                    text.SetResourceReference(TextBlock.ForegroundProperty, highlighted ? "ThemePrimaryActionForeground" : "ThemeTextPrimary");
                };
                border.MouseLeftButtonUp += (s, e) => OpenGitHubProfile(github);
            }

            return border;
        }

        private static void OpenGitHubProfile(string username)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = $"https://github.com/{username}",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var updateManager = new UpdateManager();
                await updateManager.CheckForUpdate(AboutDesc.Content?.ToString() ?? "");
                updateManager.Dispose();
            }
            catch { }
        }

        private void GitHubButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/Babyhamsta/Aimmy",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private void DiscordButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://discord.gg/aimmy",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private void VersionBorder_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                var version = AboutDesc.Content?.ToString()?.TrimStart('v') ?? "2.5.0";
                Process.Start(new ProcessStartInfo
                {
                    FileName = $"https://github.com/Babyhamsta/Aimmy/releases/tag/v{version}",
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }
}
