using Newtonsoft.Json;
using Other;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace Visuality;

internal static class WindowSizePersistence
{
    private sealed class SavedSize
    {
        public double Width { get; set; }
        public double Height { get; set; }
    }

    private static readonly object FileLock = new();
    private static string SettingsPath => Path.Combine(AppContext.BaseDirectory, "bin", "configs", "tool-window-sizes.json");

    public static void Attach(Window window, string key)
    {
        Restore(window, key);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Save(window, key);
        };
        window.SizeChanged += (_, _) =>
        {
            if (!window.IsLoaded || window.WindowState != WindowState.Normal) return;
            timer.Stop();
            timer.Start();
        };
        window.Closed += (_, _) =>
        {
            timer.Stop();
            Save(window, key);
        };
    }

    private static void Restore(Window window, string key)
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;
            var settings = JsonConvert.DeserializeObject<Dictionary<string, SavedSize>>(File.ReadAllText(SettingsPath));
            if (settings == null || !settings.TryGetValue(key, out var saved)) return;
            double maxWidth = Math.Max(window.MinWidth, SystemParameters.WorkArea.Width);
            double maxHeight = Math.Max(window.MinHeight, SystemParameters.WorkArea.Height);
            if (double.IsFinite(saved.Width) && saved.Width > 0) window.Width = Math.Clamp(saved.Width, window.MinWidth, maxWidth);
            if (double.IsFinite(saved.Height) && saved.Height > 0) window.Height = Math.Clamp(saved.Height, window.MinHeight, maxHeight);
        }
        catch (Exception ex) { LogManager.Log(LogManager.LogLevel.Warning, $"Cannot restore {key} window size: {ex.Message}"); }
    }

    private static void Save(Window window, string key)
    {
        double width = window.ActualWidth > 0 ? window.ActualWidth : window.Width;
        double height = window.ActualHeight > 0 ? window.ActualHeight : window.Height;
        if (window.WindowState != WindowState.Normal || !double.IsFinite(width) || !double.IsFinite(height) || width < window.MinWidth || height < window.MinHeight) return;
        try
        {
            lock (FileLock)
            {
                Dictionary<string, SavedSize> settings;
                try
                {
                    settings = File.Exists(SettingsPath)
                        ? JsonConvert.DeserializeObject<Dictionary<string, SavedSize>>(File.ReadAllText(SettingsPath)) ?? new()
                        : new();
                }
                catch { settings = new(); }
                settings[key] = new SavedSize { Width = width, Height = height };
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
                string temporary = SettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllText(temporary, JsonConvert.SerializeObject(settings, Formatting.Indented));
                    File.Move(temporary, SettingsPath, true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
        }
        catch (Exception ex) { LogManager.Log(LogManager.LogLevel.Warning, $"Cannot save {key} window size: {ex.Message}"); }
    }
}
