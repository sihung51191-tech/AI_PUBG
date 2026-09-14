using Aimmy2.AILogic;
using AILogic;
using Aimmy2.Class;
using Aimmy2.MouseMovementLibraries.GHubSupport;
using Aimmy2.UILibrary;
using MouseMovementLibraries.ddxoftSupport;
using MouseMovementLibraries.RazerSupport;
using Other;
using Class;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UILibrary;
using Visuality;
using LogLevel = Other.LogManager.LogLevel;

namespace Aimmy2.Controls
{
    public partial class SettingsMenuControl : UserControl
    {
        private MainWindow? _mainWindow;
        private bool _isInitialized;

        // Local minimize state management
        private readonly Dictionary<string, bool> _localMinimizeState = new()
        {
            { "Model AI (Slot 1)", false },
            { "Model AI (Slot 2)", false },
            { "Model Settings General", false },
            { "Settings Menu", false },
            { "Theme Settings", false },
            { "Screen Settings", false }
        };

        // Public properties for MainWindow access
        public StackPanel ModelSettingsSlot1Panel => ModelSettingsSlot1;
        public StackPanel ModelSettingsSlot2Panel => ModelSettingsSlot2;
        public StackPanel ModelSettingsGeneralPanel => ModelSettingsGeneral;
        public StackPanel SettingsConfigPanel => SettingsConfig;
        public StackPanel ThemeMenuPanel => ThemeMenu;
        public StackPanel DisplaySelectMenuPanel => DisplaySelectMenu;
        public ScrollViewer SettingsMenuScrollViewer => SettingsMenu;

        public SettingsMenuControl()
        {
            InitializeComponent();
            Loaded += (_, _) => global::Other.UiLanguage.RefreshTree(this);
            Loaded += (_, _) => { _sizeRefreshTimer.Start(); RefreshLoadedImageSizes(); };
            Unloaded += (_, _) => _sizeRefreshTimer.Stop();
            _sizeRefreshTimer.Tick += (_, _) => RefreshLoadedImageSizes();
        }

        public void Initialize(MainWindow mainWindow)
        {
            if (_isInitialized) return;

            try 
            {
                _mainWindow = mainWindow;
                _isInitialized = true;

                // Load minimize states from global dictionary if they exist
                LoadMinimizeStatesFromGlobal();

                LoadModelSettings();
                LoadSettingsConfig();
                LoadThemeMenu();
                LoadDisplaySelectMenu();

                // Apply minimize states after loading
                ApplyMinimizeStates();
                RefreshConditionalVisibility();
                
                // Subscribe to display changes
                DisplayManager.DisplayChanged += OnDisplayChanged;

                // Subscribe to AI class updates for Target Class dropdown
                AIManager.ClassesUpdated += OnClassesChanged;

                // Subscribe to dynamic model status changes
                AIManager.DynamicModelStatusChanged += OnDynamicModelStatusChanged;

                // Set visibility based on current model status (handles case where model loaded before panel opened)
                UpdateDynamicModelDropdownsVisibility(AIManager.CurrentModelIsDynamic);
                
                if (_mainWindow?.uiManager != null) {
                    if (_mainWindow.uiManager.D_Slot1TargetClass != null) UpdateTargetClassDropdown(_mainWindow.uiManager.D_Slot1TargetClass, 1);
                    if (_mainWindow.uiManager.D_Slot2TargetClass != null) UpdateTargetClassDropdown(_mainWindow.uiManager.D_Slot2TargetClass, 2);
                }
                
                ErrorText.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                ErrorText.Text = $"CRITICAL ERROR IN SETTINGS: {ex.Message}\n{ex.StackTrace}";
                ErrorText.Visibility = Visibility.Visible;
                LogManager.Log(LogLevel.Error, $"Settings initialization error: {ex}");
            }
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
            ApplyPanelState("Model AI (Slot 1)", ModelSettingsSlot1Panel);
            ApplyPanelState("Model AI (Slot 2)", ModelSettingsSlot2Panel);
            ApplyPanelState("Model Settings General", ModelSettingsGeneralPanel);
            ApplyPanelState("Settings Menu", SettingsConfigPanel);
            ApplyPanelState("Theme Settings", ThemeMenuPanel);
            ApplyPanelState("Screen Settings", DisplaySelectMenuPanel);
        }

        private void RefreshConditionalVisibility()
        {
            if (_mainWindow == null) return;
            var ui = _mainWindow.uiManager;

            static bool Enabled(string key) => Dictionary.toggleState.TryGetValue(key, out var value) && Convert.ToBoolean(value);
            void Set(FrameworkElement? element, string section, params string[] parents)
            {
                if (element == null) return;
                bool visible = !_localMinimizeState.GetValueOrDefault(section) && parents.All(Enabled);
                element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            }

            Set(ui.C_Slot1PriorityKey, "Model AI (Slot 1)", "Slot 1 Priority Aiming");
            Set(ui.C_Slot2PriorityKey, "Model AI (Slot 2)", "Slot 2 Priority Aiming");
            Set(ui.C_ModelSwitchKeybind, "Model Settings General", "Enable Model Switch Keybind");
            Set(ui.T_AutoLabelData, "Settings Menu", "Collect Data While Playing");
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
                // Only the title remains when collapsed; the enclosing Border is the single frame.
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

            // Save to global dictionary
            SaveMinimizeStatesToGlobal();
        }

        #endregion

        #region Menu Section Loaders

        private void LoadModelSettings()
        {
            var uiManager = _mainWindow!.uiManager;
            
            // --- Slot 1 ---
            var builder1 = new SectionBuilder(this, ModelSettingsSlot1);
            builder1.AddTitle("Model AI (Slot 1)", true, t =>
            {
                t.Minimize.Click += (s, e) => TogglePanel("Model AI (Slot 1)", ModelSettingsSlot1Panel);
            })
            .AddDropdown("Slot 1 Image Size", d =>
            {
                uiManager.D_Slot1ImageSize = d;
                _mainWindow.AddDropdownItem(d, "640");
                _mainWindow.AddDropdownItem(d, "512");
                _mainWindow.AddDropdownItem(d, "416");
                _mainWindow.AddDropdownItem(d, "320");
                _mainWindow.AddDropdownItem(d, "256");
                _mainWindow.AddDropdownItem(d, "160");

                string currentSize = "640";
                if (Dictionary.dropdownState.TryGetValue("Slot 1 Image Size", out var size)) currentSize = size.ToString();
                else Dictionary.dropdownState["Slot 1 Image Size"] = currentSize;

                for (int i = 0; i < d.DropdownBox.Items.Count; i++)
                    if ((d.DropdownBox.Items[i] as ComboBoxItem)?.Content?.ToString() == currentSize) { d.DropdownBox.SelectedIndex = i; break; }

                d.DropdownBox.SelectionChanged += async (s, e) => await HandleImageSizeChange(1, d);
            }, tooltip: "Độ phân giải cho Slot 1.")
            .AddDropdown("Slot 1 Target Class", d =>
            {
                uiManager.D_Slot1TargetClass = d;
                d.DropdownBox.SelectedIndex = 0;
                _mainWindow.AddDropdownItem(d, "Best Confidence");
                UpdateTargetClassDropdown(d, 1);
            }, tooltip: "Loại mục tiêu cho Slot 1.")
            .AddSlider("Slot 1 AI Confidence", "Slot 1 %", 1, 1, 1, 100, s =>
            {
                uiManager.S_Slot1AIMinimumConfidence = s;
                double conf = 50.0;
                if (Dictionary.sliderSettings.TryGetValue("AI Minimum Confidence", out var val)) conf = Convert.ToDouble(val);
                
                s.Slider.Value = conf; 
                s.Slider.ValueChanged += (sender, e) => Dictionary.sliderSettings["AI Minimum Confidence"] = s.Slider.Value;
            }, tooltip: "Độ tin cậy cho Slot 1.")
            .AddToggle("Slot 1 Priority Aiming", t => uiManager.T_Slot1PriorityAiming = t, tooltip: "Ưu tiên chọn đúng bộ phận (Đầu/Thân) nếu model hỗ trợ.")
            .AddKeyChanger("Slot 1 Priority Key", k => uiManager.C_Slot1PriorityKey = k, tooltip: "Hold this key to prioritize Head; release it to default to Body (Slot 1).")
            .AddSeparator();

            // --- Slot 2 ---
            var builder2 = new SectionBuilder(this, ModelSettingsSlot2);
            builder2.AddTitle("Model AI (Slot 2)", true, t =>
            {
                t.Minimize.Click += (s, e) => TogglePanel("Model AI (Slot 2)", ModelSettingsSlot2Panel);
            })
            .AddDropdown("Slot 2 Image Size", d =>
            {
                uiManager.D_Slot2ImageSize = d;
                _mainWindow.AddDropdownItem(d, "640");
                _mainWindow.AddDropdownItem(d, "512");
                _mainWindow.AddDropdownItem(d, "416");
                _mainWindow.AddDropdownItem(d, "320");
                _mainWindow.AddDropdownItem(d, "256");
                _mainWindow.AddDropdownItem(d, "160");

                string currentSize = "640";
                if (Dictionary.dropdownState.TryGetValue("Slot 2 Image Size", out var size)) currentSize = size.ToString();
                else Dictionary.dropdownState["Slot 2 Image Size"] = currentSize;

                for (int i = 0; i < d.DropdownBox.Items.Count; i++)
                    if ((d.DropdownBox.Items[i] as ComboBoxItem)?.Content?.ToString() == currentSize) { d.DropdownBox.SelectedIndex = i; break; }

                d.DropdownBox.SelectionChanged += async (s, e) => await HandleImageSizeChange(2, d);
            }, tooltip: "Độ phân giải cho Slot 2.")
            .AddDropdown("Slot 2 Target Class", d =>
            {
                uiManager.D_Slot2TargetClass = d;
                d.DropdownBox.SelectedIndex = 0;
                _mainWindow.AddDropdownItem(d, "Best Confidence");
                UpdateTargetClassDropdown(d, 2);
            }, tooltip: "Loại mục tiêu cho Slot 2.")
            .AddSlider("Slot 2 AI Confidence", "Slot 2 %", 1, 1, 1, 100, s =>
            {
                uiManager.S_Slot2AIMinimumConfidence = s;
                double conf = 50.0;
                if (Dictionary.sliderSettings.TryGetValue("Slot 2 AI Minimum Confidence", out var val)) conf = Convert.ToDouble(val);

                s.Slider.Value = conf;
                s.Slider.ValueChanged += (sender, e) => Dictionary.sliderSettings["Slot 2 AI Minimum Confidence"] = s.Slider.Value;
            }, tooltip: "Độ tin cậy cho Slot 2.")
            .AddToggle("Slot 2 Priority Aiming", t => uiManager.T_Slot2PriorityAiming = t, tooltip: "Ưu tiên chọn đúng bộ phận (Đầu/Thân) nếu model hỗ trợ.")
            .AddKeyChanger("Slot 2 Priority Key", k => uiManager.C_Slot2PriorityKey = k, tooltip: "Hold this key to prioritize Head; release it to default to Body (Slot 2).")
            .AddSeparator();

            // --- General ---
            var builderGen = new SectionBuilder(this, ModelSettingsGeneral);
            builderGen.AddTitle("Model Settings General", true, t =>
            {
                t.Minimize.Click += (s, e) => TogglePanel("Model Settings General", ModelSettingsGeneralPanel);
            })
            .AddToggle("Enable Model Switch Keybind", t => uiManager.T_EnableModelSwitchKeybind = t)
            .AddKeyChanger("Model Switch Keybind", k => uiManager.C_ModelSwitchKeybind = k)
            .AddKeyChanger("Emergency Stop Keybind", k => uiManager.C_EmergencyKeybind = k)
            .AddSeparator();
        }

        private async Task HandleImageSizeChange(int slot, ADropdown d)
        {
            if (_updatingImageSizeDropdown) return;
            if (d.DropdownBox.SelectedItem == null || FileManager.CurrentlyLoadingModel) return;

            var newSize = (d.DropdownBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
            if (string.IsNullOrEmpty(newSize)) return;

            string dictionaryKey = (slot == 1) ? "Slot 1 Image Size" : "Slot 2 Image Size";
            Dictionary.dropdownState[dictionaryKey] = newSize;

            if (FileManager.AIManager != null)
            {
                bool isDynamic = (slot == 1) ? AIManager.Slot1IsDynamic : AIManager.Slot2IsDynamic;
                if (!isDynamic)
                {
                    LogManager.Log(LogLevel.Warning, $"Model ở Slot {slot} là model tĩnh. Không thể thay đổi Image Size.", true);
                    // Reset dropdown back to fixed size
                    int fixedSize = (slot == 1) ? AIManager.Slot1FixedSize : AIManager.Slot2FixedSize;
                    UpdateImageSizeDropdown(fixedSize.ToString(), slot);
                    return;
                }

                FileManager.CurrentlyLoadingModel = true;
                LogManager.Log(LogLevel.Info, $"Đang thay đổi Image Size cho Slot {slot} thành {newSize}");
                
                try {
                    FileManager.AIManager.RequestSizeChange(int.Parse(newSize), slot);
                    await Task.Delay(150);
                    
                    var modelName = (slot == 1) ? Dictionary.lastLoadedModel : Dictionary.lastLoadedModelSlot2;
                    if (modelName != "N/A" && !string.IsNullOrEmpty(modelName)) {
                        // Re-trigger load to apply new size
                        string modelPath = System.IO.Path.Combine("bin", "models", slot == 1 ? "Slot1" : "Slot2", modelName);
                        if (slot == 1) await FileManager.AIManager.LoadModelAsync(modelPath);
                        else await FileManager.AIManager.LoadSecondaryModel(modelPath);
                    }
                } catch (Exception ex) { LogManager.Log(LogLevel.Error, $"Lỗi khi đổi Image Size: {ex.Message}"); }
                finally { FileManager.CurrentlyLoadingModel = false; }
            }
            MouseSensitivityProfiles.NotifyChanged();
        }

        private void LoadSettingsConfig()
        {
            var uiManager = _mainWindow!.uiManager;
            var builder = new SectionBuilder(this, SettingsConfig);

            builder
                .AddTitle("Settings Menu", true, t =>
                {
                    uiManager.AT_SettingsMenu = t;
                    t.Minimize.Click += (s, e) => TogglePanel("Settings Menu", SettingsConfigPanel);
                })
                .AddDropdown("Language", d =>
                {
                    d.Name = "LanguageSelector";
                    var dark = new SolidColorBrush(Color.FromRgb(31,29,41));
                    d.DropdownBox.Resources[SystemColors.WindowBrushKey] = dark;
                    var languageStyle = new Style(typeof(ComboBoxItem));
                    languageStyle.Setters.Add(new Setter(Control.BackgroundProperty, dark));
                    languageStyle.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
                    var border = new FrameworkElementFactory(typeof(Border));
                    border.SetBinding(Border.BackgroundProperty,new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
                    border.SetValue(Border.PaddingProperty,new Thickness(10,8,10,8));
                    var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
                    presenter.SetValue(ContentPresenter.ContentSourceProperty,"Content");
                    border.AppendChild(presenter);
                    languageStyle.Setters.Add(new Setter(Control.TemplateProperty,new ControlTemplate(typeof(ComboBoxItem)) { VisualTree = border }));
                    var highlight = new Trigger { Property = ComboBoxItem.IsHighlightedProperty, Value = true };
                    highlight.Setters.Add(new Setter(Control.BackgroundProperty,new SolidColorBrush(Color.FromRgb(65,55,89))));
                    languageStyle.Triggers.Add(highlight);
                    d.DropdownBox.ItemContainerStyle = languageStyle;
                    d.DropdownBox.Items.Add(new ComboBoxItem { Content = "English" });
                    d.DropdownBox.Items.Add(new ComboBoxItem { Content = "Tiếng Việt" });
                    d.DropdownBox.SelectedIndex = UiLanguage.Current.Code == "vi" ? 1 : 0;
                    d.DropdownBox.SelectionChanged += (_, _) =>
                    {
                        try { UiLanguage.Current.SetLanguage(d.DropdownBox.SelectedIndex == 1 ? "vi" : "en"); }
                        catch (Exception ex) { global::Other.LocalizedMessageBox.Show("Không lưu được ngôn ngữ: " + ex.Message); }
                    };
                }, tooltip: "Chọn ngôn ngữ giao diện. Thay đổi được áp dụng ngay và lưu cho lần mở sau.")
                .AddToggle("Collect Data While Playing", t => uiManager.T_CollectDataWhilePlaying = t,
                    tooltip: "Lưu ảnh chụp màn hình khi phát hiện mục tiêu để huấn luyện AI mới.")
                .AddToggle("Auto Label Data", t => uiManager.T_AutoLabelData = t,
                    tooltip: "Tự động gán nhãn dữ liệu cho ảnh chụp màn hình.")
                .AddToggle("Mouse Background Effect", t => uiManager.T_MouseBackgroundEffect = t,
                    tooltip: "Hiển thị hiệu ứng hình ảnh trên UI khi di chuyển chuột.")
                .AddToggle("UI TopMost", t => uiManager.T_UITopMost = t,
                    tooltip: "Giữ cửa sổ này luôn nằm trên các cửa sổ khác.")
                .AddToggle("Debug Mode", t => uiManager.T_DebugMode = t,
                    tooltip: "Hiển thị thêm thông tin hữu ích để khắc phục sự cố.")
                .AddButton("Save Config", b =>
                {
                    uiManager.B_SaveConfig = b;
                    b.Reader.Click += (s, e) => new ConfigSaver().ShowDialog();
                }, tooltip: "Lưu cài đặt hiện tại của bạn vào tệp tin để tải lại sau.")
                .AddSlider("Window Height", "Pixels", 1, 10, 444, 1080, s => 
                {
                    // Initialize with current height
                    s.Slider.Value = _mainWindow!.Height;
                    s.Slider.ValueChanged += (sender, e) => 
                    {
                        _mainWindow!.Height = s.Slider.Value;
                        Dictionary.sliderSettings["WindowHeight"] = s.Slider.Value;
                    };
                }, tooltip: "Điều chỉnh chiều cao cửa sổ ứng dụng.")
                .AddSlider("Window Width", "Pixels", 1, 10, 670, 1920, s => 
                {
                    s.Slider.Value = _mainWindow!.Width;
                    s.Slider.ValueChanged += (sender, e) => 
                    {
                        _mainWindow!.Width = s.Slider.Value;
                        Dictionary.sliderSettings["WindowWidth"] = s.Slider.Value;
                    };
                }, tooltip: "Điều chỉnh chiều rộng cửa sổ ứng dụng.")
                .AddSeparator();
        }

        private void LoadDisplaySelectMenu()
        {
            var uiManager = _mainWindow!.uiManager;
            var builder = new SectionBuilder(this, DisplaySelectMenu);

            builder
                .AddTitle("Screen Settings", true, t =>
                {
                    uiManager.AT_DisplaySelector = t;
                    t.Minimize.Click += (s, e) =>
                        TogglePanel("Screen Settings", DisplaySelectMenuPanel);
                })
                .AddDropdown("Screen Capture Method", d =>
                {
                    uiManager.D_ScreenCaptureMethod = d;
                    _mainWindow.AddDropdownItem(d, "DirectX");
                    _mainWindow.AddDropdownItem(d, "GDI+");
                    _mainWindow.AddDropdownItem(d, "WGC");
                    d.DropdownBox.SelectedIndex = 1;  // Default to GDI+ to avoid empty selection

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
                                        testManager.CaptureMethodKey = "Screen Capture Method";
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
                                    CaptureManager.ReportCaptureFailure("Screen Capture Method", "DirectX", new NotSupportedException(supportError));
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
                            Dictionary.dropdownState["Screen Capture Method"] = method;
                            _mainWindow.SaveAllConfigurations();
                            await CaptureManager.ReleaseInactiveWgcSessionsAsync();
                        }
                    };
                }, tooltip: "Cách chụp màn hình. DirectX nhanh hơn, GDI+ hoạt động trên nhiều hệ thống hơn. WGC dùng Windows Graphics Capture (Windows 10 1903 trở lên).")
                .AddToggle("StreamGuard", t => uiManager.T_StreamGuard = t,
                    tooltip: "Ẩn lớp phủ (overlay) khỏi các phần mềm quay màn hình và stream.")
                .AddSeparator();

            // Handle DisplaySelector separately as it's a custom control
            uiManager.DisplaySelector = new ADisplaySelector();
            uiManager.DisplaySelector.RefreshDisplays();

            // Insert after title but before separator
            var insertIndex = DisplaySelectMenu.Children.Count - 2;
            DisplaySelectMenu.Children.Insert(insertIndex, uiManager.DisplaySelector);

            // Add refresh button after DisplaySelector
            var refreshButton = new APButton("Refresh Displays", "Cập nhật danh sách màn hình.");
            refreshButton.Reader.Click += (s, e) =>
            {
                try
                {
                    DisplayManager.RefreshDisplays();
                    uiManager.DisplaySelector.RefreshDisplays();
                    LogManager.Log(LogLevel.Info, "Làm mới danh sách màn hình thành công.", true);
                }
                catch (Exception ex)
                {
                    LogManager.Log(LogLevel.Error, $"Lỗi khi làm mới màn hình: {ex.Message}", true);
                }
            };
            DisplaySelectMenu.Children.Insert(insertIndex + 1, refreshButton);
        }



        private void LoadThemeMenu()
        {
            var uiManager = _mainWindow!.uiManager;
            var builder = new SectionBuilder(this, ThemeMenu);

            builder
                .AddTitle("Theme Settings", true, t =>
                {
                    uiManager.AT_ThemeColorWheel = t;
                    t.Minimize.Click += (s, e) =>
                        TogglePanel("Theme Settings", ThemeMenuPanel);
                })
                .AddSeparator();

            // Handle ColorWheel separately as it's a custom control
            uiManager.ThemeColorWheel = new AColorWheel();

            //--
            var arrowButton = uiManager.ThemeColorWheel.FindName("ArrowButton") as Button;
            arrowButton.Visibility = Visibility.Visible;
            //--

            // Insert before separator
            var insertIndex = ThemeMenu.Children.Count - 2;
            ThemeMenu.Children.Insert(insertIndex, uiManager.ThemeColorWheel);
        }

        #endregion

        #region Helper Methods

        private void OnDisplayChanged(object? sender, DisplayChangedEventArgs e)
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    LogManager.Log(LogLevel.Info, $"AI đã chuyển tiêu điểm sang Màn hình {e.DisplayIndex + 1} ({e.Bounds.Width}x{e.Bounds.Height})", true);
                    UpdateDisplayRelatedSettings(e);
                }
                catch (Exception ex)
                {
                }
            });
        }

        private void UpdateDisplayRelatedSettings(DisplayChangedEventArgs e)
        {
            Dictionary.sliderSettings["SelectedDisplay"] = e.DisplayIndex;
        }

        private async Task ResetToMouseEvent()
        {
            await Task.Delay(500);
            _mainWindow!.uiManager.D_MouseMovementMethod!.DropdownBox.SelectedIndex = 0;
        }

        private readonly System.Windows.Threading.DispatcherTimer _sizeRefreshTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
        public void RefreshLoadedImageSizes()
        {
            if (FileManager.CurrentlyLoadingModel) return;
            var manager = FileManager.AIManager;
            if (manager == null) return;
            foreach (int slot in new[] { 1, 2 })
            {
                if (!manager.TryGetModelMetadata(slot, out var metadata) || metadata == null) continue;
                var size = metadata.ResolveSize(manager.GetSlotImageSize(slot));
                UpdateImageSizeDropdown(size.Width.ToString(), slot);
                var control = slot == 1 ? _mainWindow?.uiManager.D_Slot1ImageSize : _mainWindow?.uiManager.D_Slot2ImageSize;
                if (control != null) control.DropdownBox.IsEnabled = metadata.Dynamic && metadata.Format == "ONNX";
            }
        }

        private bool _updatingImageSizeDropdown;
        public void UpdateImageSizeDropdown(string newSize, int slot)
        {
            _updatingImageSizeDropdown = true;
            try
            {
                var dropdown = slot == 1 ? _mainWindow?.uiManager.D_Slot1ImageSize : _mainWindow?.uiManager.D_Slot2ImageSize;
                if (dropdown == null) return;
                if (!dropdown.DropdownBox.Items.OfType<ComboBoxItem>().Any(x => x.Content?.ToString() == newSize))
                    dropdown.DropdownBox.Items.Add(new ComboBoxItem { Content = newSize });
                for (int i = 0; i < dropdown.DropdownBox.Items.Count; i++)
                {
                    if ((dropdown.DropdownBox.Items[i] as ComboBoxItem)?.Content?.ToString() != newSize) continue;
                    dropdown.DropdownBox.SelectedIndex = i;
                    break;
                }
            }
            finally { _updatingImageSizeDropdown = false; }
        }
        private void OnClassesChanged(Dictionary<int, string> classes)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (_mainWindow?.uiManager.D_Slot1TargetClass != null)
                    UpdateTargetClassDropdown(_mainWindow.uiManager.D_Slot1TargetClass, 1, classes);
                if (_mainWindow?.uiManager.D_Slot2TargetClass != null)
                    UpdateTargetClassDropdown(_mainWindow.uiManager.D_Slot2TargetClass, 2, classes);
            });
        }

        private void OnDynamicModelStatusChanged(bool isDynamic)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                UpdateDynamicModelDropdownsVisibility(isDynamic);
            });
        }

        private void UpdateDynamicModelDropdownsVisibility(bool isDynamic)
        {
            // Always show the image size dropdown for UI consistency, but respect minimized/collapsed state
            if (_mainWindow?.uiManager.D_Slot1ImageSize != null)
            {
                bool slot1Minimized = _localMinimizeState.TryGetValue("Model AI (Slot 1)", out var m1) && m1;
                _mainWindow.uiManager.D_Slot1ImageSize.Visibility = slot1Minimized ? Visibility.Collapsed : Visibility.Visible;
            }
            
            if (_mainWindow?.uiManager.D_Slot2ImageSize != null)
            {
                bool slot2Minimized = _localMinimizeState.TryGetValue("Model AI (Slot 2)", out var m2) && m2;
                _mainWindow.uiManager.D_Slot2ImageSize.Visibility = slot2Minimized ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        private void UpdateTargetClassDropdown(ADropdown dropdown, int slot, Dictionary<int, string>? _classes = null)
        {
            if (dropdown?.DropdownBox == null) return;
            string? selection = (dropdown.DropdownBox.SelectedItem as ComboBoxItem)?.Content?.ToString();

            // Store current selection to dictionary to ensure persistence
            string dictionaryKey = (slot == 1) ? "Slot 1 Target Class" : "Slot 2 Target Class";
            if (!string.IsNullOrEmpty(selection)) Dictionary.dropdownState[dictionaryKey] = selection;

            var removedItems = dropdown.DropdownBox.Items.Cast<ComboBoxItem>()
                .Where(item => item.Content?.ToString() != "Best Confidence")
                .ToList();

            foreach (var item in removedItems) dropdown.DropdownBox.Items.Remove(item);

            var classes = _classes ?? FileManager.AIManager?.ModelClasses ?? new Dictionary<int, string>();
            foreach (var kvp in classes.OrderBy(x => x.Key))
                _mainWindow!.AddDropdownItem(dropdown, kvp.Value);

            string targetSelection = selection ?? Dictionary.dropdownState[dictionaryKey];
            for (int i = 0; i < dropdown.DropdownBox.Items.Count; i++)
            {
                if ((dropdown.DropdownBox.Items[i] as ComboBoxItem)?.Content?.ToString() == targetSelection)
                {
                    dropdown.DropdownBox.SelectedIndex = i;
                    return;
                }
            }

            dropdown.DropdownBox.SelectedIndex = 0;
        }

        public void Dispose()
        {
            _sizeRefreshTimer.Stop();
            DisplayManager.DisplayChanged -= OnDisplayChanged;
            AIManager.ClassesUpdated -= OnClassesChanged;
            _mainWindow?.uiManager.DisplaySelector?.Dispose();

            // Save minimize states before disposing
            SaveMinimizeStatesToGlobal();
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
                Dispatcher.BeginInvoke(RefreshConditionalVisibility);
            };

            return toggle;
        }

        //copied & Pasted from other class
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
                        if (PriorityAimingConfig.IsPriorityKey(bindingId))
                        {
                            PriorityAimingConfig.Save();
                        }
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
            var slider = new ASlider(title, label, buttonSteps, UiTooltipGuidance.ForSlider(title, tooltip))
            {
                Slider = { Minimum = min, Maximum = max, TickFrequency = frequency }
            };

            slider.Slider.Value = Dictionary.sliderSettings.TryGetValue(title, out var value) ? value : min;
            slider.Slider.ValueChanged += (s, e) => Dictionary.sliderSettings[title] = slider.Slider.Value;

            return slider;
        }

        private ADropdown CreateDropdown(string title, string? tooltip = null) => new(title, title, tooltip);

        #endregion

        #region Section Builder

        private class SectionBuilder
        {
            private readonly SettingsMenuControl _parent;
            private readonly StackPanel _panel;

            public SectionBuilder(SettingsMenuControl parent, StackPanel panel)
            {
                _parent = parent;
                _panel = panel;
            }

            public SectionBuilder AddTitle(string title, bool canMinimize, Action<ATitle>? configure = null)
            {
                var titleControl = new ATitle(title, canMinimize);
                configure?.Invoke(titleControl);
                _panel.Children.Add(titleControl);
                titleControl.Minimize.Click += (_, _) => _parent.Dispatcher.BeginInvoke(_parent.RefreshConditionalVisibility);
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

            public SectionBuilder AddButton(string title, Action<APButton>? configure = null, string? tooltip = null)
            {
                var button = new APButton(title, tooltip);
                configure?.Invoke(button);
                _panel.Children.Add(button);
                return this;
            }

            public SectionBuilder AddSeparator()
            {
                _panel.Children.Add(new ARectangleBottom());
                _panel.Children.Add(new ASpacer());
                return this;
            }
        }

        #endregion
    }
}

