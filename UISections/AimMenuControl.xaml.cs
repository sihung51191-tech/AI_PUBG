using Aimmy2.AILogic;
using AILogic;
using Aimmy2.Class;
using Aimmy2.MouseMovementLibraries.GHubSupport;
using Aimmy2.UILibrary;
using Class;
using InputLogic;
using MouseMovementLibraries.ddxoftSupport;
using MouseMovementLibraries.RazerSupport;
using Other;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UILibrary;
using Visuality;
using Aimmy2.AILogic.Recognition;

namespace Aimmy2.Controls
{
    public partial class AimMenuControl : UserControl
    {
        //--
        UISections.ColorPicker colorPickerInstance = null;
        UISections.ColorPicker fovColorPickerInstance = null;
        UISections.ColorPicker crosshairColorPickerInstance = null;
        //--
        private MainWindow? _mainWindow;
        private bool _isInitialized;
        private Action? _refreshRecognitionVisibility;

        // Local minimize state management
        private readonly Dictionary<string, bool> _localMinimizeState = new()
        {
            { "Aim Assist", false },
            { "Aim Config", false },
            { "Aim Config (Slot 1)", false },
            { "Aim Config (Slot 2)", false },
            { "Predictions", false },
            { "Auto Trigger", false },
            { "FOV Config", false },
            { "ESP Config", false },
            { "Weapon Slot System", false },
            { "Weapon Recognition System", false },
            { "Scope Recognition System", false },
            { "Recoil Config", false },
            { "Fast Loot Config", false }
        };

        // Public properties for MainWindow access
        public StackPanel AimAssistPanel => AimAssist;
        public StackPanel TriggerBotPanel => TriggerBot;
        public StackPanel ESPConfigPanel => ESPConfig;
        public StackPanel AimConfigSlot1Panel => AimConfigSlot1;
        public StackPanel AimConfigPanel => AimConfig;
        public StackPanel PredictionsPanel => Predictions;
        public StackPanel FOVConfigPanel => FOVConfig;
        public StackPanel WeaponSlotSystemPanel => WeaponSlotSystem;
        public StackPanel WeaponRecognitionSystemPanel => WeaponRecognitionSystem;
        public StackPanel ScopeRecognitionSystemPanel => ScopeRecognitionSystem;
        public StackPanel LootConfigPanel => LootConfig;
        public StackPanel RecoilConfigPanel => RecoilConfig;
        public ScrollViewer AimMenuScrollViewer => AimMenu;

        public AimMenuControl()
        {
            InitializeComponent();
            Loaded += (_, _) => global::Other.UiLanguage.RefreshTree(this);
        }

        private static Border ApplyCardTheme(Border border)
        {
            border.SetResourceReference(Border.BackgroundProperty, "ThemeSurface");
            border.SetResourceReference(Border.BorderBrushProperty, "ThemeOutline");
            return border;
        }

        private static T ApplyThemeBackground<T>(T control, string resource = "ThemeColor") where T : Control
        {
            control.SetResourceReference(Control.BackgroundProperty, resource);
            string foregroundResource = resource switch
            {
                "ThemePrimaryAction" => "ThemePrimaryActionForeground",
                "ThemeSecondaryAction" => "ThemeSecondaryActionForeground",
                "ThemeTertiaryAction" => "ThemeTertiaryActionForeground",
                "ThemeDangerAction" => "ThemeDangerActionForeground",
                _ => "ThemePrimaryActionForeground"
            };
            control.SetResourceReference(Control.ForegroundProperty, foregroundResource);
            return control;
        }

        public void Initialize(MainWindow mainWindow)
        {
            if (_isInitialized) return;

            _mainWindow = mainWindow;
            _isInitialized = true;

            // Load minimize states from global dictionary if they exist
            LoadMinimizeStatesFromGlobal();

            AIManager.ImageSizeUpdated += OnImageSizeChanged;

            // Load all sections with error handling
            try { LoadAimAssist(); } catch (Exception ex) { LogManager.Log(LogManager.LogLevel.Error, "Error loading AimAssist: " + ex.Message); }
            try { LoadAimConfig(); } catch (Exception ex) { LogManager.Log(LogManager.LogLevel.Error, "Error loading AimConfig: " + ex.Message); }
            try { LoadPredictions(); } catch (Exception ex) { LogManager.Log(LogManager.LogLevel.Error, "Error loading Predictions: " + ex.Message); }
            try { LoadTriggerBot(); } catch (Exception ex) { LogManager.Log(LogManager.LogLevel.Error, "Error loading TriggerBot: " + ex.Message); }
            try { LoadFOVConfig(); } catch (Exception ex) { LogManager.Log(LogManager.LogLevel.Error, "Error loading FOVConfig: " + ex.Message); }
            OnImageSizeChanged(FovSettings.ImageSize);
            try { LoadESPConfig(); } catch (Exception ex) { LogManager.Log(LogManager.LogLevel.Error, "Error loading ESPConfig: " + ex.Message); }
            
            try 
            { 
                LoadWeaponSlotSystem(); 
            } 
            catch (Exception ex) 
            { 
                LogManager.Log(LogManager.LogLevel.Error, "Error loading WeaponSlotSystem: " + ex.Message); 
            }

            try 
            { 
                LoadRecoilConfig(); 
            } 
            catch (Exception ex) 
            { 
                LogManager.Log(LogManager.LogLevel.Error, "Error loading RecoilConfig: " + ex.Message); 
            }

            try 
            { 
                LoadLootConfig(); 
            } 
            catch (Exception ex) 
            { 
                LogManager.Log(LogManager.LogLevel.Error, "Error loading LootConfig: " + ex.Message); 
            }

            // Apply minimize states after loading
            ApplyMinimizeStates();
            MainWindow.UpdateSliderVisibility(_mainWindow.uiManager);
        }

        #region Minimize State Management

        private void LoadMinimizeStatesFromGlobal()
        {
            foreach (var key in _localMinimizeState.Keys.ToList())
            {
                if (Dictionary.minimizeState.ContainsKey(key))
                {
                    _localMinimizeState[key] = Dictionary.minimizeState[key];
                }
            }
        }

        private void SaveMinimizeStatesToGlobal()
        {
            foreach (var kvp in _localMinimizeState)
            {
                Dictionary.minimizeState[kvp.Key] = kvp.Value;
            }
        }

        private void ApplyMinimizeStates()
        {
            ApplyPanelState("Aim Assist", AimAssistPanel);
            ApplyPanelState("Aim Config (Slot 1)", AimConfigSlot1Panel);
            ApplyPanelState("Aim Config (Slot 2)", AimConfigPanel);
            ApplyPanelState("Predictions", PredictionsPanel);
            ApplyPanelState("Auto Trigger", TriggerBotPanel);
            ApplyPanelState("FOV Config", FOVConfigPanel);
            ApplyPanelState("ESP Config", ESPConfigPanel);
            ApplyPanelState("Weapon Recognition System", WeaponRecognitionSystemPanel);
            ApplyPanelState("Scope Recognition System", ScopeRecognitionSystemPanel);
            ApplyPanelState("Fast Loot Config", LootConfigPanel);
            ApplyPanelState("Recoil Config", RecoilConfigPanel);
            _refreshRecognitionVisibility?.Invoke();
        }

        private void ApplyPanelState(string stateName, StackPanel panel)
        {
            if (_localMinimizeState.TryGetValue(stateName, out bool isMinimized))
            {
                panel.Children.OfType<ATitle>().FirstOrDefault()?.SetMinimized(isMinimized);
                SetPanelVisibility(panel, !isMinimized);
            }
        }

        private void SetPanelVisibility(StackPanel panel, bool isVisible)
        {
            foreach (UIElement child in panel.Children)
            {
                // The real parent Border now supplies the collapsed section's bottom edge.
                // Keeping the old spacer visible created a second horizontal line below the title.
                bool shouldStayVisible = child is ATitle;

                child.Visibility = shouldStayVisible
                    ? Visibility.Visible
                    : (isVisible ? Visibility.Visible : Visibility.Collapsed);
            }
        }

        private void TogglePanel(string stateName, StackPanel panel)
        {
            if (!_localMinimizeState.ContainsKey(stateName)) return;

            // Toggle the state
            _localMinimizeState[stateName] = !_localMinimizeState[stateName];
            bool isMinimized = _localMinimizeState[stateName];

            // Apply the new visibility
            panel.Children.OfType<ATitle>().FirstOrDefault()?.SetMinimized(isMinimized);
            SetPanelVisibility(panel, !isMinimized);
            if (stateName is "Weapon Recognition System" or "Scope Recognition System")
                _refreshRecognitionVisibility?.Invoke();

            // Save to global dictionary
            SaveMinimizeStatesToGlobal();
        }



        #endregion

        #region Menu Section Loaders

        private void LoadAimAssist()
        {
            var uiManager = _mainWindow!.uiManager;
            var builder = new SectionBuilder(this, AimAssist);

            builder
                .AddTitle("Aim Assist", true, t =>
                {
                    uiManager.AT_Aim = t;
                    t.Minimize.Click += (s, e) =>
                    {
                        TogglePanel("Aim Assist", AimAssistPanel);
                        if (_mainWindow != null) MainWindow.UpdateSliderVisibility(_mainWindow.uiManager);
                    };
                })
                .AddToggle("Aim Assist", t =>
                {
                    uiManager.T_AimAligner = t;
                    t.Reader.Click += (s, e) =>
                    {
                        _mainWindow?.CancelDelayedActivation("Aim Assist");
                        if (Dictionary.toggleState["Aim Assist"] && Dictionary.lastLoadedModel == "N/A")
                        {
                            Dictionary.toggleState["Aim Assist"] = false;
                            _mainWindow.UpdateToggleUI(t, false);
                            LogManager.Log(LogManager.LogLevel.Warning, "Please load a model first", true);
                        }
                        else if (Dictionary.toggleState["Aim Assist"] && Dictionary.lastLoadedModelSlot2 == "N/A")
                        {
                             LogManager.Log(LogManager.LogLevel.Warning, "Please choose model slot 2", true);
                        }
                    };
                }, tooltip: "Bật/Tắt hỗ trợ nhắm. Bạn cần tải Model trước.")
                .AddToggle("Constant AI Tracking", t =>
                {
                    uiManager.T_ConstantAITracking = t;
                    t.Reader.Click += (s, e) =>
                    {
                        if (Dictionary.toggleState["Constant AI Tracking"])
                        {
                            if (Dictionary.lastLoadedModel == "N/A")
                            {
                                Dictionary.toggleState["Constant AI Tracking"] = false;
                                _mainWindow.UpdateToggleUI(t, false);
                            }
                            else
                            {
                                Dictionary.toggleState["Aim Assist"] = true;
                                _mainWindow.UpdateToggleUI(uiManager.T_AimAligner, true);
                            }
                        }
                    };
                }, tooltip: "Luôn theo dõi mục tiêu mà không cần giữ phím. Khi tắt, bạn phải giữ phím aim.")
                .AddToggle("Sticky Aim", t => 
                {
                    uiManager.T_StickyAim = t;
                    t.Reader.Click += (s, e) =>
                    {
                         bool isSticky = Dictionary.toggleState["Sticky Aim"];
                         if (uiManager.S_StickyAimThreshold != null)
                             uiManager.S_StickyAimThreshold.Visibility = isSticky ? Visibility.Visible : Visibility.Collapsed;
                         
                         if (uiManager.S_TargetLockDuration != null)
                             uiManager.S_TargetLockDuration.Visibility = isSticky ? Visibility.Visible : Visibility.Collapsed;
                    };
                },
                    tooltip: "Khóa vào một mục tiêu cho đến khi nó ra khỏi phạm vi thay vì chuyển sang mục tiêu khác.")
                .AddSlider("Sticky Aim Threshold", "Pixels", 1, 1, 0, 100, s =>
                {
                    uiManager.S_StickyAimThreshold = s;
                    // Set initial visibility based on toggle state
                    s.Visibility = Dictionary.toggleState["Sticky Aim"]
                        ? Visibility.Visible : Visibility.Collapsed;
                }, tooltip: "Khoảng cách mục tiêu di chuyển để chuyển sang mục tiêu mới. Cao hơn = khóa lâu hơn.")
                .AddSlider("Target Lock Duration", "ms", 10, 10, 0, 2000, s =>
                {
                    uiManager.S_TargetLockDuration = s;
                     // Set initial visibility based on toggle state
                    s.Visibility = Dictionary.toggleState["Sticky Aim"]
                        ? Visibility.Visible : Visibility.Collapsed;
                }, tooltip: "Thời gian (ms) giữ mục tiêu sau khi khóa trước khi cho phép đổi mục tiêu. Giúp tránh aim nhảy loạn xạ.")
                .AddKeyChanger("Aim Keybind", k => uiManager.C_Keybind = k,
                    tooltip: "Phím bạn giữ để kích hoạt hỗ trợ nhắm.")
                .AddKeyChanger("Second Aim Keybind", tooltip: "Phím thay thế để kích hoạt hỗ trợ nhắm.")
                .AddSeparator();

        }

        private void LoadAimConfig()
        {
            try
            {
                LoadAimConfigSlot1();
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, "Error loading AimConfigSlot1: " + ex.Message);
                AimConfigSlot1.Children.Add(new TextBlock 
                { 
                    Text = "Error loading Slot 1 Config: " + ex.Message + "\n" + ex.StackTrace, 
                    Foreground = Brushes.Red, 
                    TextWrapping = TextWrapping.Wrap,
                    FontWeight = FontWeights.Bold
                });
            }

            try
            {
                LoadAimConfigSlot2();
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, "Error loading AimConfigSlot2: " + ex.Message);
                 AimConfig.Children.Add(new TextBlock 
                { 
                    Text = "Error loading Slot 2 Config: " + ex.Message + "\n" + ex.StackTrace, 
                    Foreground = Brushes.Red, 
                    TextWrapping = TextWrapping.Wrap,
                    FontWeight = FontWeights.Bold
                });
            }
        }

        private void LoadAimConfigSlot1()
        {
            var uiManager = _mainWindow!.uiManager;
            // Ensure child is cleared if reloaded
            AimConfigSlot1.Children.Clear();
            
            var builder = new SectionBuilder(this, AimConfigSlot1);

            builder.AddTitle("Aim Config (Slot 1)", true, t =>
            {
                t.Minimize.Click += (s, e) => 
                {
                    TogglePanel("Aim Config (Slot 1)", AimConfigSlot1Panel);
                    if (_mainWindow != null) MainWindow.UpdateSliderVisibility(_mainWindow.uiManager);
                };
            })
            // Add Dropdowns for Slot 1
            .AddDropdown("Slot 1 Mouse Movement Method", d =>
            {
                uiManager.D_Slot1MouseMovementMethod = d;
                d.DropdownBox.SelectedIndex = -1;
                _mainWindow.AddDropdownItem(d, "Mouse Event");
                _mainWindow.AddDropdownItem(d, "SendInput");
                _mainWindow.AddDropdownItem(d, "LG HUB");
                _mainWindow.AddDropdownItem(d, "Razer Synapse (Require Razer Peripheral)");
                var dd = _mainWindow.AddDropdownItem(d, "ddxoft Virtual Input Driver");
                dd.Selected += async (_, _) =>
                {
                    if (!await DdxoftMain.Load() && d.DropdownBox.SelectedItem == dd)
                        d.DropdownBox.SelectedIndex = 0;
                };
            }, tooltip: "Phương thức di chuyển chuột cho Slot 1.")
            .AddDropdown("Slot 1 Movement Path", d =>
            {
                uiManager.D_Slot1MovementPath = d;
                _mainWindow.AddDropdownItem(d, "Cubic Bezier");
                _mainWindow.AddDropdownItem(d, "Exponential");
                _mainWindow.AddDropdownItem(d, "Linear");
                _mainWindow.AddDropdownItem(d, "Adaptive");
                _mainWindow.AddDropdownItem(d, "Perlin Noise");
                d.DropdownBox.SelectedIndex = 2;
            }, tooltip: "Đường đi chuyển chuột cho Slot 1.")
             .AddDropdown("Slot 1 Detection Area Type", d =>
            {
                uiManager.D_Slot1DetectionAreaType = d;
                d.DropdownBox.SelectedIndex = -1;
                _mainWindow.AddDropdownItem(d, "Closest to Center Screen");
                _mainWindow.AddDropdownItem(d, "Closest to Mouse");
            }, tooltip: "Loại vùng phát hiện cho Slot 1.")
             .AddDropdown("Slot 1 Aiming Boundaries Alignment", d =>
            {
                uiManager.D_Slot1AimingBoundariesAlignment = d;
                d.DropdownBox.SelectedIndex = -1;
                _mainWindow.AddDropdownItem(d, "Center");
                _mainWindow.AddDropdownItem(d, "Top");
                _mainWindow.AddDropdownItem(d, "Bottom");
            }, tooltip: "Căn chỉnh biên ngắm cho Slot 1.");

            AddSlot1ConfigSliders(builder, uiManager);
            
            builder.AddSeparator();
        }

        private void LoadAimConfigSlot2()
        {
            var uiManager = _mainWindow!.uiManager;
            // Ensure child is cleared if reloaded
            AimConfig.Children.Clear();

            var builder = new SectionBuilder(this, AimConfig);

            builder
                .AddTitle("Aim Config (Slot 2)", true, t =>
                {
                    uiManager.AT_AimConfig = t;
                    t.Minimize.Click += (s, e) =>
                    {
                        TogglePanel("Aim Config (Slot 2)", AimConfigPanel);
                        if (_mainWindow != null) MainWindow.UpdateSliderVisibility(_mainWindow.uiManager);
                    };
                })
                .AddDropdown("Mouse Movement Method", d =>
                {
                    uiManager.D_MouseMovementMethod = d;
                    d.DropdownBox.SelectedIndex = -1;  // Prevent auto-selection
                    _mainWindow.AddDropdownItem(d, "Mouse Event");
                    _mainWindow.AddDropdownItem(d, "SendInput");
                    uiManager.DDI_LGHUB = _mainWindow.AddDropdownItem(d, "LG HUB");
                    uiManager.DDI_RazerSynapse = _mainWindow.AddDropdownItem(d, "Razer Synapse (Require Razer Peripheral)");
                    uiManager.DDI_ddxoft = _mainWindow.AddDropdownItem(d, "ddxoft Virtual Input Driver");

                    // Setup handlers
                    uiManager.DDI_LGHUB.Selected += async (s, e) => { if (!new LGHubMain().Load()) await ResetToMouseEvent(); };


                    uiManager.DDI_RazerSynapse.Selected += async (s, e) => { if (!await RZMouse.Load()) await ResetToMouseEvent(); };
                    var dd = uiManager.DDI_ddxoft;
                    dd.Selected += async (_, _) =>
                    {
                        if (!await DdxoftMain.Load() && d.DropdownBox.SelectedItem == dd)
                            d.DropdownBox.SelectedIndex = 0;
                    };
                }, tooltip: "Cách thức gửi tín hiệu di chuyển chuột (Dùng chung).")
                .AddDropdown("Movement Path", d =>
                {
                    uiManager.D_MovementPath = d;
                    _mainWindow.AddDropdownItem(d, "Cubic Bezier");
                    _mainWindow.AddDropdownItem(d, "Exponential");
                    _mainWindow.AddDropdownItem(d, "Linear");
                    _mainWindow.AddDropdownItem(d, "Adaptive");
                    _mainWindow.AddDropdownItem(d, "Perlin Noise");
                    d.DropdownBox.SelectedIndex = 2; // Default to Linear
                }, tooltip: "Kiểu đường cong khi di chuyển tới mục tiêu (Dùng chung).")
                .AddDropdown("Detection Area Type", d =>
                {
                    d.DropdownBox.SelectedIndex = -1;
                    uiManager.D_DetectionAreaType = d;
                    uiManager.DDI_ClosestToCenterScreen = _mainWindow.AddDropdownItem(d, "Closest to Center Screen");
                    _mainWindow.AddDropdownItem(d, "Closest to Mouse");

                    uiManager.DDI_ClosestToCenterScreen.Selected += (_, _) =>
                        Dispatcher.BeginInvoke(() =>
                        {
                            MainWindow.FOVWindow.FOVStrictEnclosure.Margin = new Thickness(
                                Convert.ToInt16((WinAPICaller.ScreenWidth / 2) / WinAPICaller.scalingFactorX) - 320,
                                Convert.ToInt16((WinAPICaller.ScreenHeight / 2) / WinAPICaller.scalingFactorY) - 320,
                                0, 0);
                        }, System.Windows.Threading.DispatcherPriority.Loaded);
                }, tooltip: "Cách ưu tiên mục tiêu (Dùng chung).")
                .AddDropdown("Aiming Boundaries Alignment", d =>
                {
                    d.DropdownBox.SelectedIndex = -1;
                    uiManager.D_AimingBoundariesAlignment = d;
                    _mainWindow.AddDropdownItem(d, "Center");
                    _mainWindow.AddDropdownItem(d, "Top");
                    _mainWindow.AddDropdownItem(d, "Bottom");
                }, tooltip: "Vị trí ngắm trên ô mục tiêu được phát hiện (Dùng chung).");

            // Add sliders for Slot 2
            AddConfigSliders(builder, uiManager);
            builder.AddSeparator();
        }

        private void AddSlot1ConfigSliders(SectionBuilder builder, UI uiManager)
        {
             // These keys MUST match what we added to Dictionary.cs
            builder
                .AddSlider("Slot 1 Mouse Sensitivity", "Sens", 0.01, 0.01, 0.01, 1, s =>
                {
                    uiManager.S_Slot1MouseSensitivity = s;
                }, tooltip: "Độ nhạy chuột cho Slot 1.")
                .AddSlider("Slot 1 Mouse Jitter", "Jitter", 1, 1, 0, 15, s => 
                {
                    uiManager.S_Slot1MouseJitter = s;
                }, tooltip: "Độ rung chuột cho Slot 1.")
                .AddToggle("Slot 1 Y Axis Percentage Adjustment", t => 
                {
                    uiManager.T_Slot1YAxisPercentageAdjustment = t;
                    t.Reader.Click += (s, e) => _mainWindow?.Toggle_Action("Slot 1 Y Axis Percentage Adjustment");
                }, tooltip: "Bật điều chỉnh trục Y theo % cho Slot 1.")
                .AddToggle("Slot 1 X Axis Percentage Adjustment", t => 
                {
                    uiManager.T_Slot1XAxisPercentageAdjustment = t;
                    t.Reader.Click += (s, e) => _mainWindow?.Toggle_Action("Slot 1 X Axis Percentage Adjustment");
                }, tooltip: "Bật điều chỉnh trục X theo % cho Slot 1.")
                .AddSlider("Slot 1 Y Offset (Up/Down)", "Offset", 1, 1, -150, 150, s =>
                {
                    uiManager.S_Slot1YOffset = s;
                    s.Visibility = Dictionary.toggleState["Slot 1 Y Axis Percentage Adjustment"]
                        ? Visibility.Collapsed : Visibility.Visible;
                }, tooltip: "Độ lệch pixel trục Y cho Slot 1.")
                .AddSlider("Slot 1 Y Offset (%)", "Percent", 1, 1, 0, 100, s =>
                {
                    uiManager.S_Slot1YOffsetPercent = s;
                    s.Visibility = Dictionary.toggleState["Slot 1 Y Axis Percentage Adjustment"]
                        ? Visibility.Visible : Visibility.Collapsed;
                }, tooltip: "Độ lệch phần trăm trục Y cho Slot 1.")
                .AddSlider("Slot 1 X Offset (Left/Right)", "Offset", 1, 1, -150, 150, s =>
                {
                    uiManager.S_Slot1XOffset = s;
                    s.Visibility = Dictionary.toggleState["Slot 1 X Axis Percentage Adjustment"]
                        ? Visibility.Collapsed : Visibility.Visible;
                }, tooltip: "Độ lệch pixel trục X cho Slot 1.")
                .AddSlider("Slot 1 X Offset (%)", "Percent", 1, 1, 0, 100, s =>
                {
                    uiManager.S_Slot1XOffsetPercent = s;
                    s.Visibility = Dictionary.toggleState["Slot 1 X Axis Percentage Adjustment"]
                        ? Visibility.Visible : Visibility.Collapsed;
                }, tooltip: "Độ lệch phần trăm trục X cho Slot 1.");
        }

        private void AddConfigSliders(SectionBuilder builder, UI uiManager)
        {
            builder
                .AddSlider("Mouse Sensitivity (+/-)", "Sensitivity", 0.01, 0.01, 0.01, 1, s =>
                {
                    uiManager.S_MouseSensitivity = s;
                }, tooltip: "Độ nhạy cho Slot 2.")
                .AddSlider("Mouse Jitter", "Jitter", 1, 1, 0, 15, s => uiManager.S_MouseJitter = s,
                    tooltip: "Độ rung cho Slot 2.")
                .AddToggle("Y Axis Percentage Adjustment", t => uiManager.T_YAxisPercentageAdjustment = t,
                    tooltip: "Bật điều chỉnh trục Y theo % cho Slot 2.")
                .AddToggle("X Axis Percentage Adjustment", t => uiManager.T_XAxisPercentageAdjustment = t,
                    tooltip: "Bật điều chỉnh trục X theo % cho Slot 2.")
                .AddSlider("Y Offset (Up/Down)", "Offset", 1, 1, -150, 150, s =>
                {
                    uiManager.S_YOffset = s;
                    s.Visibility = Dictionary.toggleState["Y Axis Percentage Adjustment"]
                        ? Visibility.Collapsed : Visibility.Visible;
                }, tooltip: "Độ lệch pixel trục Y cho Slot 2.")
                .AddSlider("Y Offset (%)", "Percent", 1, 1, 0, 100, s =>
                {
                    uiManager.S_YOffsetPercent = s;
                    s.Visibility = Dictionary.toggleState["Y Axis Percentage Adjustment"]
                        ? Visibility.Visible : Visibility.Collapsed;
                }, tooltip: "Độ lệch phần trăm trục Y cho Slot 2.")
                .AddSlider("X Offset (Left/Right)", "Offset", 1, 1, -150, 150, s =>
                {
                    uiManager.S_XOffset = s;
                    s.Visibility = Dictionary.toggleState["X Axis Percentage Adjustment"]
                        ? Visibility.Collapsed : Visibility.Visible;
                }, tooltip: "Độ lệch pixel trục X cho Slot 2.")
                .AddSlider("X Offset (%)", "Percent", 1, 1, 0, 100, s =>
                {
                    uiManager.S_XOffsetPercent = s;
                    s.Visibility = Dictionary.toggleState["X Axis Percentage Adjustment"]
                        ? Visibility.Visible : Visibility.Collapsed;
                }, tooltip: "Độ lệch phần trăm trục X cho Slot 2.");
        }

        private void LoadPredictions()
        {
            var uiManager = _mainWindow!.uiManager;
            var builder = new SectionBuilder(this, Predictions);

            builder
                .AddTitle("Predictions", true, t =>
                {
                    uiManager.AT_Predictions = t;
                    t.Minimize.Click += (s, e) =>
                    {
                        TogglePanel("Predictions", PredictionsPanel);
                        if (_mainWindow != null) MainWindow.UpdatePredictionSliderVisibility(_mainWindow.uiManager);
                    };
                })
                .AddToggle("Predictions", t => uiManager.T_Predictions = t,
                    tooltip: "VI: Đưa tâm đón trước theo chuyển động. / EN: Lead the aim point using target motion.")
                .AddToggle("Enable Kalman Filter", t => uiManager.T_KalmanFilter = t,
                    tooltip: "VI: Làm mượt vị trí và ước lượng vận tốc của mục tiêu đang khóa. / EN: Smooth the locked target and estimate its velocity.")
                .AddSlider("Prediction Time", "ms", 5, 5, 0, 150, s => uiManager.S_PredictionTime = s,
                    tooltip: "VI: Thời gian đón đầu cơ bản, 0–150 ms. / EN: Base prediction lead time, 0–150 ms.")
                .AddSlider("Kalman Smoothness", "%", 1, 5, 0, 100, s => uiManager.S_KalmanSmoothness = s,
                    tooltip: "VI: Cao hơn giảm rung nhiều hơn nhưng có thể tăng trễ nhẹ. / EN: Higher values reduce jitter but may add slight lag.")
                .AddSlider("Maximum Missing Frames", "Frames", 1, 1, 0, 10, s => uiManager.S_MaximumMissingFrames = s,
                    tooltip: "VI: Số frame mất tối đa trước khi hủy track. / EN: Maximum missed frames before the track resets.")
                .AddSlider("Maximum Frame Age", "ms", 5, 10, 25, 500, s => uiManager.S_MaximumFrameAge = s,
                    tooltip: "VI: Bỏ frame quá cũ để tránh kéo chuột trễ. / EN: Reject stale frames to avoid delayed mouse movement.")
                .AddSlider("Maximum Prediction Distance", "Pixels", 1, 5, 0, 300, s => uiManager.S_MaximumPredictionDistance = s,
                    tooltip: "VI: Giới hạn khoảng đón đầu; 0 dùng kích thước box. / EN: Lead-distance cap; 0 derives it from the target box.")
                .AddDropdown("Prediction Method", d =>
                {
                    d.DropdownBox.SelectedIndex = -1;
                    uiManager.D_PredictionMethod = d;
                    _mainWindow.AddDropdownItem(d, "Kalman Filter");
                    _mainWindow.AddDropdownItem(d, "Shall0e's Prediction");
                    _mainWindow.AddDropdownItem(d, "wisethef0x's EMA Prediction");
                    _mainWindow.AddDropdownItem(d, "Constant Acceleration");
 
                    // Update slider visibility when prediction method changes
                    d.DropdownBox.SelectionChanged += (s, e) => 
                    {
                        if (_mainWindow != null) MainWindow.UpdatePredictionSliderVisibility(_mainWindow.uiManager);
                    };
                }, tooltip: "Thuật toán dự đoán chuyển động mục tiêu. Hãy thử các loại khác nhau để xem cái nào tốt nhất.")
                .AddSlider("CA Lead Multiplier", "Multiplier", 0.01, 0.01, 0.01, 0.50, s =>
                {
                    uiManager.S_CALeadMultiplier = s;
                    s.Visibility = Visibility.Collapsed;
                }, tooltip: "Hệ số dự đoán cho gia tốc. Cao hơn = dự đoán xa hơn, giúp bám đuổi tốt khi đổi hướng.")
                .AddSlider("Kalman Lead Time", "Seconds", 0.01, 0.01, 0.02, 0.30, s =>
                {
                    uiManager.S_KalmanLeadTime = s;
                    // Start collapsed - visibility will be set by LoadDropdownStates
                    s.Visibility = Visibility.Collapsed;
                }, tooltip: "Thời gian dự đoán trước. Cao hơn = dự đoán xa hơn, có thể bị vọt lố.")
                .AddSlider("WiseTheFox Lead Time", "Seconds", 0.01, 0.01, 0.02, 0.30, s =>
                {
                    uiManager.S_WiseTheFoxLeadTime = s;
                    // Start collapsed - visibility will be set by LoadDropdownStates
                    s.Visibility = Visibility.Collapsed;
                }, tooltip: "Thời gian dự đoán trước. Cao hơn = dự đoán xa hơn, có thể bị vọt lố.")
                .AddSlider("Shalloe Lead Multiplier", "Frames", 0.5, 0.5, 1, 10, s =>
                {
                    uiManager.S_ShalloeLeadMultiplier = s;
                    // Start collapsed - visibility will be set by LoadDropdownStates
                    s.Visibility = Visibility.Collapsed;
                }, tooltip: "Số khung hình dự đoán trước. Cao hơn = dự đoán xa hơn, có thể bị vọt lố.")
                .AddToggle("EMA Smoothening", t => uiManager.T_EMASmoothing = t,
                    tooltip: "Làm mượt chuyển động ngắm để giảm rung và giúp theo dõi ổn định hơn.")
                .AddSlider("EMA Smoothening", "Amount", 0.01, 0.01, 0.01, 1, s =>
                {
                    uiManager.S_EMASmoothing = s;
                    s.Slider.ValueChanged += (sender, e) =>
                    {
                        if (Dictionary.toggleState["EMA Smoothening"])
                        {
                            MouseManager.smoothingFactor = s.Slider.Value;
                        }
                    };
                }, tooltip: "Mức độ làm mượt. Thấp = mượt hơn nhưng chậm hơn, cao = nhanh hơn nhưng rung.")
                .AddSeparator();

            MainWindow.UpdatePredictionSliderVisibility(uiManager);
        }

        private void LoadTriggerBot()
        {
            var uiManager = _mainWindow!.uiManager;
            var builder = new SectionBuilder(this, TriggerBot);

            builder
                .AddTitle("Auto Trigger", true, t =>
                {
                    uiManager.AT_TriggerBot = t;
                    t.Minimize.Click += (s, e) => 
                    {
                        TogglePanel("Auto Trigger", TriggerBotPanel);
                        if (_mainWindow != null) MainWindow.UpdateSliderVisibility(_mainWindow.uiManager);
                    };
                })
                .AddToggle("Auto Trigger", t => uiManager.T_AutoTrigger = t,
                    tooltip: "Tự động click khi phát hiện mục tiêu trong vùng hồng tâm.")
                .AddToggle("Cursor Check", t => uiManager.T_CursorCheck = t,
                    tooltip: "Chỉ kích hoạt khi con trỏ chuột nằm trực tiếp trên mục tiêu. Chính xác hơn nhưng có thể bỏ lỡ.")
                .AddToggle("Spray Mode", t => uiManager.T_SprayMode = t,
                    tooltip: "Giữ chuột thay vì click từng cái. Tốt cho súng tự động.")
                //.AddToggle("Only When Held", t => uiManager.T_OnlyWhenHeld = t)
                .AddSlider("Auto Trigger Delay", "Seconds", 0.01, 0.1, 0.01, 1, s => uiManager.S_AutoTriggerDelay = s,
                    tooltip: "Thời gian chờ trước khi bắn sau khi phát hiện mục tiêu. Giúp tránh bắn nhầm.")
                .AddKeyChanger("Auto Click Keybind", tooltip: "Phím giữ để auto click liên tục khi tâm vào địch.")
                .AddSeparator();
        }

        private void LoadWeaponSlotSystem()
        {
            try
            {
                var uiManager = _mainWindow!.uiManager;
                WeaponRecognitionSystem.Children.Clear();
                ScopeRecognitionSystem.Children.Clear();
                var weaponBuilder = new SectionBuilder(this, WeaponRecognitionSystem);
                var scopeBuilder = new SectionBuilder(this, ScopeRecognitionSystem);
                var weaponAiControls = new List<FrameworkElement>();
                var scopeAiControls = new List<FrameworkElement>();
                var weaponTemplateControls = new List<FrameworkElement>();
                var scopeTemplateControls = new List<FrameworkElement>();
                var weaponOrbControls = new List<FrameworkElement>();
                var scopeOrbControls = new List<FrameworkElement>();
                var weaponSiftControls = new List<FrameworkElement>();
                var scopeSiftControls = new List<FrameworkElement>();
                ADropdown? weaponMethodDropdown = null;
                ADropdown? scopeMethodDropdown = null;
                ASlider? weaponScopeInfoSize = null;
                ASlider? weaponScopeInfoOpacity = null;
                bool updatingRecognitionDropdowns = false;
                Button? weaponTemplateManager = null;
                Button? scopeTemplateManager = null;
                var recognitionConfig = WeaponSlotManager.Instance.RecognitionConfig;
                var weaponTemplateSettings = recognitionConfig.WeaponTemplateSettings;
                var scopeTemplateSettings = recognitionConfig.ScopeTemplateSettings;
                var weaponFeatureSettings = recognitionConfig.WeaponFeatureSettings;
                var scopeFeatureSettings = recognitionConfig.ScopeFeatureSettings;
                void LoadTemplateUiState(string prefix, TemplateMatchingSettings settings)
                {
                    Dictionary.sliderSettings[$"{prefix} Template Confidence Threshold"] = settings.ConfidenceThreshold * 100;
                    Dictionary.sliderSettings[$"{prefix} Template Scale Min"] = settings.ScaleMin;
                    Dictionary.sliderSettings[$"{prefix} Template Scale Max"] = settings.ScaleMax;
                    Dictionary.sliderSettings[$"{prefix} Template Scale Step"] = settings.ScaleStep;
                    Dictionary.toggleState[$"{prefix} Template Multi-scale"] = settings.EnableMultiScale;
                    Dictionary.toggleState[$"{prefix} Template Edge Matching"] = settings.EnableEdgeMatching;
                    Dictionary.toggleState[$"{prefix} Template Alpha Mask"] = settings.EnableAlphaMask;
                    Dictionary.toggleState[$"{prefix} Template Color Mask"] = settings.EnableColorMask;
                }
                LoadTemplateUiState("Weapon", weaponTemplateSettings);
                LoadTemplateUiState("Scope", scopeTemplateSettings);
                void LoadFeatureUiState(string prefix, FeatureMatchingSettings settings)
                {
                    Dictionary.sliderSettings[$"{prefix} ORB Feature Count"] = settings.OrbFeatureCount;
                    Dictionary.sliderSettings[$"{prefix} ORB Ratio Threshold"] = settings.OrbRatioThreshold;
                    Dictionary.sliderSettings[$"{prefix} ORB Minimum Good Matches"] = settings.OrbMinimumGoodMatches;
                    Dictionary.sliderSettings[$"{prefix} ORB RANSAC Threshold"] = settings.OrbRansacThreshold;
                    Dictionary.sliderSettings[$"{prefix} SIFT Feature Count"] = settings.SiftFeatureCount;
                    Dictionary.sliderSettings[$"{prefix} SIFT Ratio Threshold"] = settings.SiftRatioThreshold;
                    Dictionary.sliderSettings[$"{prefix} SIFT Minimum Good Matches"] = settings.SiftMinimumGoodMatches;
                    Dictionary.sliderSettings[$"{prefix} SIFT RANSAC Threshold"] = settings.SiftRansacThreshold;
                    Dictionary.sliderSettings[$"{prefix} SIFT Scan Interval"] = settings.SiftScanIntervalMs;
                }
                LoadFeatureUiState("Weapon", weaponFeatureSettings);
                LoadFeatureUiState("Scope", scopeFeatureSettings);
                Dictionary.sliderSettings["Weapon AI Confidence"] = WeaponSlotManager.Instance.RecognitionConfig.WeaponAiConfidence * 100;
                void SaveRecognitionSettings() => WeaponSlotManager.Instance.SaveRecognitionConfig();
                void SelectRecognitionMethod(ADropdown? dropdown, RecognitionMethod method)
                {
                    if (dropdown == null) return;
                    var match = dropdown.DropdownBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag is RecognitionMethod value && value == method);
                    if (match != null && !ReferenceEquals(dropdown.DropdownBox.SelectedItem, match)) dropdown.DropdownBox.SelectedItem = match;
                }
                void ApplyRecognitionMethod(bool scope, ADropdown dropdown)
                {
                    if (updatingRecognitionDropdowns || dropdown.DropdownBox.SelectedItem is not ComboBoxItem item || item.Tag is not RecognitionMethod method) return;
                    updatingRecognitionDropdowns = true;
                    try
                    {
                        var manager = WeaponSlotManager.Instance;
                        manager.SetRecognitionMethod(scope, method);
                        Dictionary.dropdownState[scope ? "Scope Recognition Method" : "Weapon Recognition Method"] = item.Content?.ToString() ?? method.ToString();
                        // Re-apply both independent values so a style refresh or translated item
                        // cannot visually copy one ComboBox selection into the other.
                        SelectRecognitionMethod(weaponMethodDropdown, manager.RecognitionConfig.WeaponMethod);
                        SelectRecognitionMethod(scopeMethodDropdown, manager.RecognitionConfig.ScopeMethod);
                    }
                    finally { updatingRecognitionDropdowns = false; }
                    RefreshRecognitionSettingVisibility();
                }

                weaponBuilder.AddTitle("Weapon Recognition System", true, t =>
                {
                    t.Minimize.Click += (s, e) =>
                {
                    TogglePanel("Weapon Recognition System", WeaponRecognitionSystemPanel);
                    if (_mainWindow != null) MainWindow.UpdateSliderVisibility(_mainWindow.uiManager);
                };
                })
                .AddToggle("Weapon Recognition", t => {
                        uiManager.T_WeaponRecognition = t;
                        t.Reader.Click += (s, e) => {
                             _mainWindow?.CancelDelayedActivation("Weapon Recognition");
                             if(Dictionary.toggleState["Weapon Recognition"] && !WeaponSlotManager.Instance.IsInitialized)
                            {
                                 WeaponSlotManager.Instance.Initialize();
                                 // Re-check just in case
                                 if(!WeaponSlotManager.Instance.IsInitialized)
                                 {
                                     Dictionary.toggleState["Weapon Recognition"] = false;
                                     _mainWindow.UpdateToggleUI(t, false);
                                     global::Other.LocalizedMessageBox.Show("Failed to initialize Weapon Recognition. Please check your model settings.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                                 }
                            }
                        };
                    }, tooltip: "Tự động phát hiện vũ khí đang cầm để chuyển đổi cài đặt.")
                    .AddToggle("Toggle Weapon Scan", tooltip: "Nếu BẬT, nhấn phím scan (Tab) để Bật/Tắt scan. Nếu TẮT, bạn phải GIỮ phím để scan.");

                weaponBuilder.AddDropdown("Weapon Recognition Method", d =>
                {
                    weaponMethodDropdown = d;
                    var methods = new[] { ("AI Model", RecognitionMethod.AiModel), ("Template Matching", RecognitionMethod.TemplateMatching), ("OCR", RecognitionMethod.Ocr), ("ORB Feature Matching", RecognitionMethod.OrbFeatureMatching), ("SIFT Feature Matching", RecognitionMethod.SiftFeatureMatching), ("Auto Hybrid", RecognitionMethod.AutoHybrid) };
                    foreach (var item in methods) _mainWindow.AddDropdownItem(d, item.Item1).Tag = item.Item2;
                    d.DropdownBox.SelectedIndex = Array.FindIndex(methods, x => x.Item2 == WeaponSlotManager.Instance.RecognitionConfig.WeaponMethod);
                    d.DropdownBox.SelectionChanged += (_, _) => ApplyRecognitionMethod(false, d);
                }, tooltip: "Chọn AI, template, OCR hoặc chuỗi fallback nhẹ cho tên súng.");

                scopeBuilder.AddTitle("Scope Recognition System", true, t =>
                {
                    t.Minimize.Click += (_, _) =>
                    {
                        TogglePanel("Scope Recognition System", ScopeRecognitionSystemPanel);
                        if (_mainWindow != null) MainWindow.UpdateSliderVisibility(_mainWindow.uiManager);
                    };
                }).AddToggle("Scope Recognition", t =>
                {
                    t.Reader.Click += (_, _) =>
                    {
                        if (Dictionary.toggleState["Scope Recognition"] && !WeaponSlotManager.Instance.IsInitialized)
                        {
                            WeaponSlotManager.Instance.Initialize();
                            if (!WeaponSlotManager.Instance.IsInitialized)
                            {
                                Dictionary.toggleState["Scope Recognition"] = false;
                                _mainWindow.UpdateToggleUI(t, false);
                                global::Other.LocalizedMessageBox.Show("Failed to initialize Scope Recognition. Please check your model settings.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            }
                        }
                    };
                }, tooltip: "Bật hoặc tắt riêng việc nhận diện scope.")
                .AddToggle("Toggle Scope Scan", tooltip: "Nếu BẬT, nhấn phím quét Scope để Bật/Tắt. Nếu TẮT, bạn phải giữ phím để quét.")
                .AddToggle("Show Weapon + Scope Info", t =>
                {
                    t.Reader.Click += (_, _) =>
                    {
                        _refreshRecognitionVisibility?.Invoke();
                    };
                }, tooltip: "Hiển thị thông tin Súng và Scope đã nhận diện trên màn hình.")
                .AddSlider("Weapon + Scope Info Size", "%", 1, 1, 60, 120, s =>
                {
                    weaponScopeInfoSize = s;
                    uiManager.S_WeaponScopeInfoSize = s;
                    s.Visibility = Dictionary.toggleState["Show Weapon + Scope Info"]
                        ? Visibility.Visible : Visibility.Collapsed;
                    s.Slider.ValueChanged += (_, _) =>
                        Dictionary.DetectedScopeOverlay?.SetScalePercent(s.Slider.Value);
                }, tooltip: "Điều chỉnh kích thước bảng Súng + Scope trên màn hình.")
                .AddSlider("Weapon + Scope Info Opacity", "%", 1, 1, 20, 100, s =>
                {
                    weaponScopeInfoOpacity = s;
                    uiManager.S_WeaponScopeInfoOpacity = s;
                    s.Visibility = Dictionary.toggleState["Show Weapon + Scope Info"]
                        ? Visibility.Visible : Visibility.Collapsed;
                    s.Slider.ValueChanged += (_, _) =>
                        Dictionary.DetectedScopeOverlay?.SetOpacityPercent(s.Slider.Value);
                }, tooltip: "Điều chỉnh độ trong suốt bảng Súng + Scope trên màn hình.");

                scopeBuilder.AddDropdown("Scope Recognition Method", d =>
                {
                    scopeMethodDropdown = d;
                    var methods = new[] { ("AI Model", RecognitionMethod.AiModel), ("Template Matching", RecognitionMethod.TemplateMatching), ("ORB Feature Matching", RecognitionMethod.OrbFeatureMatching), ("SIFT Feature Matching", RecognitionMethod.SiftFeatureMatching), ("Auto Hybrid", RecognitionMethod.AutoHybrid) };
                    foreach (var item in methods) _mainWindow.AddDropdownItem(d, item.Item1).Tag = item.Item2;
                    d.DropdownBox.SelectedIndex = Array.FindIndex(methods, x => x.Item2 == WeaponSlotManager.Instance.RecognitionConfig.ScopeMethod);
                    d.DropdownBox.SelectionChanged += (_, _) => ApplyRecognitionMethod(true, d);
                }, tooltip: "Chọn riêng phương pháp nhận diện hình scope.");

                // Add Toggle for Tab Reset Feature
                if (!Dictionary.toggleState.ContainsKey("Enable Tab Reset"))
                    Dictionary.toggleState["Enable Tab Reset"] = true; // Default to true
                weaponBuilder.AddToggle("Enable Tab Reset", tooltip: "Giữ Tab để đặt lại các điều chỉnh tạm thời.");

                try
                {
                    weaponBuilder.AddFileLocator("Weapon Model Location", fl =>
                    {
                        weaponAiControls.Add(fl);
                        fl.FileChanged += path => WeaponSlotManager.Instance.ConfigureWeaponModel(path);
                    }, filter: "Model Files (*.onnx;*.engine;*.trt)|*.onnx;*.engine;*.trt|ONNX Model (*.onnx)|*.onnx|TensorRT Engine (*.engine;*.trt)|*.engine;*.trt", dlExtension: "\\bin\\weapon_models");
                    scopeBuilder.AddFileLocator("Scope Model Location", fl => {
                        scopeAiControls.Add(fl);
                        fl.FileChanged += (path) => {
                            try {
                                bool isEngine = path.EndsWith(".engine", StringComparison.OrdinalIgnoreCase) || 
                                                path.EndsWith(".trt", StringComparison.OrdinalIgnoreCase);

                                if (isEngine && !AIManager.IsTensorRTAvaliable())
                                {
                                    LogManager.Log(LogManager.LogLevel.Error, "TensorRT (.engine) is not supported on this machine. CUDA 12.x or TensorRT 10.x DLLs not found in PATH.", true, 8000);
                                    
                                    // Reset file locator to previous path
                                    fl.SetFilePath(Dictionary.filelocationState["Scope Model Location"].ToString());
                                    return;
                                }

                                WeaponSlotManager.Instance.LoadModel(path);
                            }
                            catch { }
                        };
                    }, filter: "Model Files (*.onnx;*.engine;*.trt)|*.onnx;*.engine;*.trt|ONNX Model (*.onnx)|*.onnx|TensorRT Engine (*.engine;*.trt)|*.engine;*.trt", dlExtension: "\\bin\\scope_models");
                }
                catch (Exception ex)
                {
                    LogManager.Log(LogManager.LogLevel.Error, "Error adding FileLocator: " + ex.Message);
                }

                weaponBuilder.AddDropdown("Weapon Image Size", d =>
                {
                    weaponAiControls.Add(d);
                    int current = WeaponSlotManager.Instance.RecognitionConfig.WeaponImageSize;
                    int[] sizes = { 160, 256, 320, 416, 480, 512, 640, 768, 960, 1280 };
                    foreach (int size in sizes) _mainWindow.AddDropdownItem(d, size.ToString());
                    d.DropdownBox.SelectedIndex = Math.Max(0, Array.IndexOf(sizes, current));
                    d.DropdownBox.SelectionChanged += (_, _) => { if (int.TryParse((d.DropdownBox.SelectedItem as ComboBoxItem)?.Content?.ToString(), out int size)) WeaponSlotManager.Instance.SetWeaponImageSize(size); };
                }, tooltip: "Kích thước đầu vào riêng của model tên súng.");
                scopeBuilder.AddDropdown("Scope Image Size", d =>
                {
                    scopeAiControls.Add(d);
                    var manager = WeaponSlotManager.Instance;
                    bool updating = false;
                    void RefreshScopeSize()
                    {
                        updating = true;
                        try
                        {
                            int size = manager.ScopeImageSize;
                            var sizes = new[] { 160, 256, 320, 416, 480, 512, 640, 768, 800, 960, 1024, 1280, 1536, 1920, 2048, size }
                                .Distinct().OrderBy(value => value).ToArray();
                            d.DropdownBox.Items.Clear();
                            foreach (int value in sizes) d.DropdownBox.Items.Add(new ComboBoxItem { Content = value.ToString() });
                            d.DropdownBox.SelectedIndex = Array.IndexOf(sizes, size);
                            d.DropdownBox.IsEnabled = manager.CanChangeScopeImageSize && !WeaponSlotManager.CurrentlyLoadingScopeModel;
                            d.ToolTip = manager.CanChangeScopeImageSize
                                ? "Model scope động: chọn kích thước đầu vào nhận diện scope."
                                : "Kích thước theo model scope cố định; chọn model khác để thay đổi.";
                        }
                        finally { updating = false; }
                    }
                    d.Loaded += (s, e) => { manager.ScopeInputChanged -= RefreshScopeSize; manager.ScopeInputChanged += RefreshScopeSize; RefreshScopeSize(); };
                    d.Unloaded += (s, e) => manager.ScopeInputChanged -= RefreshScopeSize;
                    d.DropdownBox.SelectionChanged += (s, e) =>
                    {
                        if (updating) return;
                        if (int.TryParse((d.DropdownBox.SelectedItem as ComboBoxItem)?.Content?.ToString(), out int size))
                            manager.SetScopeImageSize(size);
                    };
                    RefreshScopeSize();
                }, tooltip: "Image Size riêng cho model scope; không thay đổi kích thước model Slot 1/2.");

                scopeBuilder.AddDropdown("Scope Capture Method", d =>
                {
                    scopeAiControls.Add(d);
                    uiManager.D_ScopeCaptureMethod = d;
                    _mainWindow.AddDropdownItem(d, "DirectX");
                    _mainWindow.AddDropdownItem(d, "GDI+");
                    _mainWindow.AddDropdownItem(d, "WGC");
                    d.DropdownBox.SelectedIndex = 1; // GDI+ default; saved selection is restored later.

                    int captureSelectionVersion = 0;
                    d.DropdownBox.SelectionChanged += async (s, e) =>
                    {
                        int selectionVersion = ++captureSelectionVersion;
                        var method = (d.DropdownBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
                        if (!string.IsNullOrEmpty(method))
                        {
                            if (method == "DirectX")
                            {
                                bool isDirectXSupported = false;
                                string? supportError = null;
                                await Task.Run(() =>
                                {
                                    try
                                    {
                                        var testManager = new CaptureManager();
                                        testManager.CaptureMethodKey = "Scope Capture Method";
                                        try { testManager.InitializeDxgiDuplication(updateSettingsOnFailure: false); }
                                        finally { testManager.Dispose(); }
                                        isDirectXSupported = true;
                                    }
                                    catch (Exception ex)
                                    {
                                        supportError = ex.Message;
                                    }
                                });

                                if (selectionVersion != captureSelectionVersion) return;

                                if (!isDirectXSupported)
                                {
                                    CaptureManager.ReportCaptureFailure("Scope Capture Method", "DirectX", new NotSupportedException(supportError));
                                    // Revert selection to GDI+
                                    for (int i = 0; i < d.DropdownBox.Items.Count; i++)
                                    {
                                        if ((d.DropdownBox.Items[i] as ComboBoxItem)?.Content?.ToString() == "GDI+")
                                        {
                                            d.DropdownBox.SelectedIndex = i;
                                            break;
                                        }
                                    }
                                    return;
                                }
                            }
                            Dictionary.dropdownState["Scope Capture Method"] = method;
                            _mainWindow.SaveAllConfigurations();
                            await CaptureManager.ReleaseInactiveWgcSessionsAsync();
                        }
                    };
                }, tooltip: "Phương thức chụp màn hình để nhận diện scope. WGC dùng Windows Graphics Capture; lựa chọn này độc lập với Screen Capture Method.");

                scopeBuilder.AddSlider("Scope Confidence", "% Confidence", 1, 1, 1, 100, s => { scopeAiControls.Add(s); uiManager.S_ScopeConfidence = s; }, tooltip: "Độ tin cậy tối thiểu để phát hiện scope.");
                weaponBuilder.AddSlider("Weapon AI Confidence", "%", 1, 1, 1, 100, s => { weaponAiControls.Add(s); s.Slider.ValueChanged += (_, _) => { WeaponSlotManager.Instance.RecognitionConfig.WeaponAiConfidence = s.Slider.Value / 100; SaveRecognitionSettings(); }; });
                void AddTemplateControls(SectionBuilder builder, List<FrameworkElement> controls, string prefix, TemplateMatchingSettings settings)
                {
                    string Key(string suffix) => $"{prefix} Template {suffix}";
                    builder.AddSlider(Key("Confidence Threshold"), "%", 1, 1, 1, 100, s => { controls.Add(s); s.Slider.ValueChanged += (_, _) => { settings.ConfidenceThreshold = s.Slider.Value / 100; SaveRecognitionSettings(); }; }, tooltip: "Mức giống nhau tối thiểu để chấp nhận template.")
                           .AddSlider(Key("Scale Min"), "Scale", .01, .05, .2, 2, s => { controls.Add(s); s.Slider.ValueChanged += (_, _) => { settings.ScaleMin = s.Slider.Value; SaveRecognitionSettings(); }; }, tooltip: "Tỷ lệ nhỏ nhất khi dò template.")
                           .AddSlider(Key("Scale Max"), "Scale", .01, .05, .2, 3, s => { controls.Add(s); s.Slider.ValueChanged += (_, _) => { settings.ScaleMax = s.Slider.Value; SaveRecognitionSettings(); }; }, tooltip: "Tỷ lệ lớn nhất khi dò template.")
                           .AddSlider(Key("Scale Step"), "Scale", .01, .01, .01, .5, s => { controls.Add(s); s.Slider.ValueChanged += (_, _) => { settings.ScaleStep = s.Slider.Value; SaveRecognitionSettings(); }; }, tooltip: "Khoảng tăng giữa các tỷ lệ dò.")
                           .AddToggle(Key("Multi-scale"), t => { controls.Add(t); t.Reader.Click += (_, _) => { settings.EnableMultiScale = Dictionary.toggleState[Key("Multi-scale")]; SaveRecognitionSettings(); }; }, tooltip: "Dò template ở nhiều kích thước khác nhau.")
                           .AddToggle(Key("Edge Matching"), t => { controls.Add(t); t.Reader.Click += (_, _) => { settings.EnableEdgeMatching = Dictionary.toggleState[Key("Edge Matching")]; SaveRecognitionSettings(); }; }, tooltip: "So khớp theo đường viền của hình.")
                           .AddToggle(Key("Alpha Mask"), t => { controls.Add(t); t.Reader.Click += (_, _) => { settings.EnableAlphaMask = Dictionary.toggleState[Key("Alpha Mask")]; SaveRecognitionSettings(); }; }, tooltip: "Bỏ qua vùng trong suốt của template.")
                           .AddToggle(Key("Color Mask"), t => { controls.Add(t); t.Reader.Click += (_, _) => { settings.EnableColorMask = Dictionary.toggleState[Key("Color Mask")]; SaveRecognitionSettings(); }; }, tooltip: "Giới hạn so khớp theo vùng màu của template.");
                }
                AddTemplateControls(weaponBuilder, weaponTemplateControls, "Weapon", weaponTemplateSettings);
                AddTemplateControls(scopeBuilder, scopeTemplateControls, "Scope", scopeTemplateSettings);

                void AddFeatureControls(SectionBuilder builder, string prefix, FeatureMatchingSettings settings,
                    List<FrameworkElement> orb, List<FrameworkElement> sift)
                {
                    string Key(string method, string suffix) => $"{prefix} {method} {suffix}";
                    builder.AddSlider(Key("ORB", "Feature Count"), "Features", 10, 50, 50, 2000, s => { orb.Add(s); s.Slider.ValueChanged += (_, _) => { settings.OrbFeatureCount = (int)s.Slider.Value; SaveRecognitionSettings(); }; })
                           .AddSlider(Key("ORB", "Ratio Threshold"), "Ratio", .01, .05, .3, .95, s => { orb.Add(s); s.Slider.ValueChanged += (_, _) => { settings.OrbRatioThreshold = s.Slider.Value; SaveRecognitionSettings(); }; })
                           .AddSlider(Key("ORB", "Minimum Good Matches"), "Matches", 1, 1, 4, 100, s => { orb.Add(s); s.Slider.ValueChanged += (_, _) => { settings.OrbMinimumGoodMatches = (int)s.Slider.Value; SaveRecognitionSettings(); }; })
                           .AddSlider(Key("ORB", "RANSAC Threshold"), "Pixels", .1, .5, .5, 20, s => { orb.Add(s); s.Slider.ValueChanged += (_, _) => { settings.OrbRansacThreshold = s.Slider.Value; SaveRecognitionSettings(); }; })
                           .AddSlider(Key("SIFT", "Feature Count"), "Features", 10, 50, 50, 3000, s => { sift.Add(s); s.Slider.ValueChanged += (_, _) => { settings.SiftFeatureCount = (int)s.Slider.Value; SaveRecognitionSettings(); }; })
                           .AddSlider(Key("SIFT", "Ratio Threshold"), "Ratio", .01, .05, .3, .95, s => { sift.Add(s); s.Slider.ValueChanged += (_, _) => { settings.SiftRatioThreshold = s.Slider.Value; SaveRecognitionSettings(); }; })
                           .AddSlider(Key("SIFT", "Minimum Good Matches"), "Matches", 1, 1, 4, 100, s => { sift.Add(s); s.Slider.ValueChanged += (_, _) => { settings.SiftMinimumGoodMatches = (int)s.Slider.Value; SaveRecognitionSettings(); }; })
                           .AddSlider(Key("SIFT", "RANSAC Threshold"), "Pixels", .1, .5, .5, 20, s => { sift.Add(s); s.Slider.ValueChanged += (_, _) => { settings.SiftRansacThreshold = s.Slider.Value; SaveRecognitionSettings(); }; })
                           .AddSlider(Key("SIFT", "Scan Interval"), "ms", 10, 50, 100, 2000, s => { sift.Add(s); s.Slider.ValueChanged += (_, _) => { settings.SiftScanIntervalMs = (int)s.Slider.Value; SaveRecognitionSettings(); }; });
                }
                AddFeatureControls(weaponBuilder, "Weapon", weaponFeatureSettings, weaponOrbControls, weaponSiftControls);
                AddFeatureControls(scopeBuilder, "Scope", scopeFeatureSettings, scopeOrbControls, scopeSiftControls);
                weaponBuilder.AddSlider("Weapon Scan Delay", "Seconds", 0.1, 0.1, 0, 5, s => uiManager.S_WeaponScanDelay = s, tooltip: "Thời gian giữ phím scan (Tab) trước khi bắt đầu quét.")
                       .AddSlider("Tab Reset Adjust", "Seconds", 0.1, 0.1, 0, 5, tooltip: "Thời gian giữ Tab để đặt lại các điều chỉnh recoil tạm thời.")
                       .AddKeyChanger("Weapon Scan Keybind", tooltip: "Phím riêng để quét nhận diện Súng.")
                       .AddKeyChanger("Weapon Slot 1 Keybind", tooltip: "Phím tắt để áp dụng cài đặt vũ khí slot 1.")
                       .AddKeyChanger("Weapon Slot 2 Keybind", tooltip: "Phím tắt để áp dụng cài đặt vũ khí slot 2.");
                scopeBuilder.AddKeyChanger("Scope Scan Keybind", tooltip: "Phím riêng để quét nhận diện Scope.");
                       
                // Add Region Selectors manually with better styling
                // Wrap in a Border to match the style of other items (AToggle, etc.)
                // This ensures the vertical side lines continue seamlessly.
                Border CreateRegionContainer(Grid grid)
                {
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    for (int i = 0; i < 2; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    return ApplyCardTheme(new Border
                    {
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(8),
                        Margin = new Thickness(6, 3, 6, 3),
                        Padding = new Thickness(10, 5, 10, 5),
                        Child = grid
                    });
                }

                Border CreateActionContainer(params Button[] buttons)
                {
                    var stack = new StackPanel();
                    foreach (var button in buttons) stack.Children.Add(button);
                    return ApplyCardTheme(new Border
                    {
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(8),
                        Margin = new Thickness(6, 3, 6, 3),
                        Padding = new Thickness(0, 2, 0, 6),
                        Child = stack
                    });
                }

                // Style for buttons to match app theme with Rounded Corners
                var btnStyle = new Style(typeof(Button));
                btnStyle.Setters.Add(new Setter(Button.BackgroundProperty, new DynamicResourceExtension("ThemePrimaryAction")));
                btnStyle.Setters.Add(new Setter(Button.ForegroundProperty, new DynamicResourceExtension("ThemePrimaryActionForeground")));
                btnStyle.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(0)));
                btnStyle.Setters.Add(new Setter(Button.HeightProperty, 30.0));
                btnStyle.Setters.Add(new Setter(Button.CursorProperty, System.Windows.Input.Cursors.Hand));
                btnStyle.Setters.Add(new Setter(Button.FontSizeProperty, 12.0));
                
                // Create ControlTemplate for Rounded Corners
                var btnTemplate = new ControlTemplate(typeof(Button));
                var borderFactory = new FrameworkElementFactory(typeof(Border));
                borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
                borderFactory.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));
                borderFactory.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
                borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));

                var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
                contentPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
                contentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
                
                borderFactory.AppendChild(contentPresenter);
                btnTemplate.VisualTree = borderFactory;
                
                btnStyle.Setters.Add(new Setter(Button.TemplateProperty, btnTemplate));
                var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
                hoverTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, .86));
                btnStyle.Triggers.Add(hoverTrigger);
                var pressedTrigger = new Trigger { Property = Button.IsPressedProperty, Value = true };
                pressedTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, .70));
                btnStyle.Triggers.Add(pressedTrigger);

                var refreshRegionText = new Dictionary<(bool Scope, int Slot), Action>();
                void AddRegionRow(Grid grid, int row, int slot, bool scope)
                {
                    string kind = scope ? "Scope" : "tên Súng";
                    var select = ApplyThemeBackground(new Button { Content = $"Chọn vùng {kind} {slot}", Style = btnStyle, Margin = new Thickness(2) }, "ThemeSecondaryAction");
                    var clear = ApplyThemeBackground(new Button { Content = $"Xóa vùng", Style = btnStyle, Margin = new Thickness(2) }, "ThemeDangerAction");
                    void RefreshText() { var state = WeaponSlotManager.Instance.GetSlotSnapshot(slot); var roi = scope ? state.ScopeRegion : state.WeaponRegion; select.Content = roi.IsEmpty ? $"{kind} {slot}: Chưa chọn vùng" : $"{kind} {slot}: {roi.X},{roi.Y} {roi.Width}×{roi.Height}"; }
                    refreshRegionText[(scope, slot)] = RefreshText;
                    select.Click += async (_, _) =>
                    {
                        var owner = Window.GetWindow(this);
                        var hiddenWindows = Application.Current?.Windows.OfType<Window>()
                            .Where(window => window.IsVisible).ToArray() ?? Array.Empty<Window>();
                        try
                        {
                            foreach (var window in hiddenWindows) window.Hide();
                            await Task.Delay(180);
                            bool SelectRegion(int targetSlot)
                            {
                                var selector = new RegionSelectorWindow($"Chọn vùng {kind} Slot {targetSlot}");
                                selector.ShowDialog();
                                if (!selector.IsConfirmed) return false;
                                WeaponSlotManager.Instance.SetRegion(targetSlot, scope, selector.SelectedRegion);
                                if (refreshRegionText.TryGetValue((scope, targetSlot), out var refresh)) refresh();
                                return true;
                            }
                            SelectRegion(slot);
                        }
                        finally
                        {
                            foreach (var window in hiddenWindows) window.Show();
                            owner?.Activate();
                        }
                    };
                    clear.Click += (_, _) => { if (MessageBox.Show($"Xóa riêng vùng {kind} Slot {slot}?", "ROI", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) { WeaponSlotManager.Instance.ClearRegion(slot, scope); RefreshText(); } };
                    RefreshText();
                    Grid.SetRow(select, row); Grid.SetColumn(select, 0); Grid.SetRow(clear, row); Grid.SetColumn(clear, 1); grid.Children.Add(select); grid.Children.Add(clear);
                }
                var weaponRegionGrid = new Grid();
                var scopeRegionGrid = new Grid();
                AddRegionRow(weaponRegionGrid, 0, 1, false); AddRegionRow(weaponRegionGrid, 1, 2, false);
                AddRegionRow(scopeRegionGrid, 0, 1, true); AddRegionRow(scopeRegionGrid, 1, 2, true);
                WeaponRecognitionSystem.Children.Add(CreateRegionContainer(weaponRegionGrid));
                ScopeRecognitionSystem.Children.Add(CreateRegionContainer(scopeRegionGrid));
                Button CreateCaptureButton(bool scope, int slot)
                {
                    var button = ApplyThemeBackground(new Button { Content = $"Chụp ảnh {(scope ? "Scope" : "Súng")} Slot {slot}", Tag = $"RecognitionCapture:{(scope ? "Scope" : "Weapon")}:{slot}", Style = btnStyle, Height = 34, Margin = new Thickness(10, slot == 1 ? 7 : 3, 10, 3) }, "ThemeTertiaryAction");
                    button.Click += async (_, _) => { if (_mainWindow != null) await _mainWindow.CaptureRecognitionTemplateAsync(scope, slot); };
                    return button;
                }
                weaponTemplateManager = ApplyThemeBackground(new Button { Content = "Quản lý Template Súng", Style = btnStyle, Height = 32, Margin = new Thickness(10, 6, 10, 2) }, "ThemePrimaryAction");
                scopeTemplateManager = ApplyThemeBackground(new Button { Content = "Quản lý Template Scope", Style = btnStyle, Height = 32, Margin = new Thickness(10, 6, 10, 2) }, "ThemePrimaryAction");
                weaponTemplateManager.Click += (_, _) => new TemplateManagerWindow(false) { Owner = Window.GetWindow(this) }.ShowDialog();
                scopeTemplateManager.Click += (_, _) => new TemplateManagerWindow(true) { Owner = Window.GetWindow(this) }.ShowDialog();
                WeaponRecognitionSystem.Children.Add(CreateActionContainer(CreateCaptureButton(false, 1), CreateCaptureButton(false, 2), weaponTemplateManager));
                ScopeRecognitionSystem.Children.Add(CreateActionContainer(CreateCaptureButton(true, 1), CreateCaptureButton(true, 2), scopeTemplateManager));
                WeaponRecognitionSystem.Children.Add(new ARectangleBottom());
                ScopeRecognitionSystem.Children.Add(new ARectangleBottom());

                void RefreshRecognitionSettingVisibility()
                {
                    var weaponMethod = (weaponMethodDropdown?.DropdownBox.SelectedItem as ComboBoxItem)?.Tag as RecognitionMethod?;
                    var scopeMethod = (scopeMethodDropdown?.DropdownBox.SelectedItem as ComboBoxItem)?.Tag as RecognitionMethod?;
                    bool weaponExpanded = !_localMinimizeState.GetValueOrDefault("Weapon Recognition System");
                    bool scopeExpanded = !_localMinimizeState.GetValueOrDefault("Scope Recognition System");
                    static void Show(IEnumerable<FrameworkElement> controls, bool show)
                    {
                        foreach (var control in controls) control.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                    }
                    Show(weaponAiControls, weaponExpanded && weaponMethod == RecognitionMethod.AiModel);
                    Show(scopeAiControls, scopeExpanded && scopeMethod == RecognitionMethod.AiModel);
                    Show(weaponTemplateControls, weaponExpanded && weaponMethod == RecognitionMethod.TemplateMatching);
                    Show(scopeTemplateControls, scopeExpanded && scopeMethod == RecognitionMethod.TemplateMatching);
                    Show(weaponOrbControls, weaponExpanded && weaponMethod == RecognitionMethod.OrbFeatureMatching);
                    Show(scopeOrbControls, scopeExpanded && scopeMethod == RecognitionMethod.OrbFeatureMatching);
                    Show(weaponSiftControls, weaponExpanded && weaponMethod == RecognitionMethod.SiftFeatureMatching);
                    Show(scopeSiftControls, scopeExpanded && scopeMethod == RecognitionMethod.SiftFeatureMatching);
                    if (weaponScopeInfoSize != null)
                        weaponScopeInfoSize.Visibility = scopeExpanded && Dictionary.toggleState["Show Weapon + Scope Info"]
                            ? Visibility.Visible : Visibility.Collapsed;
                    if (weaponScopeInfoOpacity != null)
                        weaponScopeInfoOpacity.Visibility = scopeExpanded && Dictionary.toggleState["Show Weapon + Scope Info"]
                            ? Visibility.Visible : Visibility.Collapsed;
                    if (weaponTemplateManager != null) weaponTemplateManager.Visibility = weaponExpanded ? Visibility.Visible : Visibility.Collapsed;
                    if (scopeTemplateManager != null) scopeTemplateManager.Visibility = scopeExpanded ? Visibility.Visible : Visibility.Collapsed;
                }
                _refreshRecognitionVisibility = RefreshRecognitionSettingVisibility;
                RefreshRecognitionSettingVisibility();

            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, "Critical Error inside LoadWeaponSlotSystem: " + ex.Message);
                // Displays error on UI
                var errorText = new TextBlock
                {
                    Text = $"Error loading Weapon Slot System:\n{ex.Message}\n{ex.StackTrace}",
                    Foreground = Brushes.Red,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(5)
                };
                WeaponSlotSystem.Children.Add(errorText);
            }
        }

        private void LoadFOVConfig()
        {
            var uiManager = _mainWindow!.uiManager;
            var builder = new SectionBuilder(this, FOVConfig);

            builder
                .AddTitle("FOV Config", true, t =>
                {
                    uiManager.AT_FOV = t;
                    t.Minimize.Click += (s, e) => 
                    {
                        TogglePanel("FOV Config", FOVConfigPanel);
                        if (_mainWindow != null) MainWindow.UpdateSliderVisibility(_mainWindow.uiManager);
                    };
                })
                .AddToggle("FOV", t => uiManager.T_FOV = t,
                    tooltip: "Hiển thị vòng tròn trên màn hình biểu thị khu vực phát hiện.")
                .AddToggle("Dynamic FOV", t => uiManager.T_DynamicFOV = t,
                    tooltip: "Thay đổi kích thước FOV khi giữ phím. Hữu ích khi bật ống ngắm.")
                .AddToggle("Third Person Support", t => uiManager.T_ThirdPersonSupport = t,
                    tooltip: "Điều chỉnh vị trí FOV cho game góc nhìn thứ ba.")
                .AddToggle("Virtual Crosshair", t => uiManager.T_VirtualCrosshair = t,
                    tooltip: "Hiển thị chấm trắng nhỏ ở chính giữa màn hình làm tâm ảo. Sẽ ẩn đi khi nhấn giữ đồng thời cả chuột trái và chuột phải.")
                .AddColorChanger("Crosshair Color", c =>
                {
                    uiManager.CC_CrosshairColor = c;
                    c.Reader.Click += (s, e) =>
                    {
                        if (crosshairColorPickerInstance != null && crosshairColorPickerInstance.IsVisible)
                        {
                            crosshairColorPickerInstance.Activate();
                            return;
                        }

                        Color initialColor = Colors.White;
                        if (c.ColorChangingBorder.Background is SolidColorBrush scb)
                            initialColor = scb.Color;
                        crosshairColorPickerInstance = new UISections.ColorPicker(initialColor, "Crosshair Color");

                        crosshairColorPickerInstance.ColorChanged += (color) =>
                        {
                            // Update the color square
                            c.ColorChangingBorder.Background = new SolidColorBrush(color);
                            // Save to dictionary for persistence
                            Dictionary.colorState["Crosshair Color"] = $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
                            PropertyChanger.PostCrosshairColor(color);
                        };

                        crosshairColorPickerInstance.Closed += (sender, args) =>
                        {
                            crosshairColorPickerInstance = null;
                        };

                        crosshairColorPickerInstance.Show();
                    };
                })
                .AddSlider("Crosshair Size", "Size", 1, 1, 1, 30, s =>
                {
                    uiManager.S_CrosshairSize = s;
                    s.Slider.ValueChanged += (sender, e) =>
                    {
                        PropertyChanger.PostCrosshairSize(s.Slider.Value);
                    };
                }, tooltip: "Kích thước của chấm tâm ảo. (Mặc định: 6)")
                .AddKeyChanger("Crosshair Hide Key 1", k => uiManager.C_CrosshairHideKey1 = k,
                    tooltip: "Phím ẩn tâm ảo thứ nhất (Mặc định: Chuột phải).")
                .AddKeyChanger("Dynamic FOV Keybind", k => uiManager.C_DynamicFOV = k,
                    tooltip: "Phím giữ để chuyển sang kích thước FOV động.")
                .AddDropdown("FOV Style", d =>
                {
                    uiManager.D_FOVSTYLE = d;

                    var circleItem = _mainWindow.AddDropdownItem(d, "Circle");
                    var rectangleItem = _mainWindow.AddDropdownItem(d, "Rectangle");

                    circleItem.Selected += (s, e) =>
                    {
                        MainWindow.FOVWindow.Circle.Visibility = Visibility.Visible;
                        MainWindow.FOVWindow.RectangleShape.Visibility = Visibility.Collapsed;
                    };

                    rectangleItem.Selected += (s, e) =>
                    {
                        MainWindow.FOVWindow.Circle.Visibility = Visibility.Collapsed;
                        MainWindow.FOVWindow.RectangleShape.Visibility = Visibility.Visible;
                    };
                }, tooltip: "Shape of the FOV overlay. Circle is most common.")
                .AddColorChanger("FOV Color", c =>
                {
                    c.Reader.Click += (s, e) =>
                    {
                        if (fovColorPickerInstance != null && fovColorPickerInstance.IsVisible)
                        {
                            fovColorPickerInstance.Activate();
                            return;
                        }

                        Color initialColor = Colors.White;
                        if (c.ColorChangingBorder.Background is SolidColorBrush scb)
                            initialColor = scb.Color;
                        fovColorPickerInstance = new UISections.ColorPicker(initialColor, "FOV Color");

                        fovColorPickerInstance.ColorChanged += (color) =>
                        {
                            // Update the color square
                            c.ColorChangingBorder.Background = new SolidColorBrush(color);
                            // Save to dictionary for persistence
                            Dictionary.colorState["FOV Color"] = $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
                            PropertyChanger.PostColor(color);
                        };

                        fovColorPickerInstance.Closed += (sender, args) =>
                        {
                            fovColorPickerInstance = null;
                        };

                        fovColorPickerInstance.Show();
                    };
                })
                .AddSlider("FOV Size", "Size", 1, 1, 10, 640, s =>
                {
                    uiManager.S_FOVSize = s;
                    s.Slider.ValueChanged += (sender, e) =>
                    {
                        _mainWindow.ActualFOV = s.Slider.Value;
                        PropertyChanger.PostNewFOVSize(_mainWindow.ActualFOV);
                    };
                }, tooltip: "Size of the detection area. Smaller = more precise, larger = wider coverage.")
                .AddSlider("Dynamic FOV Size", "Size", 1, 1, 10, 640, s =>
                {
                    uiManager.S_DynamicFOVSize = s;
                    s.Slider.ValueChanged += (sender, e) =>
                    {
                        if (Dictionary.toggleState["Dynamic FOV"])
                            PropertyChanger.PostNewFOVSize(s.Slider.Value);
                    };
                }, tooltip: "FOV size when holding the Dynamic FOV key. Usually smaller for scoped aim.")
                .AddSeparator();
        }

        private void LoadESPConfig()
        {
            var uiManager = _mainWindow!.uiManager;
            var builder = new SectionBuilder(this, ESPConfig);

            builder
                .AddTitle("ESP Config", true, t =>
                {
                    uiManager.AT_DetectedPlayer = t;
                    t.Minimize.Click += (s, e) => 
                    {
                        TogglePanel("ESP Config", ESPConfigPanel);
                        if (_mainWindow != null) MainWindow.UpdateSliderVisibility(_mainWindow.uiManager);
                    };
                })
                .AddToggle("Show Detected Player", t => uiManager.T_ShowDetectedPlayer = t,
                    tooltip: "Vẽ một khung bao quanh các mục tiêu bị phát hiện trên màn hình.")
                .AddToggle("Show Detection Performance", t =>
                {
                    t.ToggleTitle.Content = "Show FPS / Inference";
                    t.IsEnabled = Dictionary.toggleState["Show Detected Player"];
                    t.Loaded += (_, _) => t.IsEnabled = Dictionary.toggleState["Show Detected Player"];
                }, tooltip: "Hiển thị FPS và thời gian suy luận trên ESP. Chỉ dùng khi bật Show Detected Player.")
                .AddToggle("Show AI Confidence", t => uiManager.T_ShowAIConfidence = t,
                    tooltip: "Hiển thị mức độ tin cậy của AI đối với từng phát hiện (0-100%).")
                .AddToggle("Show Tracers", t => uiManager.T_ShowTracers = t,
                    tooltip: "Vẽ các đường kẻ từ cạnh màn hình đến các mục tiêu được phát hiện.");

            builder.AddDropdown("Tracer Position", d =>
            {
                d.DropdownBox.SelectedIndex = 0;
                uiManager.D_TracerPosition = d;
                // Changed the positions of these as top is above middle & bottom - ts (this) bothered me so i had to
                _mainWindow.AddDropdownItem(d, "Top");
                _mainWindow.AddDropdownItem(d, "Middle");
                _mainWindow.AddDropdownItem(d, "Bottom");
                d.DropdownBox.SelectionChanged += (s, e) =>
                {
                    if (Dictionary.toggleState["Show Detected Player"])
                    {
                        // simulate a click to turn it off - this is to force a reload of the ui cause tracer doesn't update otherwise - helz
                        uiManager.T_ShowDetectedPlayer.Reader.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                        // simulate a click to turn it back on - same as before ^ - helz
                        uiManager.T_ShowDetectedPlayer.Reader.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    }
                    else
                    {
                        if (Dictionary.DetectedPlayerOverlay != null)
                        {
                            Dictionary.DetectedPlayerOverlay.ForceReposition();
                        }
                    }
                };
            }, tooltip: "Vị trí bắt đầu của các đường kẻ (tracers) trên màn hình.");

            builder
                .AddColorChanger("Detected Player Color", c =>
                {
                    c.Reader.Click += (s, e) =>
                    {
                        if (colorPickerInstance != null && colorPickerInstance.IsVisible)
                        {
                            colorPickerInstance.Activate();
                            return;
                        }

                        Color initialColor = Colors.White;
                        if (c.ColorChangingBorder.Background is SolidColorBrush scb)
                            initialColor = scb.Color;
                        colorPickerInstance = new UISections.ColorPicker(initialColor, "ESP Color");

                        colorPickerInstance.ColorChanged += (color) =>
                        {
                            // Update the color square
                            c.ColorChangingBorder.Background = new SolidColorBrush(color);
                            // Save to dictionary for persistence
                            Dictionary.colorState["Detected Player Color"] = $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
                            PropertyChanger.PostDPColor(color);
                        };

                        colorPickerInstance.Closed += (sender, args) =>
                        {
                            colorPickerInstance = null;
                        };

                        colorPickerInstance.Show();
                    };
                })
                .AddColorChanger("Single Class Color", c =>
                {
                    c.Reader.Click += (s, e) =>
                    {
                        if (colorPickerInstance != null && colorPickerInstance.IsVisible)
                        {
                            colorPickerInstance.Activate();
                            return;
                        }

                        Color initialColor = Colors.White;
                        if (c.ColorChangingBorder.Background is SolidColorBrush scb)
                            initialColor = scb.Color;
                        colorPickerInstance = new UISections.ColorPicker(initialColor, "Single Class Color");

                        colorPickerInstance.ColorChanged += (color) =>
                        {
                            c.ColorChangingBorder.Background = new SolidColorBrush(color);
                            Dictionary.colorState["Single Class Color"] = $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
                        };

                        colorPickerInstance.Closed += (sender, args) =>
                        {
                            colorPickerInstance = null;
                        };

                        colorPickerInstance.Show();
                    };
                })
                .AddColorChanger("ONNX Head Color", c =>
                {
                    c.Reader.Click += (s, e) =>
                    {
                        if (colorPickerInstance != null && colorPickerInstance.IsVisible)
                        {
                            colorPickerInstance.Activate();
                            return;
                        }

                        Color initialColor = Colors.White;
                        if (c.ColorChangingBorder.Background is SolidColorBrush scb)
                            initialColor = scb.Color;
                        colorPickerInstance = new UISections.ColorPicker(initialColor, "ONNX Head Color");

                        colorPickerInstance.ColorChanged += (color) =>
                        {
                            c.ColorChangingBorder.Background = new SolidColorBrush(color);
                            Dictionary.colorState["ONNX Head Color"] = $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
                        };

                        colorPickerInstance.Closed += (sender, args) =>
                        {
                            colorPickerInstance = null;
                        };

                        colorPickerInstance.Show();
                    };
                })
                .AddColorChanger("ONNX Body Color", c =>
                {
                    c.Reader.Click += (s, e) =>
                    {
                        if (colorPickerInstance != null && colorPickerInstance.IsVisible)
                        {
                            colorPickerInstance.Activate();
                            return;
                        }

                        Color initialColor = Colors.White;
                        if (c.ColorChangingBorder.Background is SolidColorBrush scb)
                            initialColor = scb.Color;
                        colorPickerInstance = new UISections.ColorPicker(initialColor, "ONNX Body Color");

                        colorPickerInstance.ColorChanged += (color) =>
                        {
                            c.ColorChangingBorder.Background = new SolidColorBrush(color);
                            Dictionary.colorState["ONNX Body Color"] = $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
                        };

                        colorPickerInstance.Closed += (sender, args) =>
                        {
                            colorPickerInstance = null;
                        };

                        colorPickerInstance.Show();
                    };
                })
                .AddColorChanger("Engine Head Color", c =>
                {
                    c.Reader.Click += (s, e) =>
                    {
                        if (colorPickerInstance != null && colorPickerInstance.IsVisible)
                        {
                            colorPickerInstance.Activate();
                            return;
                        }

                        Color initialColor = Colors.White;
                        if (c.ColorChangingBorder.Background is SolidColorBrush scb)
                            initialColor = scb.Color;
                        colorPickerInstance = new UISections.ColorPicker(initialColor, "Engine Head Color");

                        colorPickerInstance.ColorChanged += (color) =>
                        {
                            c.ColorChangingBorder.Background = new SolidColorBrush(color);
                            Dictionary.colorState["Engine Head Color"] = $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
                        };

                        colorPickerInstance.Closed += (sender, args) =>
                        {
                            colorPickerInstance = null;
                        };

                        colorPickerInstance.Show();
                    };
                })
                .AddColorChanger("Engine Body Color", c =>
                {
                    c.Reader.Click += (s, e) =>
                    {
                        if (colorPickerInstance != null && colorPickerInstance.IsVisible)
                        {
                            colorPickerInstance.Activate();
                            return;
                        }

                        Color initialColor = Colors.White;
                        if (c.ColorChangingBorder.Background is SolidColorBrush scb)
                            initialColor = scb.Color;
                        colorPickerInstance = new UISections.ColorPicker(initialColor, "Engine Body Color");

                        colorPickerInstance.ColorChanged += (color) =>
                        {
                            c.ColorChangingBorder.Background = new SolidColorBrush(color);
                            Dictionary.colorState["Engine Body Color"] = $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
                        };

                        colorPickerInstance.Closed += (sender, args) =>
                        {
                            colorPickerInstance = null;
                        };

                        colorPickerInstance.Show();
                    };
                })
                .AddSlider("AI Confidence Font Size", "Size", 1, 1, 1, 30, s =>
                {
                    uiManager.S_DPFontSize = s;
                    s.Slider.ValueChanged += (sender, e) => PropertyChanger.PostDPFontSize((int)s.Slider.Value);
                }, tooltip: "Kích thước văn bản cho phần hiển thị tỷ lệ phần trăm độ tin cậy.")
                .AddSlider("Corner Radius", "Radius", 1, 1, 0, 100, s =>
                {
                    uiManager.S_DPCornerRadius = s;
                    s.Slider.ValueChanged += (sender, e) => PropertyChanger.PostDPWCornerRadius((int)s.Slider.Value);
                }, tooltip: "Độ bo góc của khung phát hiện. 0 = góc nhọn.")
                .AddSlider("Border Thickness", "Thickness", 0.1, 1, 0.1, 10, s =>
                {
                    uiManager.S_DPBorderThickness = s;
                    s.Slider.ValueChanged += (sender, e) => PropertyChanger.PostDPWBorderThickness(s.Slider.Value);
                }, tooltip: "Độ dày viền của khung phát hiện.")
                .AddSlider("Opacity", "Opacity", 0.1, 0.1, 0, 1, s =>
                {
                    uiManager.S_DPOpacity = s;
                    s.Slider.ValueChanged += (sender, e) => PropertyChanger.PostDPWOpacity(s.Slider.Value);
                }, tooltip: "Độ trong suốt của khung phát hiện. 0 = trong suốt hoàn toàn, 1 = đặc.")
                .AddSeparator();
        }

        private void LoadRecoilConfig()
        {
            try
            {
                var uiManager = _mainWindow!.uiManager;
                RecoilConfig.Children.Clear();
                ASlider? conditionalWheelSlider = null;

                // 1. Title
                var title = new ATitle("Recoil Config", true);
                title.Minimize.Click += (s, e) => 
                {
                    TogglePanel("Recoil Config", RecoilConfigPanel);
                    if (_mainWindow != null) MainWindow.UpdateSliderVisibility(_mainWindow.uiManager);
                    if (conditionalWheelSlider != null && !Dictionary.toggleState["Mouse Wheel Adjust"])
                        conditionalWheelSlider.Visibility = Visibility.Collapsed;
                };
                RecoilConfig.Children.Add(title);

                // 2. Main Toggle
                if (!Dictionary.toggleState.ContainsKey("Scope Recoil Control"))
                    Dictionary.toggleState["Scope Recoil Control"] = false;

                var toggleScopeRecoil = CreateToggle("Scope Recoil Control", "Tự động ghì tâm chuột xuống khi bắn.");
                toggleScopeRecoil.ToggleTitle.Content = "Bật ghì tâm tự động";
                uiManager.T_ScopeRecoil = toggleScopeRecoil;
                RecoilConfig.Children.Add(toggleScopeRecoil);

                // 3. Keybind
                string toggleKey = Dictionary.bindingSettings.ContainsKey("Recoil Toggle Keybind") ? Dictionary.bindingSettings["Recoil Toggle Keybind"] : "None";
                var keybind = CreateKeyChanger("Recoil Toggle", toggleKey, "Phím tắt để bật/tắt kiểm soát độ giật.");
                uiManager.C_RecoilKeybind = keybind;
                RecoilConfig.Children.Add(keybind);

                // 4. Mouse Wheel Toggle
                if (!Dictionary.toggleState.ContainsKey("Mouse Wheel Adjust"))
                    Dictionary.toggleState["Mouse Wheel Adjust"] = false;

                var toggleWheel = CreateToggle("Mouse Wheel Adjust", "Sử dụng con lăn chuột để điều chỉnh độ mạnh của độ giật ngay tức thì.");
                RecoilConfig.Children.Add(toggleWheel);

                // Mouse Wheel Step
                if (!Dictionary.sliderSettings.ContainsKey("Mouse Wheel Adjust Step"))
                    Dictionary.sliderSettings["Mouse Wheel Adjust Step"] = 2.0;

                var sliderWheelStep = CreateSlider("Mouse Wheel Adjust Step", "Độ nhạy con lăn", 0.1, 0.5, 0.1, 10.0, "Độ thay đổi của lực ghì tâm mỗi khi lăn chuột.");
                conditionalWheelSlider = sliderWheelStep;
                sliderWheelStep.SliderTitle.Content = "Bước điều chỉnh con lăn";
                uiManager.S_MouseWheelAdjustStep = sliderWheelStep;
                void RefreshWheelStepVisibility() => sliderWheelStep.Visibility = Dictionary.toggleState["Mouse Wheel Adjust"] && !_localMinimizeState.GetValueOrDefault("Recoil Config") ? Visibility.Visible : Visibility.Collapsed;
                toggleWheel.Reader.Click += (_, _) => RefreshWheelStepVisibility();
                RefreshWheelStepVisibility();
                RecoilConfig.Children.Add(sliderWheelStep);

                var profileEditor = ApplyThemeBackground(new Button { Content = "Mở profile Súng + Scope", Height = 34, Margin = new Thickness(8) }, "ThemePrimaryAction");
                profileEditor.Click += (_, _) => new WeaponScopeProfileWindow { Owner = Window.GetWindow(this) }.ShowDialog();

                var fallbackEditor = ApplyThemeBackground(new Button
                {
                    Content = "Chỉnh recoil dự phòng theo Scope",
                    Height = 34,
                    Margin = new Thickness(8, 0, 8, 8),
                    ToolTip = "Mở GUI 4 giai đoạn dùng khi chưa có profile phù hợp cho súng."
                }, "ThemeSecondaryAction");
                fallbackEditor.Click += (_, _) => new WeaponScopeProfileWindow(defaultScopeOnly: true) { Owner = Window.GetWindow(this) }.ShowDialog();
                var recoilActions = new StackPanel();
                recoilActions.Children.Add(profileEditor);
                recoilActions.Children.Add(fallbackEditor);
                RecoilConfig.Children.Add(ApplyCardTheme(new Border
                {
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Margin = new Thickness(6, 3, 6, 3),
                    Child = recoilActions
                }));

                RecoilConfig.Children.Add(new ARectangleBottom());
                RecoilConfig.Children.Add(new ASpacer());

                // Apply minimize state to the newly loaded controls
                ApplyPanelState("Recoil Config", RecoilConfigPanel);
                if (_mainWindow != null)
                {
                    MainWindow.UpdateSliderVisibility(_mainWindow.uiManager);
                }
                RefreshWheelStepVisibility();
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, "Critical Error LoadRecoilConfig: " + ex.Message);
                RecoilConfig.Children.Add(new TextBlock { Text = "ERROR: " + ex.Message, Foreground = Brushes.Red });
            }
        }

        private void DeleteLootItem(int itemNumToDelete)
        {
            int itemCount = 6;
            if (Dictionary.sliderSettings.ContainsKey("Fast Loot Item Count"))
            {
                itemCount = (int)Convert.ToDouble(Dictionary.sliderSettings["Fast Loot Item Count"]);
            }

            // Shift remaining items
            for (int i = itemNumToDelete; i < itemCount; i++)
            {
                int nextItem = i + 1;
                
                string nextXKey = $"Loot Item {nextItem} X";
                string currentXKey = $"Loot Item {i} X";
                if (Dictionary.sliderSettings.ContainsKey(nextXKey))
                    Dictionary.sliderSettings[currentXKey] = Dictionary.sliderSettings[nextXKey];
                
                string nextYKey = $"Loot Item {nextItem} Y";
                string currentYKey = $"Loot Item {i} Y";
                if (Dictionary.sliderSettings.ContainsKey(nextYKey))
                    Dictionary.sliderSettings[currentYKey] = Dictionary.sliderSettings[nextYKey];
                    
                string nextActionKey = $"Loot Item {nextItem} Action";
                string currentActionKey = $"Loot Item {i} Action";
                if (Dictionary.sliderSettings.ContainsKey(nextActionKey))
                    Dictionary.sliderSettings[currentActionKey] = Dictionary.sliderSettings[nextActionKey];
                else
                    Dictionary.sliderSettings[currentActionKey] = (double)((i % 2 == 1) ? 0 : 1);
            }

            // Remove last item keys
            Dictionary.sliderSettings.Remove($"Loot Item {itemCount} X");
            Dictionary.sliderSettings.Remove($"Loot Item {itemCount} Y");
            Dictionary.sliderSettings.Remove($"Loot Item {itemCount} Action");

            // Decrement item count
            Dictionary.sliderSettings["Fast Loot Item Count"] = (double)(itemCount - 1);

            _mainWindow?.SaveAllConfigurations();
            LoadLootConfig();
        }

        private void LoadLootConfig()
        {
            var uiManager = _mainWindow!.uiManager;
            LootConfig.Children.Clear();

            var builder = new SectionBuilder(this, LootConfig);

            builder.AddTitle("Fast Loot Config", true, t =>
            {
                t.Minimize.Click += (s, e) =>
                {
                    TogglePanel("Fast Loot Config", LootConfigPanel);
                };
            })
            .AddToggle("Fast Loot", t =>
            {
                uiManager.T_FastLoot = t;
            }, tooltip: "Bật/Tắt chức năng Fast Loot. Nhấn phím loot để tự động kéo item vào inventory.")
            .AddKeyChanger("Fast Loot Keybind", k =>
            {
                uiManager.C_FastLootKeybind = k;
            }, tooltip: "Phím tắt để kích hoạt Fast Loot (mặc định: Nút giữa chuột).")
            .AddSlider("Fast Loot Delay", "ms", 1, 1, 1, 50, tooltip: "Thời gian chờ giữa các lần kéo item (ms). Nhỏ hơn = nhanh hơn nhưng có thể bị lỗi.");

            // Create button template for rounded corners
            var btnTemplate = new ControlTemplate(typeof(Button));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            borderFactory.AppendChild(cp);
            btnTemplate.VisualTree = borderFactory;

            // Get dynamic item count
            int itemCount = 6;
            if (Dictionary.sliderSettings.ContainsKey("Fast Loot Item Count"))
            {
                itemCount = (int)Convert.ToDouble(Dictionary.sliderSettings["Fast Loot Item Count"]);
            }
            else
            {
                Dictionary.sliderSettings["Fast Loot Item Count"] = 6.0;
            }

            // Render items
            for (int i = 0; i < itemCount; i++)
            {
                int itemNum = i + 1;

                string posName = $"Item {itemNum}";
                if (i < InputLogic.LootManager.PositionNames.Length)
                {
                    posName = InputLogic.LootManager.PositionNames[i];
                }

                int currentX = Dictionary.sliderSettings.ContainsKey($"Loot Item {itemNum} X") ? (int)Convert.ToDouble(Dictionary.sliderSettings[$"Loot Item {itemNum} X"]) : 0;
                int currentY = Dictionary.sliderSettings.ContainsKey($"Loot Item {itemNum} Y") ? (int)Convert.ToDouble(Dictionary.sliderSettings[$"Loot Item {itemNum} Y"]) : 0;

                string actionKey = $"Loot Item {itemNum} Action";
                int actionValue = 1; // default to Drag, matching the original Loot.ahk behavior
                if (Dictionary.sliderSettings.ContainsKey(actionKey))
                {
                    actionValue = (int)Convert.ToDouble(Dictionary.sliderSettings[actionKey]);
                }
                else
                {
                    Dictionary.sliderSettings[actionKey] = (double)actionValue;
                }

                var containerBorder = ApplyCardTheme(new Border
                {
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Margin = new Thickness(6, 3, 6, 3),
                    Padding = new Thickness(10, 6, 10, 6)
                });

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                // Label
                var label = new TextBlock
                {
                    Text = posName,
                    Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontFamily = (FontFamily?)TryFindResource("Atkinson Hyperlegible") ?? new FontFamily("Segoe UI")
                };
                Grid.SetColumn(label, 0);

                // Coordinates
                var coordText = new TextBlock
                {
                    Text = $"X:{currentX} Y:{currentY}",
                    Foreground = new SolidColorBrush(Color.FromRgb(0, 200, 150)),
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 10, 0),
                    FontFamily = (FontFamily?)TryFindResource("Atkinson Hyperlegible") ?? new FontFamily("Segoe UI")
                };
                Grid.SetColumn(coordText, 1);

                // Action Toggle Button
                var actionBtn = new Button
                {
                    Content = actionValue == 0 ? "R-Click" : "Drag",
                    Width = 55,
                    Height = 24,
                    Background = actionValue == 0 ? new SolidColorBrush(Color.FromRgb(38, 108, 200)) : new SolidColorBrush(Color.FromRgb(38, 166, 91)),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    FontSize = 10,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Template = btnTemplate,
                    Margin = new Thickness(0, 0, 5, 0),
                    ToolTip = "Click để đổi hành động: Right Click (Loot nhanh) hoặc Drag (Kéo thả vào inventory)"
                };
                Grid.SetColumn(actionBtn, 2);

                actionBtn.Click += (s, e) =>
                {
                    int currentVal = (int)Convert.ToDouble(Dictionary.sliderSettings[actionKey]);
                    int newVal = currentVal == 0 ? 1 : 0;
                    Dictionary.sliderSettings[actionKey] = (double)newVal;
                    actionBtn.Content = newVal == 0 ? "R-Click" : "Drag";
                    actionBtn.Background = newVal == 0 ? new SolidColorBrush(Color.FromRgb(38, 108, 200)) : new SolidColorBrush(Color.FromRgb(38, 166, 91));
                    _mainWindow?.SaveAllConfigurations();
                };

                // Set Button
                var setBtn = ApplyThemeBackground(new Button
                {
                    Content = "Set",
                    Width = 35,
                    Height = 24,
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    FontSize = 11,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Template = btnTemplate,
                    Margin = new Thickness(0, 0, 5, 0),
                    ToolTip = $"Click rồi chọn vị trí {posName} trên màn hình"
                });
                Grid.SetColumn(setBtn, 3);

                setBtn.Click += (s, e) =>
                {
                    LogManager.Log(LogManager.LogLevel.Info, $"Click chuột trái vào vị trí {posName} trên màn hình...", true, 5000);
                    Task.Run(() =>
                    {
                        while ((GetAsyncKeyState(0x01) & 0x8000) == 0)
                        {
                            Thread.Sleep(10);
                        }
                        GetCursorPos(out POINT pt);
                        int newX = pt.X;
                        int newY = pt.Y;
                        while ((GetAsyncKeyState(0x01) & 0x8000) != 0)
                        {
                            Thread.Sleep(10);
                        }
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            Dictionary.sliderSettings[$"Loot Item {itemNum} X"] = (double)newX;
                            Dictionary.sliderSettings[$"Loot Item {itemNum} Y"] = (double)newY;
                            coordText.Text = $"X:{newX} Y:{newY}";
                            LogManager.Log(LogManager.LogLevel.Info, $"{posName}: X={newX} Y={newY}", true, 3000);
                            _mainWindow?.SaveAllConfigurations();
                        });
                    });
                };

                // Delete Button
                var delBtn = new Button
                {
                    Content = "X",
                    Width = 24,
                    Height = 24,
                    Background = new SolidColorBrush(Color.FromRgb(211, 47, 47)),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Template = btnTemplate,
                    ToolTip = $"Xóa {posName}"
                };
                Grid.SetColumn(delBtn, 4);

                delBtn.Click += (s, e) =>
                {
                    DeleteLootItem(itemNum);
                };

                grid.Children.Add(label);
                grid.Children.Add(coordText);
                grid.Children.Add(actionBtn);
                grid.Children.Add(setBtn);
                grid.Children.Add(delBtn);
                containerBorder.Child = grid;
                LootConfig.Children.Add(containerBorder);
            }

            // Inventory row container
            {
                int invX = Dictionary.sliderSettings.ContainsKey("Loot Inventory X") ? (int)Convert.ToDouble(Dictionary.sliderSettings["Loot Inventory X"]) : 0;
                int invY = Dictionary.sliderSettings.ContainsKey("Loot Inventory Y") ? (int)Convert.ToDouble(Dictionary.sliderSettings["Loot Inventory Y"]) : 0;

                var containerBorder = ApplyCardTheme(new Border
                {
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Margin = new Thickness(6, 3, 6, 3),
                    Padding = new Thickness(10, 6, 10, 6)
                });

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // placeholder for action
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Set
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // delete placeholder/empty

                var label = new TextBlock
                {
                    Text = "Inventory",
                    Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontFamily = (FontFamily?)TryFindResource("Atkinson Hyperlegible") ?? new FontFamily("Segoe UI")
                };
                Grid.SetColumn(label, 0);

                var coordText = new TextBlock
                {
                    Text = $"X:{invX} Y:{invY}",
                    Foreground = new SolidColorBrush(Color.FromRgb(0, 200, 150)),
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 10, 0),
                    FontFamily = (FontFamily?)TryFindResource("Atkinson Hyperlegible") ?? new FontFamily("Segoe UI")
                };
                Grid.SetColumn(coordText, 1);

                var setBtn = ApplyThemeBackground(new Button
                {
                    Content = "Set",
                    Width = 35,
                    Height = 24,
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    FontSize = 11,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Template = btnTemplate,
                    Margin = new Thickness(0, 0, 29, 0), // Shift to align with other rows since no delete button (24 width + 5 margin = 29)
                    ToolTip = "Click rồi chọn vị trí Inventory trên màn hình"
                });
                Grid.SetColumn(setBtn, 3);

                setBtn.Click += (s, e) =>
                {
                    LogManager.Log(LogManager.LogLevel.Info, "Click chuột trái vào vị trí Inventory trên màn hình...", true, 5000);
                    Task.Run(() =>
                    {
                        while ((GetAsyncKeyState(0x01) & 0x8000) == 0)
                        {
                            Thread.Sleep(10);
                        }
                        GetCursorPos(out POINT pt);
                        int newX = pt.X;
                        int newY = pt.Y;
                        while ((GetAsyncKeyState(0x01) & 0x8000) != 0)
                        {
                            Thread.Sleep(10);
                        }
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            Dictionary.sliderSettings["Loot Inventory X"] = (double)newX;
                            Dictionary.sliderSettings["Loot Inventory Y"] = (double)newY;
                            coordText.Text = $"X:{newX} Y:{newY}";
                            LogManager.Log(LogManager.LogLevel.Info, $"Inventory: X={newX} Y={newY}", true, 3000);
                            _mainWindow?.SaveAllConfigurations();
                        });
                    });
                };

                grid.Children.Add(label);
                grid.Children.Add(coordText);
                grid.Children.Add(setBtn);
                containerBorder.Child = grid;
                LootConfig.Children.Add(containerBorder);
            }

            // Add Item button container
            {
                var containerBorder = ApplyCardTheme(new Border
                {
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Margin = new Thickness(6, 3, 6, 3),
                    Padding = new Thickness(10, 6, 10, 6)
                });

                var addBtn = new Button
                {
                    Content = "+ Add Loot Item",
                    Height = 24,
                    Background = new SolidColorBrush(Color.FromRgb(38, 166, 91)),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Template = btnTemplate,
                    ToolTip = "Thêm một vật phẩm mới vào danh sách Fast Loot"
                };

                addBtn.Click += (s, e) =>
                {
                    int currentCount = 6;
                    if (Dictionary.sliderSettings.ContainsKey("Fast Loot Item Count"))
                    {
                        currentCount = (int)Convert.ToDouble(Dictionary.sliderSettings["Fast Loot Item Count"]);
                    }

                    int newCount = currentCount + 1;
                    Dictionary.sliderSettings["Fast Loot Item Count"] = (double)newCount;
                    
                    Dictionary.sliderSettings[$"Loot Item {newCount} X"] = 0.0;
                    Dictionary.sliderSettings[$"Loot Item {newCount} Y"] = 0.0;
                    Dictionary.sliderSettings[$"Loot Item {newCount} Action"] = 1.0;

                    _mainWindow?.SaveAllConfigurations();
                    LoadLootConfig();
                };

                containerBorder.Child = addBtn;
                LootConfig.Children.Add(containerBorder);
            }

            builder.AddSeparator();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        #endregion

        #region Helper Methods

        private void OnImageSizeChanged(int imageSize)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                imageSize = FovSettings.ImageSize;
                FovSettings.Synchronize(imageSize);
                if (_mainWindow?.uiManager.S_FOVSize != null && _mainWindow.uiManager.S_DynamicFOVSize != null)
                {
                    UpdateFovSizeSlider(_mainWindow.uiManager.S_FOVSize, imageSize);
                    UpdateFovSizeSlider(_mainWindow.uiManager.S_DynamicFOVSize, imageSize);
                    _mainWindow.uiManager.S_FOVSize.Slider.Value = Convert.ToDouble(Dictionary.sliderSettings["FOV Size"]);
                    _mainWindow.uiManager.S_DynamicFOVSize.Slider.Value = Convert.ToDouble(Dictionary.sliderSettings["Dynamic FOV Size"]);
                    _mainWindow.ActualFOV = Convert.ToDouble(Dictionary.sliderSettings["FOV Size"]);
                    PropertyChanger.PostNewFOVSize(_mainWindow.ActualFOV);
                }
            }));
        }        private void UpdateFovSizeSlider(ASlider slider, int imageSize = 640)
        {
            if (slider.Slider == null) return;
            if (imageSize < slider.Slider.Value)
            {
                slider.Slider.Value = imageSize;
            }
            slider.Slider.Maximum = imageSize;
        }

        private async Task ResetToMouseEvent()
        {
            await Task.Delay(500);
            _mainWindow!.uiManager.D_MouseMovementMethod!.DropdownBox.SelectedIndex = 0;
        }

        private void HandleColorChange(AColorChanger colorChanger, string settingKey, Action<Color> updateAction)
        {
            var colorDialog = new System.Windows.Forms.ColorDialog();
            if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var color = Color.FromArgb(colorDialog.Color.A, colorDialog.Color.R, colorDialog.Color.G, colorDialog.Color.B);
                colorChanger.ColorChangingBorder.Background = new SolidColorBrush(color);
                Dictionary.colorState[settingKey] = color.ToString();
                updateAction(color);
            }
        }

        public void RefreshRecoilConfig()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                LoadRecoilConfig();
            });
        }

        public void Dispose()
        {
            // Save minimize states before disposing
            SaveMinimizeStatesToGlobal();
        }

        #endregion

        #region Section Builder

        private class SectionBuilder
        {
            private readonly AimMenuControl _parent;
            private readonly StackPanel _panel;

            public SectionBuilder(AimMenuControl parent, StackPanel panel)
            {
                _parent = parent;
                _panel = panel;
            }

            public SectionBuilder AddTitle(string title, bool canMinimize, Action<ATitle>? configure = null)
            {
                var titleControl = new ATitle(title, canMinimize);
                configure?.Invoke(titleControl);
                _panel.Children.Add(titleControl);
                return this;
            }

            public SectionBuilder AddToggle(string title, Action<AToggle>? configure = null, string? tooltip = null)
            {
                var toggle = _parent.CreateToggle(title, tooltip);
                configure?.Invoke(toggle);
                _panel.Children.Add(toggle);
                return this;
            }

            public SectionBuilder AddKeyChanger(string title, Action<AKeyChanger>? configure = null, string? defaultKey = null, string? tooltip = null)
            {
                var key = defaultKey ?? Dictionary.bindingSettings[title];
                var keyChanger = _parent.CreateKeyChanger(title, key, tooltip);
                configure?.Invoke(keyChanger);
                _panel.Children.Add(keyChanger);
                return this;
            }

            public SectionBuilder AddSlider(string title, string label, double frequency, double buttonSteps,
                double min, double max, Action<ASlider>? configure = null, string? tooltip = null)
            {
                var slider = _parent.CreateSlider(title, label, frequency, buttonSteps, min, max, tooltip);
                configure?.Invoke(slider);
                _panel.Children.Add(slider);
                return this;
            }

            public SectionBuilder AddDropdown(string title, Action<ADropdown>? configure = null, string? tooltip = null)
            {
                var dropdown = _parent.CreateDropdown(title, tooltip);
                configure?.Invoke(dropdown);
                _panel.Children.Add(dropdown);
                return this;
            }

            public SectionBuilder AddColorChanger(string title, Action<AColorChanger>? configure = null)
            {
                var colorChanger = _parent.CreateColorChanger(title);
                configure?.Invoke(colorChanger);
                _panel.Children.Add(colorChanger);
                return this;
            }

            public SectionBuilder AddButton(string title, Action<APButton>? configure = null, string? tooltip = null)
            {
                var button = new APButton(title, tooltip);
                configure?.Invoke(button);
                _panel.Children.Add(button);
                return this;
            }

            public SectionBuilder AddFileLocator(string title, Action<AFileLocator>? configure = null,
                string filter = "All files (*.*)|*.*", string dlExtension = "")
            {
                var fileLocator = new AFileLocator(title, title, filter, dlExtension);
                configure?.Invoke(fileLocator);
                _panel.Children.Add(fileLocator);
                return this;
            }

            public SectionBuilder AddSeparator()
            {
                _panel.Children.Add(new ARectangleBottom());
                _panel.Children.Add(new ASpacer());
                return this;
            }

            public SectionBuilder AddCustomElement(Func<UIElement> createElement)
            {
                var element = createElement();
                _panel.Children.Add(element);
                return this;
            }
        }

        #endregion

        #region Control Creation Methods

        private AToggle CreateToggle(string title, string? tooltip = null)
        {
            var toggle = new AToggle(title, tooltip);
            _mainWindow!.toggleInstances[title] = toggle;

            // Set initial state
            if (Dictionary.toggleState[title])
                toggle.EnableSwitch();
            else
                toggle.DisableSwitch();

            // Handle click
            toggle.Reader.Click += (sender, e) =>
            {
                Dictionary.toggleState[title] = !Dictionary.toggleState[title];
                _mainWindow.UpdateToggleUI(toggle, Dictionary.toggleState[title]);
                _mainWindow.Toggle_Action(title);
            };

            return toggle;
        }

        private AKeyChanger CreateKeyChanger(string title, string keybind, string? tooltip = null)
        {
            var keyChanger = new AKeyChanger(title, keybind, tooltip);

            keyChanger.Reader.Click += (sender, e) =>
            {
                keyChanger.KeyNotifier.Content = "...";
                _mainWindow!.bindingManager.StartListeningForBinding(title);

                Action<string, string>? bindingSetHandler = null;
                bindingSetHandler = (bindingId, key) =>
                {
                    if (bindingId == title)
                    {
                        keyChanger.KeyNotifier.Content = KeybindNameManager.ConvertToRegularKey(key);
                        Dictionary.bindingSettings[bindingId] = key;
                        _mainWindow.bindingManager.OnBindingSet -= bindingSetHandler;
                    }
                };

                _mainWindow.bindingManager.OnBindingSet += bindingSetHandler;
            };

            return keyChanger;
        }

        private ASlider CreateSlider(string title, string label, double frequency, double buttonSteps,
            double min, double max, string? tooltip = null)
        {
            var slider = new ASlider(title, label, buttonSteps, tooltip)
            {
                Slider = { Minimum = min, Maximum = max, TickFrequency = frequency }
            };

            slider.Slider.Value = Dictionary.sliderSettings.TryGetValue(title, out var value) ? value : min;
            if (title is "Slot 1 Mouse Sensitivity" or "Mouse Sensitivity (+/-)")
                MouseSensitivityProfiles.Bind(slider, title == "Slot 1 Mouse Sensitivity" ? 1 : 2);
            else
                slider.Slider.ValueChanged += (s, e) => Dictionary.sliderSettings[title] = slider.Slider.Value;

            return slider;
        }

        private ADropdown CreateDropdown(string title, string? tooltip = null) => new(title, title, tooltip);

        private AColorChanger CreateColorChanger(string title)
        {
            var colorChanger = new AColorChanger(title);
            colorChanger.ColorChangingBorder.Background =
                (Brush)new BrushConverter().ConvertFromString(Dictionary.colorState[title]);
            return colorChanger;
        }

        // Scope info panel with public TextBlocks for updates
        public static TextBlock? Slot1ScopeText;
        public static TextBlock? Slot2ScopeText;
        public static TextBlock? ActiveSlotText;

        private UIElement CreateScopeInfoPanel()
        {
            var panel = new StackPanel
            {
                Margin = new Thickness(0, 5, 0, 5),
                Background = new SolidColorBrush(Color.FromArgb(40, 114, 46, 209))
            };

            // Title
            var titleText = new TextBlock
            {
                Text = "Detected Scopes",
                Foreground = new SolidColorBrush(Colors.White),
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(10, 5, 10, 2),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            panel.Children.Add(titleText);

            // Slot 1
            var slot1Panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(10, 2, 10, 2)
            };
            var slot1Label = new TextBlock
            {
                Text = "Slot 1: ",
                Foreground = new SolidColorBrush(Color.FromRgb(170, 170, 170)),
                FontSize = 11,
                Width = 50
            };
            Slot1ScopeText = new TextBlock
            {
                Text = "None",
                Foreground = new SolidColorBrush(Color.FromRgb(0, 255, 0)),
                FontSize = 11,
                FontWeight = FontWeights.Bold
            };
            slot1Panel.Children.Add(slot1Label);
            slot1Panel.Children.Add(Slot1ScopeText);
            panel.Children.Add(slot1Panel);

            // Slot 2
            var slot2Panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(10, 2, 10, 2)
            };
            var slot2Label = new TextBlock
            {
                Text = "Slot 2: ",
                Foreground = new SolidColorBrush(Color.FromRgb(170, 170, 170)),
                FontSize = 11,
                Width = 50
            };
            Slot2ScopeText = new TextBlock
            {
                Text = "None",
                Foreground = new SolidColorBrush(Color.FromRgb(0, 255, 0)),
                FontSize = 11,
                FontWeight = FontWeights.Bold
            };
            slot2Panel.Children.Add(slot2Label);
            slot2Panel.Children.Add(Slot2ScopeText);
            panel.Children.Add(slot2Panel);

            // Active Slot
            var activePanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(10, 2, 10, 5)
            };
            var activeLabel = new TextBlock
            {
                Text = "Active: ",
                Foreground = new SolidColorBrush(Color.FromRgb(170, 170, 170)),
                FontSize = 11,
                Width = 50
            };
            ActiveSlotText = new TextBlock
            {
                Text = "Slot 1",
                Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 0)),
                FontSize = 11,
                FontWeight = FontWeights.Bold
            };
            activePanel.Children.Add(activeLabel);
            activePanel.Children.Add(ActiveSlotText);
            panel.Children.Add(activePanel);

            return panel;
        }

        #endregion
    }
}


