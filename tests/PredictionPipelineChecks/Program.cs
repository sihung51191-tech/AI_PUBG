using Aimmy2.AILogic;
using InputLogic;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using PipelineModelMetadata = Aimmy2.AILogic.ModelMetadata;

var results = new List<object>();
int failed = 0;
double kalmanMillisecondsPerFrame = 0;
long kalmanAllocatedBytes = 0;
void Check(string name, Action test)
{
    try { test(); results.Add(new { Test = name, Status = "PASS" }); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed++; results.Add(new { Test = name, Status = "FAIL", Error = ex.Message }); Console.WriteLine("FAIL " + name + ": " + ex.Message); }
}
void True(bool value, string message = "condition is false") { if (!value) throw new Exception(message); }
void Equal(float expected, float actual, float tolerance = .001f)
{
    if (Math.Abs(expected - actual) > tolerance) throw new Exception($"{expected} != {actual}");
}

var classes = new Dictionary<int, string> { [0] = "body", [1] = "head" };
var capture = new Rectangle(0, 0, 640, 640);
long Ticks(double milliseconds) => (long)(milliseconds * Stopwatch.Frequency / 1000d);
Prediction P(float centerX, float centerY, float width = 40, float height = 80,
    int classId = 0, float confidence = .9f, long frame = 1)
{
    return new Prediction
    {
        Rectangle = new RectangleF(centerX - width / 2, centerY - height / 2, width, height),
        ModelRectangle = new RectangleF(centerX - width / 2, centerY - height / 2, width, height),
        ScreenCenterX = centerX,
        ScreenCenterY = centerY,
        CenterXTranslated = centerX / 640,
        CenterYTranslated = centerY / 640,
        Confidence = confidence,
        ClassId = classId,
        ClassName = classes[classId],
        FrameId = frame
    };
}
StickyAimContext Context(long frame, string method = "WGC", int slot = 1, int model = 10,
    int size = 640, int generation = 0, long now = 0, long captured = 0) =>
    new(slot, model, method, HashCode.Combine(method, size), new Rectangle(0, 0, size, size), size, generation, frame,
        captured == 0 ? now : captured, now);
StickyAimSettings Settings(bool synthetic = false, int misses = 3, double lockMs = 0) =>
    new(true, true, 100, .5f, lockMs, synthetic, misses);

Check("1. one prediction", () =>
{
    Tensor<float> tensor = new DenseTensor<float>(new float[] { 300, 280, 340, 360, .9f, 0 }, [1, 1, 6]);
    var list = new List<Prediction>();
    PredictionFilter.CreatePredictions(tensor, capture, 640, 2, classes, .5f,
        "Best Confidence", list, frameId: 7, frameTimestamp: 11);
    True(list.Count == 1);
    Equal(320, list[0].CenterX); Equal(320, list[0].CenterY);
    True(list[0].FrameId == 7 && list[0].FrameTimestamp == 11);
});

Check("2. nearby predictions do not cause pixel-scale switching", () =>
{
    var selector = new StickyAimSelector();
    long now = Ticks(1000);
    var original = P(315, 320, frame: 1);
    True(ReferenceEquals(original, selector.SelectTarget(Settings(), Context(1, now: now), original, [original])));
    var updated = P(316, 320, frame: 2);
    var slightlyCloser = P(322, 320, frame: 2);
    var selected = selector.SelectTarget(Settings(), Context(2, now: now + Ticks(16)), slightlyCloser, [updated, slightlyCloser]);
    True(ReferenceEquals(updated, selected), "lock switched for a few pixels");
    True(selector.TargetSwitchCount == 0);
});

Check("3. overlapping head and body survive class-aware NMS", () =>
{
    // BNC: x,y,w,h,body,head
    Tensor<float> tensor = new DenseTensor<float>(new float[]
    {
        320, 340, 100, 200, .90f, .05f,
        320, 290, 40, 50, .05f, .92f,
        321, 341, 100, 200, .80f, .04f
    }, [1, 3, 6]);
    var list = new List<Prediction>();
    PredictionFilter.CreatePredictions(tensor, capture, 640, 2, classes, .5f,
        "Best Confidence", list, PredictionTensorLayout.Bnc, applyNms: true);
    True(list.Count == 2, $"expected body+head, got {list.Count}");
    True(list.Any(x => x.ClassId == 0) && list.Any(x => x.ClassId == 1));
});

Check("4. head and body of one person share a lock group", () =>
{
    var selector = new StickyAimSelector();
    long now = Ticks(2000);
    var body = P(280, 340, 80, 180, 0, frame: 1);
    selector.SelectTarget(Settings(), Context(1, now: now), body, [body]);
    var head = P(280, 270, 35, 45, 1, frame: 2);
    var selected = selector.SelectTarget(Settings(), Context(2, now: now + Ticks(16)), head, [head]);
    True(ReferenceEquals(head, selected));
    True(selector.TargetSwitchCount == 0, "head/body transition counted as a person switch");
});

Check("5. head/body of different people stay distinct", () =>
{
    var selector = new StickyAimSelector();
    long now = Ticks(3000);
    var leftBody = P(220, 340, 80, 180, 0, frame: 1);
    selector.SelectTarget(Settings(lockMs: 500), Context(1, now: now), leftBody, [leftBody]);
    var leftHead = P(220, 270, 35, 45, 1, frame: 2);
    var rightHead = P(320, 270, 35, 45, 1, frame: 2);
    var selected = selector.SelectTarget(Settings(lockMs: 500), Context(2, now: now + Ticks(16)), rightHead, [leftHead, rightHead]);
    True(ReferenceEquals(leftHead, selected), "lock jumped to another person's head");
});

Check("6. confidence oscillation around threshold", () =>
{
    var selector = new StickyAimSelector();
    long now = Ticks(4000);
    var good = P(300, 320, confidence: .51f, frame: 1);
    selector.SelectTarget(Settings(), Context(1, now: now), good, [good]);
    Tensor<float> lowTensor = new DenseTensor<float>(new float[] { 280, 280, 320, 360, .49f, 0 }, [1, 1, 6]);
    var list = new List<Prediction>();
    PredictionFilter.CreatePredictions(lowTensor, capture, 640, 2, classes, .5f, "Best Confidence", list);
    True(list.Count == 0);
    True(selector.SelectTarget(Settings(), Context(2, now: now + Ticks(16)), null, list) == null);
    var recovered = P(301, 320, confidence: .51f, frame: 3);
    True(ReferenceEquals(recovered, selector.SelectTarget(Settings(), Context(3, now: now + Ticks(32)), recovered, [recovered])));
    True(selector.TargetSwitchCount == 0);
});

Check("7. target missing for one to three frames", () =>
{
    var selector = new StickyAimSelector();
    long now = Ticks(5000);
    var target = P(300, 320);
    selector.SelectTarget(Settings(), Context(1, now: now), target, [target]);
    for (int frame = 2; frame <= 4; frame++)
    {
        True(selector.SelectTarget(Settings(), Context(frame, now: now + Ticks(frame * 16)), null, Array.Empty<Prediction>()) == null);
        True(selector.CurrentTarget != null, $"lock reset at missing frame {frame - 1}");
    }
    selector.SelectTarget(Settings(), Context(5, now: now + Ticks(80)), null, Array.Empty<Prediction>());
    True(selector.CurrentTarget == null, "lock did not reset after grace frames");
});

Check("8. target outside FOV is not aimable", () =>
{
    var all = new List<Prediction> { P(50, 50) };
    var aim = new List<Prediction>();
    PredictionFilter.FillAimCandidates(all, aim, 160, 480, 160, 480);
    True(aim.Count == 0);
});

Check("9. slot and model state are independent and reset", () =>
{
    long now = Ticks(6000);
    var slot1 = new StickyAimSelector();
    var slot2 = new StickyAimSelector();
    var first = P(250, 320);
    var second = P(390, 320);
    slot1.SelectTarget(Settings(), Context(1, slot: 1, now: now), first, [first]);
    slot2.SelectTarget(Settings(), Context(1, slot: 2, now: now), second, [second]);
    True(ReferenceEquals(first, slot1.CurrentTarget) && ReferenceEquals(second, slot2.CurrentTarget));
    var replacement = P(320, 320, frame: 2);
    slot1.SelectTarget(Settings(), Context(2, slot: 1, model: 11, now: now + Ticks(16)), replacement, [replacement]);
    True(ReferenceEquals(replacement, slot1.CurrentTarget));
    True(ReferenceEquals(second, slot2.CurrentTarget), "slot 1 reset leaked into slot 2");
    var released = new StickyAimSettings(true, false, 100, .5f, 500, false);
    slot1.SelectTarget(released, Context(3, slot: 1, model: 11, now: now + Ticks(32)), replacement, [replacement]);
    True(slot1.CurrentTarget == null, "aim-key release did not reset the lock");
    slot2.SelectTarget(Settings(), new StickyAimContext(2, 10, "WGC", 999,
        capture, 640, 0, 2, now + Ticks(16), now + Ticks(16)), second, [second]);
    True(ReferenceEquals(second, slot2.CurrentTarget), "capture-context reset did not reacquire cleanly");
});

Check("10. image sizes 320 416 512 640", () =>
{
    foreach (int size in new[] { 320, 416, 512, 640 })
    {
        Tensor<float> tensor = new DenseTensor<float>(new float[] { size * .25f, size * .25f, size * .75f, size * .75f, .9f, 0 }, [1, 1, 6]);
        var list = new List<Prediction>();
        PredictionFilter.CreatePredictions(tensor, new Rectangle(10, 20, size, size), size, 2, classes,
            .5f, "Best Confidence", list);
        True(list.Count == 1); Equal(.5f, list[0].CenterXTranslated); Equal(10 + size * .5f, list[0].ScreenCenterX);
    }
});

Check("11. dynamic and fixed model sizes", () =>
{
    var fixedModel = new PipelineModelMetadata { Input = new("images", [1, 3, 416, 416], "Float") };
    True(fixedModel.ResolveSize(640) == (416, 416));
    var dynamicModel = new PipelineModelMetadata { Input = new("images", [-1, 3, -1, -1], "Float"), Options = new() { DynamicWidth = 512, DynamicHeight = 320 } };
    True(dynamicModel.ResolveSize(640) == (512, 320));
    Float16 half = (Float16).5f;
    Equal(.5f, (float)half);
});

Check("12. BCN and BNC produce identical predictions", () =>
{
    float[] bnc = { 320, 300, 40, 80, .8f, .2f, 200, 220, 30, 60, .1f, .9f };
    float[] bcn = { 320, 200, 300, 220, 40, 30, 80, 60, .8f, .1f, .2f, .9f };
    var left = new List<Prediction>(); var right = new List<Prediction>();
    PredictionFilter.CreatePredictions(new DenseTensor<float>(bnc, [1, 2, 6]), capture, 640, 2, classes, .5f,
        "Best Confidence", left, PredictionTensorLayout.Bnc);
    PredictionFilter.CreatePredictions(new DenseTensor<float>(bcn, [1, 6, 2]), capture, 640, 2, classes, .5f,
        "Best Confidence", right, PredictionTensorLayout.Bcn);
    True(left.Count == right.Count && left.Count == 2);
    for (int i = 0; i < 2; i++) { Equal(left[i].Rectangle.X, right[i].Rectangle.X); Equal(left[i].Confidence, right[i].Confidence); True(left[i].ClassId == right[i].ClassId); }
});

Check("13. WGC duplicate frame is never reused", () =>
{
    var selector = new StickyAimSelector(); long now = Ticks(7000); var target = P(300, 320);
    True(selector.SelectTarget(Settings(), Context(10, now: now), target, [target]) != null);
    True(selector.SelectTarget(Settings(), Context(10, now: now + Ticks(16)), target, [target]) == null);
});

Check("14. WGC slow/irregular and expired frames", () =>
{
    var selector = new StickyAimSelector(); long now = Ticks(8000); var target = P(300, 320);
    True(selector.SelectTarget(Settings(), Context(1, now: now), target, [target]) != null);
    var slow = P(301, 320, frame: 2);
    True(selector.SelectTarget(Settings(), Context(2, now: now + Ticks(120)), slow, [slow]) != null);
    var stale = P(302, 320, frame: 3);
    True(selector.SelectTarget(Settings(), Context(3, now: now + Ticks(900), captured: now + Ticks(200)), stale, [stale]) == null);
    Equal(1, (float)MovementPaths.TimeCorrectedScale(.2, 1d / 60d));
    True(MovementPaths.TimeCorrectedScale(.2, 1d / 30d) > 1, "slow frame was not time compensated");
});

Check("14b. recreated WGC generation accepts a reset frame id", () =>
{
    var selector = new StickyAimSelector();
    long now = Ticks(8500);
    var oldSession = P(300, 320, frame: 900);
    True(selector.SelectTarget(Settings(), Context(900, generation: 1, now: now), oldSession, [oldSession]) != null);
    var newSession = P(301, 320, frame: 1);
    True(selector.SelectTarget(Settings(), Context(1, generation: 2, now: now + Ticks(16)), newSession, [newSession]) != null,
        "new WGC session frame was rejected by the previous session's frame id");
});

Check("15. GDI+ and DirectX do not enforce WGC frame IDs", () =>
{
    foreach (string method in new[] { "GDI+", "DirectX" })
    {
        var selector = new StickyAimSelector(); long now = Ticks(9000); var target = P(300, 320);
        True(selector.SelectTarget(Settings(synthetic: true), Context(0, method, now: now), target, [target]) != null);
        var next = P(301, 320);
        True(selector.SelectTarget(Settings(synthetic: true), Context(0, method, now: now + Ticks(16)), next, [next]) != null);
    }
});

Check("16. invalid, NaN and negative coordinates are safe", () =>
{
    Tensor<float> tensor = new DenseTensor<float>(new float[]
    {
        -20, -10, 30, 40, .9f, 0,
        float.NaN, 0, 20, 20, .9f, 0,
        100, 100, 90, 110, .9f, 0
    }, [1, 3, 6]);
    var list = new List<Prediction>();
    PredictionFilter.CreatePredictions(tensor, capture, 640, 2, classes, .5f, "Best Confidence", list);
    True(list.Count == 1, $"expected one clipped valid box, got {list.Count}");
    True(list[0].Rectangle.Left >= 0 && list[0].Rectangle.Top >= 0);
    True(float.IsFinite(list[0].CenterX) && float.IsFinite(list[0].CenterY));
});

Check("17. target track id survives matching and changes on switch", () =>
{
    var selector = new StickyAimSelector();
    long now = Ticks(10000);
    var first = P(240, 320, frame: 1);
    selector.SelectTarget(Settings(misses: 1), Context(1, now: now), first, [first]);
    long firstId = first.TargetTrackId;
    True(firstId > 0, "new target has no track id");
    var match = P(244, 320, frame: 2);
    selector.SelectTarget(Settings(misses: 1), Context(2, now: now + Ticks(16)), match, [match]);
    True(match.TargetTrackId == firstId, "matched target changed track id");
    selector.SelectTarget(Settings(misses: 1), Context(3, now: now + Ticks(32)), null, Array.Empty<Prediction>());
    var replacement = P(400, 320, frame: 4);
    selector.SelectTarget(Settings(misses: 1), Context(4, now: now + Ticks(48)), replacement, [replacement]);
    True(replacement.TargetTrackId > firstId, "replacement reused the previous track id");
});

Check("18. Kalman rejects invalid and old observations", () =>
{
    var tracker = new KalmanTargetTracker();
    long now = Ticks(11000);
    True(!tracker.Update(double.NaN, 1, now, .9));
    True(tracker.Update(100, 200, now, .9));
    True(!tracker.Update(101, 201, now, .9), "duplicate timestamp was accepted");
    True(!tracker.Update(double.PositiveInfinity, 201, now + Ticks(16), .9));
    True(tracker.ObservationCount == 1);
});

Check("19. Kalman smooths stationary jitter", () =>
{
    var tracker = new KalmanTargetTracker();
    tracker.Configure(55);
    long now = Ticks(12000);
    double rawDeviation = 0;
    for (int i = 0; i < 120; i++)
    {
        double measured = 320 + (i % 2 == 0 ? 4 : -4);
        rawDeviation += Math.Abs(measured - 320);
        True(tracker.Update(measured, 320, now + Ticks(i * 16), .9));
    }
    double filteredDeviation = Math.Abs(tracker.X - 320);
    True(filteredDeviation < rawDeviation / 120d, $"filter did not reduce jitter: {filteredDeviation}");
});

Check("20. Kalman estimates pixels-per-second velocity", () =>
{
    var tracker = new KalmanTargetTracker();
    tracker.Configure(35);
    long now = Ticks(14000);
    for (int i = 0; i < 120; i++)
        True(tracker.Update(100 + i * 2, 200, now + Ticks(i * 20), .95));
    True(tracker.VelocityX > 70 && tracker.VelocityX < 130, $"unexpected vx {tracker.VelocityX}");
    var predicted = tracker.GetPredictedPosition(.1, 20);
    True(predicted.X > tracker.X && predicted.X - tracker.X <= 20.001);
});

Check("21. Kalman resets safely after a long frame gap", () =>
{
    var tracker = new KalmanTargetTracker();
    long now = Ticks(17000);
    tracker.Update(100, 100, now, .8);
    tracker.Update(110, 100, now + Ticks(16), .8);
    tracker.Update(500, 400, now + Ticks(1000), .8);
    True(tracker.IsInitialized && tracker.ObservationCount == 1);
    True(Math.Abs(tracker.X - 500) < .001 && Math.Abs(tracker.VelocityX) < .001);
});

Check("22. Kalman missing-frame confidence decays and resets", () =>
{
    var tracker = new KalmanTargetTracker();
    long now = Ticks(19000);
    tracker.Update(100, 100, now, .8);
    True(tracker.MarkMissing(now + Ticks(16), 2));
    True(tracker.MarkMissing(now + Ticks(32), 2));
    True(!tracker.MarkMissing(now + Ticks(48), 2));
    True(!tracker.IsInitialized);
});

int coordinateCase = 0;
foreach (int modelSize in new[] { 160, 224, 256, 288, 320, 416, 512, 640 })
foreach (Point origin in new[] { new Point(0, 0), new Point(-1920, 120), new Point(2560, -200) })
foreach (bool letterbox in new[] { false, true })
{
    int caseNumber = ++coordinateCase;
    Check($"coordinate matrix {caseNumber:00}: {modelSize}px origin {origin.X},{origin.Y} letterbox={letterbox}", () =>
    {
        Rectangle region = letterbox
            ? new Rectangle(origin.X, origin.Y, modelSize * 2, modelSize)
            : new Rectangle(origin.X, origin.Y, modelSize, modelSize);
        var transform = new CaptureTransform(region, modelSize, modelSize, letterbox);
        float captureX = region.Width * .25f;
        float captureY = region.Height * .75f;
        float modelX = captureX * transform.ScaleX + transform.PadX;
        float modelY = captureY * transform.ScaleY + transform.PadY;
        PointF roundTrip = transform.ModelToCapture(modelX, modelY);
        PointF screen = transform.ModelToScreen(modelX, modelY);
        Equal(captureX, roundTrip.X, .01f);
        Equal(captureY, roundTrip.Y, .01f);
        Equal(origin.X + captureX, screen.X, .01f);
        Equal(origin.Y + captureY, screen.Y, .01f);
    });
}

Check("Kalman allocation and time microbenchmark", () =>
{
    var tracker = new KalmanTargetTracker();
    tracker.Configure(55);
    long now = Ticks(22000);
    for (int i = 0; i < 100; i++) tracker.Update(i, i, now + Ticks(i * 8), .9);
    const int iterations = 100000;
    var watch = new Stopwatch();
    long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
    watch.Start();
    for (int i = 0; i < iterations; i++)
    {
        tracker.Update(100 + i * .001, 200, now + Ticks((100 + i) * 8d), .9);
        _ = tracker.GetPredictedPosition(.035, 50);
    }
    watch.Stop();
    long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
    double millisecondsPerFrame = watch.Elapsed.TotalMilliseconds / iterations;
    kalmanMillisecondsPerFrame = millisecondsPerFrame;
    kalmanAllocatedBytes = allocated;
    True(allocated == 0, $"allocated {allocated} bytes");
    True(millisecondsPerFrame < .2, $"{millisecondsPerFrame:F4} ms/frame exceeds target");
});
results.Add(new
{
    Test = "Kalman allocation and time measurement",
    Status = "MEASURED",
    MillisecondsPerFrame = kalmanMillisecondsPerFrame,
    AllocatedBytes = kalmanAllocatedBytes
});

Check("coordinate regression matches legacy canonical path", () =>
{
    Tensor<float> tensor = new DenseTensor<float>(new float[] { 100, 120, 180, 280, .9f, 0 }, [1, 1, 6]);
    var list = new List<Prediction>();
    var region = new Rectangle(-300, 40, 640, 640);
    PredictionFilter.CreatePredictions(tensor, region, 640, 2, classes, .5f, "Best Confidence", list);
    float oldCenterX = (100 + 180) * .5f;
    float oldCenterY = (120 + 280) * .5f;
    Equal(oldCenterX / 640, list[0].CenterXTranslated);
    Equal(oldCenterY / 640, list[0].CenterYTranslated);
    Equal(region.Left + oldCenterX, list[0].ScreenCenterX);
    Equal(region.Top + oldCenterY, list[0].ScreenCenterY);
});

if (!string.Equals(Environment.GetEnvironmentVariable("AIMMY_SKIP_MODEL_INTEGRATION"), "1", StringComparison.Ordinal))
Check("actual YOLO11 and YOLO26 fixed/dynamic ONNX outputs", () =>
{
    DirectoryInfo? root = new(AppContext.BaseDirectory);
    while (root != null && !File.Exists(Path.Combine(root.FullName, "Aimmy2.csproj"))) root = root.Parent;
    True(root != null, "workspace root not found");
    string modelRoot = Path.Combine(root!.FullName, "bin", "Build", "bin", "models", "Slot1");
    foreach (string family in new[] { "YOLO11", "YOLO26" })
    {
        foreach (int size in new[] { 320, 416, 512, 640 })
        {
            string path = Path.Combine(modelRoot, $"best_{size}x{size}_fp32_{family}.onnx");
            True(File.Exists(path), "missing " + Path.GetFileName(path));
            using var session = OnnxModelSessionFactory.Load(path, "CPU", size);
            PipelineModelMetadata metadata = OnnxModelSessionFactory.Metadata(session);
            var resolved = metadata.ResolveSize(size);
            using var run = new RunOptions();
            Tensor<float> output = OnnxModelSessionFactory.Run(session,
                new float[3 * size * size], new CaptureTransform(new Rectangle(0, 0, size, size),
                    resolved.Width, resolved.Height, metadata.Options.Letterbox), run, .99f);
            True(output.Dimensions.Length == 3 && output.Dimensions[2] == 6,
                $"{family} {size} was not canonical [1,N,6]");
        }

        string dynamicPath = Path.Combine(modelRoot, $"best_dynamic_fp32_{family}.onnx");
        using var dynamicSession = OnnxModelSessionFactory.Load(dynamicPath, "CPU", 320);
        PipelineModelMetadata dynamicMetadata = OnnxModelSessionFactory.Metadata(dynamicSession);
        True(dynamicMetadata.Dynamic, family + " dynamic metadata was lost");
        foreach (int size in new[] { 320, 416, 512, 640 })
        {
            dynamicMetadata.Options.DynamicWidth = size;
            dynamicMetadata.Options.DynamicHeight = size;
            var resolved = dynamicMetadata.ResolveSize(size);
            using var run = new RunOptions();
            Tensor<float> output = OnnxModelSessionFactory.Run(dynamicSession,
                new float[3 * size * size], new CaptureTransform(new Rectangle(0, 0, size, size),
                    resolved.Width, resolved.Height, dynamicMetadata.Options.Letterbox), run, .99f);
            True(output.Dimensions.Length == 3 && output.Dimensions[2] == 6,
                $"dynamic {family} {size} was not canonical [1,N,6]");
        }
    }
});

const int benchmarkDetections = 300;
float[] benchmarkValues = new float[benchmarkDetections * 6];
for (int i = 0; i < benchmarkDetections; i++)
{
    int offset = i * 6;
    float x = 40 + i % 20 * 28;
    float y = 60 + i / 20 * 34;
    benchmarkValues[offset] = x;
    benchmarkValues[offset + 1] = y;
    benchmarkValues[offset + 2] = x + 24;
    benchmarkValues[offset + 3] = y + 48;
    benchmarkValues[offset + 4] = .75f;
    benchmarkValues[offset + 5] = i % 2;
}
Tensor<float> benchmarkTensor = new DenseTensor<float>(benchmarkValues, [1, benchmarkDetections, 6]);
List<Prediction> LegacyPostprocess()
{
    var decoded = new List<Prediction>(benchmarkDetections);
    for (int i = 0; i < benchmarkDetections; i++)
    {
        int offset = i * 6;
        float x1 = benchmarkValues[offset], y1 = benchmarkValues[offset + 1];
        float x2 = benchmarkValues[offset + 2], y2 = benchmarkValues[offset + 3];
        float confidence = benchmarkValues[offset + 4];
        int classId = (int)benchmarkValues[offset + 5];
        if (confidence < .5f) continue;
        float centerX = (x1 + x2) * .5f, centerY = (y1 + y2) * .5f;
        decoded.Add(new Prediction
        {
            Rectangle = new RectangleF(x1, y1, x2 - x1, y2 - y1),
            Confidence = confidence,
            ClassId = classId,
            ClassName = classes[classId],
            CenterXTranslated = centerX / 640,
            CenterYTranslated = centerY / 640,
            ScreenCenterX = centerX,
            ScreenCenterY = centerY
        });
    }
    var fov = decoded.Where(p => p.Rectangle.Left >= 80 && p.Rectangle.Right <= 560
        && p.Rectangle.Top >= 80 && p.Rectangle.Bottom <= 560).ToList();
    var preferred = fov.Where(p => PredictionFilter.IsHeadClass(p.ClassName)).ToList();
    float distance = float.MaxValue;
    foreach (Prediction prediction in preferred)
    {
        float dx = prediction.CenterX - 320, dy = prediction.CenterY - 320;
        float candidate = dx * dx + dy * dy;
        if (candidate < distance) distance = candidate;
    }
    return fov;
}
var decodedBuffer = new List<Prediction>(benchmarkDetections);
var aimBuffer = new List<Prediction>(benchmarkDetections);
void NewPostprocess()
{
    PredictionFilter.CreatePredictions(benchmarkTensor, capture, 640, 2, classes, .5f,
        "Best Confidence", decodedBuffer);
    PredictionFilter.FillAimCandidates(decodedBuffer, aimBuffer, 80, 560, 80, 560);
    _ = PredictionFilter.FindBestCandidate(aimBuffer, 320, 320, true, true);
}
for (int i = 0; i < 50; i++) { _ = LegacyPostprocess(); NewPostprocess(); }
const int benchmarkIterations = 1000;
GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
long beforeAllocationStart = GC.GetAllocatedBytesForCurrentThread();
var beforeWatch = Stopwatch.StartNew();
for (int i = 0; i < benchmarkIterations; i++) _ = LegacyPostprocess();
beforeWatch.Stop();
long beforeAllocated = GC.GetAllocatedBytesForCurrentThread() - beforeAllocationStart;
GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
long afterAllocationStart = GC.GetAllocatedBytesForCurrentThread();
var afterWatch = Stopwatch.StartNew();
for (int i = 0; i < benchmarkIterations; i++) NewPostprocess();
afterWatch.Stop();
long afterAllocated = GC.GetAllocatedBytesForCurrentThread() - afterAllocationStart;
results.Add(new
{
    Test = "postprocess microbenchmark 300 canonical detections",
    Status = "MEASURED",
    Iterations = benchmarkIterations,
    BeforeMsPerFrame = beforeWatch.Elapsed.TotalMilliseconds / benchmarkIterations,
    AfterMsPerFrame = afterWatch.Elapsed.TotalMilliseconds / benchmarkIterations,
    BeforeAllocatedBytesPerFrame = beforeAllocated / (double)benchmarkIterations,
    AfterAllocatedBytesPerFrame = afterAllocated / (double)benchmarkIterations
});

string reportDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "reports"));
Directory.CreateDirectory(reportDirectory);
File.WriteAllText(Path.Combine(reportDirectory, "prediction-pipeline-checks.json"),
    JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
return failed == 0 ? 0 : 1;

namespace Other
{
    internal static class LogManager
    {
        internal enum LogLevel { Warning }
        internal static void Log(LogLevel level, string text) => Console.WriteLine($"{level}: {text}");
    }
}
