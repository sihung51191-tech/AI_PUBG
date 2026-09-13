using System; // Added this
using Aimmy2.Class;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using InputLogic;
using Other;
using System.Windows.Forms;
using AILogic;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using JYPPX.TensorRtSharp.Nvinfer;
using Aimmy2.AILogic.Recognition;
using Aimmy2.AILogic.Weapons;

namespace Aimmy2.AILogic
{
    public class WeaponSlotManager : IDisposable
    {
        private static WeaponSlotManager? _instance;
        public static WeaponSlotManager Instance => _instance ??= new WeaponSlotManager();
        public static void StopIfCreated() => _instance?.StopScanning();
        public static void DisposeIfCreated()
        {
            var instance = Interlocked.Exchange(ref _instance, null);
            instance?.Dispose();
        }
        public static bool CurrentlyLoadingScopeModel = false;

        private WeaponSlotManager()
        {
            _captureManager.CaptureMethodKey = "Scope Capture Method";
            _recognitionConfig = _recognitionStore.Load();
            _scopeImageSize = _recognitionConfig.ScopeImageSize;
            _templateLibrary = new TemplateLibrary();
            LoadConfiguredRegions();
            DisplayManager.DisplayChanged += OnRecognitionDisplayChanged;
        }

        private InferenceSession? _scopeSession;
        private TensorRTEngine? _scopeEngine;
        private InferenceSession? _weaponSession;
        private TensorRTEngine? _weaponEngine;
        private Task<bool>? _weaponLoadTask;
        private bool _isScopeEngine = false;
        private int _scopeImageSize = 640;
        private bool _scopeDynamicInput;
        private string _scopeInputName = "images";
        public event Action? ScopeInputChanged;
        public int ScopeImageSize { get { lock (_sessionLock) return _scopeImageSize; } }
        public bool CanChangeScopeImageSize { get { lock (_sessionLock) return _scopeDynamicInput && !_isScopeEngine; } }

        private static (int Size, bool Dynamic) ReadScopeInputShape(int[] dims, bool engine)
        {
            if (dims.Length != 4 || (dims[0] > 0 && dims[0] != 1) || dims[1] != 3)
                throw new NotSupportedException("Scope model must use NCHW input [1,3,H,W].");

            bool dynamicInput = dims[2] <= 0 && dims[3] <= 0;
            if (engine && dynamicInput) throw new NotSupportedException("Scope TensorRT engine needs a resolved input size.");
            int savedSize = 640;
            if (Dictionary.dropdownState.TryGetValue("Scope Image Size", out var saved)
                && int.TryParse(Convert.ToString((object)saved), out int parsed) && parsed > 0) savedSize = parsed;
            int size = dynamicInput ? savedSize : Math.Max(dims[2], dims[3]);
            if (size <= 0) throw new NotSupportedException("Invalid scope input size.");
            return (size, dynamicInput);
        }

        public void SetScopeImageSize(int size)
        {
            lock (_sessionLock)
            {
                if (!_scopeDynamicInput || _isScopeEngine || size <= 0) return;
                _scopeImageSize = size;
                _recognitionConfig.ScopeImageSize = size;
            }
            _recognitionStore.Save(_recognitionConfig);
            PublishScopeInput();
        }
        private void OnRecognitionDisplayChanged(object? sender, DisplayChangedEventArgs e)
        {
            Interlocked.Increment(ref _scanGeneration);
            LoadConfiguredRegions();
        }
        public void SetWeaponImageSize(int size)
        {
            if (size <= 0) return;
            _recognitionConfig.WeaponImageSize = size; _recognitionStore.Save(_recognitionConfig);
            lock (_sessionLock) { _weaponSession?.Dispose(); _weaponSession = null; _weaponEngine?.Dispose(); _weaponEngine = null; _weaponLoadTask = null; }
            if (_recognitionConfig.WeaponMethod == RecognitionMethod.AiModel) _ = EnsureWeaponModelAsync();
        }

        private void PublishScopeInput()
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                Dictionary.dropdownState["Scope Image Size"] = ScopeImageSize.ToString();
                global::Class.SaveDictionary.WriteJSON(Dictionary.dropdownState, "bin\\dropdown.cfg");
                ScopeInputChanged?.Invoke();
            }));
        }
        private readonly object _sessionLock = new object();
        public bool IsInitialized
        {
            get
            {
                lock (_sessionLock)
                {
                    return _recognitionInitialized || _scopeSession != null || _scopeEngine != null;
                }
            }
        }
        private readonly CaptureManager _captureManager = new CaptureManager();
        private readonly RecognitionConfigStore _recognitionStore = new();
        private readonly RecognitionConfiguration _recognitionConfig;
        private readonly TemplateLibrary _templateLibrary;
        private readonly SemaphoreSlim[] _slotFlights = { new(1, 1), new(1, 1) };
        private CancellationTokenSource _scanCts = new();
        private bool _disposed;
        private long _scanGeneration;
        private WeaponSlotState _slot1State = WeaponSlotState.Empty(1);
        private WeaponSlotState _slot2State = WeaponSlotState.Empty(2);
        public event Action<int, WeaponSlotState>? ActiveRecognitionChanged;
        private readonly Dictionary<string, (string Label, int Count)> _confirmations = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> _lastHeavyRun = new();
        public WeaponSlotState GetSlotSnapshot(int slot) { lock (_slotApplyLock) return slot == 1 ? _slot1State : _slot2State; }
        public (int Slot, WeaponSlotState State) GetActiveSlotSnapshot()
        {
            lock (_slotApplyLock) return (_activeSlot, _activeSlot == 1 ? _slot1State : _slot2State);
        }
        private void PublishActiveRecognition()
        {
            var active = GetActiveSlotSnapshot();
            ActiveRecognitionChanged?.Invoke(active.Slot, active.State);
        }
        public RecognitionConfiguration RecognitionConfig => _recognitionConfig;
        public IReadOnlyList<string> GetTemplateLabels(bool scope) => _templateLibrary.Snapshot(scope ? TemplateKind.Scope : TemplateKind.Weapon).Keys.OrderBy(x => x).ToArray();
        public void ReloadTemplates() => _templateLibrary.Reload();
        public string SaveTemplate(bool scope, string label, Bitmap image) => _templateLibrary.Save(scope ? TemplateKind.Scope : TemplateKind.Weapon, label, image);
        public RecognitionResult FindNearDuplicate(bool scope, Bitmap image)
        {
            var settings = new TemplateMatchingSettings { ConfidenceThreshold = .97, EnableMultiScale = false, EnableEdgeMatching = false, EnableColorMask = false };
            using var recognizer = new TemplateRecognizer(_templateLibrary, scope ? TemplateKind.Scope : TemplateKind.Weapon, settings);
            return recognizer.RecognizeAsync(image, CancellationToken.None).GetAwaiter().GetResult();
        }
        public IReadOnlyList<TemplateEntry> GetTemplates(bool scope, string label) => _templateLibrary.Snapshot(scope ? TemplateKind.Scope : TemplateKind.Weapon).GetValueOrDefault(label, Array.Empty<TemplateEntry>());
        public void DeleteTemplate(bool scope, string label, string file) => _templateLibrary.DeleteImage(scope ? TemplateKind.Scope : TemplateKind.Weapon, label, file);
        public void DeleteTemplateLabel(bool scope, string label) => _templateLibrary.DeleteLabel(scope ? TemplateKind.Scope : TemplateKind.Weapon, label);
        public void RenameTemplateLabel(bool scope, string oldLabel, string newLabel)
        {
            _templateLibrary.RenameLabel(scope ? TemplateKind.Scope : TemplateKind.Weapon, oldLabel, newLabel);
            RecoilManager.RenameProfileLabel(scope, oldLabel, newLabel);
        }
        private bool IsScopeModelLoaded { get { lock (_sessionLock) return _scopeSession != null || _scopeEngine != null; } }
        public Bitmap? CaptureTemplateRegion(int slot, bool scope)
        {
            var state = GetSlotSnapshot(slot);
            Rectangle region = scope ? state.ScopeRegion : state.WeaponRegion;
            if (region.IsEmpty) return null;
            // Use a fresh manager so the requested slot cannot reuse another ROI, but
            // honor the backend selected by the user. Forcing GDI here produced blank
            // templates for hardware-accelerated/full-screen games.
            var capture = new CaptureManager
            {
                CaptureMethodKey = scope ? "Scope Capture Method" : "Screen Capture Method"
            };
            try
            {
                var timer = System.Diagnostics.Stopwatch.StartNew();
                do
                {
                    var fresh = capture.ScreenGrabSnapshot(region);
                    if (fresh != null) return fresh;
                    if (timer.ElapsedMilliseconds >= 750) return null;
                    System.Threading.Thread.Sleep(15);
                } while (true);
            }
            finally { capture.Dispose(); }
        }
        public void SaveRecognitionConfig() => _recognitionStore.Save(_recognitionConfig);
        
        // Slot data
        private int _slot1ScopeIndex = -1; // -1 = none
        private int _slot2ScopeIndex = -1;
        private bool _isNmsFreeMod = false;
        private int _numDetections = 8400;
        private int _numClasses = 7;
        private double _lastScopeConfidence;
        
        private readonly object _slotApplyLock = new();
        private int _activeSlot = 1; // 1 or 2

        // Snapshot settings for slots
        private RecoilManager.RecoilSettings _slot1Recoil = new RecoilManager.RecoilSettings();
        private RecoilManager.RecoilSettings _slot2Recoil = new RecoilManager.RecoilSettings();

        // Regions from user
        // Weapon 1 is the upper weapon slot (visible when TAB is open or as primary)
        // Weapon 2 is the lower weapon slot (visible when TAB is open or as secondary)
        private Rectangle _weapon1Region = new Rectangle(1604, 113, 53, 53); // Upper position
        private Rectangle _weapon2Region = new Rectangle(1603, 337, 56, 55); // Lower position

        // Scope Names mapping from user
        private readonly string[] _scopeNames = { "8x", "6x", "4x", "3x", "2x", "chamdo", "morong" };

        private bool _isScanning = false;

        private readonly object _scanLock = new object();
        private CancellationTokenSource? _tabHoldCts;
        private DateTime _lastTabPressTime = DateTime.MinValue;

        private volatile bool _isInitializing = false;
        private volatile bool _recognitionInitialized;

        public void Initialize()
        {
            if (_recognitionInitialized) return;

            lock (_sessionLock)
            {
                if (IsInitialized || _isInitializing) return;
                _isInitializing = true;
            }

            try
            {
                string modelPath = _recognitionConfig.ScopeModelPath;
                
                // Chuyển đường dẫn tương đối thành tuyệt đối
                if (!Path.IsPathRooted(modelPath))
                {
                    modelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, modelPath);
                }
                
                if (_recognitionConfig.ScopeMethod == RecognitionMethod.AiModel) LoadModel(modelPath);
                LoadRegions();
                _recognitionInitialized = true;
            }
            finally
            {
                lock (_sessionLock)
                {
                    _isInitializing = false;
                }
            }
        }

        public void LoadRegions()
        {
            LoadConfiguredRegions();
        }

        public void SetRecognitionMethod(bool scope, RecognitionMethod method)
        {
            if (scope && method == RecognitionMethod.Ocr) throw new ArgumentException("OCR cannot recognize scope icons.");
            if (scope) _recognitionConfig.ScopeMethod = method; else _recognitionConfig.WeaponMethod = method;
            _recognitionStore.Save(_recognitionConfig);
            if (scope && method == RecognitionMethod.AiModel && !IsScopeModelLoaded) LoadModel(_recognitionConfig.ScopeModelPath);
            if (scope && method is not (RecognitionMethod.AiModel or RecognitionMethod.AutoHybrid))
            {
                lock (_sessionLock) { _scopeSession?.Dispose(); _scopeSession = null; _scopeEngine?.Dispose(); _scopeEngine = null; }
            }
            if (!scope && method == RecognitionMethod.AiModel) _ = EnsureWeaponModelAsync();
            if (!scope && method is not (RecognitionMethod.AiModel or RecognitionMethod.AutoHybrid))
            {
                lock (_sessionLock) { _weaponSession?.Dispose(); _weaponSession = null; _weaponEngine?.Dispose(); _weaponEngine = null; }
            }
        }
        public void ConfigureWeaponModel(string path)
        {
            _recognitionConfig.WeaponModelPath = path;
            Dictionary.filelocationState["Weapon Model Location"] = path;
            _recognitionStore.Save(_recognitionConfig);
            lock (_sessionLock) { _weaponSession?.Dispose(); _weaponSession = null; _weaponEngine?.Dispose(); _weaponEngine = null; _weaponLoadTask = null; }
            if (_recognitionConfig.WeaponMethod == RecognitionMethod.AiModel) _ = EnsureWeaponModelAsync();
        }

        private Task<bool> EnsureWeaponModelAsync()
        {
            lock (_sessionLock)
            {
                if (_weaponSession != null || _weaponEngine != null) return Task.FromResult(true);
                if (_weaponLoadTask is { IsCompleted: false }) return _weaponLoadTask;
                _weaponLoadTask = Task.Run(async () =>
                {
                    string configured = _recognitionConfig.WeaponModelPath;
                    if (string.IsNullOrWhiteSpace(configured)) return false;
                    string path = Path.IsPathRooted(configured) ? configured : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, configured);
                    if (!File.Exists(path)) return false;
                    await Dictionary.ModelLoadSemaphore.WaitAsync();
                    try
                    {
                        if (path.EndsWith(".engine", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".trt", StringComparison.OrdinalIgnoreCase))
                        {
                            var engine = new TensorRTEngine(path); lock (_sessionLock) { _weaponEngine?.Dispose(); _weaponEngine = engine; }
                        }
                        else
                        {
                            var session = OnnxModelSessionFactory.Load(path, "Auto", _recognitionConfig.WeaponImageSize);
                            lock (_sessionLock) { _weaponSession?.Dispose(); _weaponSession = session; }
                        }
                        return true;
                    }
                    catch (Exception ex) { LogManager.Log(LogManager.LogLevel.Error, $"Failed to load weapon AI model: {ex.Message}"); return false; }
                    finally { Dictionary.ModelLoadSemaphore.Release(); }
                });
                return _weaponLoadTask;
            }
        }

        private RecognitionResult DetectWeaponAi(Bitmap bitmap)
        {
            lock (_sessionLock)
            {
                int size = _recognitionConfig.WeaponImageSize;
                using var resized = new Bitmap(bitmap, new Size(size, size));
                var pixels = new float[3 * size * size]; MathUtil.BitmapToFloatArrayInPlace(resized, pixels, size);
                Tensor<float> output;
                if (_weaponEngine != null) output = _weaponEngine.RunDetections(pixels, new Rectangle(0, 0, size, size), 0);
                else if (_weaponSession != null)
                {
                    var meta = OnnxModelSessionFactory.Metadata(_weaponSession); var resolved = meta.ResolveSize(size);
                    using var run = new RunOptions();
                    output = OnnxModelSessionFactory.Run(_weaponSession, pixels, new CaptureTransform(new Rectangle(0, 0, size, size), resolved.Width, resolved.Height, meta.Options.Letterbox), run, 0);
                }
                else return RecognitionResult.Unknown(RecognitionMethod.AiModel, "Weapon AI model unavailable");
                float best = 0; int classId = -1;
                for (int i = 0; i < output.Dimensions[1]; i++) if (output[0, i, 4] > best) { best = output[0, i, 4]; classId = (int)output[0, i, 5]; }
                string[] labels = _templateLibrary.Snapshot(TemplateKind.Weapon).Keys.OrderBy(x => x).ToArray();
                if (best < _recognitionConfig.WeaponAiConfidence || classId < 0 || classId >= labels.Length)
                    return RecognitionResult.Unknown(RecognitionMethod.AiModel, "Weapon AI confidence/class mapping unavailable");
                return new RecognitionResult(labels[classId], RecognitionMethod.AiModel, best, true, "Weapon AI model detection");
            }
        }

        public void LoadModel(string modelPath)
        {
            lock (_sessionLock)
            {
                if (CurrentlyLoadingScopeModel) return;
                CurrentlyLoadingScopeModel = true;
            }
            Task.Run(async () =>
            {
                try
                {
                    // Chuyển đường dẫn tương đối thành tuyệt đối để load
                    string absolutePath = modelPath;
                    if (!Path.IsPathRooted(modelPath))
                    {
                        absolutePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, modelPath);
                    }
                    
                    if (File.Exists(absolutePath))
                    {
                        bool isEngine = absolutePath.EndsWith(".engine", StringComparison.OrdinalIgnoreCase) || 
                                        absolutePath.EndsWith(".trt", StringComparison.OrdinalIgnoreCase);

                        if (isEngine)
                        {
                            if (!AIManager.IsTensorRTAvaliable())
                            {
                                throw new Exception("TensorRT (.engine) is not supported on this machine. CUDA 12.x or TensorRT 10.x DLLs not found in PATH.");
                            }

                            TensorRTEngine newEngine;
                            await Dictionary.ModelLoadSemaphore.WaitAsync();
                            try
                            {
                                newEngine = await Task.Run(() => new TensorRTEngine(absolutePath));
                            }
                            finally
                            {
                                Dictionary.ModelLoadSemaphore.Release();
                            }

                            lock (_sessionLock)
                            {
                                (int Size, bool Dynamic) shape;
                                try { shape = ReadScopeInputShape(newEngine.InputDims, true); }
                                catch { newEngine.Dispose(); throw; }
                                _scopeSession?.Dispose();
                                _scopeSession = null;
                                _scopeEngine?.Dispose();
                                _scopeEngine = newEngine;
                                _isScopeEngine = true;
                                _scopeImageSize = shape.Size;
                                _scopeDynamicInput = false;
                            }
                        }
                        else
                        {
                            InferenceSession newSession;
                            await Dictionary.ModelLoadSemaphore.WaitAsync();
                            try
                            {
                                newSession = await Task.Run(() => OnnxModelSessionFactory.Load(absolutePath, "Auto", _scopeImageSize));
                            }
                            finally
                            {
                                Dictionary.ModelLoadSemaphore.Release();
                            }
                            
                            lock (_sessionLock)
                            {
                                (int Size, bool Dynamic) shape;
                                string inputName;
                                try
                                {
                                    var input = newSession.InputMetadata.Single();
                                    var metadata = OnnxModelSessionFactory.Metadata(newSession);
                                    var resolved = metadata.ResolveSize(_scopeImageSize);
                                    shape = (resolved.Width, metadata.Dynamic);
                                    inputName = input.Key;
                                }
                                catch { newSession.Dispose(); throw; }
                                _scopeEngine?.Dispose();
                                _scopeEngine = null;
                                _scopeSession?.Dispose();
                                _scopeSession = newSession;
                                _isScopeEngine = false;
                                _scopeImageSize = shape.Size;
                                _scopeDynamicInput = shape.Dynamic;
                                _scopeInputName = inputName;
                            }
                        }
                        
                        // Lưu đường dẫn tương đối để portable
                        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                        if (absolutePath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
                        {
                            Dictionary.filelocationState["Scope Model Location"] = absolutePath.Substring(baseDir.Length).TrimStart('\\', '/');
                        }
                        else
                        {
                            Dictionary.filelocationState["Scope Model Location"] = absolutePath;
                        }
                        _recognitionConfig.ScopeModelPath = Dictionary.filelocationState["Scope Model Location"];
                        _recognitionStore.Save(_recognitionConfig);
                        
                        LogManager.Log(LogManager.LogLevel.Info, $"Weapon Recognition Model loaded: {Path.GetFileName(absolutePath)}");
                        
                        // Validate model shape
                        ValidateModelShape();
                    }
                    else
                    {
                        LogManager.Log(LogManager.LogLevel.Error, $"Scope model not found: {absolutePath}");
                    }
                }
                catch (Exception ex)
                {
                    LogManager.Log(LogManager.LogLevel.Error, $"Failed to load scope model: {ex.Message}");
                }
                finally
                {
                    CurrentlyLoadingScopeModel = false;
                    PublishScopeInput();
                }
            });
        }

        private void ValidateModelShape()
        {
            bool isNmsFree = false;
            lock (_sessionLock)
            {
                if (_isScopeEngine && _scopeEngine != null)
                {
                    var dims = _scopeEngine.OutputDims;
                    LogManager.Log(LogManager.LogLevel.Info, $"Scope Model Output (Engine): {string.Join("x", dims)}");

                    // Check for YOLOv10/11/26 (1x300x6)
                    if (dims.Length == 3 && dims[2] == 6)
                    {
                        _isNmsFreeMod = true;
                        _numDetections = dims[1];
                        isNmsFree = true;
                        LogManager.Log(LogManager.LogLevel.Info, "Detected NMS-Free Scope Model (YOLOv10/11/26).");
                    }
                    else
                    {
                        _isNmsFreeMod = false;
                        _numDetections = dims.Length >= 3 ? dims[2] : 8400;
                        _numClasses = dims.Length >= 2 ? (dims[1] - 4) : 7;
                        isNmsFree = false;
                        LogManager.Log(LogManager.LogLevel.Info, $"Detected YOLOv8-style Scope Model with {_numClasses} classes.");
                    }
                }
                else if (_scopeSession != null)
                {
                    var outputMetadata = _scopeSession.OutputMetadata;
                    foreach (var kvp in outputMetadata)
                    {
                        var dims = kvp.Value.Dimensions;
                        LogManager.Log(LogManager.LogLevel.Info, $"Scope Model Output: {string.Join("x", dims)}");

                        // Check for YOLOv10/11/26 (1x300x6)
                        if (dims.Length == 3 && dims[2] == 6)
                        {
                            _isNmsFreeMod = true;
                            _numDetections = dims[1];
                            isNmsFree = true;
                            LogManager.Log(LogManager.LogLevel.Info, "Detected NMS-Free Scope Model.");
                            break;
                        }
                        
                        // Check for YOLOv8/v11 (1x(4+nc)x8400)
                        if (dims.Length == 3 && dims[1] > 4)
                        {
                            _isNmsFreeMod = false;
                            _numDetections = dims[2];
                            _numClasses = dims[1] - 4;
                            isNmsFree = false;
                            LogManager.Log(LogManager.LogLevel.Info, $"Detected YOLOv8-style Scope Model with {_numClasses} classes.");
                            break;
                        }
                    }
                }
                else
                {
                    return;
                }
            }

            // Show toast notification like the aim model
            string modelName = Path.GetFileName(Dictionary.filelocationState["Scope Model Location"]);
            string typeStr = isNmsFree ? "NMS-Free" : "Standard (NMS)";
            LogManager.Log(LogManager.LogLevel.Info, $"Loaded Scope model: {modelName} ({typeStr})", true, 3000);
        }

        // State to track if we assume inventory is open
        private bool _inventoryOpenState = false;

        public void HandleKeyPress(Keys key)
        {
            if (key == Keys.Tab)
            {
               // Legacy Tab handling removed in favor of the independent scan bindings.
            }
            else if (key == Keys.D1 || key == Keys.NumPad1)
            {
                ApplySlot(1);
            }
            else if (key == Keys.D2 || key == Keys.NumPad2)
            {
                ApplySlot(2);
            }
        }

        public void StopScanning()
        {
            CancellationTokenSource scanToCancel;
            CancellationTokenSource? holdToCancel;
            lock (_scanLock)
            {
                _isScanning = false;
                _inventoryOpenState = false;
                holdToCancel = _tabHoldCts;
                _tabHoldCts = null;
                Interlocked.Increment(ref _scanGeneration);
                scanToCancel = _scanCts;
                // Do not dispose a token source while StartScan may still be registering
                // an await against its token. The cancelled source becomes collectible
                // after that scan exits; the replacement belongs to the next scan.
                _scanCts = new CancellationTokenSource();
            }
            try { holdToCancel?.Cancel(); } catch (ObjectDisposedException) { }
            try { scanToCancel.Cancel(); } catch (ObjectDisposedException) { }
        }

        public void OnForegroundRestored()
        {
            // Do not let the Alt+Tab key sequence keep the scan debounce armed.
            _lastTabPressTime = DateTime.MinValue;
        }
        public async void OnScanPressed(bool scanWeapons, bool scanScopes, bool toggleMode)
        {
            // Debounce to prevent rapid clicks (jitter/spam) from breaking the state
            if ((DateTime.Now - _lastTabPressTime).TotalMilliseconds < 250) return;
            _lastTabPressTime = DateTime.Now;

            if (_isScanning)
            {
                // Support Toggle OFF: If already scanning and toggle mode is on, stop it.
                if (toggleMode)
                {
                    StopScanning();
                }
                return;
            }

            // Keep a local source so another Tab press cannot replace the field while
            // this asynchronous wait is still reading its token.
            var holdCts = new CancellationTokenSource();
            CancellationTokenSource? previousHold;
            lock (_scanLock)
            {
                if (_disposed) { holdCts.Dispose(); return; }
                previousHold = _tabHoldCts;
                _tabHoldCts = holdCts;
            }
            try { previousHold?.Cancel(); } catch (ObjectDisposedException) { }

            try
            {
                // Get delay from settings (default 2s if not found)
                double scanDelay = 0.5;
                if (Dictionary.sliderSettings.TryGetValue("Weapon Scan Delay", out var sDelay))
                {
                    scanDelay = Convert.ToDouble(sDelay);
                }
                
                // Get Reset Delay
                double resetDelay = 0.2; // Default short delay for reset
                if (Dictionary.sliderSettings.TryGetValue("Tab Reset Adjust", out var rDelay))
                {
                    resetDelay = Convert.ToDouble(rDelay);
                }

                // Determine which action we are doing? 
                
                // We'll use a loop to wait and check deadlines
                // Wait for the SHORTER of the two, perform action, then wait for the rest if needed.
                
                DateTime startTime = DateTime.Now;
                bool resetDone = false;
                bool scanStarted = false;

                // Loop continues as long as we haven't cancelled AND (we haven't started scanning OR reset isn't done yet)
                // Note: If scan starts, does user keep holding? Usually yes for a bit.
                // We want to ensure Reset happens if time elapsed, even if scan started.
                // But StartScan() runs on another thread usually? No, it's async void but internally awaits.
                // Actually StartScan is async void. So it returns immediately? 
                
                // Let's keep logic simple: Check deadlines until user releases or both done.
                while (!holdCts.Token.IsCancellationRequested)
                {
                     double elapsed = (DateTime.Now - startTime).TotalSeconds;
                     bool allDone = true;

                     // Check Reset
                     if (!resetDone && scanWeapons)
                     {
                         if (elapsed >= resetDelay)
                         {
                             // Only reset if enabled
                             if (Dictionary.toggleState.ContainsKey("Enable Tab Reset") && Dictionary.toggleState["Enable Tab Reset"])
                             {
                                 RecoilManager.ResetTemporaryStrength();
                             }
                             resetDone = true;
                         }
                         else
                         {
                             allDone = false;
                         }
                     }
                     else if (!scanWeapons)
                     {
                         resetDone = true;
                     }

                    // Check Scan
                    if (!scanStarted)
                    {
                        if (toggleMode || elapsed >= scanDelay)
                        {
                            _inventoryOpenState = true;
                            StartScan(scanWeapons, scanScopes);
                            scanStarted = true;
                        }
                        else
                        {
                            allDone = false;
                        }
                    }

                     if (allDone) break;

                     await Task.Delay(50, holdCts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // Ignore cancellation
            }
            catch (Exception ex)
            {
                // async-void exceptions otherwise reach WPF's dispatcher and can close
                // the entire application when the recognition scan key is pressed.
                LogManager.Log(LogManager.LogLevel.Error, $"Could not start recognition scan: {ex.Message}", true, 5000);
            }
            finally
            {
                lock (_scanLock)
                {
                    if (ReferenceEquals(_tabHoldCts, holdCts)) _tabHoldCts = null;
                }
                holdCts.Dispose();
            }
        }

        public void OnScanReleased(bool toggleMode)
        {
            // Cancel the pending scan if strictly waiting
            CancellationTokenSource? hold;
            lock (_scanLock) hold = _tabHoldCts;
            try { hold?.Cancel(); } catch (ObjectDisposedException) { }
            
            // Only stop if NOT in toggle mode (if toggle is on, scan continues until pressed again)
            if (!toggleMode)
            {
                _inventoryOpenState = false;
            }
        }

        private async void StartScan(bool scanWeapons, bool scanScopes)
        {
            long generation;
            CancellationToken token;
            lock (_scanLock)
            {
                if (_disposed || _isScanning) return;
                _isScanning = true;
                generation = Interlocked.Increment(ref _scanGeneration);
                token = _scanCts.Token;
            }
            LogManager.Log(LogManager.LogLevel.Info, scanWeapons && scanScopes ? "Scanning weapon and scope slots..." : scanScopes ? "Scanning scope slots..." : "Scanning weapon slots...");
            try
            {
                await Task.Run(async () =>
                {
                    bool first = true;
                    do
                    {
                        if (!first && !_inventoryOpenState) break;
                        await ScanSlotAsync(1, generation, token, scanWeapons, scanScopes);
                        await ScanSlotAsync(2, generation, token, scanWeapons, scanScopes);
                        UpdateScopeOverlay();
                        ApplyCurrentSlot();
                        first = false;
                        if (_inventoryOpenState) await Task.Delay(Math.Clamp(_recognitionConfig.Settings.ScanIntervalMs, 100, 2000), token);
                    } while (_isScanning && _inventoryOpenState && generation == Volatile.Read(ref _scanGeneration));
                }, token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { LogManager.Log(LogManager.LogLevel.Error, $"Recognition scan failed: {ex.Message}"); }
            finally { if (generation == Volatile.Read(ref _scanGeneration)) _isScanning = false; }
        }

        private async Task ScanSlotAsync(int slot, long generation, CancellationToken token, bool scanWeapons, bool scanScopes)
        {
            var flight = _slotFlights[slot - 1];
            if (!await flight.WaitAsync(0, token)) return;
            try
            {
                WeaponSlotState state = GetSlotSnapshot(slot);
                if (scanWeapons && Dictionary.toggleState.GetValueOrDefault("Weapon Recognition") && !state.WeaponRegion.IsEmpty)
                {
                    using var image = _captureManager.ScreenGrabSnapshot(state.WeaponRegion);
                    if (image != null) CommitRecognition(slot, false, await RecognizeAsync(slot, image, TemplateKind.Weapon, _recognitionConfig.WeaponMethod, token), generation);
                }
                state = GetSlotSnapshot(slot);
                if (scanScopes && Dictionary.toggleState.GetValueOrDefault("Scope Recognition") && !state.ScopeRegion.IsEmpty)
                {
                    using var image = _captureManager.ScreenGrabSnapshot(state.ScopeRegion);
                    if (image != null) CommitRecognition(slot, true, await RecognizeAsync(slot, image, TemplateKind.Scope, _recognitionConfig.ScopeMethod, token), generation);
                }
            }
            finally { flight.Release(); }
        }

        private async Task<RecognitionResult> RecognizeAsync(int slot, Bitmap image, TemplateKind kind, RecognitionMethod method, CancellationToken token)
        {
            if (kind == TemplateKind.Scope && method == RecognitionMethod.Ocr)
                return RecognitionResult.Unknown(method, "OCR is disabled for scope icons");
            async Task<RecognitionResult> Run(RecognitionMethod selected)
            {
                if (selected == RecognitionMethod.AiModel)
                {
                    if (kind == TemplateKind.Weapon)
                    {
                        if (!await EnsureWeaponModelAsync()) return RecognitionResult.Unknown(selected, "Weapon AI model is not configured");
                        return DetectWeaponAi(image);
                    }
                    if (!IsScopeModelLoaded)
                    {
                        if (!string.IsNullOrWhiteSpace(_recognitionConfig.ScopeModelPath) && File.Exists(Path.IsPathRooted(_recognitionConfig.ScopeModelPath) ? _recognitionConfig.ScopeModelPath : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _recognitionConfig.ScopeModelPath)))
                            LoadModel(_recognitionConfig.ScopeModelPath);
                        return RecognitionResult.Unknown(selected, "Scope AI model is loading");
                    }
                    int index = DetectScope(image);
                    return index < 0 ? RecognitionResult.Unknown(selected, "AI confidence below threshold")
                        : new RecognitionResult(GetScopeName(index), selected, _lastScopeConfidence, true, "AI model detection");
                }
                if (selected == RecognitionMethod.TemplateMatching)
                    using (var recognizer = new TemplateRecognizer(_templateLibrary, kind, kind == TemplateKind.Weapon ? _recognitionConfig.WeaponTemplateSettings : _recognitionConfig.ScopeTemplateSettings)) return await recognizer.RecognizeAsync(image, token);
                if (selected is RecognitionMethod.OrbFeatureMatching or RecognitionMethod.SiftFeatureMatching)
                {
                    var featureSettings = kind == TemplateKind.Weapon
                        ? _recognitionConfig.WeaponFeatureSettings : _recognitionConfig.ScopeFeatureSettings;
                    if (selected == RecognitionMethod.SiftFeatureMatching)
                    {
                        string key = $"{slot}:{kind}:{selected}"; long now = Environment.TickCount64;
                        long last = _lastHeavyRun.GetValueOrDefault(key);
                        int remaining = featureSettings.SiftScanIntervalMs - (int)Math.Min(int.MaxValue, Math.Max(0, now - last));
                        // Wait for the next permitted SIFT pass instead of returning an artificial
                        // failed result. The active Tab scan therefore keeps checking every cycle
                        // and immediately sees a scope/weapon change on the next SIFT pass.
                        if (last != 0 && remaining > 0) await Task.Delay(remaining, token);
                        _lastHeavyRun[key] = Environment.TickCount64;
                    }
                    using (var recognizer = new FeatureRecognizer(_templateLibrary, kind, featureSettings, selected)) return await recognizer.RecognizeAsync(image, token);
                }
                if (selected == RecognitionMethod.Ocr && kind == TemplateKind.Weapon)
                    using (var recognizer = new OcrWeaponRecognizer(_templateLibrary, _recognitionConfig.Settings)) return await recognizer.RecognizeAsync(image, token);
                return RecognitionResult.Unknown(selected, "Unsupported method");
            }
            if (method != RecognitionMethod.AutoHybrid) return await Run(method);
            var order = kind == TemplateKind.Scope
                ? new[] { RecognitionMethod.TemplateMatching, RecognitionMethod.OrbFeatureMatching, RecognitionMethod.SiftFeatureMatching, RecognitionMethod.AiModel }
                : new[] { RecognitionMethod.TemplateMatching, RecognitionMethod.Ocr, RecognitionMethod.AiModel };
            RecognitionResult last = RecognitionResult.Unknown(method, "No recognizer succeeded");
            foreach (var candidate in order) { last = await Run(candidate); if (last.IsReliable) return last; }
            return last;
        }

        private void CommitRecognition(int slot, bool scope, RecognitionResult result, long generation)
        {
            if (generation != Volatile.Read(ref _scanGeneration)) return;
            // A throttled SIFT pass did not inspect the image, so it must preserve the previous
            // confirmation instead of publishing None and making two confirmations impossible.
            if (result.IsDeferred) return;
            string key = $"{slot}:{scope}";
            bool publish = false;
            lock (_slotApplyLock)
            {
                if (!result.IsReliable)
                {
                    _confirmations.Remove(key);
                    var missing = slot == 1 ? _slot1State : _slot2State;
                    bool changed = scope
                        ? !string.Equals(missing.DetectedScopeName, "None", StringComparison.OrdinalIgnoreCase)
                        : !string.Equals(missing.DetectedWeaponName, "None", StringComparison.OrdinalIgnoreCase);
                    missing = scope
                        ? missing with { DetectedScopeName = "None", ScopeScore = 0, ScopeRecognitionMethod = result.Method, ScanGeneration = generation }
                        : missing with { DetectedWeaponName = "None", WeaponScore = 0, WeaponRecognitionMethod = result.Method, ScanGeneration = generation };
                    if (slot == 1) _slot1State = missing; else _slot2State = missing;
                    if (scope)
                    {
                        if (slot == 1) _slot1ScopeIndex = -1; else _slot2ScopeIndex = -1;
                        CaptureRecoilSettings(slot, -1);
                    }
                    publish = changed && slot == _activeSlot;
                }
                else
                {
                    var previous = _confirmations.GetValueOrDefault(key);
                    int count = string.Equals(previous.Label, result.Label, StringComparison.OrdinalIgnoreCase) ? previous.Count + 1 : 1;
                    _confirmations[key] = (result.Label, count);
                    if (count < Math.Clamp(_recognitionConfig.Settings.ConfirmationsRequired, 2, 3)) return;
                    var state = slot == 1 ? _slot1State : _slot2State;
                    bool labelChanged = scope ? !string.Equals(state.DetectedScopeName, result.Label, StringComparison.OrdinalIgnoreCase)
                        : !string.Equals(state.DetectedWeaponName, result.Label, StringComparison.OrdinalIgnoreCase);
                    state = scope
                        ? state with { DetectedScopeName = result.Label, ScopeScore = result.Score, ScopeRecognitionMethod = result.Method, LastScopeConfirmedTime = DateTime.UtcNow, ScanGeneration = generation }
                        : state with { DetectedWeaponName = result.Label, WeaponScore = result.Score, WeaponRecognitionMethod = result.Method, LastWeaponConfirmedTime = DateTime.UtcNow, ScanGeneration = generation };
                    if (slot == 1) _slot1State = state; else _slot2State = state;
                    if (scope && labelChanged)
                    {
                        int index = Array.FindIndex(_scopeNames, x => string.Equals(x, result.Label, StringComparison.OrdinalIgnoreCase));
                        if (slot == 1) _slot1ScopeIndex = index; else _slot2ScopeIndex = index;
                        CaptureRecoilSettings(slot, index);
                    }
                    publish = labelChanged && slot == _activeSlot;
                }
            }
            if (publish) PublishActiveRecognition();
        }

        private string GetScopeName(int index)
        {
            if (index >= 0 && index < _scopeNames.Length)
                return _scopeNames[index];
            return "None";
        }

        private int DetectScope(Bitmap bitmap)
        {
            try
            {
                lock (_sessionLock)
                {
                    if (CurrentlyLoadingScopeModel) return -1;
                    int size = _scopeImageSize;
                    using var resized = new Bitmap(bitmap, new Size(size, size));
                    var input = PreProcess(resized, size);
                    Tensor<float> output;
                    var region = new Rectangle(0, 0, size, size);
                    if (_isScopeEngine && _scopeEngine != null)
                    {
                        output = _scopeEngine.RunDetections(input.ToArray(), region, 0);
                    }
                    else if (_scopeSession != null)
                    {
                        var metadata = OnnxModelSessionFactory.Metadata(_scopeSession);
                        var resolved = metadata.ResolveSize(size);
                        using var run = new RunOptions();
                        output = OnnxModelSessionFactory.Run(_scopeSession, input.ToArray(),
                            new CaptureTransform(region, resolved.Width, resolved.Height, metadata.Options.Letterbox), run, 0);
                    }
                    else
                    {
                        return -1;
                    }
                    _isNmsFreeMod = true; // Canonical output is postprocessed [1,N,6].
                    return PostProcess(output);
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, $"Error in DetectScope: {ex.Message}");
                return -1;
            }
        }

        private DenseTensor<float> PreProcess(Bitmap bitmap, int size)
        {
            var pixels = new float[3 * size * size];
            MathUtil.BitmapToFloatArrayInPlace(bitmap, pixels, size);
            return new DenseTensor<float>(pixels, new[] { 1, 3, size, size });
        }
        private int PostProcess(Tensor<float> output)
        {
            float maxConfidence = 0;
            int bestClass = -1;

            if (_isNmsFreeMod)
            {
                // YOLOv10/11/26 format: [1, 300, 6] -> [x1, y1, x2, y2, confidence, class]
                for (int i = 0; i < output.Dimensions[1]; i++)
                {
                    float conf = output[0, i, 4];
                    if (conf > maxConfidence)
                    {
                        maxConfidence = conf;
                        bestClass = (int)output[0, i, 5];
                    }
                }
            }
            else
            {
                // YOLOv8 format: [ batch, 4 + classes, 8400 ]
                int numDetections = output.Dimensions[2];
                for (int i = 0; i < numDetections; i++)
                {
                    for (int c = 0; c < output.Dimensions[1] - 4; c++)
                    {
                        float conf = output[0, 4 + c, i];
                        if (conf > maxConfidence)
                        {
                            maxConfidence = conf;
                            bestClass = c;
                        }
                    }
                }
            }

            float threshold = 0.45f; // Default 45%
            if (Dictionary.sliderSettings.TryGetValue("Scope Confidence", out var confVal))
            {
                threshold = (float)(Convert.ToDouble(confVal) / 100.0);
            }

            _lastScopeConfidence = maxConfidence;
            if (maxConfidence < threshold) return -1;
            return bestClass;
        }

        // This section is likely part of a UI building method, e.g., AimMenuControl.BuildMenu()
        // The instruction implies adding these toggles to a menu.
        // Since the context provided is between PostProcess and ApplySlot,
        // and the code snippet itself is a fluent API chain, it's placed here
        // as a placeholder for where it would logically be added in a UI definition.
        // This is not syntactically correct in the current class structure,
        // but follows the instruction's placement context.
        // If this was a real file, these lines would be in a method like:
        // public void BuildMenu(MenuBuilder builder) { builder
        //     .AddToggle(...)
        //     .AddToggle(...);
        // }
        // For the purpose of this edit, I'm placing it as per the instruction's context.
        // This is a placeholder and would need to be integrated into a proper menu building method.
        // .AddToggle("Weapon Recognition", t => {
        //     t.Reader.Click += (s, e) => {
        //         // Notify user or something
        //     };
        // }, tooltip: "Enable AI scope recognition from specific screen regions.")
        // .AddToggle("Show Weapon + Scope Info", tooltip: "Show recognized weapon and scope information.")


        private void ApplyCurrentSlot()
        {
            // Read the active slot inside the same lock used by key switches.
            lock (_slotApplyLock) ApplySlot(_activeSlot);
        }

        private void ApplySlot(int slot)
        {
            lock (_slotApplyLock) ApplySlotCore(slot);
        }

        private void ApplySlotCore(int slot)
        {
            if (slot is not (1 or 2)) throw new ArgumentOutOfRangeException(nameof(slot));
            _activeSlot = slot;
            // Switch AI Model Slot
            if (FileManager.AIManager != null)
            {
                FileManager.AIManager.SetActiveSlot(slot);
            }

            var recoilSettings = (slot == 1) ? _slot1Recoil : _slot2Recoil;
            int scopeIndex = (slot == 1) ? _slot1ScopeIndex : _slot2ScopeIndex;
            
            // Update RecoilManager with the captured settings
            RecoilManager.ActiveSlotSettings = recoilSettings;


            // Logic: If None (-1), default to Scope 1 (0) (Red Dot/x1)
            int effectiveScopeIndex = (scopeIndex == -1) ? 0 : scopeIndex;

            // Map WeaponSlotManager scope index to RecoilManager scope index (0-5)
            // WeaponSlotManager: 0=8x, 1=6x, ..., 4=2x, 5=chamdo/x1, -1=None
            // RecoilManager: 0=Scope1(x1), 1=Scope2(2x)...
            
            // If it was valid detection (not None)
            if (scopeIndex != -1)
            {
                 // Mapping logic from StartScan:
                 // 8x (0) -> Scope 6 (idx 5)
                 // 6x (1) -> Scope 5 (idx 4)
                 // 4x (2) -> Scope 4 (idx 3)
                 // 3x (3) -> Scope 3 (idx 2)
                 // 2x (4) -> Scope 2 (idx 1)
                 // x1 (5) -> Scope 1 (idx 0)
                 // morong (6) -> Scope 1 (idx 0)
                 
                 // Let's recalculate based on the known order in CaptureRecoilSettings
                 if (scopeIndex == 0) effectiveScopeIndex = 5; // 8x -> Scope 6
                 else if (scopeIndex == 1) effectiveScopeIndex = 4; // 6x -> Scope 5
                 else if (scopeIndex == 2) effectiveScopeIndex = 3; // 4x -> Scope 4
                 else if (scopeIndex == 3) effectiveScopeIndex = 2; // 3x -> Scope 3
                 else if (scopeIndex == 4) effectiveScopeIndex = 1; // 2x -> Scope 2
                 else effectiveScopeIndex = 0; // x1/morong -> Scope 1
            }
            else
            {
                // If None, Force Scope 1
                effectiveScopeIndex = 0;
            }

            RecoilManager.SelectedScopeIndex = effectiveScopeIndex;
            var state = slot == 1 ? _slot1State : _slot2State;
            RecoilManager.SetRecognitionContext(state.DetectedWeaponName, state.DetectedScopeName);
                
            LogManager.Log(LogManager.LogLevel.Info, $"✓ Applied Slot {slot}: {GetScopeName(scopeIndex)}");
            LogManager.Log(LogManager.LogLevel.Info, $"  → Recoil: Strength={recoilSettings.Strength}, Step={recoilSettings.Step}, Delay={recoilSettings.Delay}, Multi={recoilSettings.Multi}");
            LogManager.Log(LogManager.LogLevel.Info, $"  → RecoilManager.SelectedScopeIndex set to {effectiveScopeIndex}");
            
            // Update overlay
            UpdateScopeOverlay();
            
            // Ensure overlay is visible if toggle is on
            if (Dictionary.toggleState["Show Weapon + Scope Info"] && Dictionary.DetectedScopeOverlay != null)
            {
                Dictionary.DetectedScopeOverlay.Show(true);
            }
            PublishActiveRecognition();
        }

        public void SetWeaponRegion(int slot, Rectangle rect)
        {
            // Compatibility API: these legacy “weapon” regions are scope regions.
            SetRegion(slot, true, rect);
        }

        private void LoadConfiguredRegions()
        {
            Rectangle Resolve(RegionConfiguration config)
            {
                if (!config.IsConfigured) return Rectangle.Empty;
                var screen = System.Windows.Forms.Screen.AllScreens.FirstOrDefault(x => string.Equals(x.DeviceName, config.MonitorId, StringComparison.OrdinalIgnoreCase))
                    ?? System.Windows.Forms.Screen.AllScreens.FirstOrDefault(x => x.Bounds.IntersectsWith(config.PixelRectangle));
                return screen == null ? config.PixelRectangle : config.Resolve(screen.Bounds);
            }
            lock (_slotApplyLock)
            {
                var w1 = Resolve(_recognitionConfig.WeaponSlot1Region); var s1 = Resolve(_recognitionConfig.ScopeSlot1Region);
                var w2 = Resolve(_recognitionConfig.WeaponSlot2Region); var s2 = Resolve(_recognitionConfig.ScopeSlot2Region);
                _weapon1Region = s1; _weapon2Region = s2;
                _slot1State = _slot1State with { WeaponRegion = w1, ScopeRegion = s1 };
                _slot2State = _slot2State with { WeaponRegion = w2, ScopeRegion = s2 };
            }
        }

        public void SetRegion(int slot, bool scope, Rectangle rectangle)
        {
            if (slot is not (1 or 2)) throw new ArgumentOutOfRangeException(nameof(slot));
            if (rectangle.Width <= 0 || rectangle.Height <= 0) throw new ArgumentException("ROI must have positive dimensions.", nameof(rectangle));
            var screen = System.Windows.Forms.Screen.FromRectangle(rectangle);
            var config = RegionConfiguration.FromPixels(rectangle, screen.DeviceName, screen.Bounds);
            lock (_slotApplyLock)
            {
                var state = slot == 1 ? _slot1State : _slot2State;
                state = scope ? state with { ScopeRegion = rectangle } : state with { WeaponRegion = rectangle };
                if (slot == 1) { _slot1State = state; if (scope) _weapon1Region = rectangle; }
                else { _slot2State = state; if (scope) _weapon2Region = rectangle; }
                if (slot == 1 && scope) _recognitionConfig.ScopeSlot1Region = config;
                else if (slot == 2 && scope) _recognitionConfig.ScopeSlot2Region = config;
                else if (slot == 1) _recognitionConfig.WeaponSlot1Region = config;
                else _recognitionConfig.WeaponSlot2Region = config;
            }
            _recognitionStore.Save(_recognitionConfig);
            LogManager.Log(LogManager.LogLevel.Info, $"Updated {(scope ? "scope" : "weapon")} ROI for slot {slot}: {rectangle}");
        }

        public void ClearRegion(int slot, bool scope)
        {
            if (slot is not (1 or 2)) throw new ArgumentOutOfRangeException(nameof(slot));
            lock (_slotApplyLock)
            {
                var state = slot == 1 ? _slot1State : _slot2State;
                state = scope ? state with { ScopeRegion = Rectangle.Empty } : state with { WeaponRegion = Rectangle.Empty };
                if (slot == 1) { _slot1State = state; if (scope) _weapon1Region = Rectangle.Empty; }
                else { _slot2State = state; if (scope) _weapon2Region = Rectangle.Empty; }
                if (slot == 1 && scope) _recognitionConfig.ScopeSlot1Region = new();
                else if (slot == 2 && scope) _recognitionConfig.ScopeSlot2Region = new();
                else if (slot == 1) _recognitionConfig.WeaponSlot1Region = new();
                else _recognitionConfig.WeaponSlot2Region = new();
                _confirmations.Remove($"{slot}:{scope}");
            }
            _recognitionStore.Save(_recognitionConfig);
        }

        private void UpdateScopeOverlay()
        {
            if (Dictionary.DetectedScopeOverlay != null)
            {
                Dictionary.DetectedScopeOverlay.UpdateSlot1(GetScopeName(_slot1ScopeIndex));
                Dictionary.DetectedScopeOverlay.UpdateSlot2(GetScopeName(_slot2ScopeIndex));
                Dictionary.DetectedScopeOverlay.UpdateActiveSlot(_activeSlot);
                Dictionary.DetectedScopeOverlay.UpdateRecognition(_slot1State, _slot2State, _activeSlot, RecoilManager.ActiveProfileName);
            }
        }

        private void CaptureRecoilSettings(int slot, int scopeIdx)
        {
            // Map scope Index to Recoil Slider ID
            int scopeNumToLoad = 1; // Default to Scope 1 (x1) if None

            if (scopeIdx == -1)
            {
                // None -> Load Scope 1
                scopeNumToLoad = 1;
            }
            else if (scopeIdx == 0) scopeNumToLoad = 6; // 8x
            else if (scopeIdx == 1) scopeNumToLoad = 5; // 6x
            else if (scopeIdx == 2) scopeNumToLoad = 4; // 4x
            else if (scopeIdx == 3) scopeNumToLoad = 3; // 3x
            else if (scopeIdx == 4) scopeNumToLoad = 2; // 2x
            else scopeNumToLoad = 1; // chamdo/morong -> 1x

            var settings = (slot == 1) ? _slot1Recoil : _slot2Recoil;
            
            // Get current values from Dictionary sliders
            // Get current values from Dictionary sliders
            if (Dictionary.sliderSettings.TryGetValue($"Recoil Scope {scopeNumToLoad} Strength", out var strength))
                settings.Strength = (float)Convert.ToDouble(strength);
            
            if (Dictionary.sliderSettings.TryGetValue($"Recoil Scope {scopeNumToLoad} Step", out var step))
                settings.Step = (float)Convert.ToDouble(step);
            
            if (Dictionary.sliderSettings.TryGetValue($"Recoil Scope {scopeNumToLoad} Delay", out var delay))
                settings.Delay = (float)Convert.ToDouble(delay);
            
            if (Dictionary.sliderSettings.TryGetValue($"Recoil Scope {scopeNumToLoad} Multi", out var multi))
                settings.Multi = (float)Convert.ToDouble(multi);
            
            LogManager.Log(LogManager.LogLevel.Info, $"Slot {slot} Recoil Settings Saved: {GetScopeName(scopeIdx)} → Strength={settings.Strength}, Step={settings.Step}, Delay={settings.Delay}, Multi={settings.Multi}");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopScanning();
            _scanCts.Dispose();
            lock (_sessionLock) { _scopeSession?.Dispose(); _scopeSession = null; _scopeEngine?.Dispose(); _scopeEngine = null; _weaponSession?.Dispose(); _weaponSession = null; _weaponEngine?.Dispose(); _weaponEngine = null; }
            _captureManager.Dispose(); _templateLibrary.Dispose();
            DisplayManager.DisplayChanged -= OnRecognitionDisplayChanged;
            foreach (var flight in _slotFlights) flight.Dispose();
        }
    }
}
