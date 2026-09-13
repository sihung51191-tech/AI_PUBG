using System.Drawing;
using Aimmy2.AILogic.Recognition;

namespace Aimmy2.AILogic.Weapons;

public sealed record WeaponSlotState(
    int SlotNumber,
    string DetectedWeaponName,
    string DetectedScopeName,
    double WeaponScore,
    double ScopeScore,
    RecognitionMethod WeaponRecognitionMethod,
    RecognitionMethod ScopeRecognitionMethod,
    Rectangle WeaponRegion,
    Rectangle ScopeRegion,
    DateTime LastWeaponConfirmedTime,
    DateTime LastScopeConfirmedTime,
    long ScanGeneration)
{
    public static WeaponSlotState Empty(int slot) => new(slot, "None", "None", 0, 0,
        RecognitionMethod.AutoHybrid, RecognitionMethod.AutoHybrid, Rectangle.Empty, Rectangle.Empty,
        DateTime.MinValue, DateTime.MinValue, 0);
}

public sealed class RegionConfiguration
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public double NormalizedX { get; set; }
    public double NormalizedY { get; set; }
    public double NormalizedWidth { get; set; }
    public double NormalizedHeight { get; set; }
    public string MonitorId { get; set; } = "";
    public int MonitorX { get; set; }
    public int MonitorY { get; set; }
    public int MonitorWidth { get; set; }
    public int MonitorHeight { get; set; }
    public double DpiX { get; set; } = 96;
    public double DpiY { get; set; } = 96;
    public bool IsConfigured => Width > 0 && Height > 0;
    public Rectangle PixelRectangle => IsConfigured ? new(X, Y, Width, Height) : Rectangle.Empty;

    public static RegionConfiguration FromPixels(Rectangle region, string monitorId, Rectangle monitor, double dpiX = 96, double dpiY = 96)
    {
        if (region.Width <= 0 || region.Height <= 0) return new();
        return new RegionConfiguration
        {
            X = region.X, Y = region.Y, Width = region.Width, Height = region.Height, MonitorId = monitorId,
            MonitorX = monitor.X, MonitorY = monitor.Y, MonitorWidth = monitor.Width, MonitorHeight = monitor.Height,
            NormalizedX = (region.X - monitor.X) / (double)Math.Max(1, monitor.Width),
            NormalizedY = (region.Y - monitor.Y) / (double)Math.Max(1, monitor.Height),
            NormalizedWidth = region.Width / (double)Math.Max(1, monitor.Width),
            NormalizedHeight = region.Height / (double)Math.Max(1, monitor.Height), DpiX = dpiX, DpiY = dpiY
        };
    }

    public Rectangle Resolve(Rectangle currentMonitor)
    {
        if (!IsConfigured || currentMonitor.Width <= 0 || currentMonitor.Height <= 0) return Rectangle.Empty;
        int x = currentMonitor.X + (int)Math.Round(NormalizedX * currentMonitor.Width);
        int y = currentMonitor.Y + (int)Math.Round(NormalizedY * currentMonitor.Height);
        int w = Math.Max(1, (int)Math.Round(NormalizedWidth * currentMonitor.Width));
        int h = Math.Max(1, (int)Math.Round(NormalizedHeight * currentMonitor.Height));
        return Rectangle.Intersect(new Rectangle(x, y, w, h), currentMonitor);
    }
}

public sealed class RecognitionConfiguration
{
    public int ConfigVersion { get; set; } = 4;
    public RecognitionMethod WeaponMethod { get; set; } = RecognitionMethod.AutoHybrid;
    public RecognitionMethod ScopeMethod { get; set; } = RecognitionMethod.TemplateMatching;
    public string WeaponModelPath { get; set; } = "";
    public string ScopeModelPath { get; set; } = "bin\\scope_models\\scope.onnx";
    public int WeaponImageSize { get; set; } = 640;
    public int ScopeImageSize { get; set; } = 640;
    public double WeaponAiConfidence { get; set; } = .45;
    public RecognitionSettings Settings { get; set; } = new();
    public TemplateMatchingSettings WeaponTemplateSettings { get; set; } = new();
    public TemplateMatchingSettings ScopeTemplateSettings { get; set; } = new();
    public FeatureMatchingSettings WeaponFeatureSettings { get; set; } = new();
    public FeatureMatchingSettings ScopeFeatureSettings { get; set; } = new();
    public RegionConfiguration WeaponSlot1Region { get; set; } = new();
    public RegionConfiguration ScopeSlot1Region { get; set; } = new();
    public RegionConfiguration WeaponSlot2Region { get; set; } = new();
    public RegionConfiguration ScopeSlot2Region { get; set; } = new();
    public bool AlwaysAskName { get; set; } = true;
    public bool RememberWeaponLabel { get; set; }
    public bool RememberScopeLabel { get; set; }
    public bool ConfirmBeforeSave { get; set; } = true;
    public string WeaponLabelSlot1 { get; set; } = "";
    public string WeaponLabelSlot2 { get; set; } = "";
    public string ScopeLabelSlot1 { get; set; } = "";
    public string ScopeLabelSlot2 { get; set; } = "";
}
