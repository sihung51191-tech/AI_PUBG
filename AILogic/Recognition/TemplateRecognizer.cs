using System.Diagnostics;
using System.Drawing;
using OpenCvSharp;
using System.IO;

namespace Aimmy2.AILogic.Recognition;

public sealed class TemplateRecognizer : IRegionRecognizer
{
    private readonly TemplateLibrary _library;
    private readonly TemplateKind _kind;
    private readonly TemplateMatchingSettings _settings;
    public RecognitionMethod Method => RecognitionMethod.TemplateMatching;
    public TemplateRecognizer(TemplateLibrary library, TemplateKind kind, TemplateMatchingSettings settings)
        => (_library, _kind, _settings) = (library, kind, settings);

    public Task<RecognitionResult> RecognizeAsync(Bitmap image, CancellationToken cancellationToken)
        => Task.Run(() => Recognize(image, cancellationToken), cancellationToken);

    private RecognitionResult Recognize(Bitmap image, CancellationToken token)
    {
        var timer = Stopwatch.StartNew();
        using var roiGray = ImagePreprocessor.Gray(image, false, _kind == TemplateKind.Weapon && _settings.EnableColorMask);
        using var roiEdge = _settings.EnableEdgeMatching ? ImagePreprocessor.Gray(image, true, false) : null;
        double best = double.MinValue; string? label = null, file = null; Rectangle? matched = null;
        foreach (var group in _library.Snapshot(_kind))
        foreach (var entry in group.Value)
        {
            token.ThrowIfCancellationRequested();
            for (double scale = _settings.EnableMultiScale ? _settings.ScaleMin : 1;
                 scale <= (_settings.EnableMultiScale ? _settings.ScaleMax : 1) + .0001;
                 scale += _settings.EnableMultiScale ? Math.Max(.01, _settings.ScaleStep) : 1)
            {
                int width = Math.Max(2, (int)Math.Round(entry.Gray.Width * scale));
                int height = Math.Max(2, (int)Math.Round(entry.Gray.Height * scale));
                if (width > roiGray.Width || height > roiGray.Height) continue;
                using var resized = new Mat();
                using var resizedMask = new Mat();
                Mat source = _settings.EnableEdgeMatching && entry.Edge != null ? entry.Edge : entry.Gray;
                Cv2.Resize(source, resized, new OpenCvSharp.Size(width, height), 0, 0, InterpolationFlags.Area);
                Mat? mask = null;
                if (_settings.EnableAlphaMask && entry.AlphaMask != null)
                {
                    Cv2.Resize(entry.AlphaMask, resizedMask, resized.Size(), 0, 0, InterpolationFlags.Nearest);
                    mask = resizedMask;
                }
                using var scores = new Mat();
                Cv2.MatchTemplate(_settings.EnableEdgeMatching ? roiEdge! : roiGray, resized, scores,
                    TemplateMatchModes.CCoeffNormed, mask);
                Cv2.MinMaxLoc(scores, out _, out double score, out _, out var point);
                if (!double.IsFinite(score) || score <= best) continue;
                best = score; label = group.Key; file = entry.FilePath;
                matched = new Rectangle(point.X, point.Y, width, height);
            }
        }
        timer.Stop();
        if (label == null) return RecognitionResult.Unknown(Method, "No usable templates", timer.Elapsed.TotalMilliseconds);
        return new RecognitionResult(label, Method, Math.Clamp(best, 0, 1), best >= _settings.ConfidenceThreshold,
            best >= _settings.ConfidenceThreshold ? "Matched" : "Below template threshold",
            timer.Elapsed.TotalMilliseconds, MatchedRegion: matched, MatchedFile: Path.GetFileName(file));
    }
    public void Dispose() { }
}
