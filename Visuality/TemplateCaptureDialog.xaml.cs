using Aimmy2.AILogic;
using Aimmy2.AILogic.Recognition;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Visuality;

public partial class TemplateCaptureDialog : Window
{
    private Bitmap _bitmap;
    private readonly bool _scope;
    private readonly int _slot;
    private readonly System.Drawing.Rectangle _region;
    private readonly WeaponSlotManager _manager;
    private TemplateQuality _quality;
    public TemplateCaptureDialog(Bitmap image, bool scope, int slot, System.Drawing.Rectangle region)
    {
        InitializeComponent(); _bitmap = (Bitmap)image.Clone(); _scope = scope; _slot = slot; _region = region; _manager = WeaponSlotManager.Instance;
        foreach (string label in _manager.GetTemplateLabels(scope)) ExistingLabels.Items.Add(label);
        SearchableComboBoxBehavior.Attach(ExistingLabels);
        WindowSizePersistence.Attach(this, scope ? "capture-scope" : "capture-weapon");
        MetadataText.Text = $"Loại: {(scope ? "Scope" : "Súng")}  •  Slot {slot}\nROI: X={region.X}, Y={region.Y}, Width={region.Width}, Height={region.Height}";
        var config = _manager.RecognitionConfig;
        string remembered = scope ? (slot == 1 ? config.ScopeLabelSlot1 : config.ScopeLabelSlot2) : (slot == 1 ? config.WeaponLabelSlot1 : config.WeaponLabelSlot2);
        NewLabel.Text = remembered; RememberLabel.IsChecked = scope ? config.RememberScopeLabel : config.RememberWeaponLabel; ConfirmSave.IsChecked = config.ConfirmBeforeSave;
        SetPreview(); Analyze(); Loaded += (_, _) =>
        {
            Other.UiLanguage.RefreshTree(this);
            NewLabel.Focus();
            NewLabel.CaretIndex = NewLabel.Text.Length;
        };
    }
    private void SetPreview()
    {
        using var stream = new MemoryStream(); _bitmap.Save(stream, ImageFormat.Png); stream.Position = 0;
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze(); PreviewImage.Source = image;
    }
    private void Analyze()
    {
        _quality = TemplateQualityAnalyzer.Analyze(_bitmap, _scope ? TemplateKind.Scope : TemplateKind.Weapon);
        if (_quality.CanSave)
        {
            var duplicate = _manager.FindNearDuplicate(_scope, _bitmap);
            if (duplicate.IsReliable) _quality = new TemplateQuality(true, true, $"Gần trùng {duplicate.Label}/{duplicate.MatchedFile} ({duplicate.Score:0.000})");
        }
        QualityText.Text = "Chất lượng: " + _quality.Message; QualityText.Foreground = new SolidColorBrush(_quality.HasWarning ? Colors.Orange : Colors.LimeGreen); SaveButton.IsEnabled = _quality.CanSave;
    }
    private void ExistingLabels_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) { if (ExistingLabels.SelectedItem is string value) NewLabel.Text = value; }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string label = TemplateLibrary.ValidateLabel(NewLabel.Text);
            if (_quality.HasWarning && ConfirmSave.IsChecked == true && MessageBox.Show(_quality.Message + ". Vẫn lưu?", "Template", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            _manager.SaveTemplate(_scope, label, _bitmap);
            var c = _manager.RecognitionConfig; c.ConfirmBeforeSave = ConfirmSave.IsChecked == true; c.AlwaysAskName = RememberLabel.IsChecked != true;
            if (_scope) { c.RememberScopeLabel = RememberLabel.IsChecked == true; if (_slot == 1) c.ScopeLabelSlot1 = label; else c.ScopeLabelSlot2 = label; }
            else { c.RememberWeaponLabel = RememberLabel.IsChecked == true; if (_slot == 1) c.WeaponLabelSlot1 = label; else c.WeaponLabelSlot2 = label; }
            _manager.SaveRecognitionConfig(); DialogResult = true;
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Template", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
    private async void Retake_Click(object sender, RoutedEventArgs e)
    {
        var visibleWindows = Application.Current.Windows.OfType<Window>().Where(window => window.IsVisible).ToArray();
        Bitmap? fresh = null;
        try
        {
            foreach (var window in visibleWindows) window.Hide();
            await Task.Delay(180);
            fresh = _manager.CaptureTemplateRegion(_slot, _scope);
        }
        finally
        {
            foreach (var window in visibleWindows) window.Show();
            Activate();
        }
        if (fresh == null) return;
        using (fresh) { _bitmap.Dispose(); _bitmap = (Bitmap)fresh.Clone(); }
        SetPreview(); Analyze();
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) { if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) DragMove(); }
    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    protected override void OnClosed(EventArgs e) { _bitmap.Dispose(); base.OnClosed(e); }
}
