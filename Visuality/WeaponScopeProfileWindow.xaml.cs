using Aimmy2.AILogic;
using Aimmy2.AILogic.Weapons;
using Aimmy2.UILibrary;
using Gma.System.MouseKeyHook;
using InputLogic;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Visuality;

public partial class WeaponScopeProfileWindow : Window
{
    private const int StageCount = WeaponScopeRecoilProfile.ContinuousStageCount;
    private readonly bool _defaultScopeOnly;
    private readonly WeaponSlotManager _weaponManager;
    private readonly ASlider[] _forceSliders = new ASlider[StageCount];
    private readonly ASlider[] _timeSliders = new ASlider[StageCount - 1];
    private readonly ASlider[] _tapShotSliders = new ASlider[RecoilManager.TapShotCount];
    private readonly ASlider _tapResetSlider;
    private readonly ASlider _autoFireIntervalSlider;
    private readonly AToggle _tapToggle;
    private readonly AToggle _fireTimingToggle;
    private readonly Dictionary<AutoFireMode, AToggle> _autoFireToggles = new();
    private readonly DispatcherTimer _autoSaveTimer;
    private readonly DispatcherTimer _fireTimingDisplayTimer;
    private readonly Stopwatch _fireTimingWatch = new();
    private IKeyboardMouseEvents? _fireTimingHook;
    private bool _fireTimingEnabled;
    private bool _fireTimingRightHeld;
    private bool _fireTimingRunning;
    private WeaponScopeRecoilProfile _profile = new();
    private bool _updating;
    private bool _tapEnabled;
    private string _loadedWeaponName = "Default Weapon";
    private string _loadedScopeName = "chamdo";
    private static WeaponScopeRecoilProfile? _profileClipboard;

    public WeaponScopeProfileWindow(bool defaultScopeOnly = false)
    {
        _defaultScopeOnly = defaultScopeOnly;
        InitializeComponent();
        WindowSizePersistence.Attach(this, _defaultScopeOnly ? "recoil-fallback" : "recoil-weapon-scope");
        PasteButton.IsEnabled = _profileClipboard != null;
        _weaponManager = WeaponSlotManager.Instance;

        _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
        _autoSaveTimer.Tick += (_, _) => SaveNow();
        _fireTimingDisplayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _fireTimingDisplayTimer.Tick += (_, _) =>
        {
            if (_fireTimingRunning) FireTimingStatusText.Text = $"Đang đo: {FormatDuration(_fireTimingWatch.Elapsed.TotalSeconds)}";
        };

        _fireTimingToggle = new AToggle("Đo thời gian ra đạn");
        _fireTimingToggle.Reader.Click += (_, _) => SetFireTimingEnabled(!_fireTimingEnabled);
        FireTimingToggleHost.Children.Add(_fireTimingToggle);

        _tapToggle = new AToggle("Chế độ bắn từng viên");
        _tapToggle.Reader.Click += (_, _) =>
        {
            if (_updating) return;
            _tapEnabled = !_tapEnabled;
            RefreshTapToggle();
            ApplyLive();
            ScheduleAutoSave();
        };
        TapToggleHost.Children.Add(_tapToggle);

        AddAutoFireToggle(AutoFireMode.Sr, "Tự động bắn SR · một phát");
        AddAutoFireToggle(AutoFireMode.Dmr, "Tự động bắn DMR · tap liên tục");
        AddAutoFireToggle(AutoFireMode.Ar, "Tự động bắn AR · giữ liên tục");
        AddAutoFireToggle(AutoFireMode.Shotgun, "Tự động bắn Shotgun · tap liên tục");
        _autoFireIntervalSlider = CreateSlider("Thời gian giữa từng viên", "ms", 25, 2000, 5, 5);
        _autoFireIntervalSlider.Slider.ValueChanged += ProfileControl_ValueChanged;
        AutoFireIntervalHost.Children.Add(_autoFireIntervalSlider);

        for (int stage = 0; stage < StageCount; stage++)
        {
            var stageContent = new StackPanel();
            var header = new TextBlock
            {
                Text = stage == StageCount - 1 ? $"Giai đoạn {StageCount} · Giữ liên tục" : $"Giai đoạn {stage + 1}",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(4, 2, 4, 0)
            };
            header.SetResourceReference(TextBlock.ForegroundProperty, "ThemeColorLight");
            stageContent.Children.Add(header);
            _forceSliders[stage] = CreateSlider("Lực ghì", "Lực", 0, 200, .01, .01);
            MakeCardSlider(_forceSliders[stage]);
            _forceSliders[stage].Slider.ValueChanged += ProfileControl_ValueChanged;
            stageContent.Children.Add(_forceSliders[stage]);
            if (stage < StageCount - 1)
            {
                _timeSliders[stage] = CreateSlider("Thời gian", "Giây", 0, 5, .05, .1);
                MakeCardSlider(_timeSliders[stage]);
                _timeSliders[stage].Slider.ValueChanged += ProfileControl_ValueChanged;
                stageContent.Children.Add(_timeSliders[stage]);
            }
            else stageContent.Children.Add(new TextBlock { Text = "Giữ lực này đến khi nhả chuột.", Foreground = System.Windows.Media.Brushes.LightGray, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 7) });
            var stageCard = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 0, 9),
                Padding = new Thickness(6, 4, 6, 3),
                SnapsToDevicePixels = true,
                Child = stageContent
            };
            stageCard.SetResourceReference(Border.BackgroundProperty, "ThemeSurface");
            stageCard.SetResourceReference(Border.BorderBrushProperty, "ThemeOutline");
            ContinuousStagesPanel.Children.Add(stageCard);
        }

        _tapResetSlider = CreateSlider("Thời gian reset", "Giây", .1, 10, .1, .1);
        _tapResetSlider.Slider.ValueChanged += ProfileControl_ValueChanged;
        TapResetHost.Children.Add(_tapResetSlider);
        for (int shot = 0; shot < _tapShotSliders.Length; shot++)
        {
            _tapShotSliders[shot] = CreateSlider($"Lực kéo phát {shot + 1}", "Lực", 0, 2000, 1, 1);
            _tapShotSliders[shot].Slider.ValueChanged += ProfileControl_ValueChanged;
            TapShotsPanel.Children.Add(_tapShotSliders[shot]);
        }

        var active = _weaponManager.GetActiveSlotSnapshot();
        foreach (string item in new[] { "Default Weapon" }.Concat(_weaponManager.GetTemplateLabels(false)).Distinct(StringComparer.OrdinalIgnoreCase))
            WeaponBox.Items.Add(new ComboBoxItem { Content = item, Tag = item });
        var standardScopes = new[] { ("Chấm đỏ / Holo (1x)", "chamdo"), ("2x", "2x"), ("3x", "3x"), ("4x", "4x"), ("6x", "6x"), ("8x", "8x"), ("Mặc định", "Default Scope") };
        foreach (var item in standardScopes) ScopeBox.Items.Add(new ComboBoxItem { Content = item.Item1, Tag = item.Item2 });
        foreach (string item in _weaponManager.GetTemplateLabels(true).Where(label => label != "morong" && !standardScopes.Any(scope => string.Equals(scope.Item2, label, StringComparison.OrdinalIgnoreCase))))
            ScopeBox.Items.Add(new ComboBoxItem { Content = item, Tag = item });
        SelectChoice(WeaponBox, _defaultScopeOnly || IsUnknown(active.State.DetectedWeaponName) ? "Default Weapon" : active.State.DetectedWeaponName);
        string initialScope = IsUnknown(active.State.DetectedScopeName) ? "chamdo" : active.State.DetectedScopeName == "morong" ? "chamdo" : active.State.DetectedScopeName;
        SelectChoice(ScopeBox, initialScope);
        SearchableComboBoxBehavior.Attach(WeaponBox);
        SearchableComboBoxBehavior.Attach(ScopeBox);

        if (_defaultScopeOnly)
        {
            Title = "Recoil dự phòng theo Scope";
            TitleText.Text = "Recoil dự phòng theo Scope";
            WindowHeading.Text = "RECOIL DỰ PHÒNG THEO SCOPE";
            ActiveText.Text = "Dùng khi không nhận diện được súng. Nếu cả súng và scope đều None, hệ thống dùng Chấm đỏ/Holo 1x.";
            FireTimingCard.Visibility = Visibility.Collapsed;
            WeaponSelectorContainer.Visibility = Visibility.Collapsed;
            Grid.SetColumn(ScopeSelectorContainer, 0); Grid.SetColumnSpan(ScopeSelectorContainer, 3);
        }
        else UpdateActiveRecognition(active.Slot, active.State);

        LoadProfile();
        _weaponManager.ActiveRecognitionChanged += WeaponManager_ActiveRecognitionChanged;
        Closed += (_, _) =>
        {
            SaveNow();
            SetFireTimingEnabled(false);
            _weaponManager.ActiveRecognitionChanged -= WeaponManager_ActiveRecognitionChanged;
        };
        Loaded += (_, _) => Other.UiLanguage.RefreshTree(this);
    }

    private void WeaponManager_ActiveRecognitionChanged(int slot, WeaponSlotState state)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_defaultScopeOnly || !IsLoaded) return;
            UpdateActiveRecognition(slot, state);
            string weapon = IsUnknown(state.DetectedWeaponName) ? "Default Weapon" : state.DetectedWeaponName;
            string scope = IsUnknown(state.DetectedScopeName) || state.DetectedScopeName.Equals("morong", StringComparison.OrdinalIgnoreCase)
                ? "chamdo" : state.DetectedScopeName;
            if (string.Equals(SelectedWeaponName(), weapon, StringComparison.OrdinalIgnoreCase) &&
                WeaponScopeProfileStore.ScopeNamesEqual(SelectedScopeName(), scope)) return;
            SaveNow();
            CancelFireTimingRun();
            _updating = true;
            SelectChoice(WeaponBox, weapon);
            SelectChoice(ScopeBox, scope);
            _updating = false;
            LoadProfile();
            UpdateActiveRecognition(slot, state);
            SaveStatusText.Text = $"Đã chuyển theo nhận diện Slot {slot}.";
        });
    }

    private void UpdateActiveRecognition(int slot, WeaponSlotState state)
    {
        ActiveText.Text = $"Đang dùng Slot {slot}: {state.DetectedWeaponName} + {state.DetectedScopeName}\nProfile: {RecoilManager.ActiveProfileName}";
    }

    private void AddAutoFireToggle(AutoFireMode mode, string title)
    {
        var toggle = new AToggle(title);
        toggle.Reader.Click += (_, _) =>
        {
            if (_updating) return;
            _profile.AutoFire.Mode = _profile.AutoFire.Mode == mode ? AutoFireMode.None : mode;
            RefreshAutoFireControls();
            ApplyLive();
            ScheduleAutoSave();
        };
        _autoFireToggles[mode] = toggle;
        AutoFireModesPanel.Children.Add(toggle);
    }

    private static bool IsUnknown(string? value) => string.IsNullOrWhiteSpace(value) || value.Equals("Unknown", StringComparison.OrdinalIgnoreCase) || value.Equals("None", StringComparison.OrdinalIgnoreCase);
    private static ASlider CreateSlider(string title, string unit, double min, double max, double tick, double step) =>
        new(title, unit, step) { Slider = { Minimum = min, Maximum = max, TickFrequency = tick } };

    private void SetFireTimingEnabled(bool enabled)
    {
        _fireTimingEnabled = enabled && !_defaultScopeOnly;
        if (_fireTimingEnabled)
        {
            _fireTimingToggle.EnableSwitch();
            _fireTimingHook ??= Hook.GlobalEvents();
            _fireTimingHook.MouseDown -= FireTiming_MouseDown;
            _fireTimingHook.MouseUp -= FireTiming_MouseUp;
            _fireTimingHook.MouseDown += FireTiming_MouseDown;
            _fireTimingHook.MouseUp += FireTiming_MouseUp;
            RefreshFireTimingStatus("Sẵn sàng: giữ chuột phải, sau đó nhấn giữ chuột trái.");
        }
        else
        {
            _fireTimingToggle.DisableSwitch();
            CancelFireTimingRun();
            if (_fireTimingHook != null)
            {
                _fireTimingHook.MouseDown -= FireTiming_MouseDown;
                _fireTimingHook.MouseUp -= FireTiming_MouseUp;
                _fireTimingHook.Dispose();
                _fireTimingHook = null;
            }
            RefreshFireTimingStatus();
        }
    }

    private void FireTiming_MouseDown(object? sender, System.Windows.Forms.MouseEventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        if (!_fireTimingEnabled) return;
        if (e.Button == System.Windows.Forms.MouseButtons.Right)
        {
            _fireTimingRightHeld = true;
            if (!_fireTimingRunning) RefreshFireTimingStatus("Đã giữ chuột phải · nhấn giữ chuột trái để bắt đầu.");
        }
        else if (e.Button == System.Windows.Forms.MouseButtons.Left && _fireTimingRightHeld && !_fireTimingRunning)
        {
            _fireTimingRunning = true;
            _fireTimingWatch.Restart();
            _fireTimingDisplayTimer.Start();
        }
    });

    private void FireTiming_MouseUp(object? sender, System.Windows.Forms.MouseEventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        if (e.Button == System.Windows.Forms.MouseButtons.Right) _fireTimingRightHeld = false;
        if (!_fireTimingEnabled || e.Button != System.Windows.Forms.MouseButtons.Left || !_fireTimingRunning) return;
        _fireTimingWatch.Stop();
        _fireTimingDisplayTimer.Stop();
        _fireTimingRunning = false;
        _profile.LastMeasuredBurstSeconds = Math.Round(_fireTimingWatch.Elapsed.TotalSeconds, 3);
        ApplyLive();
        ScheduleAutoSave();
        RefreshFireTimingStatus($"Kết quả {_loadedWeaponName}: {FormatDuration(_profile.LastMeasuredBurstSeconds)}.");
    });

    private void CancelFireTimingRun()
    {
        _fireTimingDisplayTimer.Stop();
        _fireTimingWatch.Reset();
        _fireTimingRunning = false;
        _fireTimingRightHeld = false;
    }

    private void RefreshFireTimingStatus(string? message = null)
    {
        if (message != null) FireTimingStatusText.Text = message;
        else if (_profile.LastMeasuredBurstSeconds > 0)
            FireTimingStatusText.Text = $"Lần đo gần nhất của {_loadedWeaponName}: {FormatDuration(_profile.LastMeasuredBurstSeconds)}.";
        else FireTimingStatusText.Text = "Bật đo, giữ chuột phải rồi nhấn giữ chuột trái; thả chuột trái để kết thúc.";
    }

    private static string FormatDuration(double seconds)
    {
        long milliseconds = Math.Max(0, (long)Math.Round(seconds * 1000));
        return $"{seconds:0.000} s ({milliseconds} ms)";
    }
    private static void MakeCardSlider(ASlider slider)
    {
        if (slider.Content is Grid root && root.Children.OfType<Border>().FirstOrDefault() is Border border)
        {
            border.Background = System.Windows.Media.Brushes.Transparent;
            border.BorderThickness = new Thickness(0);
        }
    }
    private static string SelectedName(ComboBox box) => box.SelectedItem is ComboBoxItem item
        ? (item.Tag?.ToString() ?? item.Content?.ToString() ?? "").Trim()
        : (box.SelectedItem?.ToString() ?? box.Text).Trim();
    private static void SelectChoice(ComboBox box, string value)
    {
        var match = box.Items.OfType<ComboBoxItem>().FirstOrDefault(item =>
            string.Equals(item.Tag?.ToString(), value, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase));
        if (match == null)
        {
            match = new ComboBoxItem { Content = value, Tag = value };
            box.Items.Add(match);
        }
        box.SelectedItem = match;
    }
    private string SelectedWeaponName() => SelectedName(WeaponBox);
    private string SelectedScopeName() => SelectedName(ScopeBox);

    private void LoadProfile()
    {
        _updating = true;
        string weapon = _defaultScopeOnly ? "Default Weapon" : SelectedWeaponName();
        string scope = SelectedScopeName();
        _loadedWeaponName = weapon;
        _loadedScopeName = scope;
        _profile = _defaultScopeOnly ? RecoilManager.GetDefaultScopeProfileForEditing(scope) : RecoilManager.GetProfileForEditing(weapon, scope);
        PopulateControlsFromProfile();
    }
    private void PopulateControlsFromProfile()
    {
        _updating = true;
        if (_profile.ContinuousStages.Count > StageCount)
            _profile.ContinuousStages = _profile.ContinuousStages.Take(StageCount).ToList();
        while (_profile.ContinuousStages.Count < StageCount) _profile.ContinuousStages.Add(new());
        while (_profile.Tap.ShotDistances.Count < RecoilManager.TapShotCount) _profile.Tap.ShotDistances.Add(0);
        _profile.AutoFire ??= new();
        for (int stage = 0; stage < StageCount; stage++)
        {
            _forceSliders[stage].Slider.Value = Math.Clamp(_profile.ContinuousStages[stage].Force, 0, 200);
            if (stage < StageCount - 1) _timeSliders[stage].Slider.Value = Math.Clamp(_profile.ContinuousStages[stage].DurationSeconds, 0, 5);
        }
        _tapEnabled = _profile.Tap.Enabled;
        RefreshTapToggle();
        RefreshTapVisibility();
        _tapResetSlider.Slider.Value = Math.Clamp(_profile.Tap.ResetSeconds, .1, 10);
        for (int shot = 0; shot < _tapShotSliders.Length; shot++) _tapShotSliders[shot].Slider.Value = Math.Clamp(_profile.Tap.ShotDistances[shot], 0, 2000);
        RefreshAutoFireControls();
        RefreshFireTimingStatus();
        _updating = false;
    }
    private void ReadControls()
    {
        for (int stage = 0; stage < StageCount; stage++)
        {
            _profile.ContinuousStages[stage].Force = _forceSliders[stage].Slider.Value;
            _profile.ContinuousStages[stage].DurationSeconds = stage < StageCount - 1 ? _timeSliders[stage].Slider.Value : 0;
        }
        _profile.Tap.Enabled = _tapEnabled;
        _profile.Tap.ResetSeconds = _tapResetSlider.Slider.Value;
        for (int shot = 0; shot < _tapShotSliders.Length; shot++) _profile.Tap.ShotDistances[shot] = _tapShotSliders[shot].Slider.Value;
        if (_profile.AutoFire.Mode == AutoFireMode.Dmr)
            _profile.AutoFire.DmrIntervalMs = _autoFireIntervalSlider.Slider.Value;
        else if (_profile.AutoFire.Mode == AutoFireMode.Shotgun)
            _profile.AutoFire.ShotgunIntervalMs = _autoFireIntervalSlider.Slider.Value;
        _profile.Enabled = true;
        _profile.SensitivityMultiplier = 1;
    }
    private void ProfileSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || !IsLoaded || sender is not ComboBox box || box.SelectedItem == null) return;
        SaveNow();
        CancelFireTimingRun();
        LoadProfile();
    }
    private void RefreshTapToggle()
    {
        if (_tapEnabled) _tapToggle.EnableSwitch(); else _tapToggle.DisableSwitch();
        RefreshTapVisibility();
    }
    private void RefreshTapVisibility()
    {
        if (TapSettingsPanel == null || ContinuousPanelCard == null) return;
        bool tapEnabled = _tapEnabled;
        TapSettingsPanel.Visibility = tapEnabled ? Visibility.Visible : Visibility.Collapsed;
        ContinuousPanelCard.Visibility = tapEnabled ? Visibility.Collapsed : Visibility.Visible;
    }
    private void RefreshAutoFireControls()
    {
        AutoFireMode selected = _profile.AutoFire.Mode;
        foreach (var pair in _autoFireToggles)
        {
            pair.Value.Visibility = selected == AutoFireMode.None || selected == pair.Key ? Visibility.Visible : Visibility.Collapsed;
            if (selected == pair.Key) pair.Value.EnableSwitch(); else pair.Value.DisableSwitch();
        }
        bool timed = selected is AutoFireMode.Dmr or AutoFireMode.Shotgun;
        AutoFireIntervalHost.Visibility = timed ? Visibility.Visible : Visibility.Collapsed;
        if (timed)
        {
            double interval = selected == AutoFireMode.Dmr ? _profile.AutoFire.DmrIntervalMs : _profile.AutoFire.ShotgunIntervalMs;
            _autoFireIntervalSlider.Slider.Value = Math.Clamp(interval, 25, 2000);
        }
    }
    private void ProfileControl_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        ApplyLive();
        ScheduleAutoSave();
    }
    private void ApplyLive()
    {
        if (_updating || _profile == null) return;
        ReadControls();
        _profile.WeaponName = _defaultScopeOnly ? "Default Weapon" : _loadedWeaponName;
        _profile.ScopeName = _loadedScopeName;
        RecoilManager.ApplyProfileLive(_profile);
    }
    private void ScheduleAutoSave()
    {
        if (_updating) return;
        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();
    }
    private void SaveNow()
    {
        if (_updating || _profile == null) return;
        _autoSaveTimer.Stop();
        try
        {
            ReadControls();
            _profile.WeaponName = _defaultScopeOnly ? "Default Weapon" : _loadedWeaponName;
            _profile.ScopeName = _loadedScopeName;
            if (_profile.WeaponName.Length == 0 || _profile.ScopeName.Length == 0) return;
            if (_defaultScopeOnly) RecoilManager.SaveDefaultScopeProfile(_profile); else RecoilManager.SaveProfile(_profile);
            SaveStatusText.Text = $"Đã tự lưu và áp dụng: {_profile.WeaponName} + {_profile.ScopeName}.";
        }
        catch (Exception ex) { SaveStatusText.Text = $"Không thể tự lưu: {ex.Message}"; }
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        SaveNow();
    }
    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        ReadControls();
        _profileClipboard = CloneProfile(_profile);
        PasteButton.IsEnabled = true;
        SaveStatusText.Text = $"Đã sao chép cài đặt từ {SelectedWeaponName()} + {SelectedScopeName()}.";
    }
    private void Paste_Click(object sender, RoutedEventArgs e)
    {
        if (_profileClipboard == null) return;
        string targetWeapon = _defaultScopeOnly ? "Default Weapon" : SelectedWeaponName();
        string targetScope = SelectedScopeName();
        _profile = CloneProfile(_profileClipboard);
        _profile.WeaponName = targetWeapon;
        _profile.ScopeName = targetScope;
        PopulateControlsFromProfile();
        _loadedWeaponName = targetWeapon;
        _loadedScopeName = targetScope;
        SaveNow();
        SaveStatusText.Text = $"Đã dán, tự lưu và áp dụng vào {targetWeapon} + {targetScope}.";
    }
    private static WeaponScopeRecoilProfile CloneProfile(WeaponScopeRecoilProfile profile) =>
        JsonConvert.DeserializeObject<WeaponScopeRecoilProfile>(JsonConvert.SerializeObject(profile))!;

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
    private void WindowClose_Click(object sender, RoutedEventArgs e) => Close();
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        _profile = new WeaponScopeRecoilProfile { WeaponName = _defaultScopeOnly ? "Default Weapon" : SelectedWeaponName(), ScopeName = SelectedScopeName() };
        LoadFromObject();
    }
    private void LoadFromObject()
    {
        _updating = true;
        for (int stage = 0; stage < StageCount; stage++) { _forceSliders[stage].Slider.Value = 0; if (stage < StageCount - 1) _timeSliders[stage].Slider.Value = 0; }
        _tapEnabled = false; RefreshTapToggle(); _tapResetSlider.Slider.Value = 1;
        foreach (var slider in _tapShotSliders) slider.Slider.Value = 40;
        _updating = false;
    }
}
