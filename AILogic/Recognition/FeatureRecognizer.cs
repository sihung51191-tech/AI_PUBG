using System.Diagnostics;
using System.Drawing;
using OpenCvSharp;
using OpenCvSharp.Features2D;
using System.IO;

namespace Aimmy2.AILogic.Recognition;

public sealed class FeatureRecognizer : IRegionRecognizer
{
    private readonly TemplateLibrary _library;
    private readonly TemplateKind _kind;
    private readonly FeatureMatchingSettings _settings;
    public RecognitionMethod Method { get; }
    public FeatureRecognizer(TemplateLibrary library, TemplateKind kind, FeatureMatchingSettings settings, RecognitionMethod method)
    {
        if (method is not (RecognitionMethod.OrbFeatureMatching or RecognitionMethod.SiftFeatureMatching))
            throw new ArgumentOutOfRangeException(nameof(method));
        (_library, _kind, _settings, Method) = (library, kind, settings, method);
    }

    public Task<RecognitionResult> RecognizeAsync(Bitmap image, CancellationToken cancellationToken)
        => Task.Run(() => Recognize(image, cancellationToken), cancellationToken);

    private RecognitionResult Recognize(Bitmap image, CancellationToken token)
    {
        var timer = Stopwatch.StartNew();
        // Feature matching must use the same preprocessing for the live ROI and saved templates.
        // Upscaling is essential for the short weapon-name strips and 50x50 scope icons: ORB's
        // default border otherwise excludes nearly the entire image and returns zero keypoints.
        using var rawRoi = ImagePreprocessor.Gray(image, false, false);
        using var roi = PrepareFeatureImage(rawRoi);
        using Feature2D detector = CreateDetector(Method,
            Method == RecognitionMethod.OrbFeatureMatching ? _settings.OrbFeatureCount : _settings.SiftFeatureCount);
        using var roiDesc = new Mat();
        detector.DetectAndCompute(roi, null, out KeyPoint[] roiKeys, roiDesc);
        {
            if (roiDesc.Empty() || roiKeys.Length < 4)
                return RecognitionResult.Unknown(Method, "ROI has too few keypoints", timer.Elapsed.TotalMilliseconds, roiKeys.Length);
            string? bestLabel = null, bestFile = null; int bestGood = 0, bestKeys = roiKeys.Length; bool bestGeometry = false;
            foreach (var group in _library.Snapshot(_kind))
            foreach (var entry in group.Value)
            {
                token.ThrowIfCancellationRequested();
                var cached = entry.GetFeatures(Method, Method == RecognitionMethod.OrbFeatureMatching ? _settings.OrbFeatureCount : _settings.SiftFeatureCount);
                KeyPoint[] templateKeys = cached.Keys; Mat templateDesc = cached.Descriptors;
                {
                    if (templateDesc.Empty() || templateKeys.Length < 4) continue;
                    var norm = Method == RecognitionMethod.OrbFeatureMatching ? NormTypes.Hamming : NormTypes.L2;
                    using var matcher = new BFMatcher(norm);
                    DMatch[][] pairs = matcher.KnnMatch(templateDesc, roiDesc, 2);
                    double ratio = Method == RecognitionMethod.OrbFeatureMatching ? _settings.OrbRatioThreshold : _settings.SiftRatioThreshold;
                    var good = pairs.Where(x => x.Length == 2 && x[0].Distance < ratio * x[1].Distance).Select(x => x[0]).ToArray();
                    bool geometry = false;
                    if (good.Length >= 4)
                    {
                        var src = good.Select(x => templateKeys[x.QueryIdx].Pt).ToArray();
                        var dst = good.Select(x => roiKeys[x.TrainIdx].Pt).ToArray();
                        using var srcInput = InputArray.Create(src);
                        using var dstInput = InputArray.Create(dst);
                        using var homography = Cv2.FindHomography(srcInput, dstInput, HomographyMethods.Ransac,
                            Method == RecognitionMethod.OrbFeatureMatching ? _settings.OrbRansacThreshold : _settings.SiftRansacThreshold);
                        geometry = !homography.Empty();
                    }
                    if (good.Length > bestGood) { bestGood = good.Length; bestLabel = group.Key; bestFile = entry.FilePath; bestGeometry = geometry; }
                }
            }
            int minimum = Method == RecognitionMethod.OrbFeatureMatching ? _settings.OrbMinimumGoodMatches : _settings.SiftMinimumGoodMatches;
            bool reliable = bestGood >= minimum && bestGeometry;
            timer.Stop();
            if (bestLabel == null) return RecognitionResult.Unknown(Method, "Templates have too few keypoints", timer.Elapsed.TotalMilliseconds, bestKeys);
            // Score is a descriptor support ratio, kept distinct from AI/template confidence.
            double score = Math.Min(1, bestGood / (double)Math.Max(minimum, 1));
            return new RecognitionResult(bestLabel, Method, score, reliable,
                reliable ? "Feature matches and homography accepted" : "Too few geometrically consistent matches",
                timer.Elapsed.TotalMilliseconds, bestKeys, bestGood, MatchedFile: Path.GetFileName(bestFile));
        }
    }
    internal static Mat PrepareFeatureImage(Mat source)
    {
        int shortest = Math.Max(1, Math.Min(source.Width, source.Height));
        double scale = Math.Clamp(160d / shortest, 1d, 4d);
        var prepared = new Mat();
        if (scale > 1.01)
            Cv2.Resize(source, prepared, new OpenCvSharp.Size((int)Math.Round(source.Width * scale), (int)Math.Round(source.Height * scale)), 0, 0, InterpolationFlags.Cubic);
        else
            source.CopyTo(prepared);
        Cv2.Normalize(prepared, prepared, 0, 255, NormTypes.MinMax);
        return prepared;
    }

    internal static Feature2D CreateDetector(RecognitionMethod method, int count)
        => method == RecognitionMethod.OrbFeatureMatching
            ? ORB.Create(Math.Max(50, count), 1.2f, 8, 7, 0, 2, ORBScoreType.Harris, 15, 5)
            : SIFT.Create(Math.Max(50, count));

    public void Dispose() { }
}
