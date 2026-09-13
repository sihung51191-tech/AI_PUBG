using Aimmy2.AILogic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Visuality;

public partial class TemplateManagerWindow : Window
{
    private readonly WeaponSlotManager _manager = WeaponSlotManager.Instance;
    private bool Scope => KindBox.SelectedIndex == 1;
    public TemplateManagerWindow(bool scope = false)
    {
        InitializeComponent();
        WindowSizePersistence.Attach(this, scope ? "manager-scope" : "manager-weapon");
        KindBox.SelectedIndex = scope ? 1 : 0;
        Loaded += (_, _) => { Other.UiLanguage.RefreshTree(this); RefreshLabels(); };
    }
    private void RefreshLabels()
    {
        string? selected = Labels.SelectedItem?.ToString(); Labels.Items.Clear();
        foreach (string label in _manager.GetTemplateLabels(Scope)) Labels.Items.Add($"{label} ({_manager.GetTemplates(Scope, label).Count} ảnh)");
        if (selected != null) Labels.SelectedItem = selected;
    }
    private string? SelectedLabel => Labels.SelectedItem?.ToString()?.Split(" (")[0];
    private void KindBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) { if (IsLoaded) RefreshLabels(); }
    private void Labels_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        Files.Items.Clear(); Thumbnail.Source = null; if (SelectedLabel is not string label) return;
        foreach (var item in _manager.GetTemplates(Scope, label)) Files.Items.Add(Path.GetFileName(item.FilePath));
    }
    private void Files_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (SelectedLabel is not string label || Files.SelectedItem is not string file) return;
        string? path = _manager.GetTemplates(Scope, label).FirstOrDefault(x => Path.GetFileName(x.FilePath) == file)?.FilePath;
        if (path == null) return; var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.UriSource = new Uri(path); image.EndInit(); Thumbnail.Source = image;
    }
    private void DeleteImage_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedLabel is not string label || Files.SelectedItem is not string file) return;
        if (MessageBox.Show($"Xóa {file}?", "Template", MessageBoxButton.YesNo) == MessageBoxResult.Yes) { _manager.DeleteTemplate(Scope, label, file); RefreshLabels(); }
    }
    private void DeleteLabel_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedLabel is not string label) return;
        if (MessageBox.Show($"Xóa folder {label}?", "Template", MessageBoxButton.YesNo) == MessageBoxResult.Yes) { _manager.DeleteTemplateLabel(Scope, label); RefreshLabels(); }
    }
    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedLabel is not string label) return;
        string value = Microsoft.VisualBasic.Interaction.InputBox("Tên nhãn mới", "Template", label);
        if (string.IsNullOrWhiteSpace(value) || value == label) return;
        try { _manager.RenameTemplateLabel(Scope, label, value); RefreshLabels(); } catch (Exception ex) { MessageBox.Show(ex.Message); }
    }
    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedLabel is not string label) return;
        string? file = _manager.GetTemplates(Scope, label).FirstOrDefault()?.FilePath;
        if (file != null) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = true });
    }
    private void Refresh_Click(object sender, RoutedEventArgs e) { _manager.ReloadTemplates(); RefreshLabels(); }
    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source)
            for (DependencyObject? current = source; current != null; current = System.Windows.Media.VisualTreeHelper.GetParent(current))
                if (current is System.Windows.Controls.Button) return;
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) DragMove();
    }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
