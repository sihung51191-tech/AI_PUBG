using System.Drawing;

namespace Aimmy2.AILogic.Recognition;

public enum RecognitionMethod
{
    AiModel = 0,
    TemplateMatching = 1,
    Ocr = 2,
    OrbFeatureMatching = 3,
    SiftFeatureMatching = 4,
    AutoHybrid = 6
}

public sealed record RecognitionResult(
    string Label,
    RecognitionMethod Method,
    double Score,
    bool IsReliable,
    string DiagnosticReason = "",
    double ProcessingTimeMs = 0,
    int KeypointCount = 0,
    int GoodMatches = 0,
    Rectangle? MatchedRegion = null,
    string? MatchedFile = null,
    bool IsDeferred = false)
{
    public static RecognitionResult Unknown(RecognitionMethod method, string reason, double elapsed = 0,
        int keypoints = 0, int matches = 0) =>
        new("Unknown", method, 0, false, reason, elapsed, keypoints, matches);

    public static RecognitionResult Deferred(RecognitionMethod method, string reason) =>
        new("Unknown", method, 0, false, reason, IsDeferred: true);
}

public interface IRegionRecognizer : IDisposable
{
    RecognitionMethod Method { get; }
    Task<RecognitionResult> RecognizeAsync(Bitmap image, CancellationToken cancellationToken);
}

public sealed class TemplateMatchingSettings
{
    public double ConfidenceThreshold { get; set; } = .85;
    public double ScaleMin { get; set; } = .70;
    public double ScaleMax { get; set; } = 1.30;
    public double ScaleStep { get; set; } = .05;
    public bool EnableMultiScale { get; set; } = true;
    public bool EnableEdgeMatching { get; set; } = true;
    public bool EnableAlphaMask { get; set; } = true;
    public bool EnableColorMask { get; set; } = true;

    public static TemplateMatchingSettings FromLegacy(RecognitionSettings settings) => new()
    {
        ConfidenceThreshold = settings.TemplateConfidenceThreshold,
        ScaleMin = settings.TemplateScaleMin,
        ScaleMax = settings.TemplateScaleMax,
        ScaleStep = settings.TemplateScaleStep,
        EnableMultiScale = settings.EnableMultiScale,
        EnableEdgeMatching = settings.EnableEdgeMatching,
        EnableAlphaMask = settings.EnableAlphaMask,
        EnableColorMask = settings.EnableColorMask
    };
}

public sealed class RecognitionSettings
{
    // Legacy v2 fields retained only so existing configurations can be migrated.
    public double TemplateConfidenceThreshold { get; set; } = .85;
    public double TemplateScaleMin { get; set; } = .70;
    public double TemplateScaleMax { get; set; } = 1.30;
    public double TemplateScaleStep { get; set; } = .05;
    public bool EnableMultiScale { get; set; } = true;
    public bool EnableEdgeMatching { get; set; } = true;
    public bool EnableAlphaMask { get; set; } = true;
    public bool EnableColorMask { get; set; } = true;
    public int OrbFeatureCount { get; set; } = 500;
    public double OrbRatioThreshold { get; set; } = .75;
    public int OrbMinimumGoodMatches { get; set; } = 8;
    public double OrbRansacThreshold { get; set; } = 3;
    public int SiftFeatureCount { get; set; } = 500;
    public double SiftRatioThreshold { get; set; } = .72;
    public int SiftMinimumGoodMatches { get; set; } = 8;
    public double SiftRansacThreshold { get; set; } = 3;
    public int SiftScanIntervalMs { get; set; } = 300;
    public int ScanIntervalMs { get; set; } = 150;
    public int ConfirmationsRequired { get; set; } = 2;
    public double OcrConfidenceThreshold { get; set; } = .72;
    public bool ShouldSerializeTemplateConfidenceThreshold() => false;
    public bool ShouldSerializeTemplateScaleMin() => false;
    public bool ShouldSerializeTemplateScaleMax() => false;
    public bool ShouldSerializeTemplateScaleStep() => false;
    public bool ShouldSerializeEnableMultiScale() => false;
    public bool ShouldSerializeEnableEdgeMatching() => false;
    public bool ShouldSerializeEnableAlphaMask() => false;
    public bool ShouldSerializeEnableColorMask() => false;
}

public sealed class FeatureMatchingSettings
{
    public int OrbFeatureCount { get; set; } = 500;
    public double OrbRatioThreshold { get; set; } = .75;
    public int OrbMinimumGoodMatches { get; set; } = 8;
    public double OrbRansacThreshold { get; set; } = 3;
    public int SiftFeatureCount { get; set; } = 500;
    public double SiftRatioThreshold { get; set; } = .72;
    public int SiftMinimumGoodMatches { get; set; } = 8;
    public double SiftRansacThreshold { get; set; } = 3;
    public int SiftScanIntervalMs { get; set; } = 300;

    public static FeatureMatchingSettings FromLegacy(RecognitionSettings settings) => new()
    {
        OrbFeatureCount = settings.OrbFeatureCount,
        OrbRatioThreshold = settings.OrbRatioThreshold,
        OrbMinimumGoodMatches = settings.OrbMinimumGoodMatches,
        OrbRansacThreshold = settings.OrbRansacThreshold,
        SiftFeatureCount = settings.SiftFeatureCount,
        SiftRatioThreshold = settings.SiftRatioThreshold,
        SiftMinimumGoodMatches = settings.SiftMinimumGoodMatches,
        SiftRansacThreshold = settings.SiftRansacThreshold,
        SiftScanIntervalMs = settings.SiftScanIntervalMs
    };
}
