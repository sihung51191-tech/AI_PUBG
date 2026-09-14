using System.Diagnostics;

namespace Aimmy2.AILogic;

/// <summary>
/// Allocation-free constant-velocity Kalman tracker for one locked target.
/// Coordinates and velocity are physical screen pixels and pixels/second.
/// </summary>
internal sealed class KalmanTargetTracker
{
    private const double MinimumDeltaSeconds = 1d / 240d;
    private const double MaximumDeltaSeconds = 0.1d;
    private const double ResetGapSeconds = 0.35d;
    private const double MaximumVelocity = 5000d;

    private readonly double[,] _covariance = new double[4, 4];
    private readonly double[,] _predictedCovariance = new double[4, 4];
    private readonly double[] _gainX = new double[4];
    private readonly double[] _gainY = new double[4];

    private double _x;
    private double _y;
    private double _velocityX;
    private double _velocityY;
    private double _processNoise = 40d;
    private double _measurementNoise = 16d;
    private long _lastTimestamp;

    internal bool IsInitialized { get; private set; }
    internal double X => _x;
    internal double Y => _y;
    internal double VelocityX => _velocityX;
    internal double VelocityY => _velocityY;
    internal int MissingFrames { get; private set; }
    internal int ObservationCount { get; private set; }
    internal double Confidence { get; private set; }

    internal void Configure(double smoothness)
    {
        double normalized = Math.Clamp(smoothness, 0d, 100d) / 100d;
        // Higher smoothness trusts the motion model more, but the bounded range
        // avoids turning smoothing into a visibly delayed target.
        _processNoise = 120d - normalized * 108d;
        _measurementNoise = 4d + normalized * 44d;
    }

    internal bool Update(double measuredX, double measuredY, long timestamp, double confidence)
    {
        if (!IsFinite(measuredX) || !IsFinite(measuredY) || timestamp <= 0
            || !IsFinite(confidence) || confidence < 0d || confidence > 1d)
            return false;

        if (!IsInitialized)
        {
            Initialize(measuredX, measuredY, timestamp, confidence);
            return true;
        }

        if (timestamp <= _lastTimestamp) return false;
        double rawDelta = (timestamp - _lastTimestamp) / (double)Stopwatch.Frequency;
        if (!IsFinite(rawDelta) || rawDelta > ResetGapSeconds)
        {
            Reset();
            Initialize(measuredX, measuredY, timestamp, confidence);
            return true;
        }

        Predict(rawDelta);
        Correct(measuredX, measuredY);
        _lastTimestamp = timestamp;
        MissingFrames = 0;
        ObservationCount++;
        Confidence = Math.Clamp(Confidence * 0.35d + confidence * 0.65d, 0d, 1d);
        return true;
    }

    internal bool Predict(double deltaTime)
    {
        if (!IsInitialized || !IsFinite(deltaTime) || deltaTime <= 0d) return false;
        if (deltaTime > ResetGapSeconds)
        {
            Reset();
            return false;
        }

        double dt = Math.Clamp(deltaTime, MinimumDeltaSeconds, MaximumDeltaSeconds);
        _x += _velocityX * dt;
        _y += _velocityY * dt;

        // P' = F P F^T + Q for F = [[1,0,dt,0],[0,1,0,dt],...].
        for (int row = 0; row < 4; row++)
        for (int column = 0; column < 4; column++)
        {
            double value = _covariance[row, column];
            if (row < 2) value += dt * _covariance[row + 2, column];
            _predictedCovariance[row, column] = value;
        }

        for (int row = 0; row < 4; row++)
        for (int column = 0; column < 4; column++)
        {
            double value = _predictedCovariance[row, column];
            if (column < 2) value += dt * _predictedCovariance[row, column + 2];
            _covariance[row, column] = value;
        }

        double dt2 = dt * dt;
        double dt3 = dt2 * dt;
        double dt4 = dt2 * dt2;
        double qPosition = _processNoise * dt4 * 0.25d;
        double qCross = _processNoise * dt3 * 0.5d;
        double qVelocity = _processNoise * dt2;
        _covariance[0, 0] += qPosition;
        _covariance[1, 1] += qPosition;
        _covariance[0, 2] += qCross;
        _covariance[2, 0] += qCross;
        _covariance[1, 3] += qCross;
        _covariance[3, 1] += qCross;
        _covariance[2, 2] += qVelocity;
        _covariance[3, 3] += qVelocity;
        return true;
    }

    internal bool Correct(double measuredX, double measuredY)
    {
        if (!IsInitialized || !IsFinite(measuredX) || !IsFinite(measuredY)) return false;

        double s00 = _covariance[0, 0] + _measurementNoise;
        double s01 = _covariance[0, 1];
        double s10 = _covariance[1, 0];
        double s11 = _covariance[1, 1] + _measurementNoise;
        double determinant = s00 * s11 - s01 * s10;
        if (!IsFinite(determinant) || Math.Abs(determinant) < 1e-12d) return false;

        double inverse00 = s11 / determinant;
        double inverse01 = -s01 / determinant;
        double inverse10 = -s10 / determinant;
        double inverse11 = s00 / determinant;
        double innovationX = measuredX - _x;
        double innovationY = measuredY - _y;

        double k00 = _covariance[0, 0] * inverse00 + _covariance[0, 1] * inverse10;
        double k01 = _covariance[0, 0] * inverse01 + _covariance[0, 1] * inverse11;
        double k10 = _covariance[1, 0] * inverse00 + _covariance[1, 1] * inverse10;
        double k11 = _covariance[1, 0] * inverse01 + _covariance[1, 1] * inverse11;
        double k20 = _covariance[2, 0] * inverse00 + _covariance[2, 1] * inverse10;
        double k21 = _covariance[2, 0] * inverse01 + _covariance[2, 1] * inverse11;
        double k30 = _covariance[3, 0] * inverse00 + _covariance[3, 1] * inverse10;
        double k31 = _covariance[3, 0] * inverse01 + _covariance[3, 1] * inverse11;

        _x += k00 * innovationX + k01 * innovationY;
        _y += k10 * innovationX + k11 * innovationY;
        _velocityX = Math.Clamp(_velocityX + k20 * innovationX + k21 * innovationY, -MaximumVelocity, MaximumVelocity);
        _velocityY = Math.Clamp(_velocityY + k30 * innovationX + k31 * innovationY, -MaximumVelocity, MaximumVelocity);

        // P = (I - K H) P. Copy the two observed rows first because every
        // updated row depends on their pre-correction values.
        for (int column = 0; column < 4; column++)
        {
            _predictedCovariance[0, column] = _covariance[0, column];
            _predictedCovariance[1, column] = _covariance[1, column];
        }
        _gainX[0] = k00; _gainX[1] = k10; _gainX[2] = k20; _gainX[3] = k30;
        _gainY[0] = k01; _gainY[1] = k11; _gainY[2] = k21; _gainY[3] = k31;
        for (int row = 0; row < 4; row++)
        for (int column = 0; column < 4; column++)
            _covariance[row, column] -= _gainX[row] * _predictedCovariance[0, column]
                + _gainY[row] * _predictedCovariance[1, column];
        return true;
    }

    internal bool MarkMissing(long timestamp, int maximumMissingFrames)
    {
        if (!IsInitialized || timestamp <= _lastTimestamp) return false;
        double delta = (timestamp - _lastTimestamp) / (double)Stopwatch.Frequency;
        if (!Predict(delta)) return false;
        _lastTimestamp = timestamp;
        MissingFrames++;
        Confidence *= 0.75d;
        if (MissingFrames <= Math.Max(0, maximumMissingFrames)) return true;
        Reset();
        return false;
    }

    internal (double X, double Y) GetPredictedPosition(double leadTimeSeconds, double maximumDistance)
    {
        if (!IsInitialized) return (_x, _y);
        double lead = Math.Clamp(IsFinite(leadTimeSeconds) ? leadTimeSeconds : 0d, 0d, 0.15d);
        double dx = _velocityX * lead;
        double dy = _velocityY * lead;
        double distance = Math.Sqrt(dx * dx + dy * dy);
        double limit = Math.Max(0d, IsFinite(maximumDistance) ? maximumDistance : 0d);
        if (limit > 0d && distance > limit)
        {
            double scale = limit / distance;
            dx *= scale;
            dy *= scale;
        }
        return (_x + dx, _y + dy);
    }

    internal void Reset()
    {
        _x = _y = _velocityX = _velocityY = 0d;
        _lastTimestamp = 0;
        MissingFrames = 0;
        ObservationCount = 0;
        Confidence = 0d;
        IsInitialized = false;
        Array.Clear(_covariance);
        Array.Clear(_predictedCovariance);
        Array.Clear(_gainX);
        Array.Clear(_gainY);
    }

    private void Initialize(double measuredX, double measuredY, long timestamp, double confidence)
    {
        _x = measuredX;
        _y = measuredY;
        _velocityX = _velocityY = 0d;
        _lastTimestamp = timestamp;
        MissingFrames = 0;
        ObservationCount = 1;
        Confidence = confidence;
        IsInitialized = true;
        Array.Clear(_covariance);
        _covariance[0, 0] = _covariance[1, 1] = 64d;
        _covariance[2, 2] = _covariance[3, 3] = 400d;
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
