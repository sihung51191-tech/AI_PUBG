using Microsoft.ML.OnnxRuntime.Tensors;
using System.Drawing;

namespace Aimmy2.AILogic;

internal enum PredictionTensorLayout
{
    Auto,
    CanonicalNmsFree,
    Bcn,
    Bnc
}

/// <summary>Allocation-conscious confidence, class, bounds, FOV and priority filtering.</summary>
internal static class PredictionFilter
{
    internal static void CreatePredictions(
        Tensor<float> outputTensor,
        Rectangle detectionBox,
        int imageSize,
        int numClasses,
        IReadOnlyDictionary<int, string> modelClasses,
        float minConfidence,
        string selectedClass,
        List<Prediction> destination,
        PredictionTensorLayout layout = PredictionTensorLayout.CanonicalNmsFree,
        bool hasObjectness = false,
        bool applyNms = false,
        long frameId = 0,
        long frameTimestamp = 0)
    {
        ArgumentNullException.ThrowIfNull(outputTensor);
        ArgumentNullException.ThrowIfNull(modelClasses);
        ArgumentNullException.ThrowIfNull(destination);
        destination.Clear();

        int rank = outputTensor.Dimensions.Length;
        if (rank is not (2 or 3) || rank == 3 && outputTensor.Dimensions[0] != 1)
            return;

        int rows = outputTensor.Dimensions[^2];
        int columns = outputTensor.Dimensions[^1];
        ReadOnlySpan<float> dense = outputTensor is DenseTensor<float> denseTensor
            ? denseTensor.Buffer.Span : ReadOnlySpan<float>.Empty;
        int selectedClassId = ResolveSelectedClassId(modelClasses, selectedClass);
        int expectedChannels = Math.Max(1, numClasses) + (hasObjectness ? 5 : 4);

        if (layout == PredictionTensorLayout.Auto)
        {
            if (columns == 6 && rows != expectedChannels) layout = PredictionTensorLayout.CanonicalNmsFree;
            else if (rows == expectedChannels && columns != expectedChannels) layout = PredictionTensorLayout.Bcn;
            else if (columns == expectedChannels && rows != expectedChannels) layout = PredictionTensorLayout.Bnc;
            else return; // Ambiguous output must be configured instead of guessed.
        }

        if (layout == PredictionTensorLayout.CanonicalNmsFree)
        {
            if (columns < 6) return;
            for (int i = 0; i < rows; i++)
            {
                float x1 = At(outputTensor, dense, rank, columns, i, 0);
                float y1 = At(outputTensor, dense, rank, columns, i, 1);
                float x2 = At(outputTensor, dense, rank, columns, i, 2);
                float y2 = At(outputTensor, dense, rank, columns, i, 3);
                float confidence = At(outputTensor, dense, rank, columns, i, 4);
                float classValue = At(outputTensor, dense, rank, columns, i, 5);
                if (!float.IsFinite(classValue) || classValue != MathF.Truncate(classValue)) continue;
                int classId = (int)classValue;
                if (confidence < minConfidence || confidence > 1 || selectedClassId >= 0 && classId != selectedClassId) continue;
                AddClipped(destination, new RectangleF(x1, y1, x2 - x1, y2 - y1),
                    new RectangleF(x1, y1, x2 - x1, y2 - y1), confidence, classId, modelClasses,
                    detectionBox, imageSize, frameId, frameTimestamp);
            }
        }
        else
        {
            bool bcn = layout == PredictionTensorLayout.Bcn;
            int channels = bcn ? rows : columns;
            int detections = bcn ? columns : rows;
            int classOffset = hasObjectness ? 5 : 4;
            int actualClasses = channels - classOffset;
            if (actualClasses < 1 || numClasses > 0 && actualClasses != numClasses) return;

            for (int i = 0; i < detections; i++)
            {
                float x = ReadRaw(outputTensor, dense, rank, columns, bcn, i, 0);
                float y = ReadRaw(outputTensor, dense, rank, columns, bcn, i, 1);
                float width = ReadRaw(outputTensor, dense, rank, columns, bcn, i, 2);
                float height = ReadRaw(outputTensor, dense, rank, columns, bcn, i, 3);
                int classId = selectedClassId >= 0 ? selectedClassId : 0;
                float confidence = 0;
                if (selectedClassId >= actualClasses) continue;
                if (selectedClassId >= 0)
                {
                    confidence = ReadRaw(outputTensor, dense, rank, columns, bcn, i, classOffset + selectedClassId);
                }
                else
                {
                    for (int c = 0; c < actualClasses; c++)
                    {
                        float score = ReadRaw(outputTensor, dense, rank, columns, bcn, i, classOffset + c);
                        if (score > confidence) { confidence = score; classId = c; }
                    }
                }
                if (hasObjectness) confidence *= ReadRaw(outputTensor, dense, rank, columns, bcn, i, 4);
                if (confidence < minConfidence || confidence > 1) continue;
                var modelRectangle = new RectangleF(x - width * 0.5f, y - height * 0.5f, width, height);
                AddClipped(destination, modelRectangle, modelRectangle, confidence, classId, modelClasses,
                    detectionBox, imageSize, frameId, frameTimestamp);
            }
        }

        if (applyNms && destination.Count > 1) ApplyClassAwareNmsInPlace(destination, 0.45f);
    }

    internal static void FillAimCandidates(
        IReadOnlyList<Prediction> predictions,
        List<Prediction> destination,
        float fovMinX,
        float fovMaxX,
        float fovMinY,
        float fovMaxY)
    {
        destination.Clear();
        for (int i = 0; i < predictions.Count; i++)
        {
            Prediction prediction = predictions[i];
            RectangleF box = prediction.Rectangle;
            if (box.Left < fovMinX || box.Right > fovMaxX || box.Top < fovMinY || box.Bottom > fovMaxY) continue;
            destination.Add(prediction);
        }
    }

    internal static Prediction? FindBestCandidate(
        IReadOnlyList<Prediction> candidates,
        float centerX,
        float centerY,
        bool priorityEnabled,
        bool prioritizeHead)
    {
        bool hasPreferred = false;
        if (priorityEnabled)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                if (IsHeadClass(candidates[i].ClassName) == prioritizeHead) { hasPreferred = true; break; }
            }
        }

        Prediction? best = null;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < candidates.Count; i++)
        {
            Prediction candidate = candidates[i];
            if (hasPreferred && IsHeadClass(candidate.ClassName) != prioritizeHead) continue;
            float dx = candidate.CenterX - centerX;
            float dy = candidate.CenterY - centerY;
            float distance = dx * dx + dy * dy;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }
        return best;
    }

    internal static int ResolveSelectedClassId(IReadOnlyDictionary<int, string> modelClasses, string selectedClass)
    {
        if (string.Equals(selectedClass, "Best Confidence", StringComparison.OrdinalIgnoreCase)) return -1;
        foreach (KeyValuePair<int, string> pair in modelClasses)
            if (string.Equals(pair.Value, selectedClass, StringComparison.OrdinalIgnoreCase)) return pair.Key;
        return -1;
    }

    internal static bool IsHeadClass(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        return name.Contains("head", StringComparison.OrdinalIgnoreCase)
            || name.Contains("dau", StringComparison.OrdinalIgnoreCase)
            || name.Contains("đầu", StringComparison.OrdinalIgnoreCase)
            || name.Contains("sọ", StringComparison.OrdinalIgnoreCase)
            || name.Contains("face", StringComparison.OrdinalIgnoreCase)
            || name.Contains("mặt", StringComparison.OrdinalIgnoreCase)
            || name.Contains("cap", StringComparison.OrdinalIgnoreCase)
            || name.Contains("mũ", StringComparison.OrdinalIgnoreCase)
            || name.Contains("brain", StringComparison.OrdinalIgnoreCase)
            || name.Contains("helmet", StringComparison.OrdinalIgnoreCase)
            || name.Contains("nón", StringComparison.OrdinalIgnoreCase);
    }

    internal static float IoU(RectangleF first, RectangleF second)
    {
        RectangleF intersection = RectangleF.Intersect(first, second);
        float area = Math.Max(0, intersection.Width) * Math.Max(0, intersection.Height);
        float union = Math.Max(0, first.Width) * Math.Max(0, first.Height)
            + Math.Max(0, second.Width) * Math.Max(0, second.Height) - area;
        return union > 0 ? area / union : 0;
    }

    private static float At(Tensor<float> tensor, ReadOnlySpan<float> dense, int rank, int columns, int row, int column) =>
        !dense.IsEmpty ? dense[row * columns + column]
        : rank == 3 ? tensor[0, row, column] : tensor[row, column];

    private static float ReadRaw(Tensor<float> tensor, ReadOnlySpan<float> dense, int rank,
        int columns, bool bcn, int detection, int channel) => bcn
            ? At(tensor, dense, rank, columns, channel, detection)
            : At(tensor, dense, rank, columns, detection, channel);

    private static void AddClipped(
        List<Prediction> destination,
        RectangleF captureRectangle,
        RectangleF modelRectangle,
        float confidence,
        int classId,
        IReadOnlyDictionary<int, string> modelClasses,
        Rectangle detectionBox,
        int imageSize,
        long frameId,
        long frameTimestamp)
    {
        if (!float.IsFinite(captureRectangle.X) || !float.IsFinite(captureRectangle.Y)
            || !float.IsFinite(captureRectangle.Width) || !float.IsFinite(captureRectangle.Height)
            || !float.IsFinite(confidence) || captureRectangle.Width <= 0 || captureRectangle.Height <= 0
            || classId < 0) return;

        RectangleF bounds = new(0, 0, detectionBox.Width, detectionBox.Height);
        RectangleF clipped = RectangleF.Intersect(captureRectangle, bounds);
        if (clipped.Width <= 0 || clipped.Height <= 0) return;
        float centerX = clipped.X + clipped.Width * 0.5f;
        float centerY = clipped.Y + clipped.Height * 0.5f;
        float normalizer = imageSize > 0 ? imageSize : Math.Max(1, detectionBox.Width);
        destination.Add(new Prediction
        {
            Rectangle = clipped,
            ModelRectangle = modelRectangle,
            Confidence = confidence,
            ClassId = classId,
            ClassName = modelClasses.TryGetValue(classId, out string? className) ? className : $"Class_{classId}",
            CenterXTranslated = centerX / normalizer,
            CenterYTranslated = centerY / normalizer,
            ScreenCenterX = detectionBox.Left + centerX,
            ScreenCenterY = detectionBox.Top + centerY,
            FrameId = frameId,
            FrameTimestamp = frameTimestamp
        });
    }

    private static void ApplyClassAwareNmsInPlace(List<Prediction> predictions, float threshold)
    {
        predictions.Sort(static (left, right) => right.Confidence.CompareTo(left.Confidence));
        for (int i = 0; i < predictions.Count; i++)
        {
            Prediction kept = predictions[i];
            for (int j = predictions.Count - 1; j > i; j--)
            {
                Prediction candidate = predictions[j];
                if (candidate.ClassId == kept.ClassId && IoU(kept.Rectangle, candidate.Rectangle) > threshold)
                    predictions.RemoveAt(j);
            }
        }
    }
}
