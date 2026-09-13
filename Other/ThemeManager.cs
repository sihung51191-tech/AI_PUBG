using Newtonsoft.Json;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Aimmy2.Theme
{
    public static class ThemeManager
    {
        // Theme changed event
        public static event EventHandler<Color> ThemeChanged;

        // Cached theme colors
        private static Color _themeColor = Color.FromRgb(114, 46, 209);
        private static Color _themeColorDark;
        private static Color _themeColorLight;
        private static Color _themeGradientDark;
        private static Color _themeColorTransparent;
        private static Color _themeColorSemiTransparent;
        private static Color _themeSurface;
        private static Color _themeSurfaceRaised;
        private static Color _themeSurfaceElevated;
        private static Color _themeOutline;
        private static Color _themeTextPrimary;
        private static Color _themeTextSecondary;
        private static Color _themeFocusRing;
        private static Color _themePrimaryAction;
        private static Color _themePrimaryActionForeground;
        private static Color _themeSecondaryAction;
        private static Color _themeSecondaryActionForeground;
        private static Color _themeTertiaryAction;
        private static Color _themeTertiaryActionForeground;
        private static Color _themeDangerAction;
        private static Color _themeDangerActionForeground;

        // Cache of themed elements for performance
        private static readonly Dictionary<WeakReference, List<ThemeElementInfo>> _themedElements = new Dictionary<WeakReference, List<ThemeElementInfo>>();
        private static readonly DispatcherTimer _cleanupTimer;
        #region Media
        //Eclude windows such as esp/fov/notice/setatni/etc.. To use this one, make sure you use this ("ThemeManager.ExcludeWindowFromBackground(this);")
        private static readonly HashSet<Window> _excludedWindows = new HashSet<Window>();
        //Track the windows using MainBorder and apply the background to it, you need ("ThemeManager.TrackWindow(this);")
        private static readonly List<Window> _trackedWindows = new List<Window>();
        // too much testing/failing, so were sticking to last resort.
        private static readonly Dictionary<Window, Border> _windowBrightnessOverlays = new Dictionary<Window, Border>();
        #endregion
        // Theme element tags
        public const string THEME_TAG = "Theme";
        public const string THEME_DARK_TAG = "ThemeDark";
        public const string THEME_LIGHT_TAG = "ThemeLight";
        public const string THEME_GRADIENT_TAG = "ThemeGradient";
        public const string THEME_TRANSPARENT_TAG = "ThemeTransparent";
        public const string THEME_SEMI_TRANSPARENT_TAG = "ThemeSemiTransparent";

        static ThemeManager()
        {
            // Initialize colors
            CalculateThemeColors(_themeColor);

            // Setup cleanup timer for weak references
            _cleanupTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(5)
            };
            _cleanupTimer.Tick += CleanupDeadReferences;
            _cleanupTimer.Start();

            // Initialize dynamic resources when application starts
            if (Application.Current != null)
            {
                Application.Current.Activated += (s, e) =>
                {
                    // Update resources on first activation
                    UpdateDynamicResources();
                    UpdateMainWindowGradients();
                };
            }
        }

        public static Color ThemeColor => _themeColor;
        public static Color ThemeColorDark => _themeColorDark;
        public static Color ThemeColorLight => _themeColorLight;
        public static Color ThemeGradientDark => _themeGradientDark;
        public static Color ThemeSurface => _themeSurface;
        public static Color ThemeSurfaceRaised => _themeSurfaceRaised;
        public static Color ThemeSurfaceElevated => _themeSurfaceElevated;
        public static Color ThemeOutline => _themeOutline;
        public static Color ThemeTextPrimary => _themeTextPrimary;
        public static Color ThemeTextSecondary => _themeTextSecondary;
        public static Color ThemeFocusRing => _themeFocusRing;
        public static Color ThemePrimaryAction => _themePrimaryAction;
        public static Color ThemePrimaryActionForeground => _themePrimaryActionForeground;
        public static Color ThemeSecondaryAction => _themeSecondaryAction;
        public static Color ThemeSecondaryActionForeground => _themeSecondaryActionForeground;
        public static Color ThemeTertiaryAction => _themeTertiaryAction;
        public static Color ThemeTertiaryActionForeground => _themeTertiaryActionForeground;
        public static Color ThemeDangerAction => _themeDangerAction;
        public static Color ThemeDangerActionForeground => _themeDangerActionForeground;
        #region Media
        private static ImageBrush _mediaBackgroundBrush;
        private static string _currentMediaPath;
        private static bool _isMediaBackground = false;
        public static bool IsMediaBackground => _isMediaBackground;
        public static string CurrentMediaPath => _currentMediaPath;
        private static double _mediaBrightness = 1.0;
        private const string MediaConfigPath = "bin\\media.cfg";
        private static string _mediaConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, MediaConfigPath);
        #endregion
        /// <summary>
        /// Sets the base theme color and updates all registered elements
        /// </summary>
        public static void SetThemeColor(Color baseColor)
        {
            bool colorChanged = _themeColor != baseColor;
            _themeColor = baseColor;
            CalculateThemeColors(baseColor);

            // Update all themed elements EXCEPT the background if media is active
            UpdateAllThemedElements(skipBackground: _isMediaBackground);

            // Update MainWindow gradients (only if no media background)
            UpdateWindowBackgrounds();

            // Update MainWindow gradients
            UpdateMainWindowGradients();

            // Update dynamic resources
            UpdateDynamicResources();

            // Raise theme changed event
            if (colorChanged)
            {
                ThemeChanged?.Invoke(null, baseColor);
            }
        }

        /// <summary>
        /// Sets the theme color from hex string
        /// </summary>
        public static void SetThemeColor(string hexColor)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hexColor);
                SetThemeColor(color);
            }
            catch (Exception ex)
            {
                // Log error or use default color
            }
        }
        #region Window Media Logic to apply/exclude
        public static void SetMediaBackground(string filePath, double brightness = 1.0)
        {
            try
            {
                _isMediaBackground = true;
                _currentMediaPath = filePath;
                _mediaBrightness = brightness;

                // Load the media
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(filePath);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();

                _mediaBackgroundBrush = new ImageBrush(bitmap)
                {
                    Stretch = Stretch.Fill,
                    //If the stretch is too much, use this one.
                    //Stretch = Stretch.UniformToFill,
                };

                // Save settings
                SaveMediaSettings(filePath, brightness);

                UpdateWindowBackgrounds();
            }
            catch
            {
                ClearMediaBackground();
            }
        }
        #region Saving/Loading Media Image
        private static void SaveMediaSettings(string path, double brightness)
        {
            try
            {
                string fileName = Path.GetFileName(path);
                var settings = new Dictionary<string, string>
            {
            { "MediaFile", fileName },
            { "Brightness", brightness.ToString(CultureInfo.InvariantCulture) }
            };
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "media.cfg"),
                    JsonConvert.SerializeObject(settings));
            }
            catch { }
        }

        public static void LoadMediaSettings()
        {
            try
            {
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "media.cfg");
                if (File.Exists(configPath))
                {
                    var settings = JsonConvert.DeserializeObject<Dictionary<string, string>>(
                        File.ReadAllText(configPath));
                    if (settings.TryGetValue("MediaFile", out var fileName) &&
                        !string.IsNullOrEmpty(fileName))
                    {
                        string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "media", fileName);
                        if (File.Exists(filePath))
                        {
                            double brightness = 1.0;
                            if (settings.TryGetValue("Brightness", out var brightnessStr))
                            {
                                double.TryParse(brightnessStr, NumberStyles.Any, CultureInfo.InvariantCulture, out brightness);
                            }
                            SetMediaBackground(filePath, brightness);
                        }
                    }
                }
            }
            catch { }
        }

        public static void InitializeMediaBackground()
        {
            try
            {
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "media.cfg");
                if (File.Exists(configPath))
                {
                    var settings = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(configPath));
                    if (settings.TryGetValue("MediaFile", out var fileName) && !string.IsNullOrEmpty(fileName))
                    {
                        string mediaPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "media", fileName);
                        if (File.Exists(mediaPath))
                        {
                            double brightness = 1.0;
                            if (settings.TryGetValue("Brightness", out var brightnessStr))
                            {
                                brightness = double.Parse(brightnessStr, CultureInfo.InvariantCulture);
                            }
                            SetMediaBackground(mediaPath, brightness);
                        }
                    }
                }
            }
            catch { }
        }
        #endregion
        private static LinearGradientBrush CreateThemeGradientBrush(Window window)
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0.5, 1),
                GradientStops = new GradientStopCollection
            {
                new GradientStop(Colors.Black, 0.27),
                new GradientStop(_themeGradientDark, 1)
            }
            };
            if (window.FindName("RotaryGradient") is RotateTransform rotaryTransform)
            {
                brush.RelativeTransform = new TransformGroup
                {
                    Children = new TransformCollection
            {
                new ScaleTransform { CenterX = 0.5, CenterY = 0.5 },
                new SkewTransform { CenterX = 0.5, CenterY = 0.5 },
                rotaryTransform,
                new TranslateTransform()
            }
                };
            }
            else
            {
                brush.RelativeTransform = new TransformGroup
                {
                    Children = new TransformCollection
            {
                new ScaleTransform { CenterX = 0.5, CenterY = 0.5 },
                new SkewTransform { CenterX = 0.5, CenterY = 0.5 },
                new RotateTransform { Angle = 0, CenterX = 0.5, CenterY = 0.5 },
                new TranslateTransform()
            }
                };
            }

            return brush;
        }
        public static void UpdateWindowBackgrounds(Color? color = null)
        {
            if (Application.Current == null) return;
            foreach (var window in _trackedWindows.ToArray())
            {
                try
                {
                    window.Dispatcher.Invoke(() => UpdateWindowBackground(window));
                }
                catch { }
            }
            foreach (Window window in Application.Current.Windows)
            {
                if (!_trackedWindows.Contains(window) && !_excludedWindows.Contains(window))
                {
                    try
                    {
                        window.Dispatcher.Invoke(() => UpdateWindowBackground(window));
                        _trackedWindows.Add(window);
                        window.Closed += (s, e) => _trackedWindows.Remove(window);
                    }
                    catch { }
                }
            }
        }
        //too much reading so ima make my own
        public static void TrackWindow(Window window)
        {
            if (!_trackedWindows.Contains(window))
            {
                _trackedWindows.Add(window);
                window.Closed += (s, e) =>
                {
                    _trackedWindows.Remove(window);
                    CleanupWindowBrightnessOverlay(window);
                };
                UpdateWindowBackground(window);
            }
        }
        // Js to exclude fov/esp/etc
        public static void ExcludeWindowFromBackground(Window window)
        {
            if (window != null)
            {
                _excludedWindows.Add(window);
                window.Closed += (s, e) => _excludedWindows.Remove(window);
            }
        }
        private static void UpdateWindowBackground(Window window)
        {
            if (window == null || window.Dispatcher == null)
                return;

            window.Dispatcher.Invoke(() =>
            {
                try
                {
                    if (_excludedWindows.Contains(window))
                        return;

                    CleanupWindowBrightnessOverlay(window);

                    var border = window.FindName("MainBorder") as Border;
                    if (border != null)
                    {
                        if (_isMediaBackground && _mediaBackgroundBrush != null)
                        {
                            border.Background = ApplyBrightnessToBrush(_mediaBackgroundBrush, _mediaBrightness);
                        }
                        else
                        {
                            border.Background = CreateThemeGradientBrush(window);
                        }
                        return;
                    }
                    if (window.Content is Grid rootGrid)
                    {
                        rootGrid.Background = _isMediaBackground && _mediaBackgroundBrush != null
                            ? ApplyBrightnessToBrush(_mediaBackgroundBrush, _mediaBrightness)
                            : Brushes.Transparent;
                    }
                    else
                    {
                        window.Background = _isMediaBackground && _mediaBackgroundBrush != null
                            ? ApplyBrightnessToBrush(_mediaBackgroundBrush, _mediaBrightness)
                            : Brushes.Transparent;
                    }
                }
                catch
                {

                }
            });
        }
        private static Brush ApplyBrightnessToBrush(Brush originalBrush, double brightness)
        {
            if (originalBrush is ImageBrush imageBrush)
            {
                var baseBrush = new ImageBrush(imageBrush.ImageSource.Clone())
                {
                    Stretch = imageBrush.Stretch,
                    AlignmentX = imageBrush.AlignmentX,
                    AlignmentY = imageBrush.AlignmentY,
                    TileMode = imageBrush.TileMode,
                    Viewport = imageBrush.Viewport,
                    ViewportUnits = imageBrush.ViewportUnits,
                    Opacity = 1.0
                };
                var bmp = baseBrush.ImageSource as BitmapSource;
                if (bmp == null)
                    return baseBrush;
                var rect = new Rect(0, 0, bmp.PixelWidth, bmp.PixelHeight);
                Color overlayColor;
                if (brightness < 1.0)
                {
                    byte alpha = (byte)(255 * Math.Sqrt(1.0 - brightness));
                    overlayColor = Color.FromArgb(alpha, 0, 0, 0);
                }
                else if (brightness > 1.0)
                {
                    byte alpha = (byte)(255 * Math.Min(1.0, (brightness - 1.0) * 0.7));
                    overlayColor = Color.FromArgb(alpha, 255, 255, 255);
                }
                else
                {
                    return baseBrush;
                }
                var overlayBrush = new SolidColorBrush(overlayColor);
                var drawingGroup = new DrawingGroup();
                drawingGroup.Children.Add(new ImageDrawing(baseBrush.ImageSource, rect));
                drawingGroup.Children.Add(new GeometryDrawing(overlayBrush, null, new RectangleGeometry(rect)));
                var combinedBrush = new DrawingBrush(drawingGroup)
                {
                    Stretch = baseBrush.Stretch,
                    AlignmentX = baseBrush.AlignmentX,
                    AlignmentY = baseBrush.AlignmentY,
                    TileMode = baseBrush.TileMode,
                    Viewport = baseBrush.Viewport,
                    ViewportUnits = baseBrush.ViewportUnits,
                    Opacity = 1.0
                };
                return combinedBrush;
            }
            return originalBrush;
        }
        public static void UpdateMediaBrightness(double brightness)
        {
            if (_isMediaBackground)
            {
                _mediaBrightness = brightness;
                if (!string.IsNullOrEmpty(_currentMediaPath))
                {
                    string fileName = Path.GetFileName(_currentMediaPath);
                    var settings = new Dictionary<string, string>
            {
                { "MediaFile", fileName },
                { "Brightness", brightness.ToString(CultureInfo.InvariantCulture) }
            };
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "media.cfg"),
                        JsonConvert.SerializeObject(settings));
                }
                UpdateWindowBackgrounds();
            }
        }
        public static double MediaBrightness
        {
            get => _mediaBrightness;
            set
            {
                _mediaBrightness = value;
                UpdateWindowBackgrounds();
            }
        }
        #endregion

        #region Media Cleaning Code - KMP
        public static void ClearMediaBackground()
        {
            _isMediaBackground = false;
            _currentMediaPath = null;
            _mediaBackgroundBrush = null;
            _mediaBrightness = 1.0;
            try { File.Delete(_mediaConfigPath); } catch { }

            foreach (var kvp in _windowBrightnessOverlays.ToList())
            {
                if (kvp.Value.Parent is Panel parent)
                {
                    parent.Children.Remove(kvp.Value);
                }
            }
            _windowBrightnessOverlays.Clear();
            foreach (var window in _trackedWindows.ToArray())
            {
                try
                {
                    window.Dispatcher.Invoke(() =>
                    {
                        if (window.FindName("MainBorder") is Border border)
                        {
                            border.MouseMove -= RotateGradientBasedOnMouse;
                            border.Background = CreateThemeGradientBrush(window);
                        }
                        else if (window.Content is Grid rootGrid)
                        {
                            rootGrid.Background = Brushes.Transparent;
                        }
                        else
                        {
                            window.Background = Brushes.Transparent;
                        }
                    });
                }
                catch { }
            }
            UpdateAllThemedElements();
            UpdateDynamicResources();
            UpdateMainWindowGradients();
        }
        public static void RotateGradientBasedOnMouse(object sender, MouseEventArgs e)
        {
            if (sender is Border border && border.Background is LinearGradientBrush gradientBrush)
            {
                if (gradientBrush.RelativeTransform is TransformGroup transformGroup)
                {
                    var rotateTransform = transformGroup.Children.OfType<RotateTransform>().FirstOrDefault();
                    if (rotateTransform != null)
                    {
                        var mousePos = e.GetPosition(border);
                        var centerX = border.ActualWidth / 2;
                        var centerY = border.ActualHeight / 2;
                        double angle = Math.Atan2(mousePos.Y - centerY, mousePos.X - centerX) * (180 / Math.PI);
                        rotateTransform.Angle = angle;
                    }
                }
            }
        }
        // -testing the cleanup.
        private static void CleanupWindowBrightnessOverlay(Window window)
        {
            if (_windowBrightnessOverlays.ContainsKey(window))
            {
                var overlay = _windowBrightnessOverlays[window];
                if (overlay.Parent is Panel parent)
                {
                    parent.Children.Remove(overlay);
                }
                _windowBrightnessOverlays.Remove(window);
            }
        }
        #endregion
        /// <summary>
        /// Registers an element to be themed
        /// </summary>
        public static void RegisterElement(FrameworkElement element)
        {
            if (element == null) return;

            var weakRef = new WeakReference(element);
            var elementInfoList = new List<ThemeElementInfo>();

            // Check for Theme attached property
            string themeValue = ThemeBehavior.GetTheme(element);
            if (!string.IsNullOrEmpty(themeValue))
            {
                elementInfoList.Add(new ThemeElementInfo
                {
                    PropertyPath = GetPropertyPathFromTag(themeValue),
                    ThemeType = GetThemeTypeFromTag(themeValue)
                });
            }
            // Check element's tag
            else if (element.Tag is string tag && tag.StartsWith("Theme"))
            {
                elementInfoList.Add(new ThemeElementInfo
                {
                    PropertyPath = GetPropertyPathFromTag(tag),
                    ThemeType = GetThemeTypeFromTag(tag)
                });
            }

            // Check for themed children
            FindThemedChildren(element, elementInfoList);

            if (elementInfoList.Count > 0)
            {
                _themedElements[weakRef] = elementInfoList;

                // Apply theme immediately
                ApplyThemeToElement(element, elementInfoList);
            }

            // If this is the MainWindow, update gradients and resources
            if (element == Application.Current?.MainWindow)
            {
                UpdateDynamicResources();
                UpdateMainWindowGradients();
            }
        }

        /// <summary>
        /// Unregisters an element from theming
        /// </summary>
        public static void UnregisterElement(FrameworkElement element)
        {
            var toRemove = _themedElements.Keys
                .Where(wr => wr.IsAlive && wr.Target == element)
                .ToList();

            foreach (var wr in toRemove)
            {
                _themedElements.Remove(wr);
            }
        }

        /// <summary>
        /// Manually update all themed elements
        /// </summary>
        public static void UpdateAllThemedElements(bool skipBackground = false)
        {
            var deadRefs = new List<WeakReference>();

            foreach (var kvp in _themedElements)
            {
                if (kvp.Key.IsAlive && kvp.Key.Target is FrameworkElement element)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        foreach (var info in kvp.Value)
                        {
                            if (skipBackground && info.PropertyPath == "Background")
                                continue;

                            ApplyThemeToElement(element, new List<ThemeElementInfo> { info });

                        }
                    }, DispatcherPriority.Render);
                }
                else
                {
                    deadRefs.Add(kvp.Key);
                }
            }

            // Clean up dead references
            foreach (var deadRef in deadRefs)
            {
                _themedElements.Remove(deadRef);
            }
        }

        /// <summary>
        /// Updates specific MainWindow gradient elements
        /// </summary>
        public static void UpdateMainWindowGradients()
        {
            if (Application.Current?.MainWindow is FrameworkElement mainWindow)
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    // Update main border gradient theme stop (dark gradient color)
                    if (mainWindow.FindName("GradientThemeStop") is GradientStop gradientStop)
                    {
                        gradientStop.Color = _themeGradientDark;
                    }

                    // Update menu highlighter gradient (base theme color)
                    if (mainWindow.FindName("HighlighterGradient1") is GradientStop highlighter1)
                    {
                        highlighter1.Color = _themeColor;
                    }

                    // Update menu highlighter gradient (transparent theme color)
                    if (mainWindow.FindName("HighlighterGradient2") is GradientStop highlighter2)
                    {
                        highlighter2.Color = _themeColorSemiTransparent;
                    }
                }), DispatcherPriority.Render);
            }
        }

        /// <summary>
        /// Updates dynamic resources in MainWindow
        /// </summary>
        private static void UpdateDynamicResources()
        {
            Application? application = Application.Current;
            if (application == null)
            {
                return;
            }

            void ApplyResources(ResourceDictionary resources)
            {
                resources["ThemeColor"] = new SolidColorBrush(_themeColor);
                resources["ThemeColorDark"] = new SolidColorBrush(_themeColorDark);
                resources["ThemeColorLight"] = new SolidColorBrush(_themeColorLight);
                resources["ThemeGradientDark"] = new SolidColorBrush(_themeGradientDark);
                resources["ThemeColorTransparent"] = new SolidColorBrush(_themeColorTransparent);
                resources["ThemeColorSemiTransparent"] = new SolidColorBrush(_themeColorSemiTransparent);
                resources["ThemeSurface"] = new SolidColorBrush(_themeSurface);
                resources["ThemeSurfaceRaised"] = new SolidColorBrush(_themeSurfaceRaised);
                resources["ThemeSurfaceElevated"] = new SolidColorBrush(_themeSurfaceElevated);
                resources["ThemeOutline"] = new SolidColorBrush(_themeOutline);
                resources["ThemeTextPrimary"] = new SolidColorBrush(_themeTextPrimary);
                resources["ThemeTextSecondary"] = new SolidColorBrush(_themeTextSecondary);
                resources["ThemeFocusRing"] = new SolidColorBrush(_themeFocusRing);
                resources["ThemePrimaryAction"] = new SolidColorBrush(_themePrimaryAction);
                resources["ThemePrimaryActionForeground"] = new SolidColorBrush(_themePrimaryActionForeground);
                resources["ThemeSecondaryAction"] = new SolidColorBrush(_themeSecondaryAction);
                resources["ThemeSecondaryActionForeground"] = new SolidColorBrush(_themeSecondaryActionForeground);
                resources["ThemeTertiaryAction"] = new SolidColorBrush(_themeTertiaryAction);
                resources["ThemeTertiaryActionForeground"] = new SolidColorBrush(_themeTertiaryActionForeground);
                resources["ThemeDangerAction"] = new SolidColorBrush(_themeDangerAction);
                resources["ThemeDangerActionForeground"] = new SolidColorBrush(_themeDangerActionForeground);
            }

            void ApplyCurrentResources()
            {
                // Application resources must be ready before the first window is
                // constructed so a saved theme is visible on its very first frame.
                ApplyResources(application.Resources);
            }

            if (application.Dispatcher.CheckAccess())
            {
                ApplyCurrentResources();
            }
            else
            {
                application.Dispatcher.Invoke(ApplyCurrentResources, DispatcherPriority.Send);
            }
        }

        /// <summary>
        /// Loads theme color from settings
        /// </summary>
        public static void LoadThemeFromSettings(string hexColor)
        {
            SetThemeColor(hexColor);
        }

        /// <summary>
        /// Gets current theme color as hex string
        /// </summary>
        public static string GetThemeColorHex()
        {
            return $"#{_themeColor.R:X2}{_themeColor.G:X2}{_themeColor.B:X2}";
        }

        #region Private Methods

        private static void CalculateThemeColors(Color baseColor)
        {
            // Dark variant - 25% darker
            _themeColorDark = DarkenColor(baseColor, 0.25);

            // Light variant - 20% lighter
            _themeColorLight = LightenColor(baseColor, 0.2);

            // Gradient dark - 70% darker (matches the original #FF120338 darkness level)
            _themeGradientDark = DarkenColor(baseColor, 0.7);

            // Transparent variants
            _themeColorTransparent = Color.FromArgb(51, baseColor.R, baseColor.G, baseColor.B); // 20% opacity
            _themeColorSemiTransparent = Color.FromArgb(102, baseColor.R, baseColor.G, baseColor.B); // 40% opacity

            // Fluent-style hierarchy: the chosen hue remains visible, but large surfaces use
            // deliberately low saturation. This prevents bright theme colors from flooding the
            // whole page and leaves the accent available for actions and current state.
            var (surfaceHue, surfaceSaturation, _) = RgbToHsl(baseColor);
            double neutralSaturation = Math.Clamp(surfaceSaturation * 0.24, 0.08, 0.22);
            _themeSurface = HslToRgb(surfaceHue, neutralSaturation, 0.080);
            _themeSurfaceRaised = HslToRgb(surfaceHue, neutralSaturation, 0.120);
            _themeSurfaceElevated = HslToRgb(surfaceHue, neutralSaturation, 0.165);

            // Boundaries and focus indicators are intentionally stronger than decoration.
            // The loop keeps meaningful component boundaries at WCAG's 3:1 non-text target.
            double outlineLightness = 0.40;
            _themeOutline = HslToRgb(surfaceHue, Math.Clamp(surfaceSaturation * 0.45, 0.18, 0.38), outlineLightness);
            while (outlineLightness < 0.72 && ContrastRatio(_themeOutline, _themeSurfaceRaised) < 3.0)
            {
                outlineLightness += 0.025;
                _themeOutline = HslToRgb(surfaceHue, Math.Clamp(surfaceSaturation * 0.45, 0.18, 0.38), outlineLightness);
            }

            _themeTextPrimary = Color.FromRgb(244, 247, 250);
            _themeTextSecondary = Color.FromRgb(174, 186, 196);
            _themeFocusRing = CreateAccessibleAction(baseColor, 0, 0.58).Background;

            // Keep action hierarchy recognizable for every user-selected hue. Related roles
            // preserve the selected color family while secondary/tertiary roles rotate hue so
            // neighboring buttons do not collapse into indistinguishable shades.
            (_themePrimaryAction, _themePrimaryActionForeground) = CreateAccessibleAction(baseColor, 0, 0.52);
            (_themeSecondaryAction, _themeSecondaryActionForeground) = CreateAccessibleAction(baseColor, 52, 0.43);
            (_themeTertiaryAction, _themeTertiaryActionForeground) = CreateAccessibleAction(baseColor, 180, 0.50);
            (_themeDangerAction, _themeDangerActionForeground) = CreateAccessibleAction(Color.FromRgb(196, 48, 72), 0, 0.44);
        }

        private static (Color Background, Color Foreground) CreateAccessibleAction(Color seed, double hueOffset, double targetLightness)
        {
            var (hue, saturation, _) = RgbToHsl(seed);
            hue = (hue + hueOffset + 360) % 360;
            saturation = Math.Clamp(Math.Max(saturation, 0.50), 0.50, 0.82);
            Color background = HslToRgb(hue, saturation, targetLightness);

            // A filled control must remain identifiable against its surrounding raised surface.
            // Move its lightness away from the surface until it reaches the WCAG 3:1 boundary.
            double direction = RelativeLuminance(_themeSurfaceRaised) < 0.30 ? 1 : -1;
            for (int i = 0; i < 20 && ContrastRatio(background, _themeSurfaceRaised) < 3.0; i++)
            {
                targetLightness = Math.Clamp(targetLightness + direction * 0.025, 0.12, 0.88);
                background = HslToRgb(hue, saturation, targetLightness);
            }

            Color white = Colors.White;
            Color nearBlack = Color.FromRgb(12, 18, 22);
            Color foreground = ContrastRatio(white, background) >= ContrastRatio(nearBlack, background) ? white : nearBlack;
            return (background, foreground);
        }

        public static double ContrastRatio(Color first, Color second)
        {
            double firstLuminance = RelativeLuminance(first);
            double secondLuminance = RelativeLuminance(second);
            double lighter = Math.Max(firstLuminance, secondLuminance);
            double darker = Math.Min(firstLuminance, secondLuminance);
            return (lighter + 0.05) / (darker + 0.05);
        }

        private static double RelativeLuminance(Color color)
        {
            static double Linear(byte channel)
            {
                double value = channel / 255.0;
                return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
            }

            return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
        }

        private static (double Hue, double Saturation, double Lightness) RgbToHsl(Color color)
        {
            double red = color.R / 255.0;
            double green = color.G / 255.0;
            double blue = color.B / 255.0;
            double max = Math.Max(red, Math.Max(green, blue));
            double min = Math.Min(red, Math.Min(green, blue));
            double lightness = (max + min) / 2;
            if (Math.Abs(max - min) < 0.00001) return (0, 0, lightness);

            double delta = max - min;
            double saturation = lightness > 0.5 ? delta / (2 - max - min) : delta / (max + min);
            double hue = max == red
                ? ((green - blue) / delta + (green < blue ? 6 : 0))
                : max == green ? ((blue - red) / delta + 2) : ((red - green) / delta + 4);
            return (hue * 60, saturation, lightness);
        }

        private static Color HslToRgb(double hue, double saturation, double lightness)
        {
            double chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
            double segment = hue / 60;
            double x = chroma * (1 - Math.Abs(segment % 2 - 1));
            (double red, double green, double blue) = segment switch
            {
                < 1 => (chroma, x, 0d),
                < 2 => (x, chroma, 0d),
                < 3 => (0d, chroma, x),
                < 4 => (0d, x, chroma),
                < 5 => (x, 0d, chroma),
                _ => (chroma, 0d, x)
            };
            double match = lightness - chroma / 2;
            return Color.FromRgb(
                (byte)Math.Round((red + match) * 255),
                (byte)Math.Round((green + match) * 255),
                (byte)Math.Round((blue + match) * 255));
        }

        private static void FindThemedChildren(DependencyObject parent, List<ThemeElementInfo> elementInfoList)
        {
            if (parent == null) return;

            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is FrameworkElement fe)
                {
                    // Check for Theme attached property first
                    string themeValue = ThemeBehavior.GetTheme(fe);
                    if (!string.IsNullOrEmpty(themeValue))
                    {
                        elementInfoList.Add(new ThemeElementInfo
                        {
                            Target = fe,
                            PropertyPath = GetPropertyPathFromTag(themeValue),
                            ThemeType = GetThemeTypeFromTag(themeValue)
                        });
                    }
                    // Then check regular Tag property
                    else if (fe.Tag is string tag && tag.StartsWith("Theme"))
                    {
                        elementInfoList.Add(new ThemeElementInfo
                        {
                            Target = fe,
                            PropertyPath = GetPropertyPathFromTag(tag),
                            ThemeType = GetThemeTypeFromTag(tag)
                        });
                    }
                }

                // Recurse
                FindThemedChildren(child, elementInfoList);
            }
        }

        private static void ApplyThemeToElement(FrameworkElement element, List<ThemeElementInfo> elementInfos)
        {
            foreach (var info in elementInfos)
            {
                var targetElement = info.Target ?? element;
                var color = GetColorForThemeType(info.ThemeType);
                var brush = new SolidColorBrush(color);

                try
                {
                    switch (info.PropertyPath)
                    {
                        case "Background":
                            if (targetElement is Control control)
                                control.Background = brush;
                            else if (targetElement is Border border)
                                border.Background = brush;
                            break;

                        case "BorderBrush":
                            if (targetElement is Control control2)
                                control2.BorderBrush = brush;
                            else if (targetElement is Border border2)
                                border2.BorderBrush = brush;
                            break;

                        case "Foreground":
                            if (targetElement is Control control3)
                                control3.Foreground = brush;
                            else if (targetElement is TextBlock textBlock)
                                textBlock.Foreground = brush;
                            break;

                        case "Fill":
                            if (targetElement is System.Windows.Shapes.Shape shape)
                                shape.Fill = brush;
                            break;

                        case "Stroke":
                            if (targetElement is System.Windows.Shapes.Shape shape2)
                                shape2.Stroke = brush;
                            break;

                        case "GradientStop":
                            // This case is now handled by UpdateMainWindowGradients for named gradient stops
                            break;
                    }
                }
                catch (Exception ex)
                {
                }
            }
        }

        private static string GetPropertyPathFromTag(string tag)
        {
            // Extract property from tag format: "Theme:Background" or "ThemeDark:BorderBrush"
            var parts = tag.Split(':');
            return parts.Length > 1 ? parts[1] : "Background";
        }

        private static ThemeType GetThemeTypeFromTag(string tag)
        {
            var parts = tag.Split(':');
            var themeTag = parts[0];

            return themeTag switch
            {
                THEME_TAG => ThemeType.Base,
                THEME_DARK_TAG => ThemeType.Dark,
                THEME_LIGHT_TAG => ThemeType.Light,
                THEME_GRADIENT_TAG => ThemeType.Gradient,
                THEME_TRANSPARENT_TAG => ThemeType.Transparent,
                THEME_SEMI_TRANSPARENT_TAG => ThemeType.SemiTransparent,
                _ => ThemeType.Base
            };
        }

        private static Color GetColorForThemeType(ThemeType type)
        {
            return type switch
            {
                ThemeType.Base => _themeColor,
                ThemeType.Dark => _themeColorDark,
                ThemeType.Light => _themeColorLight,
                ThemeType.Gradient => _themeGradientDark,
                ThemeType.Transparent => _themeColorTransparent,
                ThemeType.SemiTransparent => _themeColorSemiTransparent,
                _ => _themeColor
            };
        }

        private static Color DarkenColor(Color color, double factor)
        {
            return Color.FromRgb(
                (byte)(color.R * (1 - factor)),
                (byte)(color.G * (1 - factor)),
                (byte)(color.B * (1 - factor))
            );
        }

        private static Color LightenColor(Color color, double factor)
        {
            return Color.FromRgb(
                (byte)Math.Min(255, color.R + (255 - color.R) * factor),
                (byte)Math.Min(255, color.G + (255 - color.G) * factor),
                (byte)Math.Min(255, color.B + (255 - color.B) * factor)
            );
        }

        private static void CleanupDeadReferences(object sender, EventArgs e)
        {
            var deadRefs = _themedElements.Keys
                .Where(wr => !wr.IsAlive)
                .ToList();

            foreach (var deadRef in deadRefs)
            {
                _themedElements.Remove(deadRef);
            }
        }

        #endregion

        #region Helper Classes

        private class ThemeElementInfo
        {
            public FrameworkElement Target { get; set; }
            public string PropertyPath { get; set; }
            public ThemeType ThemeType { get; set; }
        }

        private enum ThemeType
        {
            Base,
            Dark,
            Light,
            Gradient,
            Transparent,
            SemiTransparent
        }

        #endregion
    }

    /// <summary>
    /// Attached behavior for automatic theme registration
    /// </summary>
    public static class ThemeBehavior
    {
        public static readonly DependencyProperty AutoRegisterProperty =
            DependencyProperty.RegisterAttached(
                "AutoRegister",
                typeof(bool),
                typeof(ThemeBehavior),
                new PropertyMetadata(false, OnAutoRegisterChanged));

        public static readonly DependencyProperty ThemeProperty =
            DependencyProperty.RegisterAttached(
                "Theme",
                typeof(string),
                typeof(ThemeBehavior),
                new PropertyMetadata(null, OnThemeChanged));

        public static bool GetAutoRegister(DependencyObject obj)
        {
            return (bool)obj.GetValue(AutoRegisterProperty);
        }

        public static void SetAutoRegister(DependencyObject obj, bool value)
        {
            obj.SetValue(AutoRegisterProperty, value);
        }

        public static string GetTheme(DependencyObject obj)
        {
            return (string)obj.GetValue(ThemeProperty);
        }

        public static void SetTheme(DependencyObject obj, string value)
        {
            obj.SetValue(ThemeProperty, value);
        }

        private static void OnAutoRegisterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FrameworkElement element && (bool)e.NewValue)
            {
                if (element.IsLoaded)
                {
                    ThemeManager.RegisterElement(element);
                }
                else
                {
                    element.Loaded += (s, args) => ThemeManager.RegisterElement(element);
                }

                element.Unloaded += (s, args) => ThemeManager.UnregisterElement(element);
            }
        }

        private static void OnThemeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FrameworkElement element && e.NewValue is string themeValue && !string.IsNullOrEmpty(themeValue))
            {
                // Auto-register elements that have a Theme attached property
                if (element.IsLoaded)
                {
                    ThemeManager.RegisterElement(element);
                }
                else
                {
                    element.Loaded += (s, args) => ThemeManager.RegisterElement(element);
                }

                element.Unloaded += (s, args) => ThemeManager.UnregisterElement(element);
            }
        }
    }
}
