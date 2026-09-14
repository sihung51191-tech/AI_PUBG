using System.Diagnostics;
using System.Drawing;

namespace Aimmy2.AILogic;

internal readonly record struct StickyAimContext(
    int Slot,
    int ModelIdentity,
    string CaptureMethod,
    int CaptureIdentity,
    Rectangle CaptureBox,
    int ImageSize,
    int ResetGeneration,
    long FrameId,
    long FrameTimestamp,
    long ProcessingTimestamp);

internal readonly record struct StickyAimSettings(
    bool Enabled,
    bool AimActive,
    float StickyThreshold,
    float MinimumConfidence,
    double LockDurationMilliseconds,
    bool AllowSyntheticTargetOnMiss,
    int MaxFramesWithoutTarget = 3,
    double MaxWgcFrameAgeMilliseconds = 500);

/// <summary>Stateful, single-slot target association and hysteresis.</summary>
internal sealed class StickyAimSelector
{
    private const float LockScoreDecay = 0.85f;
    private const float LockScoreGain = 15f;
    private const float MaxLockScore = 100f;
    private const float ReferenceTargetSize = 10000f;

    private Prediction? _currentTarget;
    private StickyAimContext _context;
    private bool _hasContext;
    private int _consecutiveFramesWithoutTarget;
    private int _framesWithoutMatch;
    private float _lastTargetVelocityX;
    private float _lastTargetVelocityY;
    private float _targetLockScore;
    private long _targetLockedTimestamp;
    private long _lastProcessedFrameId = long.MinValue;
    private long _nextTrackId;
    private long _currentTrackId;

    internal long TargetSwitchCount { get; private set; }
    internal Prediction? CurrentTarget => _currentTarget;

    internal Prediction? SelectTarget(
        StickyAimSettings settings,
        StickyAimContext context,
        Prediction? bestCandidate,
        IReadOnlyList<Prediction> predictions)
    {
        if (!SameSource(context))
        {
            ResetState();
            // A recreated WGC session starts its frame counter again. Frame IDs are
            // monotonic only inside one capture context/generation.
            _lastProcessedFrameId = long.MinValue;
            _context = context;
            _hasContext = true;
        }

        bool isWgc = string.Equals(context.CaptureMethod, "WGC", StringComparison.Ordinal);
        if (isWgc)
        {
            if (context.FrameId <= 0 || context.FrameId <= _lastProcessedFrameId
                || IsExpired(context, settings.MaxWgcFrameAgeMilliseconds)) return null;
            _lastProcessedFrameId = context.FrameId;
        }

        if (!settings.Enabled || !settings.AimActive)
        {
            ResetState();
            return bestCandidate;
        }

        if (bestCandidate == null || predictions.Count == 0)
            return HandleNoDetections(settings);

        if (bestCandidate.Confidence < settings.MinimumConfidence)
            return HandleNoDetections(settings);

        _consecutiveFramesWithoutTarget = 0;
        if (_currentTarget == null) return AcquireNewTarget(bestCandidate, context.ProcessingTimestamp, false);

        Prediction? matched = FindCurrentTargetMatch(predictions, _currentTarget, settings.MinimumConfidence);
        if (matched != null)
        {
            _framesWithoutMatch = 0;
            float sizeFactor = GetSizeFactor(_currentTarget.Area);
            UpdateVelocity(matched, sizeFactor);
            _targetLockScore = Math.Min(MaxLockScore, _targetLockScore + LockScoreGain);

            if (!ReferenceEquals(bestCandidate, matched)
                && !SamePerson(bestCandidate, matched)
                && !IsLockActive(settings.LockDurationMilliseconds, context.ProcessingTimestamp)
                && IsClearlyBetter(bestCandidate, matched, context, settings.StickyThreshold))
                return AcquireNewTarget(bestCandidate, context.ProcessingTimestamp, true);

            matched.TargetTrackId = _currentTrackId;
            _currentTarget = matched;
            return matched;
        }

        _framesWithoutMatch++;
        if (_framesWithoutMatch >= Math.Max(1, settings.MaxFramesWithoutTarget))
            return AcquireNewTarget(bestCandidate, context.ProcessingTimestamp, true);

        // Keep lock state through short misses, but never move again from an old WGC frame.
        return settings.AllowSyntheticTargetOnMiss
            ? _currentTarget.CopyForMiss(_lastTargetVelocityX, _lastTargetVelocityY, _framesWithoutMatch)
            : null;
    }

    internal void Reset()
    {
        ResetState();
        _hasContext = false;
        _lastProcessedFrameId = long.MinValue;
    }

    private bool SameSource(StickyAimContext context)
    {
        return _hasContext
            && _context.Slot == context.Slot
            && _context.ModelIdentity == context.ModelIdentity
            && string.Equals(_context.CaptureMethod, context.CaptureMethod, StringComparison.Ordinal)
            && _context.CaptureIdentity == context.CaptureIdentity
            && _context.CaptureBox.Size == context.CaptureBox.Size
            && _context.ImageSize == context.ImageSize
            && _context.ResetGeneration == context.ResetGeneration;
    }

    private static bool IsExpired(StickyAimContext context, double maximumAgeMilliseconds)
    {
        if (context.FrameTimestamp <= 0 || context.ProcessingTimestamp <= context.FrameTimestamp) return false;
        return (context.ProcessingTimestamp - context.FrameTimestamp) * 1000d / Stopwatch.Frequency > maximumAgeMilliseconds;
    }

    private Prediction? HandleNoDetections(StickyAimSettings settings)
    {
        if (_currentTarget != null && ++_consecutiveFramesWithoutTarget <= Math.Max(0, settings.MaxFramesWithoutTarget))
        {
            _targetLockScore *= LockScoreDecay;
            return settings.AllowSyntheticTargetOnMiss
                ? _currentTarget.CopyForMiss(_lastTargetVelocityX, _lastTargetVelocityY, _consecutiveFramesWithoutTarget)
                : null;
        }

        ResetState();
        return null;
    }

    private Prediction AcquireNewTarget(Prediction target, long timestamp, bool switchTarget)
    {
        if (switchTarget) TargetSwitchCount++;
        _lastTargetVelocityX = 0;
        _lastTargetVelocityY = 0;
        _targetLockScore = LockScoreGain;
        _framesWithoutMatch = 0;
        _consecutiveFramesWithoutTarget = 0;
        _targetLockedTimestamp = timestamp;
        _currentTrackId = ++_nextTrackId;
        target.TargetTrackId = _currentTrackId;
        _currentTarget = target;
        return target;
    }

    private Prediction? FindCurrentTargetMatch(
        IReadOnlyList<Prediction> predictions,
        Prediction current,
        float minimumConfidence)
    {
        Prediction? best = null;
        float bestScore = float.MaxValue;
        float currentArea = Math.Max(current.Area, 1);
        float targetSize = MathF.Sqrt(currentArea);
        float trackingRadius = Math.Max(8, targetSize * 3f);
        float trackingRadiusSquared = trackingRadius * trackingRadius;

        for (int i = 0; i < predictions.Count; i++)
        {
            Prediction candidate = predictions[i];
            if (candidate.Confidence < minimumConfidence || candidate.Area <= 0) continue;
            float dx = candidate.ScreenCenterX - current.ScreenCenterX;
            float dy = candidate.ScreenCenterY - current.ScreenCenterY;
            float distanceSquared = dx * dx + dy * dy;
            float sizeRatio = Math.Min(currentArea, candidate.Area) / Math.Max(currentArea, candidate.Area);
            bool sameClass = candidate.ClassId == current.ClassId;
            bool samePerson = SamePerson(candidate, current);
            float iou = PredictionFilter.IoU(candidate.Rectangle, current.Rectangle);
            if ((!sameClass && !samePerson) || distanceSquared > trackingRadiusSquared
                || !samePerson && sizeRatio < 0.45f || iou <= 0 && !samePerson && distanceSquared > targetSize * targetSize)
                continue;

            float normalizedDistance = distanceSquared / Math.Max(1, trackingRadiusSquared);
            float expectedDx = candidate.ScreenCenterX - (current.ScreenCenterX + _lastTargetVelocityX);
            float expectedDy = candidate.ScreenCenterY - (current.ScreenCenterY + _lastTargetVelocityY);
            float predictedDistance = (expectedDx * expectedDx + expectedDy * expectedDy)
                / Math.Max(1, trackingRadiusSquared);
            float directionPenalty = 0f;
            float movementX = candidate.ScreenCenterX - current.ScreenCenterX;
            float movementY = candidate.ScreenCenterY - current.ScreenCenterY;
            float previousSpeed = MathF.Sqrt(_lastTargetVelocityX * _lastTargetVelocityX + _lastTargetVelocityY * _lastTargetVelocityY);
            float movementSpeed = MathF.Sqrt(movementX * movementX + movementY * movementY);
            if (previousSpeed > 0.5f && movementSpeed > 0.5f)
            {
                float cosine = (_lastTargetVelocityX * movementX + _lastTargetVelocityY * movementY)
                    / (previousSpeed * movementSpeed);
                directionPenalty = (1f - Math.Clamp(cosine, -1f, 1f)) * 0.15f;
            }
            float score = normalizedDistance + (1f - iou) * 0.55f + (1f - sizeRatio) * 0.35f
                + predictedDistance * 0.25f + directionPenalty
                + (1f - candidate.Confidence) * 0.1f + (sameClass ? 0 : 0.08f);
            if (score < bestScore) { bestScore = score; best = candidate; }
        }
        return best;
    }

    private static bool SamePerson(Prediction first, Prediction second)
    {
        if (first.ClassId == second.ClassId)
        {
            float dx = first.ScreenCenterX - second.ScreenCenterX;
            float dy = first.ScreenCenterY - second.ScreenCenterY;
            float scale = Math.Max(MathF.Sqrt(Math.Max(first.Area, 1)), MathF.Sqrt(Math.Max(second.Area, 1)));
            return PredictionFilter.IoU(first.Rectangle, second.Rectangle) > 0.1f
                || dx * dx + dy * dy < scale * scale;
        }
        bool firstHead = PredictionFilter.IsHeadClass(first.ClassName);
        bool secondHead = PredictionFilter.IsHeadClass(second.ClassName);
        if (firstHead == secondHead) return false;

        RectangleF head = firstHead ? first.Rectangle : second.Rectangle;
        RectangleF body = firstHead ? second.Rectangle : first.Rectangle;
        float headX = head.X + head.Width * 0.5f;
        float headY = head.Y + head.Height * 0.5f;
        float horizontalMargin = body.Width * 0.25f;
        return headX >= body.Left - horizontalMargin && headX <= body.Right + horizontalMargin
            && headY >= body.Top - body.Height * 0.35f && headY <= body.Top + body.Height * 0.7f;
    }

    private static bool IsClearlyBetter(Prediction candidate, Prediction current, StickyAimContext context, float stickyThreshold)
    {
        float centerX = context.CaptureBox.Left + context.CaptureBox.Width * 0.5f;
        float centerY = context.CaptureBox.Top + context.CaptureBox.Height * 0.5f;
        float candidateDistance = DistanceSquared(candidate.ScreenCenterX, candidate.ScreenCenterY, centerX, centerY);
        float currentDistance = DistanceSquared(current.ScreenCenterX, current.ScreenCenterY, centerX, centerY);
        float margin = Math.Max(4, stickyThreshold * 0.05f);
        return candidateDistance < currentDistance * 0.55f
            && currentDistance - candidateDistance > margin * margin;
    }

    private bool IsLockActive(double durationMilliseconds, long now)
    {
        return durationMilliseconds > 0 && _targetLockedTimestamp > 0 && now > _targetLockedTimestamp
            && (now - _targetLockedTimestamp) * 1000d / Stopwatch.Frequency < durationMilliseconds;
    }

    private void UpdateVelocity(Prediction target, float sizeFactor)
    {
        if (_currentTarget == null) return;
        float smoothing = Math.Clamp(0.6f + sizeFactor * 0.1f, 0.7f, 0.9f);
        float weight = 1f - smoothing;
        _lastTargetVelocityX = _lastTargetVelocityX * smoothing
            + (target.ScreenCenterX - _currentTarget.ScreenCenterX) * weight;
        _lastTargetVelocityY = _lastTargetVelocityY * smoothing
            + (target.ScreenCenterY - _currentTarget.ScreenCenterY) * weight;
    }

    private static float GetSizeFactor(float targetArea)
    {
        float ratio = ReferenceTargetSize / Math.Max(targetArea, 100f);
        return Math.Clamp(ratio, 1, 3);
    }

    private static float DistanceSquared(float x1, float y1, float x2, float y2)
    {
        float dx = x1 - x2;
        float dy = y1 - y2;
        return dx * dx + dy * dy;
    }

    private void ResetState()
    {
        _currentTarget = null;
        _consecutiveFramesWithoutTarget = 0;
        _framesWithoutMatch = 0;
        _lastTargetVelocityX = 0;
        _lastTargetVelocityY = 0;
        _targetLockScore = 0;
        _targetLockedTimestamp = 0;
        _currentTrackId = 0;
    }
}
