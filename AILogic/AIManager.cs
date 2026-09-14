using AILogic;
using Aimmy2.Class;
using Class;
using InputLogic;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Newtonsoft.Json.Linq;
using Other;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using Visuality;
using static AILogic.MathUtil;
using static Other.LogManager;

namespace Aimmy2.AILogic
{
    public partial class AIManager : IDisposable
    {
        #region Variables

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private int _slot1ImageSize;
        private int _slot2ImageSize;
        public static int ActiveSlot { get; set; } = 1; // Default to Slot 1
        private static long _lastAimTargetTick = long.MinValue;
        public static bool HasRecentAimTarget(int maximumAgeMs = 120)
        {
            long seen = Interlocked.Read(ref _lastAimTargetTick);
            return seen != long.MinValue && Environment.TickCount64 - seen <= maximumAgeMs;
        }
        /*
         * Note: ActiveSlot is modified by WeaponSlotManager when keys are pressed.
         * MouseManager will read this to determine which config to use.
         */
        private readonly object _sizeLock = new object();
        private volatile bool _sizeChangePending = false;

        public void RequestSizeChange(int newSize, int slot)
        {
            if (newSize <= 0) throw new ArgumentOutOfRangeException(nameof(newSize));
            var metadata = GetModelMetadata(slot);
            if (metadata != null && (!metadata.Dynamic || metadata.Format == "TensorRT")) return;
            lock (_modelLock)
            {
                if (metadata != null)
                {
                    metadata.Options.DynamicWidth = newSize;
                    metadata.Options.DynamicHeight = newSize;
                    metadata.ResolveSize(newSize);
                }
                if (slot == 1) _slot1ImageSize = newSize;
                else _slot2ImageSize = newSize;
                _sizeChangePending = false;
            }
            RequestStickyAimReset();
        }

        // Dynamic properties instead of constants
        public int IMAGE_SIZE => Dictionary.sliderSettings.TryGetValue("Capture Size", out var capture) && Convert.ToInt32(capture) > 0
            ? Convert.ToInt32(capture) : ActiveSlot == 1 ? _slot1ImageSize : _slot2ImageSize;
        internal int GetSlotImageSize(int slot) => slot == 1 ? _slot1ImageSize : _slot2ImageSize;

        private void PublishSlotImageSize(int slot, int size, bool dynamicModel)
        {
            string key = slot == 1 ? "Slot 1 Image Size" : "Slot 2 Image Size";
            Dictionary.dropdownState[key] = size.ToString();
            MouseSensitivityProfiles.NotifyChanged();
            if (ActiveSlot == slot)
            {
                FovSettings.Synchronize(size);
                ImageSizeUpdated?.Invoke(size);
            }
            if (slot == 1) { Slot1IsDynamic = dynamicModel; Slot1FixedSize = size; }
            else { Slot2IsDynamic = dynamicModel; Slot2FixedSize = size; }
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "bin", "dropdown.cfg");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                global::Class.SaveDictionary.WriteJSON(Dictionary.dropdownState, path);
            }
            catch (Exception ex) { Log(LogLevel.Warning, $"Cannot save model image size: {ex.Message}"); }
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                var window = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
                window?.SettingsMenuControlInstance?.UpdateImageSizeDropdown((string)Dictionary.dropdownState[key], slot);
            }));
        }
        private int NUM_DETECTIONS { get; set; } = 8400; // Will be set dynamically for dynamic models
        private bool IsDynamicModel { get; set; } = false;
        private bool IsNmsFreeModel { get; set; } = false;
        
        private bool _slot1IsNmsFree = false;
        private bool _slot2IsNmsFree = false;
        private int _slot1NumDetections = 8400;
        private int _slot2NumDetections = 8400;
        private int _slot1FixedSize = 640;
        private int _slot2FixedSize = 640;
        private bool _slot1IsDynamic = false;
        private bool _slot2IsDynamic = false;

        public static bool Slot1IsDynamic { get; set; } = true;
        public static bool Slot2IsDynamic { get; set; } = true;
        public static bool CurrentModelIsDynamic { get; set; } = true;

        public static int Slot1FixedSize { get; private set; } = 640;
        public static int Slot2FixedSize { get; private set; } = 640;
        private int NUM_CLASSES { get; set; } = 1;
        private Dictionary<int, string> _modelClasses = new Dictionary<int, string>
        {
            { 0, "enemy" }
        };
        public Dictionary<int, string> ModelClasses => _modelClasses; // apparently this is better than making _modelClasses public
        public static event Action<Dictionary<int, string>>? ClassesUpdated;
        public static event Action<int>? ImageSizeUpdated;
        public static event Action<bool>? DynamicModelStatusChanged;

        private const int SAVE_FRAME_COOLDOWN_MS = 500;

        private DateTime lastSavedTime = DateTime.MinValue;
        private List<string>? _outputNames;
        private RectangleF LastDetectionBox;
        private KalmanPrediction kalmanPrediction;
        private WiseTheFoxPrediction wtfpredictionManager;
        private ConstantAccelerationPrediction caPrediction;

        private byte[]? _bitmapBuffer; // Reusable buffer for bitmap operations

        // Display-aware properties
        private int ScreenWidth => DisplayManager.ScreenWidth;
        private int ScreenHeight => DisplayManager.ScreenHeight;
        private int ScreenLeft => DisplayManager.ScreenLeft;
        private int ScreenTop => DisplayManager.ScreenTop;

        private readonly RunOptions? _modeloptions;
        private InferenceSession? _onnxModel; // Current Active Model
        private InferenceSession? _onnxModelSlot1;
        private InferenceSession? _onnxModelSlot2;

        private TensorRTEngine? _engineModel;
        private TensorRTEngine? _engineModelSlot1;
        private TensorRTEngine? _engineModelSlot2;
        private bool _isEngineModelSlot1 = false;
        private bool _isEngineModelSlot2 = false;
        private bool _isActiveSlotEngine = false;

        private readonly object _modelLock = new object();
        private volatile bool _isDisposed = false;

        private List<string>? _outputNamesSlot1;
        private List<string>? _outputNamesSlot2;


        public bool Slot1AimHead { get; set; } = true;
        public bool Slot2AimHead { get; set; } = true;

        private Dictionary<int, string> _modelClassesSlot1 = new Dictionary<int, string>{{ 0, "enemy" }};
        private Dictionary<int, string> _modelClassesSlot2 = new Dictionary<int, string>{{ 0, "enemy" }};


        private Thread? _aiLoopThread;
        private volatile bool _isAiLoopRunning;

        // For Auto-Labelling Data System
        private bool PlayerFound = false;

        // Store all predictions for overlay rendering
        private List<Prediction>? _allPredictions = null;
        private readonly List<Prediction> _predictionBuffer = new(512);
        private readonly List<Prediction> _aimCandidateBuffer = new(512);
        private Rectangle _currentDetectionBox;
        private Action? _pendingWgcOverlay;
        private int _wgcOverlayScheduled;

        // Sticky-Aim
        private readonly StickyAimSelector[] _stickyAimSelectors = [new(), new()];
        private readonly KalmanTargetTracker[] _kalmanTargetTrackers = [new(), new()];
        private readonly long[] _kalmanTrackIds = new long[2];
        private readonly int[] _kalmanResetGenerations = new int[2];
        private long _legacyPredictionTrackId;
        private int _legacyPredictionResetGeneration;
        private int _stickyAimResetGeneration;
        private long _logicalFrameId;
        private long _lastWgcMovementFrameTimestamp;
        private double? _currentWgcFrameDeltaSeconds;
        private bool _wasAimActive;

        private double CenterXTranslated = 0;
        private double CenterYTranslated = 0;

        // Benchmarking
        private int iterationCount = 0;
        private long totalTime = 0;

        private int detectedX { get; set; }
        private int detectedY { get; set; }

        public double AIConf = 0;
        private static int targetX, targetY;

        // Pre-calculated values - now dynamic
        private float _scaleX => ScreenWidth / (float)IMAGE_SIZE;
        private float _scaleY => ScreenHeight / (float)IMAGE_SIZE;

        // Tensor reuse (model inference)
        private DenseTensor<float>? _reusableTensor;
        private float[]? _reusableInputArray;
        private float[]? _tensorBackingArray;
        private List<NamedOnnxValue>? _reusableInputs;

        // Benchmarking
        private readonly Dictionary<string, BenchmarkData> _benchmarks = new();
        private readonly object _benchmarkLock = new();


        private readonly CaptureManager _captureManager = new();
        #endregion Variables

        #region Benchmarking

        private class BenchmarkData
        {
            public double TotalTime { get; set; }
            public int CallCount { get; set; }
            public double MinTime { get; set; } = double.MaxValue;
            public double MaxTime { get; set; }
            public double AverageTime => CallCount > 0 ? (double)TotalTime / CallCount : 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private IDisposable Benchmark(string name)
        {
            if (!(bool)Dictionary.toggleState["Debug Mode"] && name is not ("ModelInference" or "ScreenGrab" or "GetClosestPrediction")) return NoopBenchmark.Instance;
            return new BenchmarkScope(this, name);
        }

        private sealed class NoopBenchmark : IDisposable
        {
            public static readonly NoopBenchmark Instance = new();
            public void Dispose() { }
        }

        private class BenchmarkScope : IDisposable
        {
            private readonly AIManager _manager;
            private readonly string _name;
            private readonly Stopwatch _sw;
            private readonly string _context;

            public BenchmarkScope(AIManager manager, string name)
            {
                _manager = manager;
                _name = name;
                _context = manager.PerformanceContext();
                _sw = Stopwatch.StartNew();
            }

            public void Dispose()
            {
                _sw.Stop();
                bool emptyWgcPoll = _name == "ScreenGrab" && _manager._captureManager.LastCaptureWaitingForFrame;
                if (!emptyWgcPoll && _context == _manager.PerformanceContext())
                    _manager.RecordBenchmark(_name, _sw.Elapsed.TotalMilliseconds);
            }
        }

        private readonly Queue<(long Tick, string Name, double Ms)> _recentPerformance = new();
        private string _performanceContext = "";
        private long _performanceStart;
        private string PerformanceContext()
        {
            int slot = ActiveSlot;
            object? model = slot == 2 ? (object?)_onnxModelSlot2 ?? _engineModelSlot2 : (object?)_onnxModelSlot1 ?? _engineModelSlot1;
            return $"{Dictionary.dropdownState["Screen Capture Method"]}|{slot}|{(model == null ? 0 : RuntimeHelpers.GetHashCode(model))}|{IMAGE_SIZE}";
        }
        private void TrimPerformance(long now)
        {
            string context = PerformanceContext();
            if (_performanceContext != context)
            {
                _recentPerformance.Clear(); _performanceContext = context; _performanceStart = now;
            }
            while (_recentPerformance.TryPeek(out var item) && Stopwatch.GetElapsedTime(item.Tick, now).TotalSeconds >= 1)
                _recentPerformance.Dequeue();
        }
        public Dictionary<string, double> GetPerformanceSnapshot()
        {
            lock (_benchmarkLock)
            {
                long now = Stopwatch.GetTimestamp(); TrimPerformance(now);
                var result = _recentPerformance.GroupBy(x => x.Name).ToDictionary(x => x.Key, x => x.Average(v => v.Ms));
                double seconds = Math.Clamp(Stopwatch.GetElapsedTime(_performanceStart, now).TotalSeconds, .1, 1);
                result["InferenceFPS"] = _recentPerformance.Count(x => x.Name == "ModelInference") / seconds;
                result["TargetSwitches"] = _stickyAimSelectors[0].TargetSwitchCount + _stickyAimSelectors[1].TargetSwitchCount;
                return result;
            }
        }

        private void RecordBenchmark(string name, double elapsedMs)
        {
            lock (_benchmarkLock)
            {
                if (!_benchmarks.TryGetValue(name, out var data))
                {
                    data = new BenchmarkData();
                    _benchmarks[name] = data;
                }

                long tick = Stopwatch.GetTimestamp();
                TrimPerformance(tick);
                _recentPerformance.Enqueue((tick, name, elapsedMs));
                data.TotalTime += elapsedMs;
                data.CallCount++;
                data.MinTime = Math.Min(data.MinTime, elapsedMs);
                data.MaxTime = Math.Max(data.MaxTime, elapsedMs);
            }
        }

        public void PrintBenchmarks()
        {
            lock (_benchmarkLock)
            {
                var lines = new List<string>
                {
                    "=== AIManager Performance Benchmarks ==="
                };

                foreach (var kvp in _benchmarks.OrderBy(x => x.Key))
                {
                    var data = kvp.Value;
                    lines.Add($"{kvp.Key}: Avg={data.AverageTime:F2}ms, Min={data.MinTime}ms, Max={data.MaxTime}ms, Count={data.CallCount}");
                }

                lines.Add($"Overall FPS: {(iterationCount > 0 ? 1000.0 / (totalTime / (double)iterationCount) : 0):F2}");

                //File.WriteAllLines("AIManager_Benchmarks.txt", lines);

                Log(LogLevel.Info, string.Join(Environment.NewLine, lines));
            }
        }

        #endregion Benchmarking

        public Task Initialization { get; private set; }

        public AIManager(string modelPath, bool showLoadNotification = true)
        {
            // Initialize the cached image size
            _slot1ImageSize = int.Parse(Dictionary.dropdownState["Slot 1 Image Size"]);
            _slot2ImageSize = int.Parse(Dictionary.dropdownState["Slot 2 Image Size"]);

            // Load priority aiming settings from Dictionary
            Slot1AimHead = Dictionary.toggleState.TryGetValue("Slot 1 Priority Aiming", out var s1ah) ? (bool)s1ah : true;
            Slot2AimHead = Dictionary.toggleState.TryGetValue("Slot 2 Priority Aiming", out var s2ah) ? (bool)s2ah : true;

            // Initialize DXGI capture for current display
            if (Dictionary.dropdownState["Screen Capture Method"] == "DirectX")
            {
                try
                {
                    _captureManager.InitializeDxgiDuplication();
                }
                catch (Exception ex)
                {
                    Log(LogLevel.Error, $"Failed to initialize Screen capture via DirectX: {ex.Message}. Falling back to GDI+.");
                    Dictionary.dropdownState["Screen Capture Method"] = "GDI+";
                    SaveDictionary.WriteJSON(Dictionary.dropdownState, "bin\\dropdown.cfg");

                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        var mainWindow = System.Windows.Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
                        if (mainWindow?.uiManager?.D_ScreenCaptureMethod?.DropdownBox != null)
                        {
                            var dropdown = mainWindow.uiManager.D_ScreenCaptureMethod;
                            for (int i = 0; i < dropdown.DropdownBox.Items.Count; i++)
                            {
                                if ((dropdown.DropdownBox.Items[i] as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() == "GDI+")
                                {
                                    dropdown.DropdownBox.SelectedIndex = i;
                                    break;
                                }
                            }
                        }
                    });
                }
            }

            kalmanPrediction = new KalmanPrediction();
            wtfpredictionManager = new WiseTheFoxPrediction();
            caPrediction = new ConstantAccelerationPrediction();

            _modeloptions = new RunOptions();

            var sessionOptions = new SessionOptions
            {
                EnableCpuMemArena = true,
                EnableMemoryPattern = false,
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
                InterOpNumThreads = 1,
                IntraOpNumThreads = 4
            };

            // Attempt to load via DirectML (else fallback to CPU)
            Initialization = InitializeModel(sessionOptions, modelPath, showLoadNotification);
        }

        #region Models

        private async Task InitializeModel(SessionOptions sessionOptions, string modelPath, bool showLoadNotification)
        {
            using (Benchmark("ModelInitialization"))
            {
                try
                {
                    await LoadModelAsync(sessionOptions, modelPath, useDirectML: true, showLoadNotification);
                }
                catch (Exception ex)
                {
                    Log(LogLevel.Error, $"Error starting the model via DirectML: {ex.Message}\n\nFalling back to CPU, performance may be poor.", true);

                    try
                    {
                        await LoadModelAsync(sessionOptions, modelPath, useDirectML: false, showLoadNotification);
                    }
                    catch (Exception e)
                    {
                        Log(LogLevel.Error, $"Error starting the model via CPU: {e.Message}, you won't be able to aim assist at all.", true);
                    }
                }
            }
        }

        private static async Task<bool> ValidateTensorRTEngineSubprocess(string modelPath)
        {
            try
            {
                string? exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                {
                    return true;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = $"--validate-engine \"{modelPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    ErrorDialog = false
                };

                using (var process = Process.Start(startInfo))
                {
                    if (process != null)
                    {
                        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                        try { await process.WaitForExitAsync(timeout.Token); }
                        catch (OperationCanceledException)
                        {
                            process.Kill(entireProcessTree: true);
                            Log(LogLevel.Error, "Engine validation exceeded 45 seconds.");
                            return false;
                        }
                        return process.ExitCode == 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, $"Validation process failed to start: {ex.Message}");
            }
            return false;
        }

        public Task LoadModelAsync(string modelPath) => LoadModelAsync(new SessionOptions(), modelPath, useDirectML: true);

        public async Task LoadModelAsync(SessionOptions sessionOptions, string modelPath, bool useDirectML, bool showNotification = true)
        {
            try
            {
                bool isEngine = modelPath.EndsWith(".engine", StringComparison.OrdinalIgnoreCase) || 
                                modelPath.EndsWith(".trt", StringComparison.OrdinalIgnoreCase);

                if (isEngine)
                {
                    // Validate engine file first in a subprocess to avoid native crash on incompatible GPU architecture
                    bool isValid = await ValidateTensorRTEngineSubprocess(modelPath);
                    if (!isValid)
                    {
                        throw new Exception("Kiến trúc của GPU không tương thích với file engine đã chọn. Vui lòng chọn đúng file engine dành cho GPU này.");
                    }

                    TensorRTEngine newEngine;
                    await Dictionary.ModelLoadSemaphore.WaitAsync();
                    try
                    {
                        newEngine = await Task.Run(() => new TensorRTEngine(modelPath));
                    }
                    finally
                    {
                        Dictionary.ModelLoadSemaphore.Release();
                    }

                    lock (_modelLock)
                    {
                        if (_isDisposed)
                        {
                            newEngine.Dispose();
                            return;
                        }

                        var oldEngine = _engineModelSlot1;
                        var oldSession = _onnxModelSlot1;
                        bool wasActiveSlot = ActiveSlot == 1 || (oldSession == null && oldEngine == null);

                        _engineModelSlot1 = newEngine;
                        _isEngineModelSlot1 = true;
                        _onnxModelSlot1 = null;

                        if (wasActiveSlot)
                        {
                            _engineModel = _engineModelSlot1;
                            _onnxModel = null;
                            _isActiveSlotEngine = true;
                            ActiveSlot = 1;
                        }

                        int newSize = 640;
                        if (newEngine.InputDims.Length >= 4)
                        {
                            newSize = newEngine.InputDims[2];
                        }
                        _slot1ImageSize = newSize;

                        _slot1IsNmsFree = false;
                        _slot1NumDetections = 8400;
                        if (newEngine.OutputDims.Length >= 3)
                        {
                            if (newEngine.OutputDims[2] == 6)
                            {
                                _slot1NumDetections = newEngine.OutputDims[1];
                                _slot1IsNmsFree = true;
                            }
                            else
                            {
                                _slot1NumDetections = newEngine.OutputDims[2];
                            }
                        }

                        _slot1IsDynamic = false;
                        _slot1FixedSize = newSize;
                        PublishSlotImageSize(1, newSize, false);

                        if (wasActiveSlot)
                        {
                            ImageSizeUpdated?.Invoke(newSize);
                            NUM_DETECTIONS = _slot1NumDetections;
                            IsNmsFreeModel = _slot1IsNmsFree;
                            IsDynamicModel = false;
                            CurrentModelIsDynamic = false;
                            DynamicModelStatusChanged?.Invoke(false);
                        }

                        _modelClassesSlot1 = LoadClassesForEngine(modelPath, newEngine.OutputDims);
                        if (wasActiveSlot)
                        {
                            _modelClasses = _modelClassesSlot1;
                            NUM_CLASSES = _modelClasses.Count > 0 ? _modelClasses.Keys.Max() + 1 : 1;
                            ClassesUpdated?.Invoke(new Dictionary<int, string>(_modelClasses));
                        }

                        oldSession?.Dispose();
                        oldEngine?.Dispose();

                        _bitmapBuffer = new byte[3 * IMAGE_SIZE * IMAGE_SIZE];
                    }
                    Log(LogLevel.Info, $"Loaded (Slot 1) TensorRT Engine model: {Path.GetFileName(modelPath)} ({(_slot1IsNmsFree ? "NMS-Free" : "Standard")})", showNotification, 2000);
                }
                else
                {
                    

                    InferenceSession newSession;
                    await Dictionary.ModelLoadSemaphore.WaitAsync();
                    try
                    {
                        newSession = await Task.Run(() => OnnxModelSessionFactory.Load(modelPath, RequestedProvider(useDirectML), GetSlotImageSize(1)));
                    }
                    finally
                    {
                        Dictionary.ModelLoadSemaphore.Release();
                    }
                    
                    lock (_modelLock)
                    {
                        if (_isDisposed)
                        {
                            newSession.Dispose();
                            return;
                        }

                        var oldEngine = _engineModelSlot1;
                        var oldSession = _onnxModelSlot1;
                        var oldOutputNames = _outputNamesSlot1;
                        bool wasActiveSlot = ActiveSlot == 1 || (oldSession == null && oldEngine == null);

                        _onnxModelSlot1 = newSession;
                        _isEngineModelSlot1 = false;
                        _engineModelSlot1 = null;
                        _outputNamesSlot1 = new List<string>(newSession.OutputMetadata.Keys);

                        if (wasActiveSlot)
                        {
                            _onnxModel = _onnxModelSlot1;
                            _engineModel = null;
                            _isActiveSlotEngine = false;
                            _outputNames = _outputNamesSlot1;
                            ActiveSlot = 1;
                        }

                        LoadClasses(1);

                        // Validate the onnx model output shape before disposing the previous valid session.
                        if (!ValidateOnnxShape(1, showNotification))
                        {
                            newSession.Dispose();
                            _onnxModelSlot1 = oldSession;
                            _outputNamesSlot1 = oldOutputNames;

                            if (wasActiveSlot)
                            {
                                _onnxModel = oldSession;
                                _outputNames = oldOutputNames;
                            }

                            if (oldSession != null)
                            {
                                LoadClasses(1);
                                SetActiveSlot(1);
                            }
                            else
                            {
                                _modelClassesSlot1 = new Dictionary<int, string> { { 0, "enemy" } };
                                _modelClasses = _modelClassesSlot1;
                            }

                            return;
                        }

                        oldSession?.Dispose();
                        oldEngine?.Dispose();

                        // Pre-allocate bitmap buffer
                        _bitmapBuffer = new byte[3 * IMAGE_SIZE * IMAGE_SIZE];
                    }
                }
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, $"Error loading the model: {ex.Message}", true);
                return;
            }

            finally { sessionOptions.Dispose(); }

            // Begin the loop
            if (!_isAiLoopRunning)
            {
                _isAiLoopRunning = true;
                lock (_sizeLock) { _sizeChangePending = false; } // Clear flag
                _aiLoopThread = new Thread(AiLoop)
                {
                    IsBackground = true,
                    Priority = ThreadPriority.AboveNormal // Higher priority for AI thread
                };
                _aiLoopThread.Start();
            }
            return;
        }

        private static string RequestedProvider(bool directML = true) =>
            OnnxProviderPreference.Resolve(Dictionary.dropdownState, Dictionary.toggleState, directML);

        public ModelMetadata? GetModelMetadata(int slot)
        {
            lock (_modelLock)
            {
                var session = slot == 2 ? _onnxModelSlot2 : _onnxModelSlot1;
                return session != null ? OnnxModelSessionFactory.Metadata(session) : (slot == 2 ? _engineModelSlot2 : _engineModelSlot1)?.Metadata;
            }
        }

        // UI polling must never wait behind a GPU inference or model load.
        public bool TryGetModelMetadata(int slot, out ModelMetadata? metadata)
        {
            metadata = null;
            if (!Monitor.TryEnter(_modelLock)) return false;
            try { metadata = GetModelMetadata(slot); return true; }
            finally { Monitor.Exit(_modelLock); }
        }

        public void ConfigureDimensions(int slot, int width, int height, int captureSize)
        {
            lock (_modelLock)
            {
                var session = slot == 2 ? _onnxModelSlot2 : _onnxModelSlot1;
                if (session == null) throw new NotSupportedException("Chọn model ONNX. Engine TensorRT dùng profile lúc nạp engine.");
                var metadata = OnnxModelSessionFactory.Metadata(session);
                int oldWidth = metadata.Options.DynamicWidth, oldHeight = metadata.Options.DynamicHeight;
                try
                {
                    if (!metadata.Dynamic && (width != metadata.Input.Shape[metadata.WidthAxis] || height != metadata.Input.Shape[metadata.HeightAxis]))
                        throw new NotSupportedException("Input tĩnh: nhập đúng chiều rộng/cao hiển thị trong Model Inspector.");
                    metadata.Options.DynamicWidth = width; metadata.Options.DynamicHeight = height;
                    var size = metadata.ResolveSize(width);
                    using var run = new RunOptions();
                    OnnxModelSessionFactory.Run(session, new float[checked(size.Width * size.Height * 3)],
                        new CaptureTransform(new Rectangle(0, 0, size.Width, size.Height), size.Width, size.Height, false), run, 1);
                    string path = metadata.ModelPath + ".aimmy.json";
                    if (File.Exists(path) && !File.Exists(path + ".bak")) File.Copy(path, path + ".bak", false);
                    string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        File.WriteAllText(temporary, Newtonsoft.Json.JsonConvert.SerializeObject(metadata.Options, Newtonsoft.Json.Formatting.Indented));
                        File.Move(temporary, path, true);
                    }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                    if (slot == 1) _slot1ImageSize = size.Width; else _slot2ImageSize = size.Width;
                    Dictionary.sliderSettings["Capture Size"] = captureSize;
                    _allPredictions = null;
                    RequestStickyAimReset();
                    PublishSlotImageSize(slot, size.Width, metadata.Dynamic);
                    FovSettings.Synchronize(IMAGE_SIZE);
                    global::Class.SaveDictionary.WriteJSON(Dictionary.sliderSettings, Path.Combine(AppContext.BaseDirectory, "bin", "configs", "Default.cfg"));
                }
                catch { metadata.Options.DynamicWidth = oldWidth; metadata.Options.DynamicHeight = oldHeight; throw; }
            }
        }

        private bool ValidateOnnxShape(int slot, bool showNotification = true)
        {
            var session = slot == 2 ? _onnxModelSlot2 : _onnxModelSlot1;
            if (session == null) return false;
            try
            {
                var metadata = OnnxModelSessionFactory.Metadata(session);
                var size = metadata.ResolveSize(GetSlotImageSize(slot));
                bool dynamic = metadata.Dynamic;
                if (slot == 1) { _slot1ImageSize = size.Width; _slot1IsDynamic = dynamic; _slot1FixedSize = size.Width; _slot1IsNmsFree = true; _slot1NumDetections = 0; }
                else { _slot2ImageSize = size.Width; _slot2IsDynamic = dynamic; _slot2FixedSize = size.Width; _slot2IsNmsFree = true; _slot2NumDetections = 0; }
                PublishSlotImageSize(slot, size.Width, dynamic);
                if (ActiveSlot == slot)
                {
                    IsDynamicModel = CurrentModelIsDynamic = dynamic;
                    IsNmsFreeModel = true; NUM_DETECTIONS = 0;
                    DynamicModelStatusChanged?.Invoke(dynamic);
                }
                Log(LogLevel.Info, $"Slot {slot}: {metadata.Backend}, {metadata.Input.DataType}, {size.Width} × {size.Height}, {(dynamic ? "kích thước động" : "kích thước cố định")}", showNotification);
                return true;
            }
            catch (Exception ex) { Log(LogLevel.Error, $"Model không tương thích: {ex.Message}", showNotification); return false; }
        }

        private void LoadClasses(int slot = 0)
        {
            var model = (slot == 2) ? _onnxModelSlot2 : _onnxModelSlot1;
            if (model == null && slot != 2) model = _onnxModel;
            if (model == null) return;

            var targetDict = (slot == 2) ? _modelClassesSlot2 : _modelClassesSlot1;
            targetDict.Clear();

            try
            {
                var metadata = model.ModelMetadata;

                if (metadata != null && 
                    metadata.CustomMetadataMap.TryGetValue("names", out string? value) &&
                    !string.IsNullOrEmpty(value))
                {
                    JObject data = JObject.Parse(value);
                    if (data != null && data.Type == JTokenType.Object)
                    {
                        foreach (var item in data)
                        {
                            if (int.TryParse(item.Key, out int classId) && item.Value.Type == JTokenType.String)
                            {
                                targetDict[classId] = item.Value.ToString();
                            }
                        }
                        
                        // Update NUM_CLASSES if this is the active slot
                        if ((slot == 2 && ActiveSlot == 2) || (slot != 2 && ActiveSlot == 1))
                        {
                             NUM_CLASSES = targetDict.Count > 0 ? targetDict.Keys.Max() + 1 : 1;
                             _modelClasses = targetDict;
                        }

                        Log(LogLevel.Info, $"Loaded {targetDict.Count} class(es) from model metadata (Slot {(slot == 0 ? 1 : slot)}): {data.ToString(Newtonsoft.Json.Formatting.None)}", false);
                    }
                    else
                    {
                        Log(LogLevel.Error, "Model metadata 'names' field is not a valid JSON object.", true);
                    }
                }
                else
                {
                    Log(LogLevel.Error, "Model metadata does not contain 'names' field for classes.", true);
                }
                
                if ((slot == 2 && ActiveSlot == 2) || (slot != 2 && ActiveSlot == 1))
                {
                    ClassesUpdated?.Invoke(new Dictionary<int, string>(targetDict));
                }
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, $"Error loading classes: {ex.Message}", true);
            }
        }

        private Dictionary<int, string> LoadClassesForEngine(string enginePath, int[] outputDims)
        {
            var classes = new Dictionary<int, string>();
            
            // 1. Try JSON file of the same name (e.g. mymodel.json next to mymodel.engine)
            string jsonPath = Path.ChangeExtension(enginePath, ".json");
            if (File.Exists(jsonPath))
            {
                try
                {
                    string jsonContent = File.ReadAllText(jsonPath);
                    JToken token = JToken.Parse(jsonContent);
                    if (token is JObject obj)
                    {
                        if (obj.TryGetValue("names", out var namesProp) && namesProp != null)
                        {
                            if (namesProp is JObject namesObj)
                            {
                                foreach (var item in namesObj)
                                {
                                    if (int.TryParse(item.Key, out int classId))
                                    {
                                        classes[classId] = item.Value?.ToString() ?? $"Class_{classId}";
                                    }
                                }
                            }
                            else if (namesProp is JArray namesArr)
                            {
                                for (int i = 0; i < namesArr.Count; i++)
                                {
                                    classes[i] = namesArr[i].ToString();
                                }
                            }
                        }
                        else
                        {
                            foreach (var item in obj)
                            {
                                if (int.TryParse(item.Key, out int classId))
                                {
                                    classes[classId] = item.Value?.ToString() ?? $"Class_{classId}";
                                }
                            }
                        }
                    }
                    else if (token is JArray arr)
                    {
                        for (int i = 0; i < arr.Count; i++)
                        {
                            classes[i] = arr[i].ToString();
                        }
                    }
                    
                    if (classes.Count > 0)
                    {
                        Log(LogLevel.Info, $"Loaded {classes.Count} class(es) from JSON: {jsonPath}", false);
                        return classes;
                    }
                }
                catch (Exception ex)
                {
                    Log(LogLevel.Warning, $"Failed to parse classes JSON {jsonPath}: {ex.Message}");
                }
            }

            // 2. Try TXT file of the same name (e.g. mymodel.txt containing one class per line)
            string txtPath = Path.ChangeExtension(enginePath, ".txt");
            if (File.Exists(txtPath))
            {
                try
                {
                    var lines = File.ReadAllLines(txtPath)
                                    .Select(l => l.Trim())
                                    .Where(l => !string.IsNullOrEmpty(l))
                                    .ToList();
                    for (int i = 0; i < lines.Count; i++)
                    {
                        classes[i] = lines[i];
                    }
                    
                    if (classes.Count > 0)
                    {
                        Log(LogLevel.Info, $"Loaded {classes.Count} class(es) from TXT: {txtPath}", false);
                        return classes;
                    }
                }
                catch (Exception ex)
                {
                    Log(LogLevel.Warning, $"Failed to read classes TXT {txtPath}: {ex.Message}");
                }
            }

            // 3. Try ONNX file of the same name (e.g. mymodel.onnx to extract metadata)
            string onnxPath = Path.ChangeExtension(enginePath, ".onnx");
            if (File.Exists(onnxPath))
            {
                try
                {
                    var opt = new SessionOptions();
                    opt.AppendExecutionProvider_CPU();
                    using (var session = new InferenceSession(onnxPath, opt))
                    {
                        var metadata = session.ModelMetadata;
                        if (metadata != null && 
                            metadata.CustomMetadataMap.TryGetValue("names", out string? value) &&
                            !string.IsNullOrEmpty(value))
                        {
                            JObject data = JObject.Parse(value);
                            if (data != null && data.Type == JTokenType.Object)
                            {
                                foreach (var item in data)
                                {
                                    if (int.TryParse(item.Key, out int classId) && item.Value?.Type == JTokenType.String)
                                    {
                                        classes[classId] = item.Value.ToString();
                                    }
                                }
                            }
                        }
                    }
                    
                    if (classes.Count > 0)
                    {
                        Log(LogLevel.Info, $"Loaded {classes.Count} class(es) from ONNX metadata: {onnxPath}", false);
                        return classes;
                    }
                }
                catch (Exception ex)
                {
                    Log(LogLevel.Warning, $"Failed to read classes from ONNX metadata {onnxPath}: {ex.Message}");
                }
            }

            // 4. Try classes.txt in the same directory
            string dir = Path.GetDirectoryName(enginePath) ?? "";
            string commonTxtPath = Path.Combine(dir, "classes.txt");
            if (File.Exists(commonTxtPath))
            {
                try
                {
                    var lines = File.ReadAllLines(commonTxtPath)
                                    .Select(l => l.Trim())
                                    .Where(l => !string.IsNullOrEmpty(l))
                                    .ToList();
                    for (int i = 0; i < lines.Count; i++)
                    {
                        classes[i] = lines[i];
                    }
                    
                    if (classes.Count > 0)
                    {
                        Log(LogLevel.Info, $"Loaded {classes.Count} class(es) from classes.txt: {commonTxtPath}", false);
                        return classes;
                    }
                }
                catch (Exception ex)
                {
                    Log(LogLevel.Warning, $"Failed to read classes.txt {commonTxtPath}: {ex.Message}");
                }
            }

            // 5. Default mapping based on output tensor dimensions
            int calculatedClasses = 1;
            if (outputDims != null)
            {
                if (outputDims.Length == 3)
                {
                    if (outputDims[2] == 6)
                    {
                        calculatedClasses = 2; // Default to 2 classes to support head priority aiming
                    }
                    else if (outputDims[1] > 4)
                    {
                        calculatedClasses = outputDims[1] - 4;
                    }
                }
            }

            if (calculatedClasses == 2)
            {
                classes[0] = "head";
                classes[1] = "body";
                Log(LogLevel.Info, $"No class names file found. Defaulting to 2 classes: 0=head, 1=body", false);
            }
            else
            {
                for (int i = 0; i < calculatedClasses; i++)
                {
                    classes[i] = i == 0 ? "enemy" : $"Class_{i}";
                }
                Log(LogLevel.Info, $"No class names file found. Defaulted to {calculatedClasses} generic classes (e.g. 0=enemy)", false);
            }

            return classes;
        }

        public async Task LoadSecondaryModel(string modelPath, bool showNotification = true)
        {
             bool isEngine = modelPath.EndsWith(".engine", StringComparison.OrdinalIgnoreCase) || 
                             modelPath.EndsWith(".trt", StringComparison.OrdinalIgnoreCase);

             if (isEngine)
             {
                 // Validate engine file first in a subprocess to avoid native crash on incompatible GPU architecture
                 bool isValid = await ValidateTensorRTEngineSubprocess(modelPath);
                 if (!isValid)
                 {
                     Log(LogLevel.Error, "Kiến trúc của GPU không tương thích với file engine đã chọn. Vui lòng chọn đúng file engine dành cho GPU này.", showNotification);
                     return;
                 }

                 TensorRTEngine? newEngine = null;
                 await Dictionary.ModelLoadSemaphore.WaitAsync();
                 try
                 {
                     newEngine = await Task.Run(() => new TensorRTEngine(modelPath));
                 }
                 catch (Exception ex)
                 {
                     Log(LogLevel.Error, $"Error loading Slot 2 TensorRT model: {ex.Message}", true);
                     return;
                 }
                 finally
                 {
                     Dictionary.ModelLoadSemaphore.Release();
                 }

                 lock (_modelLock)
                 {
                     if (_isDisposed)
                     {
                         newEngine?.Dispose();
                         return;
                     }

                     var oldEngine = _engineModelSlot2;
                     var oldSession = _onnxModelSlot2;
                     bool wasActiveSlot = ActiveSlot == 2 || (oldSession == null && oldEngine == null);

                     _engineModelSlot2 = newEngine;
                     _isEngineModelSlot2 = true;
                     _onnxModelSlot2 = null;

                     if (wasActiveSlot)
                     {
                         _engineModel = _engineModelSlot2;
                         _onnxModel = null;
                         _isActiveSlotEngine = true;
                         ActiveSlot = 2;
                     }

                     int newSize = 640;
                     if (newEngine != null && newEngine.InputDims.Length >= 4)
                     {
                         newSize = newEngine.InputDims[2];
                     }
                     _slot2ImageSize = newSize;

                      _slot2IsNmsFree = false;
                      _slot2NumDetections = 8400;
                      if (newEngine != null && newEngine.OutputDims.Length >= 3)
                      {
                          if (newEngine.OutputDims[2] == 6)
                          {
                              _slot2NumDetections = newEngine.OutputDims[1];
                              _slot2IsNmsFree = true;
                          }
                          else
                          {
                              _slot2NumDetections = newEngine.OutputDims[2];
                          }
                      }

                     _slot2IsDynamic = false;
                     _slot2FixedSize = newSize;
                     PublishSlotImageSize(2, newSize, false);

                     if (wasActiveSlot)
                     {
                         ImageSizeUpdated?.Invoke(newSize);
                         NUM_DETECTIONS = _slot2NumDetections;
                         IsNmsFreeModel = _slot2IsNmsFree;
                         IsDynamicModel = false;
                         CurrentModelIsDynamic = false;
                         DynamicModelStatusChanged?.Invoke(false);
                     }

                     _modelClassesSlot2 = LoadClassesForEngine(modelPath, newEngine.OutputDims);
                     if (wasActiveSlot)
                     {
                         _modelClasses = _modelClassesSlot2;
                         NUM_CLASSES = _modelClasses.Count > 0 ? _modelClasses.Keys.Max() + 1 : 1;
                         ClassesUpdated?.Invoke(new Dictionary<int, string>(_modelClasses));
                     }

                     oldSession?.Dispose();
                     oldEngine?.Dispose();
                 }
                 Log(LogLevel.Info, $"Loaded (Slot 2) TensorRT Engine model: {Path.GetFileName(modelPath)} ({(_slot2IsNmsFree ? "NMS-Free" : "Standard")})", showNotification, 2000);
             }
             else
             {
                 using var sessionOptions = new SessionOptions
                 {
                     EnableCpuMemArena = true,
                     EnableMemoryPattern = false,
                     GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                     ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
                     InterOpNumThreads = 1,
                     IntraOpNumThreads = 4
                 };

                 InferenceSession? newSession = null;
                 await Dictionary.ModelLoadSemaphore.WaitAsync();
                 try
                 {
                     string provider = RequestedProvider();
                     newSession = await Task.Run(() => OnnxModelSessionFactory.Load(modelPath, provider, GetSlotImageSize(2)));
                 }
                 finally
                 {
                     Dictionary.ModelLoadSemaphore.Release();
                 }

                 lock (_modelLock)
                 {
                     if (_isDisposed)
                     {
                         newSession?.Dispose();
                         return;
                     }

                     var oldEngine = _engineModelSlot2;
                     var oldSession = _onnxModelSlot2;
                     var oldOutputNames = _outputNamesSlot2;
                     _onnxModelSlot2 = newSession;
                     _isEngineModelSlot2 = false;
                     _engineModelSlot2 = null;

                     if (_onnxModelSlot2 != null)
                     {
                         _outputNamesSlot2 = new List<string>(_onnxModelSlot2.OutputMetadata.Keys);
                         if (!ValidateOnnxShape(2, showNotification))
                         {
                             _onnxModelSlot2.Dispose();
                             _onnxModelSlot2 = oldSession;
                             _outputNamesSlot2 = oldOutputNames;

                             if (ActiveSlot == 2)
                             {
                                 _onnxModel = oldSession;
                                 _outputNames = oldOutputNames;
                                 _isActiveSlotEngine = false;
                                 if (oldSession != null)
                                 {
                                     LoadClasses(2);
                                     SetActiveSlot(2);
                                 }
                             }

                             return;
                         }
                     }

                     oldSession?.Dispose();
                     oldEngine?.Dispose();

                     if (ActiveSlot == 2)
                     {
                         _onnxModel = _onnxModelSlot2;
                         _engineModel = null;
                         _isActiveSlotEngine = false;
                         _outputNames = _outputNamesSlot2;
                     }
                 }

                 LoadClasses(2);
                 Log(LogLevel.Info, $"Secondary Model (Slot 2) Loaded: {Path.GetFileName(modelPath)}", showNotification);
             }
        }

        public void SetActiveSlot(int slot)
        {
            lock (_modelLock)
            {
                if (_isDisposed) return;

                if (slot == 1)
                {
                    if (_isEngineModelSlot1 && _engineModelSlot1 != null)
                    {
                        _engineModel = _engineModelSlot1;
                        _onnxModel = null;
                        _modelClasses = _modelClassesSlot1;
                        _isActiveSlotEngine = true;
                        ActiveSlot = 1;

                        IsNmsFreeModel = _slot1IsNmsFree;
                        NUM_DETECTIONS = _slot1NumDetections;
                        IsDynamicModel = _slot1IsDynamic;
                        CurrentModelIsDynamic = _slot1IsDynamic;

                        NUM_CLASSES = _modelClasses.Count > 0 ? _modelClasses.Keys.Max() + 1 : 1;
                        Slot1AimHead = Dictionary.toggleState.TryGetValue("Slot 1 Priority Aiming", out var s1ah) ? (bool)s1ah : true;

                        ClassesUpdated?.Invoke(new Dictionary<int, string>(_modelClasses));
                        DynamicModelStatusChanged?.Invoke(IsDynamicModel);
                    }
                    else if (_onnxModelSlot1 != null) {
                        _onnxModel = _onnxModelSlot1;
                        _engineModel = null;
                        _isActiveSlotEngine = false;
                        _modelClasses = _modelClassesSlot1;
                        _outputNames = _outputNamesSlot1;
                        ActiveSlot = 1;
                        
                        IsNmsFreeModel = _slot1IsNmsFree;
                        NUM_DETECTIONS = _slot1NumDetections;
                        IsDynamicModel = _slot1IsDynamic;
                        CurrentModelIsDynamic = _slot1IsDynamic;

                        NUM_CLASSES = _modelClasses.Count > 0 ? _modelClasses.Keys.Max() + 1 : 1;
                        Slot1AimHead = Dictionary.toggleState.TryGetValue("Slot 1 Priority Aiming", out var s1ah) ? (bool)s1ah : true;

                        ClassesUpdated?.Invoke(new Dictionary<int, string>(_modelClasses));
                        DynamicModelStatusChanged?.Invoke(IsDynamicModel);
                    }
                }
                else if (slot == 2)
                {
                    if (_isEngineModelSlot2 && _engineModelSlot2 != null)
                    {
                        _engineModel = _engineModelSlot2;
                        _onnxModel = null;
                        _modelClasses = _modelClassesSlot2;
                        _isActiveSlotEngine = true;
                        ActiveSlot = 2;

                        IsNmsFreeModel = _slot2IsNmsFree;
                        NUM_DETECTIONS = _slot2NumDetections;
                        IsDynamicModel = _slot2IsDynamic;
                        CurrentModelIsDynamic = _slot2IsDynamic;

                        NUM_CLASSES = _modelClasses.Count > 0 ? _modelClasses.Keys.Max() + 1 : 1;
                        Slot2AimHead = Dictionary.toggleState.TryGetValue("Slot 2 Priority Aiming", out var s2ah) ? (bool)s2ah : true;

                        ClassesUpdated?.Invoke(new Dictionary<int, string>(_modelClasses));
                        DynamicModelStatusChanged?.Invoke(IsDynamicModel);
                    }
                    else if (_onnxModelSlot2 != null) {
                        _onnxModel = _onnxModelSlot2;
                        _engineModel = null;
                        _isActiveSlotEngine = false;
                        _modelClasses = _modelClassesSlot2;
                        _outputNames = _outputNamesSlot2;
                        ActiveSlot = 2;

                        IsNmsFreeModel = _slot2IsNmsFree;
                        NUM_DETECTIONS = _slot2NumDetections;
                        IsDynamicModel = _slot2IsDynamic;
                        CurrentModelIsDynamic = _slot2IsDynamic;

                        NUM_CLASSES = _modelClasses.Count > 0 ? _modelClasses.Keys.Max() + 1 : 1;
                        Slot2AimHead = Dictionary.toggleState.TryGetValue("Slot 2 Priority Aiming", out var s2ah) ? (bool)s2ah : true;

                        ClassesUpdated?.Invoke(new Dictionary<int, string>(_modelClasses));
                        DynamicModelStatusChanged?.Invoke(IsDynamicModel);
                    }
                    else
                    {
                        Log(LogLevel.Warning, "Attempted to switch to Slot 2 but no model loaded. Keeping Slot 1.");
                    }
                }
            }
            RequestStickyAimReset();
            FovSettings.Synchronize(IMAGE_SIZE);
            ImageSizeUpdated?.Invoke(IMAGE_SIZE);
        }

        #endregion Models

        #region AI

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ShouldPredict() => AimProcessingDecisions.ShouldRunPrediction(
            Dictionary.toggleState["Show Detected Player"], Dictionary.toggleState["Constant AI Tracking"],
            InputBindingManager.IsHoldingBinding("Aim Keybind"), InputBindingManager.IsHoldingBinding("Second Aim Keybind"));

        private static bool ShouldProcess() => AimProcessingDecisions.ShouldProcessFrame(
            Dictionary.toggleState["Aim Assist"], Dictionary.toggleState["Show Detected Player"], Dictionary.toggleState["Auto Trigger"]);

        private static bool IsAimActivationActive() => Dictionary.toggleState["Aim Assist"]
            && (Dictionary.toggleState["Constant AI Tracking"]
                || InputBindingManager.IsHoldingBinding("Aim Keybind")
                || InputBindingManager.IsHoldingBinding("Second Aim Keybind"));

        private void AiLoop()
        {
            Stopwatch stopwatch = new();
            // Resolve this inside the loop to avoid race conditions with initialization
            DetectedPlayerWindow? DetectedPlayerOverlay = null;

            while (_isAiLoopRunning)
            {
                int fpsLimit = Dictionary.sliderSettings.TryGetValue("AI FPS Limit", out var fps) ? Math.Max(0, Convert.ToInt32(fps)) : 0;
                if (fpsLimit > 0 && stopwatch.IsRunning)
                {
                    double remaining = 1000.0 / fpsLimit - stopwatch.Elapsed.TotalMilliseconds;
                    if (remaining >= 1) Thread.Sleep((int)remaining);
                }
                // Check for pending size changes at the start of each iteration
                lock (_sizeLock)
                {
                    if (_sizeChangePending)
                    {
                        // Avoid a busy spin while shutdown is pending.
                        Thread.Sleep(1);
                        continue;
                    }
                }

                stopwatch.Restart();

                // Handle any pending display changes
                _captureManager.HandlePendingDisplayChanges();

                using (Benchmark("AILoopIteration"))
                {
                    try
                    {
                        UpdateFOV();

                        bool aimActive = IsAimActivationActive();
                        if (_wasAimActive && !aimActive) RequestStickyAimReset();
                        _wasAimActive = aimActive;

                        if (ShouldProcess())
                        {
                            // Try to get overlay if we don't have it yet
                            if (DetectedPlayerOverlay == null)
                            {
                                DetectedPlayerOverlay = Dictionary.DetectedPlayerOverlay;
                            }

                            if (ShouldPredict())
                            {
                                Prediction? closestPrediction;
                                using (Benchmark("GetClosestPrediction"))
                                {
                                    closestPrediction = GetClosestPredictionSync();
                                }

                                if ((string)Dictionary.dropdownState["Screen Capture Method"] == "WGC"
                                    && _captureManager.LastCaptureWaitingForFrame)
                                {
                                    // No new observation: do not infer/move again from a stale frame.
                                    // Back off only while WGC has nothing available, not an FPS cap.
                                    Thread.Sleep(1);
                                    continue;
                                }
                                // Update overlay logic - decoupled from whether we have a closest prediction
                                if (Dictionary.toggleState["Show Detected Player"] && DetectedPlayerOverlay != null)
                                {
                                    using (Benchmark("UpdateOverlay"))
                                    {
                                        // UpdateOverlay now handles its own internal checks and null closestPrediction
                                        UpdateOverlay(DetectedPlayerOverlay, closestPrediction);
                                    }
                                }

                                if (closestPrediction == null)
                                {
                                    // Only disable if we actually don't have ANY predictions to show
                                    if ((_allPredictions == null || _allPredictions.Count == 0) && DetectedPlayerOverlay != null)
                                    {
                                        DisableOverlay(DetectedPlayerOverlay);
                                    }
                                }
                                else
                                {
                                    Interlocked.Exchange(ref _lastAimTargetTick, Environment.TickCount64);
                                    using (Benchmark("AutoTrigger"))
                                    {
                                        AutoTrigger();
                                    }

                                    using (Benchmark("CalculateCoordinates"))
                                    {
                                        // CalculateCoordinates still handles aiming logic
                                        CalculateCoordinates(DetectedPlayerOverlay, closestPrediction, _scaleX, _scaleY);
                                    }

                                    using (Benchmark("HandleAim"))
                                    {
                                        HandleAim(closestPrediction);
                                    }

                                    totalTime += stopwatch.ElapsedMilliseconds;
                                    iterationCount++;
                                }
                            }
                            else
                            {
                                // Processing so we are at the ready but not holding right/click.
                                Thread.Sleep(1);
                            }
                        }
                        else
                        {
                             // No work to do—sleep briefly to free up CPU
                             Thread.Sleep(1);
                        }
                    }
                    catch (ObjectDisposedException) { /* Model disposed */ }
                    catch (Exception ex)
                    {
                        Log(LogLevel.Error, $"AI Loop Error: {ex.Message}");
                    }
                }

                stopwatch.Stop();
            }
        }

        #region AI Loop Functions

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AutoTrigger()
        {
            // NEW LOGIC: Only trigger if the toggle is ON AND the specific Auto Click Keybind is held.
            // Holding the Aim Keybind will NOT trigger the shot anymore.
            
            bool isAutoClickHeld = InputBindingManager.IsHoldingBinding("Auto Click Keybind");
            bool shouldTrigger = Dictionary.toggleState["Auto Trigger"] && isAutoClickHeld;

            if (!shouldTrigger)
            {
                CheckSprayRelease();
                return;
            }


            if (Dictionary.toggleState["Spray Mode"])
            {
                MouseManager.DoTriggerClick(LastDetectionBox);
                return;
            }


            if (Dictionary.toggleState["Cursor Check"])
            {
                var mousePos = WinAPICaller.GetCursorPosition();

                if (!DisplayManager.IsPointInCurrentDisplay(new System.Windows.Point(mousePos.X, mousePos.Y)))
                {
                    return;
                }

                if (LastDetectionBox.Contains(mousePos.X, mousePos.Y))
                {
                    MouseManager.DoTriggerClick(LastDetectionBox);
                }
            }
            else
            {
                MouseManager.DoTriggerClick();
            }

            if (!Dictionary.toggleState["Aim Assist"] || !Dictionary.toggleState["Show Detected Player"]) return;

        }
        private void CheckSprayRelease()
        {
            if (!Dictionary.toggleState["Spray Mode"]) return;

            bool isAutoClickHeld = InputBindingManager.IsHoldingBinding("Auto Click Keybind");
            bool shouldTrigger = Dictionary.toggleState["Auto Trigger"] && isAutoClickHeld;

            if (!shouldTrigger)
            {
                MouseManager.ResetSprayState();
            }
        }

        private void UpdateFOV()
        {
            if (Dictionary.dropdownState["Detection Area Type"] == "Closest to Mouse" && Dictionary.toggleState["FOV"])
            {
                var mousePosition = WinAPICaller.GetCursorPosition();

                // Check if mouse is on the current display
                if (!DisplayManager.IsPointInCurrentDisplay(new System.Windows.Point(mousePosition.X, mousePosition.Y)))
                {
                    // Mouse is on a different display - don't update FOV position
                    return;
                }

                // Translate mouse position relative to current display
                var displayRelativeX = mousePosition.X - DisplayManager.ScreenLeft;
                var displayRelativeY = mousePosition.Y - DisplayManager.ScreenTop;

                Application.Current?.Dispatcher?.BeginInvoke(() =>
                {
                    if (Dictionary.FOVWindow != null && Dictionary.FOVWindow.FOVStrictEnclosure != null)
                    {
                        Dictionary.FOVWindow.FOVStrictEnclosure.Margin = new Thickness(
                            Convert.ToInt16(displayRelativeX / WinAPICaller.scalingFactorX) - 320, // this is based off the window size, not the size of the model -whip
                            Convert.ToInt16(displayRelativeY / WinAPICaller.scalingFactorY) - 320, 0, 0);
                    }
                });
            }
        }

        private void QueueOverlayUpdate(Action update)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted || _isDisposed) return;
            if ((string)Dictionary.dropdownState["Screen Capture Method"] != "WGC")
            {
                Interlocked.Exchange(ref _pendingWgcOverlay, null);
                dispatcher.BeginInvoke(update);
                return;
            }
            Interlocked.Exchange(ref _pendingWgcOverlay, update);
            ScheduleWgcOverlay();
        }

        private void ScheduleWgcOverlay()
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted || _isDisposed) return;
            if (Interlocked.CompareExchange(ref _wgcOverlayScheduled, 1, 0) != 0) return;
            dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
            {
                try
                {
                    var update = Interlocked.Exchange(ref _pendingWgcOverlay, null);
                    if (!_isDisposed && (string)Dictionary.dropdownState["Screen Capture Method"] == "WGC") update?.Invoke();
                }
                finally
                {
                    Interlocked.Exchange(ref _wgcOverlayScheduled, 0);
                    if (Volatile.Read(ref _pendingWgcOverlay) != null) ScheduleWgcOverlay();
                }
            }));
        }

        private void DisableOverlay(DetectedPlayerWindow DetectedPlayerOverlay)
        {
            var showAIConfidence = Dictionary.toggleState["Show AI Confidence"];
            var showTracers = Dictionary.toggleState["Show Tracers"];
            var showDetectedPlayer = Dictionary.toggleState["Show Detected Player"];

            QueueOverlayUpdate(() =>
            {
                if (showDetectedPlayer && DetectedPlayerOverlay != null)
                {
                    if (showAIConfidence)
                    {
                        DetectedPlayerOverlay!.DetectedPlayerConfidence.Opacity = 0;
                    }

                    if (showTracers)
                    {
                        DetectedPlayerOverlay!.DetectedTracers.Opacity = 0;
                    }

                    DetectedPlayerOverlay!.DetectedPlayerFocus.Opacity = 0;
                    
                    // Clear all detection boxes
                    DetectedPlayerOverlay!.ClearAllDetections();
                }
            });
        }

        private void UpdateOverlay(DetectedPlayerWindow DetectedPlayerOverlay, Prediction closestPrediction)
        {
            var lastBoxSnapshot = LastDetectionBox;
            var scalingFactorX = WinAPICaller.scalingFactorX;
            var scalingFactorY = WinAPICaller.scalingFactorY;

            // Convert screen coordinates to display-relative coordinates
            var displayRelativeX = lastBoxSnapshot.X - DisplayManager.ScreenLeft;
            var displayRelativeY = lastBoxSnapshot.Y - DisplayManager.ScreenTop;

            // Calculate center position in display-relative coordinates
            var centerX = Convert.ToInt16(displayRelativeX / scalingFactorX) + (lastBoxSnapshot.Width / 2.0);
            var centerY = Convert.ToInt16(displayRelativeY / scalingFactorY);

            // Snapshot variables for BeginInvoke to ensure thread-safety
            var currentAIConf = AIConf;
            var showAIConfidence = Dictionary.toggleState["Show AI Confidence"];
            var showTracers = Dictionary.toggleState["Show Tracers"];
            var tracerPosition = Dictionary.dropdownState["Tracer Position"];
            var opacity = Dictionary.sliderSettings["Opacity"];
            var allPredsSnapshot = _allPredictions != null ? new List<Prediction>(_allPredictions) : null;
            var currentDetBoxSnapshot = _currentDetectionBox;
            bool isEngineSnapshot = _isActiveSlotEngine;
            bool isSingleClassSnapshot = NUM_CLASSES == 1;
            bool isWgcSnapshot = Dictionary.dropdownState["Screen Capture Method"] == "WGC";
            int capturedDisplayLeft = DisplayManager.ScreenLeft;
            int capturedDisplayTop = DisplayManager.ScreenTop;

            QueueOverlayUpdate(() =>
            {
                if (isWgcSnapshot && Dictionary.dropdownState["Screen Capture Method"] != "WGC") return;
                if (showAIConfidence && closestPrediction != null)
                {
                    DetectedPlayerOverlay.DetectedPlayerConfidence.Opacity = 1;
                    DetectedPlayerOverlay.DetectedPlayerConfidence.Content = $"{closestPrediction.ClassName}: {Math.Round((currentAIConf * 100), 2)}%";

                    var labelEstimatedHalfWidth = DetectedPlayerOverlay.DetectedPlayerConfidence.ActualWidth / 2.0;
                    DetectedPlayerOverlay.DetectedPlayerConfidence.Margin = new Thickness(
                        centerX - labelEstimatedHalfWidth,
                        centerY - DetectedPlayerOverlay.DetectedPlayerConfidence.ActualHeight - 2, 0, 0);
                }
                else
                {
                    DetectedPlayerOverlay.DetectedPlayerConfidence.Opacity = 0;
                }
                var showTracerLines = showTracers && closestPrediction != null;
                DetectedPlayerOverlay.DetectedTracers.Opacity = showTracerLines ? 1 : 0;
                if (showTracerLines && closestPrediction != null)
                {
                    var boxTop = centerY;
                    var boxBottom = centerY + lastBoxSnapshot.Height;
                    var boxHorizontalCenter = centerX;
                    var boxVerticalCenter = centerY + (lastBoxSnapshot.Height / 2.0);
                    var boxLeft = centerX - (lastBoxSnapshot.Width / 2.0);
                    var boxRight = centerX + (lastBoxSnapshot.Width / 2.0);

                    switch (tracerPosition)
                    {
                        case "Top":
                            DetectedPlayerOverlay.DetectedTracers.X2 = boxHorizontalCenter;
                            DetectedPlayerOverlay.DetectedTracers.Y2 = boxTop;
                            break;

                        case "Bottom":
                            DetectedPlayerOverlay.DetectedTracers.X2 = boxHorizontalCenter;
                            DetectedPlayerOverlay.DetectedTracers.Y2 = boxBottom;
                            break;

                        case "Middle":
                            var screenHorizontalCenter = DisplayManager.ScreenWidth / (2.0 * WinAPICaller.scalingFactorX);
                            if (boxHorizontalCenter < screenHorizontalCenter)
                            {
                                // if the box is on the left half of the screen, aim for the right-middle of the box
                                DetectedPlayerOverlay.DetectedTracers.X2 = boxRight;
                                DetectedPlayerOverlay.DetectedTracers.Y2 = boxVerticalCenter;
                            }
                            else
                            {
                                // if the box is on the right half, aim for the left-middle
                                DetectedPlayerOverlay.DetectedTracers.X2 = boxLeft;
                                DetectedPlayerOverlay.DetectedTracers.Y2 = boxVerticalCenter;
                            }
                            break;

                        default:
                            // default to the bottom-center if the setting is unrecognized
                            DetectedPlayerOverlay.DetectedTracers.X2 = boxHorizontalCenter;
                            DetectedPlayerOverlay.DetectedTracers.Y2 = boxBottom;
                            break;
                    }
                }

                DetectedPlayerOverlay.Opacity = opacity;

                // Draw all detected predictions with different colors
                if (allPredsSnapshot != null && allPredsSnapshot.Count > 0)
                {
                    // Apply NMS to remove overlapping boxes
                    var filteredPredictions = allPredsSnapshot; // ModelOutputAdapter already applied the correct postprocess once.
                    
                    var detectionList = new List<(string ClassName, double X, double Y, double Width, double Height, double Confidence)>();
                    
                    foreach (var prediction in filteredPredictions)
                    {
                        // Use same logic as UpdateDetectionBox
                        // Translate from model coordinates to screen coordinates
                        float translatedXMin = prediction.Rectangle.X + currentDetBoxSnapshot.Left;
                        float translatedYMin = prediction.Rectangle.Y + currentDetBoxSnapshot.Top;
                        
                        // Convert to display-relative coordinates
                        var predDisplayX = translatedXMin - (isWgcSnapshot ? capturedDisplayLeft : DisplayManager.ScreenLeft);
                        var predDisplayY = translatedYMin - (isWgcSnapshot ? capturedDisplayTop : DisplayManager.ScreenTop);
                        
                        // Apply scaling
                        var predScreenX = predDisplayX / scalingFactorX;
                        var predScreenY = predDisplayY / scalingFactorY;
                        var predWidth = prediction.Rectangle.Width / scalingFactorX;
                        var predHeight = prediction.Rectangle.Height / scalingFactorY;
                        
                        detectionList.Add((
                            prediction.ClassName ?? "Unknown",
                            predScreenX,
                            predScreenY,
                            predWidth,
                            predHeight,
                            prediction.Confidence
                        ));
                    }
                    
                    DetectedPlayerOverlay.DrawAllDetections(detectionList, showAIConfidence, isEngineSnapshot, isSingleClassSnapshot);
                }
                else if (Dictionary.dropdownState["Screen Capture Method"] == "WGC")
                {
                    DetectedPlayerOverlay.DrawAllDetections(new List<(string ClassName, double X, double Y, double Width, double Height, double Confidence)>(), showAIConfidence, isEngineSnapshot, isSingleClassSnapshot);
                }

                // Hide the old single-box overlay since we now draw all boxes via Canvas
                DetectedPlayerOverlay.DetectedPlayerFocus.Opacity = 0;
            });
        }

        private void CalculateCoordinates(DetectedPlayerWindow DetectedPlayerOverlay, Prediction closestPrediction, float scaleX, float scaleY)
        {
            // Legacy aim-response space: sensitivity profiles were calibrated with
            // screen/capture gain. These are not the physical coordinates used by ESP.
            AIConf = closestPrediction.Confidence;

            if (Dictionary.toggleState["Aim Assist"] || Dictionary.DetectedPlayerOverlay != null)
            {
                // We don't call UpdateOverlay here anymore, it's called directly in AiLoop for better responsiveness and ESP
                if (!Dictionary.toggleState["Aim Assist"]) return;
            }

            double YOffset = Dictionary.sliderSettings[ActiveSlot == 1 ? "Slot 1 Y Offset (Up/Down)" : "Y Offset (Up/Down)"];
            double XOffset = Dictionary.sliderSettings[ActiveSlot == 1 ? "Slot 1 X Offset (Left/Right)" : "X Offset (Left/Right)"];

            double YOffsetPercentage = Dictionary.sliderSettings[ActiveSlot == 1 ? "Slot 1 Y Offset (%)" : "Y Offset (%)"];
            double XOffsetPercentage = Dictionary.sliderSettings[ActiveSlot == 1 ? "Slot 1 X Offset (%)" : "X Offset (%)"];

            var rect = closestPrediction.Rectangle;
            
            bool useXPercent = Dictionary.toggleState[ActiveSlot == 1 ? "Slot 1 X Axis Percentage Adjustment" : "X Axis Percentage Adjustment"];
            bool useYPercent = Dictionary.toggleState[ActiveSlot == 1 ? "Slot 1 Y Axis Percentage Adjustment" : "Y Axis Percentage Adjustment"];

            if (useXPercent)
            {
                detectedX = (int)((rect.X + (rect.Width * (XOffsetPercentage / 100))) * scaleX);
            }
            else
            {
                detectedX = (int)((rect.X + rect.Width / 2) * scaleX + XOffset);
            }

            if (useYPercent)
            {
                detectedY = (int)((rect.Y + rect.Height - (rect.Height * (YOffsetPercentage / 100))) * scaleY + YOffset);
            }
            else
            {
                detectedY = CalculateDetectedY(scaleY, YOffset, closestPrediction);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CalculateDetectedY(float scaleY, double YOffset, Prediction closestPrediction)
        {
            var rect = closestPrediction.Rectangle;
            float yBase = rect.Y;
            float yAdjustment = 0;

            switch (Dictionary.dropdownState["Aiming Boundaries Alignment"])
            {
                case "Center":
                    yAdjustment = rect.Height / 2;
                    break;

                case "Top":
                    // yBase is already at the top
                    break;

                case "Bottom":
                    yAdjustment = rect.Height;
                    break;
            }

            return (int)((yBase + yAdjustment) * scaleY + YOffset);
        }

        private void HandleAim(Prediction closestPrediction)
        {
            if (Dictionary.toggleState["Aim Assist"] &&
                (Dictionary.toggleState["Constant AI Tracking"] ||
                 Dictionary.toggleState["Aim Assist"] && InputBindingManager.IsHoldingBinding("Aim Keybind") ||
                 Dictionary.toggleState["Aim Assist"] && InputBindingManager.IsHoldingBinding("Second Aim Keybind")))
            {
                if (Dictionary.toggleState.TryGetValue("Enable Kalman Filter", out var enabled)
                    && Convert.ToBoolean(enabled) && Dictionary.toggleState["Sticky Aim"])
                {
                    if (!TryGetKalmanAimPoint(closestPrediction, detectedX, detectedY, out int filteredX, out int filteredY))
                        return;
                    MoveCrosshairForCurrentFrame(filteredX, filteredY);
                }
                else if (Dictionary.toggleState["Predictions"])
                {
                    HandlePredictions(kalmanPrediction, closestPrediction, detectedX, detectedY);
                }
                else
                {
                    MoveCrosshairForCurrentFrame(detectedX, detectedY);
                }
            }
        }

        private bool TryGetKalmanAimPoint(Prediction target, int rawX, int rawY, out int aimX, out int aimY)
        {
            aimX = rawX;
            aimY = rawY;
            int index = Math.Clamp(ActiveSlot - 1, 0, 1);
            KalmanTargetTracker tracker = _kalmanTargetTrackers[index];
            int resetGeneration = Volatile.Read(ref _stickyAimResetGeneration);
            if (_kalmanResetGenerations[index] != resetGeneration)
            {
                tracker.Reset();
                _kalmanTrackIds[index] = 0;
                _kalmanResetGenerations[index] = resetGeneration;
            }
            long trackId = target.TargetTrackId;
            if (trackId <= 0)
            {
                tracker.Reset();
                _kalmanTrackIds[index] = 0;
                return true;
            }

            if (_kalmanTrackIds[index] != trackId)
            {
                tracker.Reset();
                _kalmanTrackIds[index] = trackId;
            }

            long now = Stopwatch.GetTimestamp();
            long observationTimestamp = target.FrameTimestamp > 0 ? target.FrameTimestamp : now;
            double frameAgeMilliseconds = Math.Max(0d,
                (now - observationTimestamp) * 1000d / Stopwatch.Frequency);
            double maximumAge = Dictionary.sliderSettings.TryGetValue("Maximum Frame Age", out var ageValue)
                ? Math.Max(0d, Convert.ToDouble(ageValue)) : 150d;
            if (frameAgeMilliseconds > maximumAge)
            {
                tracker.Reset();
                return false;
            }

            double smoothness = Dictionary.sliderSettings.TryGetValue("Kalman Smoothness", out var smoothnessValue)
                ? Convert.ToDouble(smoothnessValue) : 55d;
            tracker.Configure(smoothness);
            int maximumMissingFrames = Dictionary.sliderSettings.TryGetValue("Maximum Missing Frames", out var missingValue)
                ? Math.Max(0, Convert.ToInt32(missingValue)) : 3;
            bool accepted = target.IsSynthetic
                ? tracker.MarkMissing(now, maximumMissingFrames)
                : tracker.Update(target.ScreenCenterX, target.ScreenCenterY, observationTimestamp, target.Confidence);
            if (!accepted) return false;

            double filteredX = tracker.X;
            double filteredY = tracker.Y;
            bool predictionEnabled = Dictionary.toggleState["Predictions"]
                && tracker.ObservationCount >= 3 && tracker.Confidence >= 0.35d;
            if (predictionEnabled)
            {
                double baseLeadMilliseconds = Dictionary.sliderSettings.TryGetValue("Prediction Time", out var leadValue)
                    ? Math.Clamp(Convert.ToDouble(leadValue), 0d, 150d) : 35d;
                double speed = Math.Sqrt(tracker.VelocityX * tracker.VelocityX + tracker.VelocityY * tracker.VelocityY);
                double confidenceFactor = Math.Clamp((tracker.Confidence - 0.35d) / 0.65d, 0d, 1d);
                double leadSeconds = speed < 15d ? 0d
                    : Math.Clamp((baseLeadMilliseconds + frameAgeMilliseconds) / 1000d, 0d, 0.15d) * confidenceFactor;
                double configuredDistance = Dictionary.sliderSettings.TryGetValue("Maximum Prediction Distance", out var distanceValue)
                    ? Math.Max(0d, Convert.ToDouble(distanceValue)) : 0d;
                double maximumDistance = configuredDistance > 0d ? configuredDistance
                    : Math.Max(target.Rectangle.Width, target.Rectangle.Height) * 0.75d;
                (filteredX, filteredY) = tracker.GetPredictedPosition(leadSeconds, maximumDistance);
            }

            // Keep the custom response curve intact: apply only the filtered physical
            // screen-space offset through the same capture-to-response gain.
            aimX = (int)Math.Round(rawX + (filteredX - target.ScreenCenterX) * _scaleX);
            aimY = (int)Math.Round(rawY + (filteredY - target.ScreenCenterY) * _scaleY);
            return true;
        }

        private void HandlePredictions(KalmanPrediction kalmanPrediction, Prediction closestPrediction, int detectedX, int detectedY)
        {
            int resetGeneration = Volatile.Read(ref _stickyAimResetGeneration);
            if (_legacyPredictionResetGeneration != resetGeneration
                || closestPrediction.TargetTrackId > 0 && _legacyPredictionTrackId != closestPrediction.TargetTrackId)
            {
                kalmanPrediction.Reset();
                wtfpredictionManager.Reset();
                ShalloePredictionV2.Reset();
                caPrediction.Reset();
                _legacyPredictionResetGeneration = resetGeneration;
                _legacyPredictionTrackId = closestPrediction.TargetTrackId;
            }
            var predictionMethod = Dictionary.dropdownState["Prediction Method"];
            switch (predictionMethod)
            {
                case "Kalman Filter":
                    KalmanPrediction.Detection detection = new()
                    {
                        X = detectedX,
                        Y = detectedY,
                        Timestamp = DateTime.UtcNow
                    };

                    kalmanPrediction.UpdateKalmanFilter(detection);
                    var predictedPosition = kalmanPrediction.GetKalmanPosition();

                    MoveCrosshairForCurrentFrame(predictedPosition.X, predictedPosition.Y);
                    break;

                case "Shall0e's Prediction":
                    // Update position (calculates velocity internally)
                    ShalloePredictionV2.UpdatePosition(detectedX, detectedY);

                    // Get predicted position
                    MoveCrosshairForCurrentFrame(ShalloePredictionV2.GetSPX(), ShalloePredictionV2.GetSPY());
                    break;

                case "wisethef0x's EMA Prediction":
                    WiseTheFoxPrediction.WTFDetection wtfdetection = new()
                    {
                        X = detectedX,
                        Y = detectedY,
                        Timestamp = DateTime.UtcNow
                    };

                    wtfpredictionManager.UpdateDetection(wtfdetection);
                    var wtfpredictedPosition = wtfpredictionManager.GetEstimatedPosition();

                    // Use both predicted X and Y
                    MoveCrosshairForCurrentFrame(wtfpredictedPosition.X, wtfpredictedPosition.Y);
                    break;

                case "Constant Acceleration":
                    caPrediction.Update(detectedX, detectedY);
                    var (caX, caY) = caPrediction.GetPrediction();
                    MoveCrosshairForCurrentFrame(caX, caY);
                    break;
            }
        }

        private Prediction? GetClosestPredictionSync(bool useMousePosition = true)
        {
            //whats these variables for? - taylor 
            //int adjustedTargetX, adjustedTargetY;
            int activeSlot;
            int imageSize;
            int numDetections;
            bool isNmsFreeModel;
            int numClasses;
            IReadOnlyDictionary<int, string> modelClasses;
            int modelIdentity;

            lock (_modelLock)
            {
                if (_isDisposed) return null;
                if (_isActiveSlotEngine)
                {
                    if (_engineModel == null) return null;
                }
                else
                {
                    if (_onnxModel == null || _outputNames == null) return null;
                }

                activeSlot = ActiveSlot;
                imageSize = IMAGE_SIZE;
                numDetections = NUM_DETECTIONS;
                isNmsFreeModel = IsNmsFreeModel;
                numClasses = NUM_CLASSES;
                modelClasses = _modelClasses;
                object activeModel = (object?)_onnxModel ?? _engineModel!;
                modelIdentity = RuntimeHelpers.GetHashCode(activeModel);
            }

            string captureMethod = Dictionary.dropdownState["Screen Capture Method"];

            var mouse = WinAPICaller.GetCursorPosition();
            Rectangle detectionBox = CaptureTargetSelector.SelectDetectionBox(
                Dictionary.dropdownState["Detection Area Type"], imageSize,
                new Rectangle(DisplayManager.ScreenLeft, DisplayManager.ScreenTop, DisplayManager.ScreenWidth, DisplayManager.ScreenHeight),
                new System.Drawing.Point(mouse.X, mouse.Y),
                DisplayManager.IsPointInCurrentDisplay(new System.Windows.Point(mouse.X, mouse.Y)));
            targetX = detectionBox.Left + detectionBox.Width / 2;
            targetY = detectionBox.Top + detectionBox.Height / 2;
            _currentDetectionBox = detectionBox; // Store for overlay rendering

            Bitmap? frame = null;
            long frameId = 0;
            long frameTimestamp = 0;
            bool collectData = Dictionary.toggleState.TryGetValue("Collect Data While Playing", out var cd) && (bool)cd;
            bool useDirectX = captureMethod == "DirectX";

            if (useDirectX && !collectData)
            {
                // Fast path: direct copy from DX mapped VRAM to float array!
                if (_reusableInputArray == null || _reusableInputArray.Length != 3 * imageSize * imageSize)
                {
                    _reusableInputArray = new float[3 * imageSize * imageSize];
                }
                
                using (Benchmark("ScreenGrab"))
                {
                    bool success = _captureManager.CaptureAndConvertDirectX(detectionBox, _reusableInputArray, imageSize, Dictionary.toggleState["Third Person Support"]);
                    if (!success) return null;
                }
                frameId = Interlocked.Increment(ref _logicalFrameId);
                frameTimestamp = Stopwatch.GetTimestamp();
            }
            else
            {
                // WGC writes directly into the reusable tensor unless image collection needs a Bitmap.
                if (_reusableInputArray == null || _reusableInputArray.Length != 3 * imageSize * imageSize)
                    _reusableInputArray = new float[3 * imageSize * imageSize];
                bool useWgcTensor = !collectData && captureMethod == "WGC";
                using (Benchmark("ScreenGrab"))
                {
                    frame = _captureManager.ScreenGrab(detectionBox, useWgcTensor ? _reusableInputArray : null);
                }

                if (frame == null && !_captureManager.LastCaptureConverted)
                {
                    if (_captureManager.LastCaptureWaitingForFrame) return null;
                    if (captureMethod == "WGC")
                        _allPredictions = null; // Do not draw old model coordinates against a new ROI.
                    return null;
                }

                if (captureMethod == "WGC")
                {
                    frameId = _captureManager.LastCaptureFrameId;
                    frameTimestamp = _captureManager.LastCaptureFrameTimestamp;
                }
                else
                {
                    frameId = Interlocked.Increment(ref _logicalFrameId);
                    frameTimestamp = Stopwatch.GetTimestamp();
                }

                if (frame != null)
                using (Benchmark("BitmapToFloatArray"))
                {
                    if (_reusableInputArray == null || _reusableInputArray.Length != 3 * imageSize * imageSize)
                    {
                        _reusableInputArray = new float[3 * imageSize * imageSize];
                    }
                    BitmapToFloatArrayInPlace(frame, _reusableInputArray, imageSize);
                }
            }

            IDisposableReadOnlyCollection<DisposableNamedOnnxValue>? results = null;
            Tensor<float>? outputTensor = null;

            try
            {
                if (_isActiveSlotEngine)
                {
                    lock (_modelLock)
                    {
                        if (_engineModel == null || _isDisposed || ActiveSlot != activeSlot || IMAGE_SIZE != imageSize ||
                            NUM_DETECTIONS != numDetections || IsNmsFreeModel != isNmsFreeModel || NUM_CLASSES != numClasses)
                        {
                            return null;
                        }

                        using (Benchmark("ModelInference"))
                        {
                            outputTensor = _engineModel.RunDetections(_reusableInputArray, detectionBox,
                                (float)Dictionary.sliderSettings[activeSlot == 2 ? "Slot 2 AI Minimum Confidence" : "AI Minimum Confidence"] / 100f);
                        }
                    }
                }
                else
                {
                    lock (_modelLock)
                    {
                        if (_onnxModel == null || _isDisposed || ActiveSlot != activeSlot || IMAGE_SIZE != imageSize) return null;
                        var metadata = OnnxModelSessionFactory.Metadata(_onnxModel);
                        var size = metadata.ResolveSize(GetSlotImageSize(activeSlot));
                        var transform = new CaptureTransform(detectionBox, size.Width, size.Height, metadata.Options.Letterbox);
                        using (Benchmark("ModelInference"))
                            outputTensor = OnnxModelSessionFactory.Run(_onnxModel, _reusableInputArray, transform, _modeloptions,
                                (float)Dictionary.sliderSettings[activeSlot == 2 ? "Slot 2 AI Minimum Confidence" : "AI Minimum Confidence"] / 100f);
                    }
                }

                if (outputTensor == null)
                {
                    Log(LogLevel.Error, "Model inference returned null output tensor.", true, 2000);
                    if (frame != null) SaveFrame(frame);
                    return null;
                }

                float fovSize = (float)Dictionary.sliderSettings["FOV Size"];
                float fovMinX = (imageSize - fovSize) * 0.5f;
                float fovMaxX = (imageSize + fovSize) * 0.5f;
                float fovMinY = (imageSize - fovSize) * 0.5f;
                float fovMaxY = (imageSize + fovSize) * 0.5f;
                float minConfidence = (float)Dictionary.sliderSettings[activeSlot == 2
                    ? "Slot 2 AI Minimum Confidence" : "AI Minimum Confidence"] / 100f;
                string targetClassKey = activeSlot == 1 ? "Slot 1 Target Class" : "Slot 2 Target Class";
                string selectedClass = Dictionary.dropdownState[targetClassKey];
                Prediction? finalTarget;

                using (Benchmark("Postprocess"))
                {
                    PredictionFilter.CreatePredictions(outputTensor, detectionBox, imageSize, numClasses,
                        modelClasses, minConfidence, selectedClass, _predictionBuffer,
                        PredictionTensorLayout.CanonicalNmsFree, frameId: frameId, frameTimestamp: frameTimestamp);
                    _allPredictions = _predictionBuffer;

                    PredictionFilter.FillAimCandidates(_predictionBuffer, _aimCandidateBuffer,
                        fovMinX, fovMaxX, fovMinY, fovMaxY);
                    bool priorityEnabled = modelClasses.Count > 1 && IsPriorityAimingEnabled(activeSlot);
                    bool prioritizeHead = priorityEnabled && ShouldPrioritizeHead(activeSlot);
                    Prediction? bestCandidate = PredictionFilter.FindBestCandidate(_aimCandidateBuffer,
                        imageSize * 0.5f, imageSize * 0.5f, priorityEnabled, prioritizeHead);

                    bool aimActive = IsAimActivationActive();
                    double lockDuration = Dictionary.sliderSettings.TryGetValue("Target Lock Duration", out var configuredLock)
                        ? Convert.ToDouble(configuredLock) : 0;
                    long processingTimestamp = Stopwatch.GetTimestamp();
                    int captureIdentity = HashCode.Combine(captureMethod, Dictionary.dropdownState["Detection Area Type"],
                        ScreenLeft, ScreenTop, ScreenWidth, ScreenHeight,
                        WinAPICaller.scalingFactorX, WinAPICaller.scalingFactorY);
                    var context = new StickyAimContext(activeSlot, modelIdentity, captureMethod, captureIdentity, detectionBox,
                        imageSize, Volatile.Read(ref _stickyAimResetGeneration), frameId, frameTimestamp, processingTimestamp);
                    var settings = new StickyAimSettings(Dictionary.toggleState["Sticky Aim"], aimActive,
                        (float)Dictionary.sliderSettings["Sticky Aim Threshold"], minConfidence, lockDuration,
                        AllowSyntheticTargetOnMiss: captureMethod != "WGC",
                        MaxFramesWithoutTarget: Dictionary.sliderSettings.TryGetValue("Maximum Missing Frames", out var missingFrames)
                            ? Math.Max(0, Convert.ToInt32(missingFrames)) : 3,
                        MaxWgcFrameAgeMilliseconds: Dictionary.sliderSettings.TryGetValue("Maximum Frame Age", out var frameAge)
                            ? Math.Max(0d, Convert.ToDouble(frameAge)) : 150d);
                    finalTarget = _stickyAimSelectors[activeSlot - 1].SelectTarget(settings, context,
                        bestCandidate, _aimCandidateBuffer);
                }

                UpdateFrameTiming(captureMethod, frameTimestamp);
                if (finalTarget != null)
                {
                    UpdateDetectionBox(finalTarget, detectionBox);
                    if (frame != null) SaveFrame(frame, finalTarget);
                    return finalTarget;
                }

                if (frame != null) SaveFrame(frame);
                return null;
            }
            finally
            {
                results?.Dispose();
            }
        }

        private static int ParseKeyToVirtualKeyCode(string keyStr)
        {
            if (string.IsNullOrEmpty(keyStr) || string.Equals(keyStr, "None", StringComparison.OrdinalIgnoreCase))
                return 0;

            // Handle common modifier names
            if (string.Equals(keyStr, "Shift", StringComparison.OrdinalIgnoreCase)) return 16; // VK_SHIFT
            if (string.Equals(keyStr, "Control", StringComparison.OrdinalIgnoreCase)) return 17; // VK_CONTROL
            if (string.Equals(keyStr, "Alt", StringComparison.OrdinalIgnoreCase)) return 18; // VK_MENU

            // Handle custom name mappings
            if (string.Equals(keyStr, "Left Shift", StringComparison.OrdinalIgnoreCase) || string.Equals(keyStr, "LShiftKey", StringComparison.OrdinalIgnoreCase)) return 160; // VK_LSHIFT
            if (string.Equals(keyStr, "Right Shift", StringComparison.OrdinalIgnoreCase) || string.Equals(keyStr, "RShiftKey", StringComparison.OrdinalIgnoreCase)) return 161; // VK_RSHIFT
            if (string.Equals(keyStr, "Left Ctrl", StringComparison.OrdinalIgnoreCase) || string.Equals(keyStr, "LControlKey", StringComparison.OrdinalIgnoreCase)) return 162; // VK_LCONTROL
            if (string.Equals(keyStr, "Right Ctrl", StringComparison.OrdinalIgnoreCase) || string.Equals(keyStr, "RControlKey", StringComparison.OrdinalIgnoreCase)) return 163; // VK_RCONTROL

            // Try standard enum parse
            if (System.Enum.TryParse<System.Windows.Forms.Keys>(keyStr, true, out var key))
            {
                return (int)key;
            }

            return 0;
        }

        private static bool ReadBoolSetting(string key, bool fallback)
        {
            if (!Dictionary.toggleState.TryGetValue(key, out var value) || value == null)
                return fallback;

            try
            {
                return Convert.ToBoolean(value);
            }
            catch
            {
                return fallback;
            }
        }

        private bool IsPriorityAimingEnabled(int slot)
        {
            string key = slot == 1 ? "Slot 1 Priority Aiming" : "Slot 2 Priority Aiming";
            bool fallback = slot == 1 ? Slot1AimHead : Slot2AimHead;
            return ReadBoolSetting(key, fallback);
        }

        private static string GetPriorityBindingId(int slot) => slot == 1 ? "Slot 1 Priority Key" : "Slot 2 Priority Key";

        private static string GetPriorityKey(int slot)
        {
            string bindingId = GetPriorityBindingId(slot);
            return Dictionary.bindingSettings.TryGetValue(bindingId, out var key) ? key?.ToString() ?? "None" : "None";
        }

        private static bool IsPriorityKeyHeld(int slot, string key)
        {
            string bindingId = GetPriorityBindingId(slot);

            if (InputBindingManager.IsHoldingBinding(bindingId))
                return true;

            if (string.IsNullOrWhiteSpace(key) || string.Equals(key, "None", StringComparison.OrdinalIgnoreCase))
                return false;

            int vk = ParseKeyToVirtualKeyCode(key);
            return vk != 0 && (GetAsyncKeyState(vk) & 0x8000) != 0;
        }

        private bool ShouldPrioritizeHead(int slot)
        {
            string priorityKey = GetPriorityKey(slot);
            return IsPriorityKeyHeld(slot, priorityKey);
        }

        private void RequestStickyAimReset()
        {
            Interlocked.Increment(ref _stickyAimResetGeneration);
            _lastWgcMovementFrameTimestamp = 0;
            _currentWgcFrameDeltaSeconds = null;
        }

        private void UpdateFrameTiming(string captureMethod, long frameTimestamp)
        {
            if (captureMethod != "WGC" || frameTimestamp <= 0)
            {
                _lastWgcMovementFrameTimestamp = 0;
                _currentWgcFrameDeltaSeconds = null;
                return;
            }

            _currentWgcFrameDeltaSeconds = _lastWgcMovementFrameTimestamp > 0
                ? Math.Clamp((frameTimestamp - _lastWgcMovementFrameTimestamp) / (double)Stopwatch.Frequency,
                    1d / 240d, 0.05d)
                : 1d / 60d;
            _lastWgcMovementFrameTimestamp = frameTimestamp;
        }

        private void MoveCrosshairForCurrentFrame(int x, int y) =>
            MouseManager.MoveCrosshair(x, y, _currentWgcFrameDeltaSeconds);

        private void UpdateDetectionBox(Prediction target, Rectangle detectionBox)
        {
            float translatedXMin = target.Rectangle.X + detectionBox.Left;
            float translatedYMin = target.Rectangle.Y + detectionBox.Top;
            LastDetectionBox = new(translatedXMin, translatedYMin,
                target.Rectangle.Width, target.Rectangle.Height);

            CenterXTranslated = target.CenterXTranslated;
            CenterYTranslated = target.CenterYTranslated;
        }
        #endregion AI Loop Functions

        #endregion AI

        #region Screen Capture

        private void SaveFrame(Bitmap frame, Prediction? DoLabel = null)
        {
            // Only save frames if "Collect Data While Playing" is enabled
            if (!Dictionary.toggleState["Collect Data While Playing"]) return;

            // Skip if we're in constant tracking mode (unless auto-labeling is enabled)
            if (Dictionary.toggleState["Constant AI Tracking"] && !Dictionary.toggleState["Auto Label Data"]) return;

            // Cooldown check
            if ((DateTime.Now - lastSavedTime).TotalMilliseconds < SAVE_FRAME_COOLDOWN_MS) return;

            try
            {
                // Validate bitmap is still usable
                if (frame == null) return;

                // Accessing Width/Height will throw if bitmap is disposed
                int width = frame.Width;
                int height = frame.Height;
                if (width <= 0 || height <= 0) return;

                lastSavedTime = DateTime.Now;
                string uuid = Guid.NewGuid().ToString();
                string imagePath = Path.Combine("bin", "images", $"{uuid}.jpg");

                // Save synchronously to avoid "Object is currently in use elsewhere" error
                frame.Save(imagePath, ImageFormat.Jpeg);

                if (Dictionary.toggleState["Auto Label Data"] && DoLabel != null)
                {
                    var labelPath = Path.Combine("bin", "labels", $"{uuid}.txt");

                    float x = (DoLabel!.Rectangle.X + DoLabel.Rectangle.Width / 2) / width;
                    float y = (DoLabel!.Rectangle.Y + DoLabel.Rectangle.Height / 2) / height;
                    float labelWidth = DoLabel.Rectangle.Width / width;
                    float labelHeight = DoLabel.Rectangle.Height / height;

                    File.WriteAllText(labelPath, $"{DoLabel.ClassId} {x} {y} {labelWidth} {labelHeight}");
                }
            }
            catch (ArgumentException)
            {
                // Bitmap was disposed or invalid - silently ignore
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, $"SaveFrame failed: {ex.Message}");
            }
        }



        #endregion Screen Capture

        public void Dispose()
        {
            lock (_modelLock)
            {
                if (_isDisposed) return;
                _isDisposed = true;
            }

            // Signal that we're shutting down
            lock (_sizeLock)
            {
                _sizeChangePending = true;
            }

            // Stop the loop
            RequestStickyAimReset();
            _isAiLoopRunning = false;
            if (_aiLoopThread != null && _aiLoopThread.IsAlive)
            {
                if (!_aiLoopThread.Join(TimeSpan.FromSeconds(1)))
                {
                    try { _aiLoopThread.Interrupt(); }
                    catch { }
                }
            }

            // Print final benchmarks
            PrintBenchmarks();

            // Dispose DXGI objects
            _captureManager.Dispose();

            // Clean up other resources
            _reusableInputArray = null;
            _reusableInputs = null;

            lock (_modelLock)
            {
                _onnxModel?.Dispose();
                _onnxModel = null;
                _onnxModelSlot1?.Dispose();
                _onnxModelSlot1 = null;
                _onnxModelSlot2?.Dispose();
                _onnxModelSlot2 = null;

                _engineModel?.Dispose();
                _engineModel = null;
                _engineModelSlot1?.Dispose();
                _engineModelSlot1 = null;
                _engineModelSlot2?.Dispose();
                _engineModelSlot2 = null;
            }
            _modeloptions?.Dispose();
            _bitmapBuffer = null;
        }

        public static bool IsTensorRTAvaliable()
        {
            try
            {
                string? pathEnv = Environment.GetEnvironmentVariable("PATH");
                if (string.IsNullOrEmpty(pathEnv)) return false;

                var paths = pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries);
                bool hasCuda = false;
                bool hasTrt = false;

                foreach (var p in paths)
                {
                    if (!Directory.Exists(p)) continue;

                    if (!hasCuda)
                    {
                        var files = Directory.GetFiles(p, "cudart64_*.dll");
                        if (files.Length > 0) hasCuda = true;
                    }

                    if (!hasTrt)
                    {
                        var files = Directory.GetFiles(p, "nvinfer*.dll");
                        if (files.Length > 0) hasTrt = true;
                    }

                    if (hasCuda && hasTrt) return true;
                }

                return hasCuda && hasTrt;
            }
            catch
            {
                return false;
            }
        }
    }
}


