using Aimmy2.Class;
using Other;
using SharpGen.Runtime;
using System.Drawing;
using System.Drawing.Imaging;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using LogLevel = Other.LogManager.LogLevel;
using Class;
using Aimmy2;


namespace AILogic
{
    internal class CaptureManager
    {
        #region Variables
        public string CaptureMethodKey { get; set; } = "Screen Capture Method";
        private string _currentCaptureMethod = ""; // Track current method
        private bool _directXFailedPermanently = false; // Track if DirectX failed with unsupported error
        private bool _notificationShown = false; // Prevent spam notifications
        private WindowsGraphicsCapture? _wgc;
        private static readonly object InstancesLock = new();
        private static readonly List<WeakReference<CaptureManager>> Instances = new();
        // Keep a failed WGC backend disabled across model/manager recreation for this run.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Exception> WgcFailures = new();
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> ReportedCaptureFailures = new();

        // Capturing
        public Bitmap? screenCaptureBitmap { get; private set; }
        public Bitmap? directXBitmap { get; private set; }
        private ID3D11Device? _dxDevice;
        private IDXGIOutputDuplication? _deskDuplication;
        private ID3D11Texture2D? _stagingTex;

        // Frame caching for DirectX
        private Bitmap? _cachedFrame;
        private Rectangle _cachedFrameBounds;
        private DateTime _lastFrameTime = DateTime.MinValue;
        private readonly TimeSpan _frameCacheTimeout = TimeSpan.FromMilliseconds(15); // Adjust as needed


        // Display change handling
        public readonly object _displayLock = new();
        public bool _displayChangesPending { get; set; } = false;

        // Performance tracking
        private int _consecutiveFailures = 0;
        private const int MAX_CONSECUTIVE_FAILURES = 5;

        // stride matching
        private bool _lastStrideMatch = true;
        private int _lastSrcStride = 0;
        private int _lastDstStride = 0;

        #endregion
        #region Handlers
        public CaptureManager()
        {
            lock (InstancesLock)
            {
                Instances.RemoveAll(reference => !reference.TryGetTarget(out _));
                Instances.Add(new WeakReference<CaptureManager>(this));
            }
            // Subscribe to display changes FIRST
            DisplayManager.DisplayChanged += OnDisplayChanged;
        }

        internal static Task ReleaseInactiveWgcSessionsAsync()
        {
            CaptureManager[] managers;
            lock (InstancesLock)
            {
                managers = Instances.Select(reference => reference.TryGetTarget(out var manager) ? manager : null)
                    .OfType<CaptureManager>().ToArray();
            }
            // Closing a capture session may wait for the GPU. Keep that off the UI thread.
            return Task.Run(() =>
            {
                foreach (var manager in managers)
                {
                    lock (manager._displayLock)
                    {
                        // Read the latest selection so a rapid switch back to WGC is respected.
                        if ((string)Dictionary.dropdownState[manager.CaptureMethodKey] != "WGC")
                            manager.DisposeWgcResources();
                    }
                }
            });
        }

        internal static void RefreshAfterForegroundSwitch()
        {
            CaptureManager[] managers;
            lock (InstancesLock)
                managers = Instances.Select(reference => reference.TryGetTarget(out var manager) ? manager : null)
                    .OfType<CaptureManager>().ToArray();

            foreach (var manager in managers)
            {
                lock (manager._displayLock)
                {
                    manager._consecutiveFailures = 0;
                    // Desktop Duplication can retain an AccessLost frame after Alt+Tab.
                    // Recreate it on the very next capture instead of waiting for five failures.
                    if (Dictionary.dropdownState.GetValueOrDefault(manager.CaptureMethodKey) == "DirectX")
                    {
                        manager._displayChangesPending = true;
                        manager.DisposeDxgiResources();
                    }
                }
            }
        }

        internal static void ValidateSavedCaptureMethods()
        {
            foreach (string key in new[] { "Screen Capture Method", "Scope Capture Method" })
            {
                string method = Dictionary.dropdownState[key];
                if (method == "GDI+") continue;
                var manager = new CaptureManager { CaptureMethodKey = key };
                try
                {
                    if (method != "DirectX" && method != "WGC")
                        throw new NotSupportedException($"Unknown capture method: {method}");
                    if (method == "DirectX")
                    {
                        // A desktop with no new frame is not a failure. Validate the
                        // duplication session without treating its initial timeout as one.
                        manager.InitializeDxgiDuplication();
                        continue;
                    }
                    DisplayManager.Initialize();
                    var display = DisplayManager.CurrentDisplay
                        ?? throw new InvalidOperationException("No display available.");
                    var region = new Rectangle((int)display.Bounds.Left + (int)display.Bounds.Width / 2 - 16,
                        (int)display.Bounds.Top + (int)display.Bounds.Height / 2 - 16, 32, 32);
                    var timer = System.Diagnostics.Stopwatch.StartNew();
                    do
                    {
                        if (manager.ScreenGrab(region) != null) break;
                        if (timer.ElapsedMilliseconds >= 2000)
                            throw new TimeoutException($"{method} did not deliver a startup frame within two seconds.");
                        System.Threading.Thread.Sleep(10);
                    } while (true);
                    if (Dictionary.dropdownState[key] != method)
                        manager.SaveCaptureSetting();
                }
                catch (Exception ex)
                {
                    if (method == "WGC") WgcFailures.TryAdd(key, ex);
                    Dictionary.dropdownState[key] = "GDI+";
                    manager.SaveCaptureSetting();
                    ReportCaptureFailure(key, method, ex);
                }
                finally { manager.Dispose(); }
            }
        }

        private void OnDisplayChanged(object? sender, DisplayChangedEventArgs e)
        {
            lock (_displayLock)
            {
                _displayChangesPending = true;
                _consecutiveFailures = 0;
                DisposeDxgiResources();
            }
            LogManager.Log(LogLevel.Info, $"Display change detected. {CaptureMethodKey} resources will be reinitialized when needed.");
        }

        public void HandlePendingDisplayChanges()
        {
            lock (_displayLock)
            {
                if (!_displayChangesPending) return;

                if (Dictionary.dropdownState[CaptureMethodKey] != "DirectX")
                {
                    DisposeWgcResources();
                    _displayChangesPending = false;
                    return;
                }

                try
                {
                    InitializeDxgiDuplication();
                    _displayChangesPending = false;
                }
                catch (Exception ex)
                {

                }
            }
        }

        #endregion
        #region DirectX
        public void InitializeDxgiDuplication(bool updateSettingsOnFailure = true)
        {
            DisposeDxgiResources();
            try
            {
                DisplayManager.Initialize();
                var currentDisplay = DisplayManager.CurrentDisplay;
                if (currentDisplay == null)
                {
                    LogManager.Log(LogLevel.Error, "No current display available. DisplayManager may not be initialized.");
                    throw new InvalidOperationException("No current display available. DisplayManager may not be initialized.");
                }

                using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
                IDXGIOutput1? targetOutput1 = null;
                IDXGIAdapter1? targetAdapter = null;
                bool foundTarget = false;

                for (uint adapterIndex = 0;
                    factory.EnumAdapters1(adapterIndex, out var adapter).Success;
                    adapterIndex++)
                {
                    LogManager.Log(LogLevel.Info, $"Checking Adapter {adapterIndex}: {adapter.Description.Description.TrimEnd('\0')}");

                    for (uint outputIndex = 0;
                        adapter.EnumOutputs(outputIndex, out var output).Success;
                        outputIndex++)
                    {
                        using (output)
                        {
                            var output1 = output.QueryInterface<IDXGIOutput1>();
                            var outputDesc = output1.Description;
                            var outputBounds = new Vortice.Mathematics.Rect(
                                outputDesc.DesktopCoordinates.Left,
                                outputDesc.DesktopCoordinates.Top,
                                outputDesc.DesktopCoordinates.Right - outputDesc.DesktopCoordinates.Left,
                                outputDesc.DesktopCoordinates.Bottom - outputDesc.DesktopCoordinates.Top);
                            LogManager.Log(LogLevel.Info, $"Found Output {outputIndex}: DeviceName = '{outputDesc.DeviceName.TrimEnd('\0')}', Bounds = {outputBounds}");

                            // Try different matching strategies
                            bool nameMatch = currentDisplay?.DeviceName != null && outputDesc.DeviceName.TrimEnd('\0') == currentDisplay.DeviceName.TrimEnd('\0');
                            bool boundsMatch = currentDisplay?.Bounds != null && outputBounds.Equals(currentDisplay.Bounds);

                            if (nameMatch || boundsMatch)
                            {
                                targetOutput1 = output1;
                                targetAdapter = adapter;
                                foundTarget = true;
                                break;
                            }
                            output1.Dispose();
                        }
                    }

                    if (foundTarget) break;
                }

                // Fallback to specific display index if not found
                if (!foundTarget)
                {
                    int targetIndex = currentDisplay?.Index ?? 0;
                    int currentIndex = 0;

                    for (uint adapterIndex = 0;
                        factory.EnumAdapters1(adapterIndex, out var adapter).Success;
                        adapterIndex++)
                    {
                        for (uint outputIndex = 0;
                            adapter.EnumOutputs(outputIndex, out var output).Success;
                            outputIndex++)
                        {
                            if (currentIndex == targetIndex)
                            {
                                LogManager.Log(LogLevel.Warning, $"Could not match display by name or bounds. Found a fallback index, {targetIndex}.");
                                targetOutput1 = output.QueryInterface<IDXGIOutput1>();
                                targetAdapter = adapter;
                                foundTarget = true;
                                break;
                            }
                            currentIndex++;
                            output.Dispose();
                        }

                        if (foundTarget)
                            break;
                        adapter.Dispose();
                    }
                }

                if (targetAdapter == null || targetOutput1 == null)
                {
                    throw new Exception("No suitable display output found");
                }

                FeatureLevel[] featureLevels = {
                    FeatureLevel.Level_12_2, // 50 series support
                    FeatureLevel.Level_12_1,
                    FeatureLevel.Level_12_0,
                    FeatureLevel.Level_11_1,
                    FeatureLevel.Level_11_0,
                    FeatureLevel.Level_10_1,
                    FeatureLevel.Level_10_0,
                    FeatureLevel.Level_9_3,
                    FeatureLevel.Level_9_2,
                    FeatureLevel.Level_9_1
                };

                // Create D3D11 device
                var result = D3D11.D3D11CreateDevice(
                    targetAdapter,
                    DriverType.Unknown,
                    DeviceCreationFlags.None,
                    featureLevels,
                    out _dxDevice);

                if (result.Failure || _dxDevice == null)
                {
                    result = D3D11.D3D11CreateDevice(
                      targetAdapter,
                      DriverType.Unknown,
                      DeviceCreationFlags.None,
                      null,
                      out _dxDevice);

                    if (result.Failure || _dxDevice == null)
                    {
                        throw new Exception($"Failed to create D3D11 device: {result}");
                    }
                }

                // Create desktop duplication
                _deskDuplication = targetOutput1.DuplicateOutput(_dxDevice);
                _consecutiveFailures = 0; //reset on success

                LogManager.Log(LogLevel.Info, "DirectX Desktop Duplication initialized successfully.");
            }
            catch (Exception ex)
            {
                _directXFailedPermanently = true;
                DisposeDxgiResources();

                // A background support probe must never overwrite a newer UI selection.
                if (!updateSettingsOnFailure || Dictionary.dropdownState[CaptureMethodKey] != "DirectX")
                    throw;

                Dictionary.dropdownState[CaptureMethodKey] = "GDI+";
                _currentCaptureMethod = "GDI+";
                SaveCaptureSetting();
                UpdateUIDropdown("GDI+");
                ReportCaptureFailure(CaptureMethodKey, "DirectX", ex);

                throw;
            }
        }
        private Bitmap? DirectX(Rectangle detectionBox)
        {
            int w = detectionBox.Width;
            int h = detectionBox.Height;
            bool frameAcquired = false;
            IDXGIResource? desktopResource = null;


            Bitmap? resultBitmap = null;

            try
            {

                lock (_displayLock)
                {
                    if (_displayChangesPending)
                    {
                        InitializeDxgiDuplication();
                        _displayChangesPending = false;
                    }
                }

                // Check if we need to reinitialize
                if (_dxDevice == null || _dxDevice.ImmediateContext == null || _deskDuplication == null)
                {
                    InitializeDxgiDuplication();
                    if (_dxDevice == null || _dxDevice.ImmediateContext == null || _deskDuplication == null)
                    {
                        lock (_displayLock) { _displayChangesPending = true; }
                        return GetCachedFrame(detectionBox);
                    }
                }

                if (directXBitmap == null || directXBitmap.Width != w || directXBitmap.Height != h)
                {
                    directXBitmap?.Dispose();
                    directXBitmap = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                }

                // Check if we need new staging texture - always match requested size
                if (_stagingTex == null ||
                    _stagingTex.Description.Width != w ||
                    _stagingTex.Description.Height != h)
                {
                    _stagingTex?.Dispose();
                    _stagingTex = _dxDevice.CreateTexture2D(new Texture2DDescription
                    {
                        Width = (uint)w,
                        Height = (uint)h,
                        MipLevels = 1,
                        ArraySize = 1,
                        Format = Format.B8G8R8A8_UNorm,
                        SampleDescription = new(1, 0),
                        Usage = ResourceUsage.Staging,
                        CPUAccessFlags = CpuAccessFlags.Read,
                        BindFlags = BindFlags.None
                    });
                }

                int timeout = _consecutiveFailures > 0 ? 5 : 1;
                var result = _deskDuplication!.AcquireNextFrame((uint)timeout, out var frameInfo, out desktopResource);

                if (result == Vortice.DXGI.ResultCode.WaitTimeout)
                {
                    // No new frame available - this is normal
                    _consecutiveFailures = 0; // Reset failure counter
                    return GetCachedFrame(detectionBox);
                }
                else if (result == Vortice.DXGI.ResultCode.DeviceRemoved || result == Vortice.DXGI.ResultCode.AccessLost)
                { // Device lost - need to reinitialize
                    _consecutiveFailures++;

                    if (_consecutiveFailures >= MAX_CONSECUTIVE_FAILURES)
                        lock (_displayLock) { _displayChangesPending = true; }

                    return GetCachedFrame(detectionBox);
                }
                else if (result != Result.Ok)
                {
                    // Other error
                    _consecutiveFailures++;
                    return GetCachedFrame(detectionBox);
                }

                frameAcquired = true;
                _consecutiveFailures = 0; // Reset on successful acquisition

                using (var screenTexture = desktopResource.QueryInterface<ID3D11Texture2D>())
                {
                    #region Display Bounds
                    var displayBounds = new Rectangle(DisplayManager.ScreenLeft,
                                                  DisplayManager.ScreenTop,
                                                  DisplayManager.ScreenWidth,
                                                  DisplayManager.ScreenHeight);

                    // IMPORTANT: Convert absolute screen coordinates to display-relative coordinates
                    // The duplicated output starts at (0,0), not at its screen position
                    int relativeDetectionLeft = detectionBox.Left - DisplayManager.ScreenLeft;
                    int relativeDetectionTop = detectionBox.Top - DisplayManager.ScreenTop;
                    int relativeDetectionRight = relativeDetectionLeft + detectionBox.Width;
                    int relativeDetectionBottom = relativeDetectionTop + detectionBox.Height;

                    // Calculate the visible portion in display-relative coordinates
                    int srcLeft = Math.Max(relativeDetectionLeft, 0);
                    int srcTop = Math.Max(relativeDetectionTop, 0);
                    int srcRight = Math.Min(relativeDetectionRight, DisplayManager.ScreenWidth);
                    int srcBottom = Math.Min(relativeDetectionBottom, DisplayManager.ScreenHeight);

                    // Only copy if there's a visible region
                    if (srcRight > srcLeft && srcBottom > srcTop)
                    {
                        var box = new Box(srcLeft, srcTop, 0, srcRight, srcBottom, 1);

                        _dxDevice.ImmediateContext.CopySubresourceRegion(
                               _stagingTex, 0,
                               (uint)(srcLeft - relativeDetectionLeft),
                               (uint)(srcTop - relativeDetectionTop),
                               0,
                               screenTexture, 0, box);
                    }
                    else
                    {
                        LogManager.Log(LogLevel.Warning, "No visible region to copy from DirectX capture.", true, 3000);
                        return GetCachedFrame(detectionBox);
                    }

                    #endregion

                    #region Bitmap
                    var map = _dxDevice.ImmediateContext.Map(_stagingTex, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                    var boundsRect = new Rectangle(0, 0, w, h);
                    BitmapData? mapDest = directXBitmap.LockBits(boundsRect, ImageLockMode.WriteOnly, directXBitmap.PixelFormat);

                    try
                    {
                        unsafe
                        {
                            byte* src = (byte*)map.DataPointer;
                            byte* dst = (byte*)mapDest.Scan0;
                            int srcStride = (int)map.RowPitch;
                            int dstStride = mapDest.Stride;

                            int copyBytesPerRow = Math.Min(srcStride, dstStride);
                            for (int y = 0; y < h; y++)
                            {
                                Buffer.MemoryCopy(src, dst, dstStride, copyBytesPerRow);
                                // Staging pixels outside the copied ROI are undefined, not black.
                                int validLeft = srcLeft - relativeDetectionLeft, validTop = srcTop - relativeDetectionTop;
                                int validRight = srcRight - relativeDetectionLeft, validBottom = srcBottom - relativeDetectionTop;
                                for (int x = 0; x < w; x++)
                                    if (x < validLeft || x >= validRight || y < validTop || y >= validBottom)
                                    { dst[x * 4] = dst[x * 4 + 1] = dst[x * 4 + 2] = 0; dst[x * 4 + 3] = 255; }
                                src += srcStride;
                                dst += dstStride;
                            }

                            if (Dictionary.toggleState["Third Person Support"]) // a mask basically
                            {
                                int width = w / 2;
                                int height = h / 2;
                                int startY = h - height;

                                byte* basePtr = (byte*)mapDest.Scan0;
                                for (int y = startY; y < h; y++)
                                {
                                    byte* rowPtr = basePtr + (y * dstStride);
                                    for (int x = 0; x < width; x++)
                                    {
                                        int pixelOffset = x * 4;
                                        // Pixel layout: [B, G, R, A]
                                        rowPtr[pixelOffset + 0] = 0;   // Blue -> 0
                                        rowPtr[pixelOffset + 1] = 0;   // Green -> 0
                                        rowPtr[pixelOffset + 2] = 0;   // Red -> 0
                                        rowPtr[pixelOffset + 3] = 255; // Alpha -> 255 (opaque)
                                    }
                                }
                            }
                        }
                        #endregion
                    }
                    finally
                    {
                        directXBitmap.UnlockBits(mapDest);
                        _dxDevice.ImmediateContext.Unmap(_stagingTex, 0);
                    }


                    resultBitmap = directXBitmap;
                    UpdateCache(resultBitmap, detectionBox);
                    return resultBitmap;
                }
            }
            catch (Exception e)
            {
                LogManager.Log(LogLevel.Error, $"DirectX capture error: {e.Message}");

                if (++_consecutiveFailures >= MAX_CONSECUTIVE_FAILURES)
                    lock (_displayLock) { _displayChangesPending = true; }

                return GetCachedFrame(detectionBox);
            }
            finally
            {
                desktopResource?.Dispose();
                try
                {
                    if (frameAcquired && _deskDuplication != null)
                    {
                        _deskDuplication.ReleaseFrame();
                    }
                }
                catch { }

            }
        }

        /// <summary>
        /// Captures screen region directly from VRAM staging texture mapped memory and converts to float array.
        /// Bypasses Bitmap allocation entirely.
        /// </summary>
        public unsafe bool CaptureAndConvertDirectX(Rectangle detectionBox, float[] result, int imageSize, bool thirdPersonSupport)
        {
            if (detectionBox.Width != imageSize || detectionBox.Height != imageSize || result.Length != checked(3 * imageSize * imageSize))
                throw new ArgumentException("DirectX fast-path tensor must match the capture dimensions.");
            if (Dictionary.dropdownState[CaptureMethodKey] != "DirectX") return false;
            // The DirectX fast path bypasses ScreenGrab when switching away from WGC.
            DisposeWgcResources();
            int w = detectionBox.Width;
            int h = detectionBox.Height;
            bool frameAcquired = false;
            IDXGIResource? desktopResource = null;

            try
            {
                lock (_displayLock)
                {
                    if (_displayChangesPending)
                    {
                        InitializeDxgiDuplication();
                        _displayChangesPending = false;
                    }
                }

                if (_dxDevice == null || _dxDevice.ImmediateContext == null || _deskDuplication == null)
                {
                    InitializeDxgiDuplication();
                    if (_dxDevice == null || _dxDevice.ImmediateContext == null || _deskDuplication == null)
                    {
                        lock (_displayLock) { _displayChangesPending = true; }
                        return false;
                    }
                }

                if (_stagingTex == null ||
                    _stagingTex.Description.Width != w ||
                    _stagingTex.Description.Height != h)
                {
                    _stagingTex?.Dispose();
                    _stagingTex = _dxDevice.CreateTexture2D(new Texture2DDescription
                    {
                        Width = (uint)w,
                        Height = (uint)h,
                        MipLevels = 1,
                        ArraySize = 1,
                        Format = Format.B8G8R8A8_UNorm,
                        SampleDescription = new(1, 0),
                        Usage = ResourceUsage.Staging,
                        CPUAccessFlags = CpuAccessFlags.Read,
                        BindFlags = BindFlags.None
                    });
                }

                int timeout = _consecutiveFailures > 0 ? 5 : 1;
                var resultDx = _deskDuplication!.AcquireNextFrame((uint)timeout, out var frameInfo, out desktopResource);

                if (resultDx == Vortice.DXGI.ResultCode.WaitTimeout)
                {
                    _consecutiveFailures = 0;
                    return false;
                }
                else if (resultDx == Vortice.DXGI.ResultCode.DeviceRemoved || resultDx == Vortice.DXGI.ResultCode.AccessLost)
                {
                    _consecutiveFailures++;
                    if (_consecutiveFailures >= MAX_CONSECUTIVE_FAILURES)
                        lock (_displayLock) { _displayChangesPending = true; }
                    return false;
                }
                else if (resultDx != Result.Ok)
                {
                    _consecutiveFailures++;
                    return false;
                }

                frameAcquired = true;
                _consecutiveFailures = 0;

                using (var screenTexture = desktopResource.QueryInterface<ID3D11Texture2D>())
                {
                    int relativeDetectionLeft = detectionBox.Left - DisplayManager.ScreenLeft;
                    int relativeDetectionTop = detectionBox.Top - DisplayManager.ScreenTop;
                    int relativeDetectionRight = relativeDetectionLeft + detectionBox.Width;
                    int relativeDetectionBottom = relativeDetectionTop + detectionBox.Height;

                    int srcLeft = Math.Max(relativeDetectionLeft, 0);
                    int srcTop = Math.Max(relativeDetectionTop, 0);
                    int srcRight = Math.Min(relativeDetectionRight, DisplayManager.ScreenWidth);
                    int srcBottom = Math.Min(relativeDetectionBottom, DisplayManager.ScreenHeight);

                    if (srcRight > srcLeft && srcBottom > srcTop)
                    {
                        var box = new Box(srcLeft, srcTop, 0, srcRight, srcBottom, 1);

                        _dxDevice.ImmediateContext.CopySubresourceRegion(
                               _stagingTex, 0,
                               (uint)(srcLeft - relativeDetectionLeft),
                               (uint)(srcTop - relativeDetectionTop),
                               0,
                               screenTexture, 0, box);
                    }
                    else
                    {
                        return false;
                    }

                    var map = _dxDevice.ImmediateContext.Map(_stagingTex, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                    try
                    {
                        byte* srcPtr = (byte*)map.DataPointer;
                        int srcStride = (int)map.RowPitch;
                        
                        MathUtil.DxMapToFloatArray(srcPtr, srcStride, result, imageSize, thirdPersonSupport);
                        if (srcLeft != relativeDetectionLeft || srcTop != relativeDetectionTop || srcRight != relativeDetectionRight || srcBottom != relativeDetectionBottom)
                        {
                            int plane = imageSize * imageSize;
                            for (int y = 0; y < imageSize; y++)
                            for (int x = 0; x < imageSize; x++)
                            {
                                if (x + relativeDetectionLeft >= srcLeft && x + relativeDetectionLeft < srcRight && y + relativeDetectionTop >= srcTop && y + relativeDetectionTop < srcBottom) continue;
                                int index = y * imageSize + x;
                                result[index] = result[plane + index] = result[2 * plane + index] = 0;
                            }
                        }
                    }
                    finally
                    {
                        _dxDevice.ImmediateContext.Unmap(_stagingTex, 0);
                    }
                    return true;
                }
            }
            catch (Exception e)
            {
                LogManager.Log(LogLevel.Error, $"DirectX capture & convert error: {e.Message}");
                if (++_consecutiveFailures >= MAX_CONSECUTIVE_FAILURES)
                {
                    lock (_displayLock) { _displayChangesPending = true; }
                    LogManager.Log(LogLevel.Error, "DirectX capture failed repeatedly. Falling back to GDI+.");
                    Dictionary.dropdownState[CaptureMethodKey] = "GDI+";
                    _currentCaptureMethod = "GDI+";
                    SaveCaptureSetting();
                    UpdateUIDropdown("GDI+");
                    DisposeDxgiResources();
                }
                return false;
            }
            finally
            {
                desktopResource?.Dispose();
                try
                {
                    if (frameAcquired && _deskDuplication != null)
                    {
                        _deskDuplication.ReleaseFrame();
                    }
                }
                catch { }
            }
        }

        #region Frame Caching


        private void UpdateCache(Bitmap frame, Rectangle bounds)
        {
            if (_cachedFrame == null ||
                !_cachedFrameBounds.Equals(bounds) ||
                DateTime.Now - _lastFrameTime > _frameCacheTimeout)
            {
                _cachedFrame?.Dispose();
                _cachedFrame = (Bitmap)frame.Clone();
                _cachedFrameBounds = bounds;
            }
            _lastFrameTime = DateTime.Now;
        }


        private Bitmap? GetCachedFrame(Rectangle detectionBox)
        {
            if (_cachedFrame != null &&
                _cachedFrameBounds.Equals(detectionBox) &&
                DateTime.Now - _lastFrameTime <= _frameCacheTimeout)
            {
                return _cachedFrame;
            }
            return null;
        }
        #endregion
        #endregion

        #region GDI
        public Bitmap GDIScreen(Rectangle detectionBox)
        {
            if (_dxDevice != null || _deskDuplication != null)
            {
                DisposeDxgiResources();
            }

            if (screenCaptureBitmap == null || screenCaptureBitmap.Width != detectionBox.Width || screenCaptureBitmap.Height != detectionBox.Height)
            {
                screenCaptureBitmap?.Dispose();
                screenCaptureBitmap = new Bitmap(detectionBox.Width, detectionBox.Height, PixelFormat.Format32bppArgb);
            }

            try
            {
                using (var g = Graphics.FromImage(screenCaptureBitmap))
                {
                    g.Clear(System.Drawing.Color.Black);
                    // The saved ROI may belong to a monitor other than the display that
                    // is currently selected for AI inference. Capture against the full
                    // Windows virtual desktop so template previews are not filled black.
                    var visible = Rectangle.Intersect(detectionBox, System.Windows.Forms.SystemInformation.VirtualScreen);
                    if (visible.Width > 0 && visible.Height > 0)
                        g.CopyFromScreen(visible.Left, visible.Top, visible.Left - detectionBox.Left, visible.Top - detectionBox.Top,
                            visible.Size, CopyPixelOperation.SourceCopy);

                    if (Dictionary.toggleState["Third Person Support"])
                    {
                        int width = screenCaptureBitmap.Width / 2;
                        int height = screenCaptureBitmap.Height / 2;
                        int startY = screenCaptureBitmap.Height - height;

                        using var brush = new SolidBrush(System.Drawing.Color.Black);
                        g.FillRectangle(brush, 0, startY, width, height);
                    }
                }

                return screenCaptureBitmap;
            }
            catch (Exception ex)
            {
                LogManager.Log(LogLevel.Error, $"GDI+ screen capture failed: {ex.Message}");
                throw;
            }
        }
        #endregion

        public bool LastCaptureConverted { get; private set; }
        public bool LastCaptureWaitingForFrame { get; private set; }
        public long LastCaptureFrameId { get; private set; }
        public long LastCaptureFrameTimestamp { get; private set; }
        // Scope preprocessing owns its snapshot; it must never hold the reusable
        // WGC bitmap after the capture lock is released.
        public Bitmap? ScreenGrabSnapshot(Rectangle detectionBox)
        {
            return ScreenGrab(detectionBox, copyBitmap: true);
        }

        public Bitmap? ScreenGrab(Rectangle detectionBox, float[]? tensor = null, bool copyBitmap = false)
        {
            Bitmap? CopyIfRequested(Bitmap? image) => copyBitmap ? (Bitmap?)image?.Clone() : image;
            LastCaptureConverted = false;
            LastCaptureWaitingForFrame = false;
            LastCaptureFrameId = 0;
            LastCaptureFrameTimestamp = 0;
            string selectedMethod = Dictionary.dropdownState[CaptureMethodKey];

            if (selectedMethod == "WGC")
            {
                if (WgcFailures.ContainsKey(CaptureMethodKey))
                {
                    Dictionary.dropdownState[CaptureMethodKey] = "GDI+";
                    _currentCaptureMethod = "GDI+";
                    UpdateUIDropdown("GDI+");
                    return CopyIfRequested(GDIScreen(detectionBox));
                }

                // DisplayManager raises DisplayChanged while holding its own lock.
                // Never acquire that lock while holding _displayLock (opposite lock order).
                DisplayManager.Initialize();
                var display = DisplayManager.CurrentDisplay;
                lock (_displayLock)
                {
                    try
                    {
                        // A method change may have closed WGC while this call waited
                        // for the lock. Do not recreate the old session from a stale selection.
                        if ((string)Dictionary.dropdownState[CaptureMethodKey] != "WGC") return null;
                        if (_currentCaptureMethod != "WGC")
                        {
                            DisposeDxgiResources();
                            directXBitmap = null;
                            screenCaptureBitmap?.Dispose();
                            screenCaptureBitmap = null;
                            _currentCaptureMethod = "WGC";
                        }
                        if (_displayChangesPending)
                        {
                            DisposeWgcResources();
                            _displayChangesPending = false;
                        }
                        if (display == null) throw new InvalidOperationException("No display available for WGC.");
                        _wgc ??= new WindowsGraphicsCapture(new Rectangle((int)display.Bounds.Left,
                            (int)display.Bounds.Top, (int)display.Bounds.Width, (int)display.Bounds.Height));
                        var bitmap = _wgc.Capture(detectionBox, (bool)Dictionary.toggleState["Third Person Support"], tensor,
                            requireNewFrame: CaptureMethodKey == "Screen Capture Method");
                        LastCaptureConverted = _wgc.ConvertedToTensor;
                        LastCaptureWaitingForFrame = _wgc.WaitingForNewFrame;
                        if (!LastCaptureWaitingForFrame && (bitmap != null || LastCaptureConverted))
                        {
                            LastCaptureFrameId = _wgc.FrameNumber;
                            LastCaptureFrameTimestamp = _wgc.LatestFrameReceiptTimestamp;
                        }
                        if (copyBitmap && bitmap != null)
                        {
                            bitmap = (Bitmap)bitmap.Clone();
                        }
                        return bitmap;
                    }
                    catch (Exception ex)
                    {
                        // Commit fallback BEFORE cleanup, logging, or UI work can fail.
                        bool firstFailure = WgcFailures.TryAdd(CaptureMethodKey, ex);
                        Dictionary.dropdownState[CaptureMethodKey] = "GDI+";
                        _currentCaptureMethod = "GDI+";
                        DisposeWgcResources();
                        SaveCaptureSetting();
                        UpdateUIDropdown("GDI+");
                        if (firstFailure) ReportCaptureFailure(CaptureMethodKey, "WGC", ex);
                        return CopyIfRequested(GDIScreen(detectionBox));
                    }
                }
            }
            DisposeWgcResources();

            // If DirectX failed permanently, force GDI+
            if (_directXFailedPermanently && selectedMethod == "DirectX")
            {
                Dictionary.dropdownState[CaptureMethodKey] = "GDI+";
                selectedMethod = "GDI+";
                _currentCaptureMethod = "GDI+";
                SaveCaptureSetting();
                UpdateUIDropdown("GDI+");
            }

            // Handle method switch
            if (selectedMethod != _currentCaptureMethod)
            {
                // Dispose bitmap when switching methods
                screenCaptureBitmap?.Dispose();
                screenCaptureBitmap = null;

                directXBitmap?.Dispose();
                directXBitmap = null;

                _currentCaptureMethod = selectedMethod;
                _notificationShown = false; // Reset notification flag on method change

                // Dispose DX resources when switching to GDI
                if (selectedMethod == "GDI+")
                {
                    DisposeDxgiResources();
                }
                else
                {
                    try
                    {
                        InitializeDxgiDuplication();
                    }
                    catch
                    {
                        // Fallback to GDI+ if initialization fails
                        Dictionary.dropdownState[CaptureMethodKey] = "GDI+";
                        selectedMethod = "GDI+";
                        _currentCaptureMethod = "GDI+";
                        SaveCaptureSetting();
                        UpdateUIDropdown("GDI+");
                        DisposeDxgiResources();
                        return CopyIfRequested(GDIScreen(detectionBox));
                    }
                }
            }

            if (selectedMethod == "DirectX" && !_directXFailedPermanently)
            {
                var bmp = DirectX(detectionBox);
                if (bmp == null || _consecutiveFailures >= MAX_CONSECUTIVE_FAILURES)
                {
                    LogManager.Log(LogLevel.Error, "DirectX capture failed. Falling back to GDI+.");
                    Dictionary.dropdownState[CaptureMethodKey] = "GDI+";
                    _currentCaptureMethod = "GDI+";
                    SaveCaptureSetting();
                    UpdateUIDropdown("GDI+");
                    DisposeDxgiResources();
                    return CopyIfRequested(GDIScreen(detectionBox));
                }
                return CopyIfRequested(bmp);
            }
            else
            {
                return CopyIfRequested(GDIScreen(detectionBox));
            }
        }

        private void SaveCaptureSetting()
        {
            CapturePreferences.Save();
            try
            {
                SaveDictionary.WriteJSON(Dictionary.dropdownState, "bin\\dropdown.cfg");
                
                // Also save to active configuration if available
                string activeConfig = Dictionary.lastLoadedConfig;
                if (!string.IsNullOrEmpty(activeConfig) && activeConfig != "N/A")
                {
                    string activeConfigPath = System.IO.Path.Combine("bin\\configs", activeConfig);
                    SaveDictionary.WriteJSON(Dictionary.sliderSettings
                        .Where(kvp => kvp.Key != "Screen Capture Method" && kvp.Key != "Scope Capture Method"
                            && kvp.Key != "Slot 1 Image Size" && kvp.Key != "Slot 2 Image Size")
                        .Concat(Dictionary.dropdownState)
                        .GroupBy(kvp => kvp.Key)
                        .ToDictionary(g => g.Key, g => g.First().Value), activeConfigPath);
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogLevel.Error, $"Failed to save capture setting: {ex.Message}");
            }
        }

        internal static void ReportCaptureFailure(string captureMethodKey, string backend, Exception exception)
        {
            if (!ReportedCaptureFailures.TryAdd(captureMethodKey + ":" + backend, 0)) return;
            string message = $"{captureMethodKey}: {backend} failed (0x{exception.HResult:X8}): {exception.Message}. Using GDI+. See logs/capture-error.txt.";
            try
            {
                var directory = System.IO.Path.Combine(AppContext.BaseDirectory, "logs");
                System.IO.Directory.CreateDirectory(directory);
                System.IO.File.AppendAllText(System.IO.Path.Combine(directory, "capture-error.txt"),
                    $"{DateTime.Now:O}\n{captureMethodKey}: {backend}\n{exception}\n");
            }
            catch { /* Error reporting must not prevent capture fallback. */ }

            // Never wait for the UI thread while holding a capture lock.
            try
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try { LogManager.Log(LogLevel.Error, message, true, 6000); }
                    catch { /* The UI may already be shutting down. */ }
                }));
            }
            catch { }
        }

        private void UpdateUIDropdown(string method)
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    var mainWindow = System.Windows.Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
                    if (mainWindow?.uiManager != null)
                    {
                        var dropdown = (CaptureMethodKey == "Scope Capture Method") 
                            ? mainWindow.uiManager.D_ScopeCaptureMethod 
                            : mainWindow.uiManager.D_ScreenCaptureMethod;

                        if (dropdown?.DropdownBox != null)
                        {
                            for (int i = 0; i < dropdown.DropdownBox.Items.Count; i++)
                            {
                                if ((dropdown.DropdownBox.Items[i] as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() == method)
                                {
                                    dropdown.DropdownBox.SelectedIndex = i;
                                    break;
                                }
                            }
                        }
                    }
                }
                catch { }
            });
        }

        #region dispose
        private void DisposeWgcResources()
        {
            if (_wgc == null) return;
            lock (_displayLock)
            {
                var capture = _wgc;
                _wgc = null;
                capture?.Dispose();
            }
        }

        public void DisposeDxgiResources()
        {
            lock (_displayLock)
            {
                try
                {

                    // Try to release any pending frame
                    if (_deskDuplication != null)
                    {
                        try
                        {
                            _deskDuplication.ReleaseFrame();
                        }
                        catch { }
                    }

                    _deskDuplication?.Dispose();
                    _stagingTex?.Dispose();
                    _dxDevice?.Dispose();
                    _cachedFrame?.Dispose();
                    directXBitmap?.Dispose();

                    _deskDuplication = null;
                    _stagingTex = null;
                    _dxDevice = null;
                    _cachedFrame = null;

                    // Small delay to ensure resources are fully released
                    //System.Threading.Thread.Sleep(50);
                }
                catch (Exception ex)
                {
                    LogManager.Log(LogLevel.Error, $"Error disposing DXGI resources: {ex.Message}");
                }
            }
        }
        public void Dispose()
        {
            lock (InstancesLock)
                Instances.RemoveAll(reference => !reference.TryGetTarget(out var manager) || ReferenceEquals(manager, this));
            DisplayManager.DisplayChanged -= OnDisplayChanged;
            DisposeWgcResources();
            DisposeDxgiResources();
            screenCaptureBitmap?.Dispose();
        }
        #endregion
    }
}
