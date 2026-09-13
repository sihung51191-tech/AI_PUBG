using Aimmy2.Class;
using Aimmy2.Controls;
using Aimmy2.MouseMovementLibraries.GHubSupport;
using Aimmy2.Other;
using Aimmy2.Theme;
using Aimmy2.UILibrary;
using AimmyWPF.Class;
using Class;
using InputLogic;
using Other;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using UILibrary;
using Visuality;
using Aimmy2.AILogic;
using Keys = System.Windows.Forms.Keys;

namespace Aimmy2
{
    public partial class MainWindow : Window
    {
        private System.Windows.Forms.NotifyIcon _notifyIcon;
        private bool _isExplicitExit = false;
        private bool _cleanupStarted;

        private bool _savedAimAssist = false;
        private bool _savedWeaponRecognition = false;
        private bool _cancelAimAssistDelayedActivation = false;
        private bool _cancelWeaponRecognitionDelayedActivation = false;

        public void CancelDelayedActivation(string feature)
        {
            if (feature == "Aim Assist")
                _cancelAimAssistDelayedActivation = true;
            else if (feature == "Weapon Recognition")
                _cancelWeaponRecognitionDelayedActivation = true;
        }

        #region Managers and Windows

        // Core managers (lazy-loaded)
        private readonly Lazy<InputBindingManager> _bindingManager = new(() => new InputBindingManager());
        private static readonly Lazy<GithubManager> _githubManager = new(() => new GithubManager());
        private readonly Lazy<UI> _uiManager = new(() => new UI());
        private Lazy<FileManager>? _fileManager;

        // Windows
        private static readonly Lazy<FOV> _fovWindow = new(() =>
        {
            var window = new FOV();
            // Force immediate reposition to current display
            window.ForceReposition();
            return window;
        });

        private static readonly Lazy<DetectedPlayerWindow> _dpWindow = new(() =>
        {
            var window = new DetectedPlayerWindow();
            // Force immediate reposition to current display
            window.ForceReposition();
            return window;
        });

        private static readonly Lazy<DetectedScopeWindow> _scopeWindow = new(() =>
        {
            var window = new DetectedScopeWindow();
            window.ForceReposition();
            return window;
        });

        private static readonly Lazy<CrosshairWindow> _crosshairWindow = new(() =>
        {
            var window = new CrosshairWindow();
            window.ForceReposition();
            return window;
        });

        // Public accessors
        internal InputBindingManager bindingManager => _bindingManager.Value;
        internal FileManager fileManager => _fileManager?.Value ?? throw new InvalidOperationException("FileManager not initialized");
        public static FOV FOVWindow => _fovWindow.Value;
        public static DetectedPlayerWindow DPWindow => _dpWindow.Value;
        public static DetectedScopeWindow ScopeWindow => _scopeWindow.Value;
        public static CrosshairWindow CrosshairWindow => _crosshairWindow.Value;
        public static GithubManager githubManager => _githubManager.Value;
        public UI uiManager => _uiManager.Value;

        #endregion

        #region UI State
        public SettingsMenuControl? SettingsMenuControlInstance { get; set; }
        internal Dictionary<string, AToggle> toggleInstances = new();
        private readonly Dictionary<string, UserControl?> _menuControls = new();
        private readonly Dictionary<string, bool> _menuInitialized = new();
        private UserControl? _currentControl;
        private string _currentMenu = "AimMenu";
        private bool _currentlySwitching;
        private ScrollViewer? CurrentScrollViewer;
        public double ActualFOV { get; set; } = 640;
        private double _currentGradientAngle;

        // Menu names constant
        private static readonly string[] MenuNames = { "AimMenu", "ModelMenu", "SettingsMenu", "AboutMenu" };

        #endregion

        #region Initialization

        public MainWindow()
        {
            global::Other.UiLanguage.Initialize();
            InitializeComponent();
            // ThemeManager already loaded colors.cfg in App.OnStartup. Paint the
            // non-resource gradient stops before this window can render its first frame.
            ApplyThemeGradients();
            RestoreWindowSize();
            InitializeTrayIcon();
            _windowSizeSaveTimer.Tick += (_, _) => { _windowSizeSaveTimer.Stop(); SaveWindowSize(); };
            SizeChanged += (_, _) =>
            {
                if (!IsLoaded || WindowState != WindowState.Normal) return;
                _windowSizeSaveTimer.Stop();
                _windowSizeSaveTimer.Start();
            };
            Activated += (_, _) =>
            {
                bindingManager.ResetTransientInputState();
                global::AILogic.CaptureManager.RefreshAfterForegroundSwitch();
                WeaponSlotManager.Instance.OnForegroundRestored(_isWeaponScanToggled, _isScopeScanToggled);
                if (ReferenceEquals(Application.Current.MainWindow, this))
                    RecoverWindowPresentation();
            };
            StateChanged += (_, _) =>
            {
                // A transparent borderless WPF window can occasionally lose its taskbar
                // representation after a fullscreen application changes display state.
                if (WindowState == WindowState.Minimized) ShowInTaskbar = true;
            };
        }

        private readonly System.Windows.Threading.DispatcherTimer _windowSizeSaveTimer = new()
        { Interval = TimeSpan.FromMilliseconds(400) };
        private string WindowSizePath => Path.Combine(AppContext.BaseDirectory, "bin", "window.cfg");

        private void SaveWindowSize()
        {
            if (WindowState != WindowState.Normal || !double.IsFinite(Width) || !double.IsFinite(Height)) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(WindowSizePath)!);
                File.WriteAllText(WindowSizePath + ".tmp", Newtonsoft.Json.JsonConvert.SerializeObject(
                    new { WindowWidth = Width, WindowHeight = Height }, Newtonsoft.Json.Formatting.Indented));
                File.Move(WindowSizePath + ".tmp", WindowSizePath, true);
            }
            catch (Exception ex) { LogManager.Log(LogManager.LogLevel.Warning, $"Cannot save window size: {ex.Message}"); }
        }

        private void InitializeTrayIcon()
        {
            try
            {
                if (!System.IO.File.Exists("icon\\target.ico")) return;

                _notifyIcon = new System.Windows.Forms.NotifyIcon
                {
                    Icon = new System.Drawing.Icon("icon\\target.ico"),
                    Visible = true,
                    Text = "Aimmy"
                };

                var contextMenu = new System.Windows.Forms.ContextMenuStrip();
                contextMenu.Items.Add("Open", null, (s, e) => ShowWindow());
                contextMenu.Items.Add("Exit", null, (s, e) =>
                {
                    _isExplicitExit = true;
                    InputLogic.LootManager.StopExternalLootProcesses();
                    // Ensure we clean up before shutting down
                    if (_notifyIcon != null) _notifyIcon.Visible = false;
                    Application.Current.Shutdown();
                });

                contextMenu.Opening += (_, _) =>
                {
                    contextMenu.Items[0].Text = global::Other.UiLanguage.Text("Open");
                    contextMenu.Items[1].Text = global::Other.UiLanguage.Text("Exit");
                };
                _notifyIcon.ContextMenuStrip = contextMenu;
                _notifyIcon.DoubleClick += (s, e) => ShowWindow();
            }
            catch { /* Ignore tray icon errors to prevent startup crash */ }
        }


        private void ShowWindow()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(ShowWindow);
                return;
            }

            ShowInTaskbar = true;
            Show();
            WindowState = WindowState.Normal;
            RecoverWindowPresentation();
            Activate();
            Focus();
        }

        private void RecoverWindowPresentation()
        {
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            ShowInTaskbar = true;
            if (Opacity < 0.05) Opacity = 1;

            double width = ActualWidth > 1 ? ActualWidth : Width;
            double height = ActualHeight > 1 ? ActualHeight : Height;
            if (!double.IsFinite(Left) || !double.IsFinite(Top) ||
                !double.IsFinite(width) || !double.IsFinite(height)) return;

            var desktop = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            var visible = Rect.Intersect(new Rect(Left, Top, width, height), desktop);
            if (!visible.IsEmpty && visible.Width >= 80 && visible.Height >= 40) return;

            // A monitor may have disappeared or changed resolution while the game was
            // fullscreen. Put the window back in the primary work area in that case.
            Rect workArea = SystemParameters.WorkArea;
            Left = workArea.Left + Math.Max(0, (workArea.Width - width) / 2);
            Top = workArea.Top + Math.Max(0, (workArea.Height - height) / 2);
        }

        private void RestoreWindowSize()
        {
            try
            {
                // Synchronously attempt to read just the window size
                // This is a minimal read to prevent "pop-in" effect
                string configPath = File.Exists(WindowSizePath) ? WindowSizePath : "bin\\configs\\Default.cfg";
                if (File.Exists(configPath)) 
                {
                    string json = File.ReadAllText(configPath);
                    var settings = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(json);
                    
                    if (settings != null)
                    {
                         if (settings.TryGetValue("WindowHeight", out dynamic h))
                             this.Height = Math.Clamp(Convert.ToDouble(h), 444, 1080);
                         if (settings.TryGetValue("WindowWidth", out dynamic w))
                             this.Width = Math.Clamp(Convert.ToDouble(w), 670, 1920);
                    }
                }
            }
            catch { /* If it fails, we fall back to default, no crash allowed here */ }
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                DisplayManager.Initialize();
                SaveDictionary.EnsureDirectoriesExist();

                InputLogic.LootManager.StartExternalLootProcess();

                // Load loot config and defaults from old INI
                InputLogic.LootManager.LoadDefaultsFromIni();

                InitializeMenus();
                InitializeFileManagerEarly();

                // Load configurations BEFORE loading any menus
                // This ensures minimize states are loaded from file before menu initialization
                await LoadConfigurationsAsync();

                // Now load the initial menu - it will use the loaded minimize states
                LoadInitialMenu();

                // Continue with the rest of initialization
                await InitializeApplicationAsync();
                UpdateAboutSpecs();
                ApplyThemeGradients();
                ThemeManager.LoadMediaSettings();

                // Task khôi phục kích hoạt sau khi vẽ giao diện xong (500ms)
                _ = Task.Run(async () =>
                {
                    await Task.Delay(500);
                    
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (!this.IsLoaded) return;

                        if (_savedAimAssist && !_cancelAimAssistDelayedActivation)
                        {
                            if (Dictionary.lastLoadedModel != "N/A")
                            {
                                Dictionary.toggleState["Aim Assist"] = true;
                                if (uiManager.T_AimAligner != null)
                                {
                                    UpdateToggleUI(uiManager.T_AimAligner, true);
                                }
                                LogManager.Log(LogManager.LogLevel.Info, "Aim Assist tự động kích hoạt sau khi nạp mô hình.", true, 3000);
                            }
                        }

                        if (_savedWeaponRecognition && !_cancelWeaponRecognitionDelayedActivation)
                        {
                            Dictionary.toggleState["Weapon Recognition"] = true;
                            if (uiManager.T_WeaponRecognition != null)
                            {
                                UpdateToggleUI(uiManager.T_WeaponRecognition, true);
                            }
                            
                            if (!WeaponSlotManager.Instance.IsInitialized)
                            {
                                WeaponSlotManager.Instance.Initialize();
                            }
                            LogManager.Log(LogManager.LogLevel.Info, "Weapon Recognition tự động kích hoạt sau khi nạp mô hình.", true, 3000);
                        }
                    });
                });
            }
            catch (Exception ex)
            {
                ShowError($"Lỗi khi khởi động: {ex.Message}", ex);
            }
        }

        private void InitializeMenus()
        {
            foreach (var menu in MenuNames)
            {
                _menuControls[menu] = null;
                _menuInitialized[menu] = false;
            }
        }

        private void InitializeFileManagerEarly()
        {
            var modelMenu = new ModelMenuControl();
            modelMenu.Initialize(this);
            _menuControls["ModelMenu"] = modelMenu;
            InitializeFileManager(modelMenu);
        }

        private void LoadInitialMenu()
        {
            LoadMenu("AimMenu");
            // Don't call UpdateSliderVisibility here - it would override collapsed menu states
            // Visibility is handled by the toggle click actions when user interacts with toggles
            _currentMenu = "AimMenu";
        }

        private async Task InitializeApplicationAsync()
        {
            CheckRunningFromTemp();

            // Initialize DisplayManager FIRST before anything else that depends on display info
            DisplayManager.Initialize();

            // Now that DisplayManager is initialized, we can create windows
            InitializeWindows();

            EnsureRequiredFiles();

            SetupKeybindings();
            ConfigurePropertyChangers();
            ApplyInitialSettings();
            ListenForKeybinds();

            // Subscribe to display changes after everything is initialized
            DisplayManager.DisplayChanged += OnDisplayChanged;

            // Start Recoil Manager
            RecoilManager.Initialize();
        }

        private void OnDisplayChanged(object? sender, DisplayChangedEventArgs e)
        {

            // Force update all windows to new display
            DisplayManager.ForceUpdateWindows();
        }

        private void CheckRunningFromTemp()
        {
            if (Directory.GetCurrentDirectory().Contains("Temp"))
            {
                global::Other.LocalizedMessageBox.Show(
                    "Xin chào, phát hiện bạn đang chạy Aimmy trong file zip. " +
                    "Vui lòng giải nén Aimmy để phần mềm hoạt động ổn định.\n\nCảm ơn.",
                    "Aimmy V2");
            }
        }

        private void InitializeWindows()
        {
            // Create windows but don't show them yet
            var fov = FOVWindow;  // This triggers lazy initialization
            var dpw = DPWindow;   // This triggers lazy initialization
            var crosshair = CrosshairWindow; // This triggers lazy initialization

            // Ensure they're positioned on the current display
            fov.ForceReposition();
            dpw.ForceReposition();
            crosshair.ForceReposition();

            // Set references in Dictionary
            Dictionary.DetectedPlayerOverlay = dpw;
            Dictionary.FOVWindow = fov;
            Dictionary.DetectedScopeOverlay = ScopeWindow;
            ScopeWindow.SetScalePercent(Dictionary.sliderSettings.TryGetValue("Weapon + Scope Info Size", out var overlaySize)
                ? Convert.ToDouble(overlaySize) : 82.0);
            ScopeWindow.SetOpacityPercent(Dictionary.sliderSettings.TryGetValue("Weapon + Scope Info Opacity", out var overlayOpacity)
                ? Convert.ToDouble(overlayOpacity) : 90.0);
            Dictionary.CrosshairWindow = crosshair;
        }

        private void EnsureRequiredFiles()
        {
            var labelsPath = "bin\\labels\\labels.txt";
            var labelsDir = Path.GetDirectoryName(labelsPath);

            // Ensure the directory exists
            if (!string.IsNullOrEmpty(labelsDir) && !Directory.Exists(labelsDir))
            {
                Directory.CreateDirectory(labelsDir);
            }

            // Create the file if it doesn't exist
            if (!File.Exists(labelsPath))
            {
                File.WriteAllText(labelsPath, "Enemy");
            }
        }

        private async Task LoadConfigurationsAsync()
        {
            // Run non-UI operations in background
            await Task.Run(() =>
            {
                // Load configurations that don't create UI
                var configs = new[]
                {
                    (Dictionary.minimizeState, "bin\\minimize.cfg"),
                    (Dictionary.bindingSettings, "bin\\binding.cfg"),
                    (Dictionary.colorState, "bin\\colors.cfg"),
                    (Dictionary.filelocationState, "bin\\filelocations.cfg"),
                    (Dictionary.dropdownState, "bin\\dropdown.cfg"),
                    (Dictionary.toggleState, "bin\\toggles.cfg"),
                    (Dictionary.modelState, "bin\\models.cfg")
                };

                foreach (var (dict, path) in configs)
                {
                    SaveDictionary.LoadJSON(dict, path);
                }
            });

            // Trích xuất cấu hình gốc trước khi tạm tắt
            _savedAimAssist = Dictionary.toggleState.TryGetValue("Aim Assist", out var aaVal) && (bool)aaVal;
            _savedWeaponRecognition = Dictionary.toggleState.TryGetValue("Weapon Recognition", out var wrVal) && (bool)wrVal;

            // Tạm thời tắt để tránh lag lúc khởi động
            Dictionary.toggleState["Aim Assist"] = false;
            Dictionary.toggleState["Weapon Recognition"] = false;

            string lastConfig = "Default.cfg";
            if (Dictionary.modelState.TryGetValue("LastLoadedConfig", out var configVal))
            {
                lastConfig = configVal?.ToString() ?? "Default.cfg";
            }
            Dictionary.lastLoadedConfig = lastConfig;

            string lastConfigPath = Path.Combine("bin\\configs", lastConfig);
            if (!File.Exists(lastConfigPath))
            {
                lastConfigPath = "bin\\configs\\Default.cfg";
                Dictionary.lastLoadedConfig = "Default.cfg";
            }

            // Load these on UI thread since they might show notifications
            LoadConfig(lastConfigPath);
            CapturePreferences.Load();
            MouseSensitivityProfiles.Load();
            PriorityAimingConfig.Load();
            DisplayManager.LoadSavedDisplay();
            ApplyThemeColorFromConfig();
            
            // Validate and restore models after configs are loaded
            await Task.Run(global::AILogic.CaptureManager.ValidateSavedCaptureMethods);
            await ValidateAndRestoreModels();
        }


        private void ApplyThemeColorFromConfig()
        {
            if (Dictionary.colorState.TryGetValue("Theme Color", out var themeColor))
            {
                var colorString = themeColor?.ToString();
                if (!string.IsNullOrEmpty(colorString))
                {
                    try
                    {
                        ThemeManager.SetThemeColor(colorString);
                    }
                    catch (Exception ex)
                    {
                    }
                }
            }
        }

        private void SetupKeybindings()
        {
            var keybinds = new[]
            {
                "Aim Keybind", "Second Aim Keybind", "Dynamic FOV Keybind",
                "Emergency Stop Keybind", "Model Switch Keybind",
                "Recoil Toggle Keybind",
                "Weapon Scan Keybind", "Scope Scan Keybind", "Weapon Slot 1 Keybind", "Weapon Slot 2 Keybind",
                "Auto Click Keybind",
                "Slot 1 Priority Key", "Slot 2 Priority Key",
                "Crosshair Hide Key 1",
                "Fast Loot Keybind"
            };

            foreach (var keybind in keybinds)
            {
                bindingManager.SetupDefault(keybind, Dictionary.bindingSettings[keybind].ToString());
            }
        }

        private void ConfigurePropertyChangers()
        {
            PropertyChanger.ReceiveNewConfig = LoadConfig;
        }

        private void ApplyInitialSettings()
        {
            // FOV settings
            ActualFOV = Convert.ToDouble(Dictionary.sliderSettings["FOV Size"]);
            PropertyChanger.PostNewFOVSize(ActualFOV);
            PropertyChanger.PostColor((Color)ColorConverter.ConvertFromString(Dictionary.colorState["FOV Color"].ToString()));

            // Crosshair settings
            if (Dictionary.colorState.TryGetValue("Crosshair Color", out var chColor))
            {
                PropertyChanger.PostCrosshairColor((Color)ColorConverter.ConvertFromString(chColor.ToString()));
            }
            if (Dictionary.sliderSettings.TryGetValue("Crosshair Size", out var chSize))
            {
                PropertyChanger.PostCrosshairSize(Convert.ToDouble(chSize));
            }

            // Detected player window settings
            var dpSettings = new[]
            {
                ("Detected Player Color", (Action<object>)(c => PropertyChanger.PostDPColor((Color)c))),
                ("AI Confidence Font Size", (Action<object>)(s => PropertyChanger.PostDPFontSize((int)(double)s))),
                ("Corner Radius", (Action<object>)(r => PropertyChanger.PostDPWCornerRadius((int)(double)r))),
                ("Border Thickness", (Action<object>)(t => PropertyChanger.PostDPWBorderThickness((double)t))),
                ("Opacity", (Action<object>)(o => PropertyChanger.PostDPWOpacity((double)o)))
            };

            foreach (var (key, action) in dpSettings)
            {
                if (key.Contains("Color"))
                {
                    action(ColorConverter.ConvertFromString(Dictionary.colorState[key].ToString()));
                }
                else
                {
                    action(Convert.ToDouble(Dictionary.sliderSettings[key]));
                }
            }

            // Restore Window Size
            RestoreWindowSize();

            // Apply loaded toggle actions on startup
            foreach (var key in Dictionary.toggleState.Keys.ToList())
            {
                Toggle_Action(key);
            }
        }

        private void UpdateAboutSpecs()
        {
            if (_menuControls["AboutMenu"] is AboutMenuControl aboutMenu)
            {
                aboutMenu.AboutSpecsControl.Content = "Đang tải cấu hình máy...";

                Task.Run(() =>
                {
                    var specs = $"{GetProcessorName()} • {GetVideoControllerName()} • {GetFormattedMemorySize()}GB RAM";
                    Dispatcher.Invoke(() => aboutMenu.AboutSpecsControl.Content = specs);
                });
            }
        }

        private void ShowError(string message, Exception ex)
        {
            global::Other.LocalizedMessageBox.Show($"{message}\n\nStack trace: {ex.StackTrace}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void ApplyThemeGradients()
        {
            try
            {
                var color = ThemeManager.ThemeColor;

                var gradientMappings = new Dictionary<string, Func<Color, Color>>
                {
                    ["GradientThemeStop"] = c => Color.FromRgb((byte)(c.R * 0.3), (byte)(c.G * 0.3), (byte)(c.B * 0.3)),
                    ["HighlighterGradient1"] = c => c,
                    ["HighlighterGradient2"] = c => Color.FromArgb(102, c.R, c.G, c.B)
                };

                foreach (var (elementName, colorTransform) in gradientMappings)
                {
                    if (FindName(elementName) is GradientStop gradientStop)
                    {
                        gradientStop.Color = colorTransform(color);
                    }
                }
            }
            catch (Exception ex)
            {
            }
        }

        private void InitializeFileManager(ModelMenuControl modelMenu)
        {
            if (_fileManager == null)
            {
                _fileManager = new Lazy<FileManager>(() => new FileManager(
                    modelMenu.ModelListBoxControl,
                    modelMenu.SelectedModelNotifierControl,
                    modelMenu.ConfigsListBoxControl,
                    modelMenu.SelectedConfigNotifierControl,
                    modelMenu.Slot1ModelListBoxControl,
                    modelMenu.Slot1NotifierControl));

                try
                {
                    var fm = _fileManager.Value;
                }
                catch (Exception ex)
                {
                }
            }
        }

        #endregion

        #region Window Events

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }
        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void Exit_Click(object sender, RoutedEventArgs e) => Close();

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _windowSizeSaveTimer.Stop();
            SaveWindowSize();
            if (!_isExplicitExit)
            {
                e.Cancel = true;
                var exitWindow = new ExitConfirmationWindow();
                exitWindow.Owner = this;
                exitWindow.ShowDialog();

                if (exitWindow.Choice == ExitConfirmationWindow.ExitChoice.Hide)
                {
                    Hide();
                }
                else if (exitWindow.Choice == ExitConfirmationWindow.ExitChoice.Exit)
                {
                    _isExplicitExit = true;
                    Application.Current.Shutdown();
                }
                return;
            }

            if (_cleanupStarted) return;
            _cleanupStarted = true;

            try { Interlocked.Exchange(ref _focusSwitchRecoveryCts, null)?.Cancel(); }
            catch (ObjectDisposedException) { }

            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }

            // Stop all polling and global hooks before saving or releasing GPU models so the
            // machine becomes idle immediately when Exit is chosen.
            RecoilManager.Stop();
            _isWeaponScanToggled = _isScopeScanToggled = false;
            WeaponSlotManager.StopIfCreated();
            if (_bindingManager.IsValueCreated) bindingManager.StopListening();

            if (_fileManager?.IsValueCreated == true)
            {
                fileManager.Dispose();
            }

            // Loot đã được tích hợp native, không cần thoát process ngoài

            InputLogic.LootManager.StopExternalLootProcesses();

            // Do not disable features on close so that their states are saved and restored on next startup
            // DisableAllFeatures();
            CloseWindows();
            CleanupDrivers();

            // Dispose menu controls to save their states
            if (_menuControls["AimMenu"] is AimMenuControl aimMenu)
                aimMenu.Dispose();

            if (_menuControls["SettingsMenu"] is SettingsMenuControl settingsMenu)
                settingsMenu.Dispose();

            SaveAllConfigurations();
            FileManager.AIManager?.Dispose();

            // Clean up display manager
            DisplayManager.DisplayChanged -= OnDisplayChanged;
            DisplayManager.Dispose();
            WeaponSlotManager.DisposeIfCreated();

            Application.Current.Shutdown();
        }

        private void DisableAllFeatures()
        {
            var features = new[] { "Aim Assist", "FOV", "Show Detected Player", "Virtual Crosshair" };
            foreach (var feature in features)
            {
                Dictionary.toggleState[feature] = false;
            }
        }

        private void CloseWindows()
        {
            try
            {
                FOVWindow?.Close();
                DPWindow?.Close();
                ScopeWindow?.Close();
                CrosshairWindow?.Close();
                
                Dictionary.DetectedScopeOverlay?.Close();
                Dictionary.DetectedPlayerOverlay?.Close();
                Dictionary.FOVWindow?.Close();
                Dictionary.CrosshairWindow?.Close();
            }
            catch { }
        }

        private void CleanupDrivers()
        {
            if (Dictionary.dropdownState.TryGetValue("Mouse Movement Method", out var method) &&
                method?.ToString() == "LG HUB")
            {
                LGMouse.Close();
            }
        }

        public void SaveAllConfigurations()
        {
            MouseSensitivityProfiles.Save();
            CapturePreferences.Save();
            SaveWindowSize();
            // Sao lưu trạng thái toggle hiện tại trong bộ nhớ
            bool currentAimAssist = Dictionary.toggleState.TryGetValue("Aim Assist", out var aaC) && (bool)aaC;
            bool currentWeaponRecog = Dictionary.toggleState.TryGetValue("Weapon Recognition", out var wrC) && (bool)wrC;

            // Khôi phục cấu hình gốc khi lưu nếu người dùng chưa tương tác thủ công
            if (!_cancelAimAssistDelayedActivation)
            {
                Dictionary.toggleState["Aim Assist"] = _savedAimAssist;
            }
            if (!_cancelWeaponRecognitionDelayedActivation)
            {
                Dictionary.toggleState["Weapon Recognition"] = _savedWeaponRecognition;
            }

            Dictionary.colorState["Theme Color"] = ThemeManager.GetThemeColorHex();

            SaveDictionary.WriteJSON(PriorityAimingConfig.WithoutPriorityKeys(Dictionary.sliderSettings)
                .Where(kvp => kvp.Key != "Screen Capture Method" && kvp.Key != "Scope Capture Method" && kvp.Key != "Slot 1 Image Size" && kvp.Key != "Slot 2 Image Size")
                .Concat(PriorityAimingConfig.WithoutPriorityKeys(Dictionary.dropdownState))
                //.Where(kvp => kvp.Key != "Screen Capture Method")
                .GroupBy(kvp => kvp.Key)
                .ToDictionary(g => g.Key, g => g
                .First().Value));
            SaveDictionary.WriteJSON(Dictionary.minimizeState, "bin\\minimize.cfg");
            SaveDictionary.WriteJSON(PriorityAimingConfig.WithoutPriorityKeys(Dictionary.bindingSettings), "bin\\binding.cfg");
            SaveDictionary.WriteJSON(PriorityAimingConfig.WithoutPriorityKeys(Dictionary.dropdownState), "bin\\dropdown.cfg");
            SaveDictionary.WriteJSON(Dictionary.colorState, "bin\\colors.cfg");
            SaveDictionary.WriteJSON(Dictionary.filelocationState, "bin\\filelocations.cfg");
            SaveDictionary.WriteJSON(Dictionary.filelocationState, "bin\\filelocations.cfg");
            SaveDictionary.WriteJSON(PriorityAimingConfig.WithoutPriorityKeys(Dictionary.toggleState), "bin\\toggles.cfg");
            PriorityAimingConfig.Save();

            // Save Model State
            Dictionary.modelState["Slot1"] = Dictionary.lastLoadedModel;
            Dictionary.modelState["Slot2"] = Dictionary.lastLoadedModelSlot2;
            Dictionary.modelState["LastLoadedConfig"] = Dictionary.lastLoadedConfig;
            SaveDictionary.WriteJSON(Dictionary.modelState, "bin\\models.cfg");

            // Phục hồi lại trạng thái toggle thực tế trong bộ nhớ
            Dictionary.toggleState["Aim Assist"] = currentAimAssist;
            Dictionary.toggleState["Weapon Recognition"] = currentWeaponRecog;
        }

        #endregion

        #region Menu Management

        private UserControl GetOrCreateMenuControl(string menuName)
        {
            if (_menuControls[menuName] != null)
                return _menuControls[menuName]!;

            var newControl = menuName == "ModelMenu" && _menuControls["ModelMenu"] != null
                ? _menuControls["ModelMenu"]!
                : CreateMenuControl(menuName);

            _menuControls[menuName] = newControl;

            if (!_menuInitialized[menuName])
            {
                InitializeMenuControl(menuName, newControl);
                _menuInitialized[menuName] = true;
            }

            return newControl;
        }

        private UserControl CreateMenuControl(string menuName) => menuName switch
        {
            "AimMenu" => new AimMenuControl(),
            "ModelMenu" => new ModelMenuControl(),
            "SettingsMenu" => new SettingsMenuControl(),
            "AboutMenu" => new AboutMenuControl(),
            _ => throw new ArgumentException($"Unknown menu: {menuName}")
        };

        private void InitializeMenuControl(string menuName, UserControl control)
        {
            switch (control)
            {
                case AimMenuControl aimMenu:
                        aimMenu.Initialize(this);
                        CurrentScrollViewer = aimMenu.AimMenuScrollViewer;
                        
                        // Reload configs to overwrite any default values set during UI initialization
                        // This prevents settings from being reset to defaults (e.g. Movement Path = Cubic, Sliders = Min)
                        string activeConfig = Dictionary.lastLoadedConfig;
                        if (string.IsNullOrEmpty(activeConfig) || activeConfig == "N/A")
                        {
                            activeConfig = "Default.cfg";
                        }
                        string activeConfigPath = Path.Combine("bin\\configs", activeConfig);
                        if (!File.Exists(activeConfigPath))
                        {
                            activeConfigPath = "bin\\configs\\Default.cfg";
                        }

                        InputLogic.LootManager.PrepareForConfigLoad();
                        SaveDictionary.LoadJSON(Dictionary.sliderSettings, activeConfigPath, false);
                        InputLogic.LootManager.PostConfigLoad();
                        SaveDictionary.LoadJSON(Dictionary.dropdownState, activeConfigPath, false);
                        if (File.Exists("bin\\dropdown.cfg"))
                        {
                            SaveDictionary.LoadJSON(Dictionary.dropdownState, "bin\\dropdown.cfg", false);
                        }
                        
                        LoadDropdownStates();
                        LoadDropdownStates();
                        ApplyConfigToSliders();
                        UpdateSliderVisibility(uiManager);
                        aimMenu.RefreshRecoilConfig();
                    break;

                case ModelMenuControl modelMenu:
                    if (!_menuInitialized["ModelMenu"])
                        modelMenu.Initialize(this);
                    break;

                case SettingsMenuControl settingsMenu:
                    settingsMenu.Initialize(this);
                    LoadDropdownStates();
                    SettingsMenuControlInstance = settingsMenu;
                    settingsMenu.RefreshLoadedImageSizes();
                    break;

                case AboutMenuControl aboutMenu:
                    aboutMenu.Initialize(this);
                    UpdateAboutSpecs();
                    break;
            }
        }

        private void LoadMenu(string menuName)
        {
            var control = GetOrCreateMenuControl(menuName);
            ContentArea.Children.Clear();
            ContentArea.Children.Add(control);
            control.BeginAnimation(UIElement.OpacityProperty, null);
            control.Opacity = 1;
            _currentControl = control;
            UpdateCurrentScrollViewer(menuName, control);
        }

        private void UpdateCurrentScrollViewer(string menuName, UserControl control)
        {
            CurrentScrollViewer = control switch
            {
                AimMenuControl aim => aim.AimMenuScrollViewer,
                ModelMenuControl model => model.ModelMenuScrollViewer,
                SettingsMenuControl settings => settings.SettingsMenuScrollViewer,
                AboutMenuControl about => about.AboutMenuScrollViewer,
                _ => CurrentScrollViewer
            };
        }

        private void MenuSwitch(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string newMenuName } ||
                !IsValidMenu(newMenuName) ||
                _currentlySwitching ||
                (_currentMenu == newMenuName && ContentArea.Children.Count > 0)) return;

            _currentlySwitching = true;

            try
            {
                MenuHighlighter.BeginAnimation(FrameworkElement.MarginProperty, null);
                MenuHighlighter.Margin = ((Button)sender).Margin;
                LoadMenu(newMenuName);
                _currentMenu = newMenuName;
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, $"Không thể mở tab {newMenuName}: {ex}", true);
            }
            finally
            {
                _currentlySwitching = false;
            }
        }

        private bool IsValidMenu(string? menuName) =>
            !string.IsNullOrEmpty(menuName) && _menuControls.ContainsKey(menuName!);

        #endregion

        #region Toggle Actions

        internal void Toggle_Action(string title)
        {
            var actions = new Dictionary<string, Action>
            {
                ["FOV"] = () =>
                {
                    FOVWindow.Visibility = GetToggleVisibility(title);
                    // Force reposition when showing the window
                    if (Dictionary.toggleState[title])
                    {
                        FOVWindow.ForceReposition();
                    }
                },
                ["Virtual Crosshair"] = () =>
                {
                    bool isEnabled = Dictionary.toggleState[title];
                    CrosshairWindow.Visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;
                    if (isEnabled)
                    {
                        CrosshairWindow.ForceReposition();
                    }
                },
                ["Sticky Aim"] = () => UpdateSliderVisibility(uiManager),
                ["Show Detected Player"] = () =>
                {
                    ShowHideDPWindow();
                    if (toggleInstances.TryGetValue("Show Detection Performance", out var performanceToggle))
                        performanceToggle.IsEnabled = Dictionary.toggleState["Show Detected Player"];
                    DPWindow.RefreshPerformanceOverlay();
                    DPWindow.DetectedPlayerFocus.Visibility = GetToggleVisibility(title, true);
                    // Force reposition when showing the window
                    if (Dictionary.toggleState[title])
                    {
                        DPWindow.ForceReposition();
                    }
                },
                ["Show Detection Performance"] = () => DPWindow.RefreshPerformanceOverlay(),
                ["Show AI Confidence"] = () => DPWindow.DetectedPlayerConfidence.Visibility = GetToggleVisibility(title, true),
                ["Mouse Background Effect"] = () => { if (!Dictionary.toggleState[title]) RotaryGradient.Angle = 0; },
                ["UI TopMost"] = () => Topmost = Dictionary.toggleState[title],
                ["StreamGuard"] = () =>
                {
                    StreamGuardManager.ApplyStreamGuardToAllWindows(Dictionary.toggleState[title]);
                },
                ["EMA Smoothening"] = () =>
                {
                    MouseManager.IsEMASmoothingEnabled = Dictionary.toggleState[title];
                    if (Dictionary.toggleState[title])
                    {
                        MouseManager.smoothingFactor = Dictionary.sliderSettings["EMA Smoothening"];
                    }
                },
                ["Show Weapon + Scope Info"] = () =>
                {
                    ScopeWindow.SetScalePercent(Dictionary.sliderSettings.TryGetValue("Weapon + Scope Info Size", out var overlaySize)
                        ? Convert.ToDouble(overlaySize) : 82.0);
                    ScopeWindow.SetOpacityPercent(Dictionary.sliderSettings.TryGetValue("Weapon + Scope Info Opacity", out var overlayOpacity)
                        ? Convert.ToDouble(overlayOpacity) : 90.0);
                    ScopeWindow.Show(Dictionary.toggleState[title]);
                },
                ["X Axis Percentage Adjustment"] = () => UpdateSliderVisibility(uiManager),
                ["Y Axis Percentage Adjustment"] = () => UpdateSliderVisibility(uiManager),
                ["Slot 1 X Axis Percentage Adjustment"] = () => UpdateSliderVisibility(uiManager),
                ["Slot 1 Y Axis Percentage Adjustment"] = () => UpdateSliderVisibility(uiManager),
                ["Slot 1 Priority Aiming"] = () => 
                {
                    if (FileManager.AIManager != null)
                        FileManager.AIManager.Slot1AimHead = Dictionary.toggleState[title];
                    PriorityAimingConfig.Save();
                },
                ["Slot 2 Priority Aiming"] = () => 
                {
                    if (FileManager.AIManager != null)
                        FileManager.AIManager.Slot2AimHead = Dictionary.toggleState[title];
                    PriorityAimingConfig.Save();
                },
                ["Mouse Wheel Adjust"] = () => UpdateSliderVisibility(uiManager)
            };

            if (actions.TryGetValue(title, out var action))
            {
                action();
            }
        }
        public static void UpdateSliderVisibility(UI uiManager)
        {
            // === Aim Assist Visibility ===
            bool thresholdEnabled = Dictionary.toggleState["Sticky Aim"];
            bool aimAssistCollapsed = Dictionary.minimizeState.TryGetValue("Aim Assist", out var aaC) && (bool)aaC == true;

            if (uiManager.S_StickyAimThreshold != null)
            {
                uiManager.S_StickyAimThreshold.Visibility = (thresholdEnabled && !aimAssistCollapsed)
                   ? Visibility.Visible 
                   : Visibility.Collapsed;
            }

            if (uiManager.S_TargetLockDuration != null)
            {
                uiManager.S_TargetLockDuration.Visibility = (thresholdEnabled && !aimAssistCollapsed)
                   ? Visibility.Visible 
                   : Visibility.Collapsed;
            }

            // === Slot 2 Visibility ===
            bool slot2Collapsed = Dictionary.minimizeState.TryGetValue("Aim Config (Slot 2)", out var s2C) && (bool)s2C == true;
            bool useYPercent = Dictionary.toggleState["Y Axis Percentage Adjustment"];
            bool useXPercent = Dictionary.toggleState["X Axis Percentage Adjustment"];

            if (slot2Collapsed)
            {
                if (uiManager.S_YOffset != null) uiManager.S_YOffset.Visibility = Visibility.Collapsed;
                if (uiManager.S_YOffsetPercent != null) uiManager.S_YOffsetPercent.Visibility = Visibility.Collapsed;
                if (uiManager.S_XOffset != null) uiManager.S_XOffset.Visibility = Visibility.Collapsed;
                if (uiManager.S_XOffsetPercent != null) uiManager.S_XOffsetPercent.Visibility = Visibility.Collapsed;
            }
            else
            {
                if (uiManager.S_YOffset != null)
                    uiManager.S_YOffset.Visibility = useYPercent ? Visibility.Collapsed : Visibility.Visible;
                if (uiManager.S_YOffsetPercent != null)
                    uiManager.S_YOffsetPercent.Visibility = useYPercent ? Visibility.Visible : Visibility.Collapsed;

                if (uiManager.S_XOffset != null)
                    uiManager.S_XOffset.Visibility = useXPercent ? Visibility.Collapsed : Visibility.Visible;
                if (uiManager.S_XOffsetPercent != null)
                    uiManager.S_XOffsetPercent.Visibility = useXPercent ? Visibility.Visible : Visibility.Collapsed;
            }

            // === Slot 1 Visibility ===
            bool slot1Collapsed = Dictionary.minimizeState.TryGetValue("Aim Config (Slot 1)", out var s1C) && (bool)s1C == true;
            bool s1YPercent = Dictionary.toggleState["Slot 1 Y Axis Percentage Adjustment"];
            bool s1XPercent = Dictionary.toggleState["Slot 1 X Axis Percentage Adjustment"];

            if (slot1Collapsed)
            {
                if (uiManager.S_Slot1YOffset != null) uiManager.S_Slot1YOffset.Visibility = Visibility.Collapsed;
                if (uiManager.S_Slot1YOffsetPercent != null) uiManager.S_Slot1YOffsetPercent.Visibility = Visibility.Collapsed;
                if (uiManager.S_Slot1XOffset != null) uiManager.S_Slot1XOffset.Visibility = Visibility.Collapsed;
                if (uiManager.S_Slot1XOffsetPercent != null) uiManager.S_Slot1XOffsetPercent.Visibility = Visibility.Collapsed;
            }
            else
            {
                // Y Axis
                if (uiManager.S_Slot1YOffset != null)
                    uiManager.S_Slot1YOffset.Visibility = s1YPercent ? Visibility.Collapsed : Visibility.Visible;
                if (uiManager.S_Slot1YOffsetPercent != null)
                    uiManager.S_Slot1YOffsetPercent.Visibility = s1YPercent ? Visibility.Visible : Visibility.Collapsed;

                // X Axis
                if (uiManager.S_Slot1XOffset != null)
                    uiManager.S_Slot1XOffset.Visibility = s1XPercent ? Visibility.Collapsed : Visibility.Visible;
                if (uiManager.S_Slot1XOffsetPercent != null)
                    uiManager.S_Slot1XOffsetPercent.Visibility = s1XPercent ? Visibility.Visible : Visibility.Collapsed;
            }

            // === Mouse Wheel Adjust Step Visibility ===
            if (uiManager.S_MouseWheelAdjustStep != null)
            {
                bool recoilCollapsed = Dictionary.minimizeState.TryGetValue("Recoil Config", out var rc) && (bool)rc == true;
                bool wheelAdjustEnabled = Dictionary.toggleState.ContainsKey("Mouse Wheel Adjust") && Dictionary.toggleState["Mouse Wheel Adjust"];
                uiManager.S_MouseWheelAdjustStep.Visibility = (wheelAdjustEnabled && !recoilCollapsed) ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private Visibility GetToggleVisibility(string title, bool collapsed = false) =>
            Dictionary.toggleState[title]
                ? Visibility.Visible
                : (collapsed ? Visibility.Collapsed : Visibility.Hidden);

        private static void ShowHideDPWindow()
        {
            if (Dictionary.toggleState["Show Detected Player"])
            {
                DPWindow.Show();
                // Force reposition when showing
                DPWindow.ForceReposition();
            }
            else
            {
                DPWindow.Hide();
            }
        }

        #endregion

        #region UI Helper Methods

        public void UpdateToggleUI(AToggle toggle, bool isEnabled)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (isEnabled)
                    toggle.EnableSwitch();
                else
                    toggle.DisableSwitch();
            });
        }

        public ComboBoxItem AddDropdownItem(ADropdown dropdown, string title)
        {
            var dropdownitem = new ComboBoxItem
            {
                Content = title,
                Foreground = Brushes.White,
                FontFamily = TryFindResource("Atkinson Hyperlegible") as FontFamily
            };

            dropdownitem.Selected += (s, e) =>
            {
                var key = dropdown.SettingKey;
                Dictionary.dropdownState[key] = title;
            };

            dropdown.DropdownBox.Items.Add(dropdownitem);
            return dropdownitem;
        }

        #endregion

        #region Keybind Handling

        private volatile bool _isWeaponScanToggled = false;
        private volatile bool _isScopeScanToggled = false;
        private readonly HashSet<Keys> _scanKeysHeldBeforeStart = [];
        private bool _scanStopArmed;
        private CancellationTokenSource? _focusSwitchRecoveryCts;
        private DateTime _lastTemplateCapture = DateTime.MinValue;

        private void ListenForKeybinds()
        {
            bindingManager.OnBindingPressed += HandleKeybindPressed;
            bindingManager.OnBindingReleased += HandleKeybindReleased;
            bindingManager.OnAnyKeyDown += HandleAnyKeyDown;
            bindingManager.OnAnyKeyUp += HandleAnyKeyUp;
        }

        private void StopRecognitionScanning()
        {
            _isWeaponScanToggled = false;
            _isScopeScanToggled = false;
            try { Interlocked.Exchange(ref _focusSwitchRecoveryCts, null)?.Cancel(); }
            catch (ObjectDisposedException) { }
            _scanKeysHeldBeforeStart.Clear();
            _scanStopArmed = false;
            WeaponSlotManager.Instance.StopScanning();
        }

        private void CaptureScanStartKeys()
        {
            _scanKeysHeldBeforeStart.Clear();
            foreach (Keys key in bindingManager.GetPressedKeyboardKeys())
            {
                bool scanKey =
                    (_isWeaponScanToggled && bindingManager.IsKeyPartOfBinding("Weapon Scan Keybind", key)) ||
                    (_isScopeScanToggled && bindingManager.IsKeyPartOfBinding("Scope Scan Keybind", key));
                if (!scanKey) _scanKeysHeldBeforeStart.Add(key);
            }
            _scanStopArmed = _scanKeysHeldBeforeStart.Count == 0;
        }

        private void HandleAnyKeyDown(Keys key)
        {
            if (!_isWeaponScanToggled && !_isScopeScanToggled) return;

            // Alt+Tab is a Windows focus switch, not the user's "other key stops scan"
            // command. Alt arrives before Tab, so both parts of the chord must be ignored.
            if (IsAltModifierKey(key)) return;
            if (IsAltTabKey(key))
            {
                ScheduleFocusSwitchRecovery();
                return;
            }

            bool belongsToActiveScan =
                (_isWeaponScanToggled && bindingManager.IsKeyPartOfBinding("Weapon Scan Keybind", key)) ||
                (_isScopeScanToggled && bindingManager.IsKeyPartOfBinding("Scope Scan Keybind", key));
            if (!belongsToActiveScan && _scanStopArmed) StopRecognitionScanning();
        }

        private bool IsAltTabKey(Keys key)
        {
            if (key != Keys.Tab) return false;
            if ((System.Windows.Forms.Control.ModifierKeys & Keys.Alt) == Keys.Alt) return true;
            return bindingManager.GetPressedKeyboardKeys().Any(IsAltModifierKey);
        }

        private static bool IsAltModifierKey(Keys key) =>
            key is Keys.Menu or Keys.LMenu or Keys.RMenu;

        private void ScheduleFocusSwitchRecovery()
        {
            var recovery = new CancellationTokenSource();
            CancellationTokenSource? previous = Interlocked.Exchange(ref _focusSwitchRecoveryCts, recovery);
            try { previous?.Cancel(); } catch (ObjectDisposedException) { }
            _ = RecoverAfterFocusSwitchAsync(recovery);
        }

        private async Task RecoverAfterFocusSwitchAsync(CancellationTokenSource recovery)
        {
            try
            {
                // Let Windows finish changing the fullscreen foreground surface first.
                await Task.Delay(350, recovery.Token).ConfigureAwait(false);
                if (recovery.IsCancellationRequested) return;
                global::AILogic.CaptureManager.RefreshAfterForegroundSwitch();
                WeaponSlotManager.Instance.OnForegroundRestored(_isWeaponScanToggled, _isScopeScanToggled);
            }
            catch (OperationCanceledException) { }
            finally
            {
                Interlocked.CompareExchange(ref _focusSwitchRecoveryCts, null, recovery);
                recovery.Dispose();
            }
        }

        private void HandleAnyKeyUp(Keys key)
        {
            if (!_isWeaponScanToggled && !_isScopeScanToggled) return;
            _scanKeysHeldBeforeStart.Remove(key);
            if (_scanKeysHeldBeforeStart.Count == 0) _scanStopArmed = true;
        }

        private void HandleRecognitionScanKey(bool scope)
        {
            string requestedBinding = scope ? "Scope Scan Keybind" : "Weapon Scan Keybind";
            // Keep plain Tab from reacting to Alt+Tab, while allowing movement keys and
            // Shift/Ctrl that were already held before the scan key.
            if (!bindingManager.IsScanBindingHeld(requestedBinding))
            {
                // Ignore Alt+Tab (or another modifier mismatch) without tearing down a
                // scan already in progress. The next real scan key can resume immediately.
                return;
            }

            string weaponBinding = bindingManager.GetBinding("Weapon Scan Keybind");
            string scopeBinding = bindingManager.GetBinding("Scope Scan Keybind");
            bool sharedBinding = weaponBinding != "None" && string.Equals(weaponBinding, scopeBinding, StringComparison.OrdinalIgnoreCase);
            if (sharedBinding && scope) return; // The weapon binding callback handles the combined scan once.
            if (sharedBinding)
            {
                bool scanWeapons = Dictionary.toggleState.GetValueOrDefault("Weapon Recognition");
                bool scanScopes = Dictionary.toggleState.GetValueOrDefault("Scope Recognition");
                if (!scanWeapons && !scanScopes) return;
                bool sharedToggleMode = Dictionary.toggleState.GetValueOrDefault("Toggle Weapon Scan") || Dictionary.toggleState.GetValueOrDefault("Toggle Scope Scan");
                if (sharedToggleMode && (_isWeaponScanToggled || _isScopeScanToggled))
                {
                    StopRecognitionScanning();
                    return;
                }
                WeaponSlotManager.Instance.StopScanning();
                _isWeaponScanToggled = scanWeapons && sharedToggleMode;
                _isScopeScanToggled = scanScopes && sharedToggleMode;
                if (sharedToggleMode) CaptureScanStartKeys();
                WeaponSlotManager.Instance.OnScanPressed(scanWeapons, scanScopes, sharedToggleMode);
                return;
            }
            string recognitionKey = scope ? "Scope Recognition" : "Weapon Recognition";
            if (!Dictionary.toggleState.GetValueOrDefault(recognitionKey)) return;
            string toggleKey = scope ? "Toggle Scope Scan" : "Toggle Weapon Scan";
            bool toggleMode = Dictionary.toggleState.GetValueOrDefault(toggleKey);
            if (scope)
            {
                if (toggleMode && _isScopeScanToggled) { StopRecognitionScanning(); return; }
                WeaponSlotManager.Instance.StopScanning();
                _isWeaponScanToggled = false;
                _isScopeScanToggled = toggleMode;
            }
            else
            {
                if (toggleMode && _isWeaponScanToggled) { StopRecognitionScanning(); return; }
                WeaponSlotManager.Instance.StopScanning();
                _isScopeScanToggled = false;
                _isWeaponScanToggled = toggleMode;
            }
            if (toggleMode) CaptureScanStartKeys();
            WeaponSlotManager.Instance.OnScanPressed(!scope, scope, toggleMode);
        }

        private void HandleRecognitionScanReleased(bool scope)
        {
            string weaponBinding = bindingManager.GetBinding("Weapon Scan Keybind");
            string scopeBinding = bindingManager.GetBinding("Scope Scan Keybind");
            bool sharedBinding = weaponBinding != "None" && string.Equals(weaponBinding, scopeBinding, StringComparison.OrdinalIgnoreCase);
            if (sharedBinding && scope) return;
            bool toggleMode = sharedBinding
                ? Dictionary.toggleState.GetValueOrDefault("Toggle Weapon Scan") || Dictionary.toggleState.GetValueOrDefault("Toggle Scope Scan")
                : Dictionary.toggleState.GetValueOrDefault(scope ? "Toggle Scope Scan" : "Toggle Weapon Scan");
            WeaponSlotManager.Instance.OnScanReleased(toggleMode);
        }

        private void HandleKeybindPressed(string bindingId)
        {
            var handlers = new Dictionary<string, Action>
            {
                ["Model Switch Keybind"] = HandleModelSwitch,
                ["Dynamic FOV Keybind"] = () => ApplyDynamicFOV(true),
                ["Emergency Stop Keybind"] = HandleEmergencyStop,
                ["Recoil Toggle Keybind"] = () => ToggleToggle("Scope Recoil Control", uiManager.T_ScopeRecoil),

                ["Weapon Scan Keybind"] = () => HandleRecognitionScanKey(false),
                ["Scope Scan Keybind"] = () => HandleRecognitionScanKey(true),
                ["Weapon Slot 1 Keybind"] = () => { WeaponSlotManager.Instance.HandleKeyPress(Keys.D1); },
                ["Weapon Slot 2 Keybind"] = () => { WeaponSlotManager.Instance.HandleKeyPress(Keys.D2); },
                ["Fast Loot Keybind"] = () => { }
            };

            handlers.GetValueOrDefault(bindingId)?.Invoke();
        }

        public async Task CaptureRecognitionTemplateAsync(bool scope, int slot)
        {
            if ((DateTime.UtcNow - _lastTemplateCapture).TotalMilliseconds < 300) return;
            _lastTemplateCapture = DateTime.UtcNow;
            slot = Math.Clamp(slot, 1, 2);
            var manager = WeaponSlotManager.Instance;
            var state = manager.GetSlotSnapshot(slot);
            var region = scope ? state.ScopeRegion : state.WeaponRegion;
            if (region.IsEmpty)
            {
                LocalizedMessageBox.Show(scope ? "Chưa chọn vùng Scope cho slot này." : "Chưa chọn vùng tên Súng cho slot này.");
                return;
            }
            var visibleWindows = Application.Current?.Windows.OfType<Window>()
                .Where(window => window.IsVisible).ToArray() ?? Array.Empty<Window>();
            System.Drawing.Bitmap? image = null;
            try
            {
                foreach (var window in visibleWindows) window.Hide();
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                await Task.Delay(180);
                image = manager.CaptureTemplateRegion(slot, scope);
            }
            finally
            {
                foreach (var window in visibleWindows) window.Show();
                Activate();
            }
            if (image == null) return;
            using (image)
            {
                var config = manager.RecognitionConfig;
                string remembered = scope ? (slot == 1 ? config.ScopeLabelSlot1 : config.ScopeLabelSlot2) : (slot == 1 ? config.WeaponLabelSlot1 : config.WeaponLabelSlot2);
                bool remember = scope ? config.RememberScopeLabel : config.RememberWeaponLabel;
                if (remember && !config.AlwaysAskName && !string.IsNullOrWhiteSpace(remembered))
                {
                    var quality = Aimmy2.AILogic.Recognition.TemplateQualityAnalyzer.Analyze(image, scope ? Aimmy2.AILogic.Recognition.TemplateKind.Scope : Aimmy2.AILogic.Recognition.TemplateKind.Weapon);
                    if (!quality.CanSave) { LocalizedMessageBox.Show(quality.Message); return; }
                    if (!quality.HasWarning || !config.ConfirmBeforeSave || MessageBox.Show(quality.Message + ". Vẫn lưu?", "Template", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                        manager.SaveTemplate(scope, remembered, image);
                    return;
                }
                var dialog = new TemplateCaptureDialog(image, scope, slot, region) { Owner = this };
                dialog.ShowDialog();
            }
        }

        private void HandleKeybindReleased(string bindingId)
        {
            var handlers = new Dictionary<string, Action>
            {
                ["Dynamic FOV Keybind"] = () => ApplyDynamicFOV(false),
                ["Weapon Scan Keybind"] = () => HandleRecognitionScanReleased(false),
                ["Scope Scan Keybind"] = () => HandleRecognitionScanReleased(true)
            };

            handlers.GetValueOrDefault(bindingId)?.Invoke();
        }

        private void HandleModelSwitch()
        {
            if (!Dictionary.toggleState["Enable Model Switch Keybind"] || FileManager.CurrentlyLoadingModel)
                return;

            if (_menuControls["ModelMenu"] is ModelMenuControl modelMenu)
            {
                var modelListBox = modelMenu.ModelListBoxControl;
                modelListBox.SelectedIndex = (modelListBox.SelectedIndex >= 0 &&
                    modelListBox.SelectedIndex < modelListBox.Items.Count - 1)
                    ? modelListBox.SelectedIndex + 1
                    : 0;
            }
        }

        private void ApplyDynamicFOV(bool apply)
        {
            if (!Dictionary.toggleState["Dynamic FOV"])
            {
                FOVWindow.Circle.BeginAnimation(FrameworkElement.WidthProperty, null);
                FOVWindow.Circle.BeginAnimation(FrameworkElement.HeightProperty, null);
                FOVWindow.RectangleShape.BeginAnimation(FrameworkElement.WidthProperty, null);
                FOVWindow.RectangleShape.BeginAnimation(FrameworkElement.HeightProperty, null);

                FOVWindow.UpdateFOVSize(ActualFOV);
                return;
            }
            var targetSize = apply ? Convert.ToDouble(Dictionary.sliderSettings["Dynamic FOV Size"]) : ActualFOV;
            Dictionary.sliderSettings["FOV Size"] = targetSize;
            AnimateFOVSize(targetSize);
        }
        /* Old
        private void ApplyDynamicFOV(bool apply)
        {
            if (!Dictionary.toggleState["Dynamic FOV"]) return;

            var targetSize = apply ? Convert.ToDouble(Dictionary.sliderSettings["Dynamic FOV Size"]) : ActualFOV;
            Dictionary.sliderSettings["FOV Size"] = targetSize;

            AnimateFOVSize(targetSize);
        }
        */
        private void AnimateFOVSize(double targetSize)
        {
            targetSize = Math.Clamp(targetSize, 10, FovSettings.ImageSize);
            var duration = TimeSpan.FromMilliseconds(500);
            Animator.WidthShift(duration, FOVWindow.Circle, FOVWindow.Circle.ActualWidth, targetSize / WinAPICaller.scalingFactorX);
            Animator.HeightShift(duration, FOVWindow.Circle, FOVWindow.Circle.ActualHeight, targetSize / WinAPICaller.scalingFactorY);
            Animator.WidthShift(duration, FOVWindow.RectangleShape, FOVWindow.RectangleShape.ActualWidth, targetSize / WinAPICaller.scalingFactorX);
            Animator.HeightShift(duration, FOVWindow.RectangleShape, FOVWindow.RectangleShape.ActualHeight, targetSize / WinAPICaller.scalingFactorY);
        }
        /* Old
        private void AnimateFOVSize(double targetSize)
        {
            var duration = TimeSpan.FromMilliseconds(500);
            Animator.WidthShift(duration, FOVWindow.Circle, FOVWindow.Circle.ActualWidth, targetSize);
            Animator.HeightShift(duration, FOVWindow.Circle, FOVWindow.Circle.ActualHeight, targetSize);
        }
        */
        private void HandleEmergencyStop()
        {
            var features = new[] { "Aim Assist", "Constant AI Tracking", "Auto Trigger" };
            var toggles = new[] { uiManager.T_AimAligner, uiManager.T_ConstantAITracking, uiManager.T_AutoTrigger };

            for (int i = 0; i < features.Length; i++)
            {
                Dictionary.toggleState[features[i]] = false;
                if (toggles[i] != null)
                    UpdateToggleUI(toggles[i], false);
            }
            LogManager.Log(LogManager.LogLevel.Info, "[Emergency Stop Keybind] Disabled all AI features.", true);
        }

        #endregion

        #region UI Effects

        private void Main_Background_Gradient(object sender, MouseEventArgs e)
        {
            if (!Dictionary.toggleState["Mouse Background Effect"]) return;

            var mousePosition = WinAPICaller.GetCursorPosition();
            var translatedMousePos = PointFromScreen(new Point(mousePosition.X, mousePosition.Y));

            var targetAngle = Math.Atan2(
                translatedMousePos.Y - (MainBorder.ActualHeight * 0.5),
                translatedMousePos.X - (MainBorder.ActualWidth * 0.5)) * (180 / Math.PI);

            _currentGradientAngle = CalculateSmoothedAngle(targetAngle);
            RotaryGradient.Angle = _currentGradientAngle;
        }

        private double CalculateSmoothedAngle(double targetAngle)
        {
            const double fullCircle = 360;
            const double halfCircle = 180;
            const double clamp = 1;

            var angleDifference = (targetAngle - _currentGradientAngle + fullCircle) % fullCircle;
            if (angleDifference > halfCircle)
                angleDifference -= fullCircle;

            var clampedDifference = Math.Max(Math.Min(angleDifference, clamp), -clamp);
            return (_currentGradientAngle + clampedDifference + fullCircle) % fullCircle;
        }

        #endregion

        #region Configuration Management

        private void LoadDropdownStates()
        {
            CapturePreferences.Load();

            var dropdownConfigs = new[]
            {
                // AimMenu dropdowns
                (uiManager.D_PredictionMethod, "Prediction Method", new Dictionary<string, int>
                {
                    ["Kalman Filter"] = 0,
                    ["Shall0e's Prediction"] = 1,
                    ["wisethef0x's EMA Prediction"] = 2
                }),
                (uiManager.D_DetectionAreaType, "Detection Area Type", new Dictionary<string, int>
                {
                    ["Closest to Center Screen"] = 0,
                    ["Closest to Mouse"] = 1
                }),
                (uiManager.D_AimingBoundariesAlignment, "Aiming Boundaries Alignment", new Dictionary<string, int>
                {
                    ["Center"] = 0,
                    ["Top"] = 1,
                    ["Bottom"] = 2
                }),
                // SettingsMenu dropdowns
                (uiManager.D_MouseMovementMethod, "Mouse Movement Method", new Dictionary<string, int>
                {
                    ["Mouse Event"] = 0,
                    ["SendInput"] = 1,
                    ["LG HUB"] = 2,
                    ["Razer Synapse (Require Razer Peripheral)"] = 3,
                    ["ddxoft Virtual Input Driver"] = 4
                }),
                (uiManager.D_ScreenCaptureMethod, "Screen Capture Method", new Dictionary<string, int>
                {
                    ["DirectX"] = 0,
                    ["GDI+"] = 1,
                    ["WGC"] = 2
                }),
                (uiManager.D_ScopeCaptureMethod, "Scope Capture Method", new Dictionary<string, int>
                {
                    ["DirectX"] = 0,
                    ["GDI+"] = 1,
                    ["WGC"] = 2
                }),
                (uiManager.D_Slot1ImageSize, "Slot 1 Image Size", new Dictionary<string, int>
                {
                    ["640"] = 0,
                    ["512"] = 1,
                    ["416"] = 2,
                    ["320"] = 3,
                    ["256"] = 4,
                    ["160"] = 5
                }),
                (uiManager.D_Slot2ImageSize, "Slot 2 Image Size", new Dictionary<string, int>
                {
                    ["640"] = 0,
                    ["512"] = 1,
                    ["416"] = 2,
                    ["320"] = 3,
                    ["256"] = 4,
                    ["160"] = 5
                }),
                // Slot 1 Dropdowns
                 (uiManager.D_Slot1MouseMovementMethod, "Slot 1 Mouse Movement Method", new Dictionary<string, int>
                {
                    ["Mouse Event"] = 0,
                    ["SendInput"] = 1,
                    ["LG HUB"] = 2,
                    ["Razer Synapse (Require Razer Peripheral)"] = 3,
                    ["ddxoft Virtual Input Driver"] = 4
                }),
                (uiManager.D_Slot1MovementPath, "Slot 1 Movement Path", new Dictionary<string, int>
                {
                    ["Cubic Bezier"] = 0,
                    ["Exponential"] = 1,
                    ["Linear"] = 2,
                    ["Adaptive"] = 3,
                    ["Perlin Noise"] = 4
                }),
                (uiManager.D_Slot1DetectionAreaType, "Slot 1 Detection Area Type", new Dictionary<string, int>
                {
                    ["Closest to Center Screen"] = 0,
                    ["Closest to Mouse"] = 1
                }),
                (uiManager.D_Slot1AimingBoundariesAlignment, "Slot 1 Aiming Boundaries Alignment", new Dictionary<string, int>
                {
                    ["Center"] = 0,
                    ["Top"] = 1,
                    ["Bottom"] = 2
                })
            };

            foreach (var (dropdown, key, mappings) in dropdownConfigs)
            {
                if (dropdown == null)
                {
                    continue;
                }

                if (Dictionary.dropdownState.TryGetValue(key, out var value))
                {
                    var stringValue = value?.ToString() ?? "";

                    if (mappings.TryGetValue(stringValue, out int index))
                    {
                        dropdown.DropdownBox.SelectedIndex = index;
                    }
                    else
                    {
                        LogManager.Log(LogManager.LogLevel.Warning, $"No mapping found for '{stringValue}' in '{key}' dropdown.");
                    }
                }
            }

            // Update slider visibility based on loaded states
            UpdatePredictionSliderVisibility(uiManager);
            UpdateAimAssistSliderVisibility();
            UpdateAimConfigSliderVisibility();
        }

        private void LoadConfig(string path = "bin\\configs\\Default.cfg", bool loading_from_configlist = false)
        {
            InputLogic.LootManager.PrepareForConfigLoad();
            SaveDictionary.LoadJSON(Dictionary.sliderSettings, path, false);
            InputLogic.LootManager.PostConfigLoad();
            SaveDictionary.LoadJSON(Dictionary.dropdownState, path, false);
            PriorityAimingConfig.Load();

            if (!loading_from_configlist || _menuControls["AimMenu"] == null || !_menuInitialized["AimMenu"])
                return;

            try
            {
                ShowSuggestedModelIfSpecified();
                ApplyConfigToSliders();
                ApplyConfigToDropdowns();

                if (_menuControls["AimMenu"] is AimMenuControl aimMenu)
                {
                    aimMenu.RefreshRecoilConfig();
                }
            }
            catch (Exception e)
            {
                global::Other.LocalizedMessageBox.Show($"Error loading config, possibly outdated\n{e}");
            }
        }

        private void ShowSuggestedModelIfSpecified()
        {
            if (Dictionary.sliderSettings.TryGetValue("Suggested Model", out var model))
            {
                var suggestedModel = model?.ToString() ?? "N/A";
                if (suggestedModel != "N/A" && !string.IsNullOrEmpty(suggestedModel))
                {
                    global::Other.LocalizedMessageBox.Show(
                        $"The creator of this model suggests you use this model:\n{suggestedModel}",
                        "Suggested Model - Aimmy");
                }
            }
        }

        private void ApplyConfigToSliders()
        {
            (string key, ASlider? slider, double defaultValue)[] sliderConfigs = new[]
            {
                ("FOV Size", uiManager.S_FOVSize, 640.0),
                ("Mouse Sensitivity (+/-)", uiManager.S_MouseSensitivity, 0.8),
                ("Mouse Jitter", uiManager.S_MouseJitter, 0.0),
                ("Sticky Aim Threshold", uiManager.S_StickyAimThreshold, 50.0),
                ("Target Lock Duration", uiManager.S_TargetLockDuration, 500.0),
                ("EMA Smoothening", uiManager.S_EMASmoothing, 0.5),
                ("Y Offset (Up/Down)", uiManager.S_YOffset, 0.0),
                ("X Offset (Left/Right)", uiManager.S_XOffset, 0.0),
                ("Y Offset (%)", uiManager.S_YOffsetPercent, 0.0),
                ("X Offset (%)", uiManager.S_XOffsetPercent, 0.0),
                ("Auto Trigger Delay", uiManager.S_AutoTriggerDelay, 0.25),
                ("AI Minimum Confidence", uiManager.S_Slot1AIMinimumConfidence, 50.0),
                ("Slot 2 AI Minimum Confidence", uiManager.S_Slot2AIMinimumConfidence, 50.0),
                ("Kalman Lead Time", uiManager.S_KalmanLeadTime, 0.10),
                ("WiseTheFox Lead Time", uiManager.S_WiseTheFoxLeadTime, 0.15),
                ("Shalloe Lead Multiplier", uiManager.S_ShalloeLeadMultiplier, 3.0),
                ("Scope Confidence", uiManager.S_ScopeConfidence, 45.0),
                ("Weapon Scan Delay", uiManager.S_WeaponScanDelay, 2.0),
                ("Crosshair Size", uiManager.S_CrosshairSize, 6.0),
                ("Mouse Wheel Adjust Step", uiManager.S_MouseWheelAdjustStep, 2.0),
                ("Weapon + Scope Info Size", uiManager.S_WeaponScopeInfoSize, 82.0),
                ("Weapon + Scope Info Opacity", uiManager.S_WeaponScopeInfoOpacity, 90.0)
            };

            ApplySliderValues(sliderConfigs, Dictionary.sliderSettings);
        }


        private void ApplyConfigToDropdowns()
        {
            (string key, ADropdown? dropdown, Dictionary<string, int> mappings)[] dropdownConfigs = new[]
            {

                ("Prediction Method", uiManager.D_PredictionMethod, new Dictionary<string, int>
                {
                    ["Kalman Filter"] = 0,
                    ["Shall0e's Prediction"] = 1,
                    ["wisethef0x's EMA Prediction"] = 2
                }),

                ("Detection Area Type", uiManager.D_DetectionAreaType, new Dictionary<string, int>
                {
                    ["Closest to Center Screen"] = 0,
                    ["Closest to Mouse"] = 1
                }),

                ("Aiming Boundaries Alignment", uiManager.D_AimingBoundariesAlignment, new Dictionary<string, int>
                {
                    ["Center"] = 0,
                    ["Top"] = 1,
                    ["Bottom"] = 2
                }),

                ("Mouse Movement Method", uiManager.D_MouseMovementMethod, new Dictionary<string, int>
                {
                    ["Mouse Event"] = 0,
                    ["SendInput"] = 1,
                    ["LG HUB"] = 2,
                    ["Razer Synapse (Require Razer Peripheral)"] = 3,
                    ["ddxoft Virtual Input Driver"] = 4
                }),

                ("Movement Path", uiManager.D_MovementPath, new Dictionary<string, int>
                {
                    ["Cubic Bezier"] = 0,
                    ["Exponential"] = 1,
                    ["Linear"] = 2,
                    ["Adaptive"] = 3,
                    ["Perlin Noise"] = 4
                }),

                ("Tracer Position", uiManager.D_TracerPosition, new Dictionary<string, int>
                {
                    ["Bottom"] = 0,
                    ["Middle"] = 1,
                    ["Top"] = 2,
                }),

                ("Slot 1 Target Class", uiManager.D_Slot1TargetClass, new Dictionary<string, int>
                {
                    ["Best Confidence"] = 0,
                }),

                ("Slot 2 Target Class", uiManager.D_Slot2TargetClass, new Dictionary<string, int>
                {
                    ["Best Confidence"] = 0,
                }),

                // Slot 1 Dropdowns
                ("Slot 1 Mouse Movement Method", uiManager.D_Slot1MouseMovementMethod, new Dictionary<string, int>
                {
                    ["Mouse Event"] = 0,
                    ["SendInput"] = 1,
                    ["LG HUB"] = 2,
                    ["Razer Synapse (Require Razer Peripheral)"] = 3,
                    ["ddxoft Virtual Input Driver"] = 4
                }),

                ("Slot 1 Movement Path", uiManager.D_Slot1MovementPath, new Dictionary<string, int>
                {
                    ["Cubic Bezier"] = 0,
                    ["Exponential"] = 1,
                    ["Linear"] = 2,
                    ["Adaptive"] = 3,
                    ["Perlin Noise"] = 4
                }),
                
                 ("Slot 1 Detection Area Type", uiManager.D_Slot1DetectionAreaType, new Dictionary<string, int>
                {
                    ["Closest to Center Screen"] = 0,
                    ["Closest to Mouse"] = 1
                }),

                ("Slot 1 Aiming Boundaries Alignment", uiManager.D_Slot1AimingBoundariesAlignment, new Dictionary<string, int>
                {
                    ["Center"] = 0,
                    ["Top"] = 1,
                    ["Bottom"] = 2
                })
            };

            ApplyDropdownValues(dropdownConfigs, Dictionary.dropdownState);

            // Update prediction slider visibility based on selected method
            UpdatePredictionSliderVisibility(uiManager);
        }

        public static void UpdatePredictionSliderVisibility(UI uiManager)
        {
            // Hide all prediction sliders first
            if (uiManager.S_KalmanLeadTime != null)
                uiManager.S_KalmanLeadTime.Visibility = Visibility.Collapsed;
            if (uiManager.S_WiseTheFoxLeadTime != null)
                uiManager.S_WiseTheFoxLeadTime.Visibility = Visibility.Collapsed;
            if (uiManager.S_ShalloeLeadMultiplier != null)
                uiManager.S_ShalloeLeadMultiplier.Visibility = Visibility.Collapsed;
            if (uiManager.S_CALeadMultiplier != null)
                uiManager.S_CALeadMultiplier.Visibility = Visibility.Collapsed;
 
            // Don't show sliders if Predictions section is collapsed
            if (Dictionary.minimizeState.TryGetValue("Predictions", out var collapsed) && (bool)collapsed == true)
                return;
 
            // Get selected method from actual dropdown selection
            var selectedItem = uiManager.D_PredictionMethod?.DropdownBox?.SelectedItem as ComboBoxItem;
            string selectedMethod = selectedItem?.Content?.ToString() ?? "";
 
            // Show only the relevant slider based on selected method
            switch (selectedMethod)
            {
                case "Kalman Filter":
                    if (uiManager.S_KalmanLeadTime != null)
                        uiManager.S_KalmanLeadTime.Visibility = Visibility.Visible;
                    break;
                case "Shall0e's Prediction":
                    if (uiManager.S_ShalloeLeadMultiplier != null)
                        uiManager.S_ShalloeLeadMultiplier.Visibility = Visibility.Visible;
                    break;
                case "wisethef0x's EMA Prediction":
                    if (uiManager.S_WiseTheFoxLeadTime != null)
                        uiManager.S_WiseTheFoxLeadTime.Visibility = Visibility.Visible;
                    break;
                case "Constant Acceleration":
                    if (uiManager.S_CALeadMultiplier != null)
                        uiManager.S_CALeadMultiplier.Visibility = Visibility.Visible;
                    break;
            }
        }

        public void UpdateAimAssistSliderVisibility()
        {
            // Don't show sliders if Aim Assist section is collapsed
            if (Dictionary.minimizeState.TryGetValue("Aim Assist", out var collapsed) && collapsed == true)
            {
                if (uiManager.S_StickyAimThreshold != null)
                    uiManager.S_StickyAimThreshold.Visibility = Visibility.Collapsed;
                return;
            }

            // Show Sticky Aim Threshold only if Sticky Aim is enabled
            if (uiManager.S_StickyAimThreshold != null)
            {
                uiManager.S_StickyAimThreshold.Visibility = Dictionary.toggleState["Sticky Aim"]
                    ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        public void UpdateAimConfigSliderVisibility()
        {
            // === Slot 2 Visibility ===
            if (Dictionary.minimizeState.TryGetValue("Aim Config (Slot 2)", out var collapsed2) && collapsed2 == true)
            {
                if (uiManager.S_YOffset != null) uiManager.S_YOffset.Visibility = Visibility.Collapsed;
                if (uiManager.S_YOffsetPercent != null) uiManager.S_YOffsetPercent.Visibility = Visibility.Collapsed;
                if (uiManager.S_XOffset != null) uiManager.S_XOffset.Visibility = Visibility.Collapsed;
                if (uiManager.S_XOffsetPercent != null) uiManager.S_XOffsetPercent.Visibility = Visibility.Collapsed;
            }
            else
            {
                // Y Axis
                bool yPercentEnabled = Dictionary.toggleState["Y Axis Percentage Adjustment"];
                if (uiManager.S_YOffset != null)
                    uiManager.S_YOffset.Visibility = yPercentEnabled ? Visibility.Collapsed : Visibility.Visible;
                if (uiManager.S_YOffsetPercent != null)
                    uiManager.S_YOffsetPercent.Visibility = yPercentEnabled ? Visibility.Visible : Visibility.Collapsed;

                // X Axis
                bool xPercentEnabled = Dictionary.toggleState["X Axis Percentage Adjustment"];
                if (uiManager.S_XOffset != null)
                    uiManager.S_XOffset.Visibility = xPercentEnabled ? Visibility.Collapsed : Visibility.Visible;
                if (uiManager.S_XOffsetPercent != null)
                    uiManager.S_XOffsetPercent.Visibility = xPercentEnabled ? Visibility.Visible : Visibility.Collapsed;
            }

            // === Slot 1 Visibility ===
            if (Dictionary.minimizeState.TryGetValue("Aim Config (Slot 1)", out var collapsed1) && collapsed1 == true)
            {
                // If collapsed, hide all offset sliders for Slot 1
                if (uiManager.S_Slot1YOffset != null) uiManager.S_Slot1YOffset.Visibility = Visibility.Collapsed;
                if (uiManager.S_Slot1YOffsetPercent != null) uiManager.S_Slot1YOffsetPercent.Visibility = Visibility.Collapsed;
                if (uiManager.S_Slot1XOffset != null) uiManager.S_Slot1XOffset.Visibility = Visibility.Collapsed;
                if (uiManager.S_Slot1XOffsetPercent != null) uiManager.S_Slot1XOffsetPercent.Visibility = Visibility.Collapsed;
                // Note: Toggles should remain visible unless inside a collapsed parent that hides them, 
                // but the Minimize logic in AimMenuControl usually hides the whole Panel content. 
                // The SectionBuilder puts everything in the panel.
                // The Sliders.Visibility logic here is for switching between Pixel/Percent modes.
                // If the whole panel is collapsed, everything is hidden by SetPanelVisibility anyway.
                // However, we must ensure that when EXPANDED, we show the CORRECT slider.
            }
            else
            {
                // Slot 1 Y Axis
                bool s1YPercent = Dictionary.toggleState["Slot 1 Y Axis Percentage Adjustment"];
                if (uiManager.S_Slot1YOffset != null)
                    uiManager.S_Slot1YOffset.Visibility = s1YPercent ? Visibility.Collapsed : Visibility.Visible;
                if (uiManager.S_Slot1YOffsetPercent != null)
                    uiManager.S_Slot1YOffsetPercent.Visibility = s1YPercent ? Visibility.Visible : Visibility.Collapsed;

                // Slot 1 X Axis
                bool s1XPercent = Dictionary.toggleState["Slot 1 X Axis Percentage Adjustment"];
                if (uiManager.S_Slot1XOffset != null)
                    uiManager.S_Slot1XOffset.Visibility = s1XPercent ? Visibility.Collapsed : Visibility.Visible;
                if (uiManager.S_Slot1XOffsetPercent != null)
                    uiManager.S_Slot1XOffsetPercent.Visibility = s1XPercent ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void ApplySliderValues((string key, ASlider? slider, double defaultValue)[] configs, Dictionary<string, dynamic> source)
        {
            foreach (var (key, slider, defaultValue) in configs)
            {
                if (slider != null && source.TryGetValue(key, out var value))
                {
                    slider.Slider.Value = Convert.ToDouble(value);
                }
                else if (slider != null)
                {
                    slider.Slider.Value = defaultValue;
                }
            }
        }

        private void ApplyDropdownValues((string key, ADropdown? dropdown, Dictionary<string, int> mappings)[] configs, Dictionary<string, dynamic> source)
        {
            foreach (var (key, dropdown, mappings) in configs)
            {
                if (dropdown != null && source.TryGetValue(key, out var value))
                {
                    var stringValue = value?.ToString() ?? "";
                    if (mappings.TryGetValue(stringValue, out int index))
                    {
                        dropdown.DropdownBox.SelectedIndex = index;
                    }
                    else
                    {
                        LogManager.Log(LogManager.LogLevel.Warning, $"No mapping found for '{stringValue}' in '{key}' dropdown.");
                    }
                }
            }
        }

        #endregion

        #region System Information

        private static string? GetProcessorName() => GetSpecs.GetSpecification("Win32_Processor", "Name");
        private static string? GetVideoControllerName() => GetSpecs.GetSpecification("Win32_VideoController", "Name");
        private static string? GetFormattedMemorySize()
        {
            var totalMemorySize = long.Parse(GetSpecs.GetSpecification("CIM_OperatingSystem", "TotalVisibleMemorySize")!);
            return Math.Round(totalMemorySize / (1024.0 * 1024.0), 0).ToString();
        }

        #endregion

        private async Task ValidateAndRestoreModels()
        {
            // 1. Recover state from Dictionary
            string savedSlot1 = Dictionary.modelState.GetValueOrDefault("Slot1", "N/A");
            string savedSlot2 = Dictionary.modelState.GetValueOrDefault("Slot2", "N/A");

            // 2. Validate Slot 1
            if (savedSlot1 != "N/A")
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "models", "Slot1", savedSlot1);
                if (File.Exists(path))
                {
                    // Do NOT set Dictionary.lastLoadedModel here. 
                    // Let the SelectionChanged event handle it, otherwise the event will exit early.
                    
                    // Trigger selection if Menu is initialized
                    if (_menuControls["ModelMenu"] is ModelMenuControl mm)
                    {
                        foreach(var item in mm.Slot1ModelListBoxControl.Items)
                        {
                            if (item.ToString() == savedSlot1)
                            {
                                mm.Slot1ModelListBoxControl.SelectedItem = item;
                                break;
                            }
                        }
                    }
                    
                    // Wait for Slot 1 loading to finish
                    while (FileManager.CurrentlyLoadingModel)
                    {
                        await Task.Delay(100);
                    }
                }
                else
                {
                    Dictionary.lastLoadedModel = "N/A"; // Force reset
                }
            }

            // 3. Validate Slot 2
            if (savedSlot2 != "N/A")
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "models", "Slot2", savedSlot2);
                if (File.Exists(path))
                {
                    // Do NOT set Dictionary.lastLoadedModelSlot2 here.

                    if (_menuControls["ModelMenu"] is ModelMenuControl mm)
                    {
                        foreach (var item in mm.ModelListBoxControl.Items)
                        {
                            if (item.ToString() == savedSlot2)
                            {
                                mm.ModelListBoxControl.SelectedItem = item;
                                break;
                            }
                        }
                    }
                    
                    // Wait for Slot 2 loading to finish
                    while (FileManager.CurrentlyLoadingSecondaryModel)
                    {
                        await Task.Delay(100);
                    }
                }
                else
                {
                    Dictionary.lastLoadedModelSlot2 = "N/A"; // Force reset
                }
            }

            // 4. Validate Scope Model
            string scopePath = Dictionary.filelocationState.TryGetValue("Scope Model Location", out var loc) ? loc.ToString() : "";
            if (!string.IsNullOrEmpty(scopePath) && scopePath != "N/A")
            {
                string path = scopePath;
                if (!Path.IsPathRooted(path))
                {
                    path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);
                }

                if (File.Exists(path))
                {
                    // Initialize WeaponSlotManager (this calls LoadModel)
                    WeaponSlotManager.Instance.Initialize();

                    // Wait for Scope model loading to finish
                    while (WeaponSlotManager.CurrentlyLoadingScopeModel)
                    {
                        await Task.Delay(100);
                    }
                }
            }
        }


        
        private void ToggleToggle(string key, AToggle? toggle)
        {
            if (toggle == null) return;
            // Simulate click to trigger all standard behavior
            Application.Current.Dispatcher.Invoke(() => toggle.Reader.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)));
        }

        #region Unimplemented Methods (For Controls)

        public AToggle AddToggle(StackPanel panel, string title) =>
            throw new NotImplementedException("Use control's internal implementation");

        public AKeyChanger AddKeyChanger(StackPanel panel, string title, string keybind) =>
            throw new NotImplementedException("Use control's internal implementation");

        public AColorChanger AddColorChanger(StackPanel panel, string title) =>
            throw new NotImplementedException("Use control's internal implementation");

        public ASlider AddSlider(StackPanel panel, string title, string label, double frequency, double buttonsteps, double min, double max) =>
            throw new NotImplementedException("Use control's internal implementation");

        public ADropdown AddDropdown(StackPanel panel, string title) =>
            throw new NotImplementedException("Use control's internal implementation");

        public AFileLocator AddFileLocator(StackPanel panel, string title, string filter = "All files (*.*)|*.*", string DLExtension = "") =>
            throw new NotImplementedException("Use control's internal implementation");

        #endregion
    }

    #region Extension Methods

    internal static class DictionaryExtensions
    {
        public static T GetValueOrDefault<T>(this Dictionary<string, T> dictionary, string key, T defaultValue) =>
            dictionary.TryGetValue(key, out var value) ? value : defaultValue;

        public static T GetValueOrDefault<T>(this Dictionary<string, dynamic> dictionary, string key, T defaultValue)
        {
            if (dictionary.TryGetValue(key, out var value))
            {
                try
                {
                    return (T)Convert.ChangeType(value, typeof(T));
                }
                catch
                {
                    return defaultValue;
                }
            }
            return defaultValue;
        }
    }

    #endregion
}

