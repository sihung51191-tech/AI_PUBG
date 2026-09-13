using System.Drawing;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;
using Color = System.Drawing.Color;

namespace AILogic;

/// <summary>Independent WGC session. All access is serialized by CaptureManager.</summary>
internal sealed class WindowsGraphicsCapture : IDisposable
{
    private ID3D11Device? _device;
    private IDirect3DDevice? _winrtDevice;
    private GraphicsCaptureItem? _item;
    private Direct3D11CaptureFramePool? _pool;
    private readonly object _frameLock = new();
    private ID3D11Texture2D? _latestTexture;
    private Rectangle _latestBounds;
    private Exception? _frameError;
    private bool _disposed;
    private GraphicsCaptureSession? _session;
    private Bitmap? _bitmap;
    private Windows.Graphics.SizeInt32 _size;
    private Rectangle _displayBounds;
    private volatile bool _closed;
    public bool ConvertedToTensor { get; private set; }
    public bool WaitingForNewFrame { get; private set; }
    private long _frameNumber;
    private long _latestFrameReceiptTimestamp;

    public long FrameNumber => Interlocked.Read(ref _frameNumber);
    public long LatestFrameReceiptTimestamp => Interlocked.Read(ref _latestFrameReceiptTimestamp);

    public WindowsGraphicsCapture(Rectangle displayBounds)
    {
        try
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362) || !GraphicsCaptureSession.IsSupported())
                throw new NotSupportedException("Windows Graphics Capture requires Windows 10 1903 or newer and a supported GPU.");

            _displayBounds = displayBounds;
            var monitor = MonitorFromPoint(new NativePoint(displayBounds.Left + displayBounds.Width / 2,
                displayBounds.Top + displayBounds.Height / 2), 0);
            if (monitor == IntPtr.Zero) throw new InvalidOperationException("The selected monitor is no longer available.");

            D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport,
                null, out _device).CheckError();
            using (var dxgiDevice = _device!.QueryInterface<IDXGIDevice>())
            {
                Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out var pointer));
                try { _winrtDevice = MarshalInterface<IDirect3DDevice>.FromAbi(pointer); }
                finally { Marshal.Release(pointer); }
            }

            _item = CreateItem(monitor);
            _item.Closed += OnClosed;
            _size = _item.Size;
            _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_winrtDevice,
                DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _size);
            _session = _pool.CreateCaptureSession(_item);
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
                _session.IsCursorCaptureEnabled = false;
            _session.StartCapture();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private void OnClosed(GraphicsCaptureItem sender, object args) => _closed = true;

    private void RefreshFrame(Rectangle bounds)
    {
        lock (_frameLock)
        {
            if (_disposed) return;
            try
            {
                // Do not retain pool-owned frames between inference calls. Drain queued
                // frames, copy the newest into our own texture, and release the pool buffer.
                var sender = _pool!;
                using var first = sender.TryGetNextFrame();
                if (first == null) return;
                using var second = sender.TryGetNextFrame();
                var frame = second ?? first;
                if (frame == null) return;
                var size = frame.ContentSize;
                if (size.Width <= 0 || size.Height <= 0) return;
                if (size.Width != _size.Width || size.Height != _size.Height)
                {
                    _latestTexture?.Dispose();
                    _latestTexture = null;
                    _size = size;
                    second?.Dispose();
                    first.Dispose();
                    sender.Recreate(_winrtDevice!, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, size);
                    return;
                }
                var access = frame.Surface.As<IDirect3DDxgiInterfaceAccess>();
                var textureId = typeof(ID3D11Texture2D).GUID;
                Marshal.ThrowExceptionForHR(access.GetInterface(ref textureId, out var pointer));
                using var texture = new ID3D11Texture2D(pointer);
                if (texture.Description.Format != Format.B8G8R8A8_UNorm)
                    throw new NotSupportedException($"Unexpected WGC pixel format: {texture.Description.Format}");
                if (_latestTexture == null || _latestTexture.Description.Width != bounds.Width
                    || _latestTexture.Description.Height != bounds.Height)
                {
                    _latestTexture?.Dispose();
                    var description = texture.Description;
                    description.Width = (uint)bounds.Width;
                    description.Height = (uint)bounds.Height;
                    description.Usage = ResourceUsage.Staging;
                    description.BindFlags = BindFlags.None;
                    description.CPUAccessFlags = CpuAccessFlags.Read;
                    description.MiscFlags = ResourceOptionFlags.None;
                    _latestTexture = _device!.CreateTexture2D(description);
                }
                int x = bounds.Left - _displayBounds.Left, y = bounds.Top - _displayBounds.Top;
                int left = Math.Max(0, x), top = Math.Max(0, y);
                int right = Math.Min(Math.Min(size.Width, (int)texture.Description.Width), x + bounds.Width);
                int bottom = Math.Min(Math.Min(size.Height, (int)texture.Description.Height), y + bounds.Height);
                if (right > left && bottom > top)
                {
                    _device!.ImmediateContext.CopySubresourceRegion(_latestTexture, 0, 0, 0, 0,
                        texture, 0, new Box(left, top, 0, right, bottom, 1));
                    // Submit the ROI copy before returning the source buffer to WGC.
                    _device.ImmediateContext.Flush();
                }
                _latestBounds = bounds;
                _frameNumber++;
                _latestFrameReceiptTimestamp = Stopwatch.GetTimestamp();
            }
            catch (Exception ex) { _frameError = ex; }
        }
    }

    public unsafe Bitmap? Capture(Rectangle bounds, bool thirdPersonSupport, float[]? tensor = null, bool requireNewFrame = false)
    {
        lock (_frameLock)
        {
            ConvertedToTensor = false;
            WaitingForNewFrame = false;
            if (bounds.Width <= 0 || bounds.Height <= 0) return null;
            long previousFrame = _frameNumber;
            RefreshFrame(bounds);
            if (_frameError != null) throw new InvalidOperationException("WGC frame delivery failed.", _frameError);
            if (_closed || _disposed) throw new InvalidOperationException("The WGC capture session was closed.");
            if (requireNewFrame && previousFrame == _frameNumber)
            {
                WaitingForNewFrame = true;
                return null;
            }
            return CaptureLatest(bounds, thirdPersonSupport, tensor);
        }
    }

    private unsafe Bitmap? CaptureLatest(Rectangle bounds, bool thirdPersonSupport, float[]? tensor)
    {
        if (_frameError != null) throw new InvalidOperationException("WGC frame delivery failed.", _frameError);
        if (_closed) throw new InvalidOperationException("The WGC monitor capture session was closed.");
        if (bounds.Width <= 0 || bounds.Height <= 0) return null;

        var texture = _latestTexture;
        if (texture == null || _latestBounds != bounds) return null;
        // An unchanged desktop may not deliver another frame. The cached ROI is
        // valid until a newer frame, a display change, or an explicit session error.
        var size = _size;
        if (tensor != null && tensor.Length != checked(3 * bounds.Width * bounds.Height))
            throw new ArgumentException("WGC tensor dimensions must match the capture region.");
        if (tensor == null && (_bitmap == null || _bitmap.Width != bounds.Width || _bitmap.Height != bounds.Height))
        {
            _bitmap?.Dispose();
            _bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        }
        // Black padding for regions outside this monitor, including monitors at negative coordinates.
        if (tensor == null) { using var graphics = Graphics.FromImage(_bitmap!); graphics.Clear(Color.Black); }
        int relativeX = bounds.Left - _displayBounds.Left;
        int relativeY = bounds.Top - _displayBounds.Top;
        int left = Math.Max(0, relativeX), top = Math.Max(0, relativeY);
        int right = Math.Min(size.Width, relativeX + bounds.Width);
        int bottom = Math.Min(size.Height, relativeY + bounds.Height);
        if (right > left && bottom > top)
        {
            var context = _device!.ImmediateContext;
            // The owned ROI is CPU-readable: no second GPU copy is needed.
            var mapped = context.Map(texture, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            try
            {
                if (tensor != null)
                {
                    if (bounds.Width == bounds.Height && left == relativeX && top == relativeY && right - left == bounds.Width && bottom - top == bounds.Height)
                        MathUtil.DxMapToFloatArray((byte*)mapped.DataPointer, (int)mapped.RowPitch, tensor, bounds.Width, thirdPersonSupport);
                    else
                    {
                        Array.Clear(tensor); // Black padding for a crop crossing the monitor edge.
                        int plane = bounds.Width * bounds.Height;
                        for (int y = 0; y < bottom - top; y++)
                        {
                            byte* source = (byte*)mapped.DataPointer + y * mapped.RowPitch;
                            int dy = y + top - relativeY;
                            for (int x = 0; x < right - left; x++)
                            {
                                int dx = x + left - relativeX;
                                if (thirdPersonSupport && dy >= bounds.Height - bounds.Height / 2 && dx < bounds.Width / 2) continue;
                                int index = dy * bounds.Width + dx;
                                tensor[index] = source[x * 4 + 2] / 255f;
                                tensor[plane + index] = source[x * 4 + 1] / 255f;
                                tensor[2 * plane + index] = source[x * 4] / 255f;
                            }
                        }
                    }
                    ConvertedToTensor = true;
                    return null;
                }
                var data = _bitmap.LockBits(new Rectangle(0, 0, bounds.Width, bounds.Height),
                    ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    int rowBytes = (right - left) * 4;
                    for (int y = 0; y < bottom - top; y++)
                    {
                        byte* source = (byte*)mapped.DataPointer + y * mapped.RowPitch;
                        byte* destination = (byte*)data.Scan0 + (y + top - relativeY) * data.Stride + (left - relativeX) * 4;
                        Buffer.MemoryCopy(source, destination, rowBytes, rowBytes);
                    }
                }
                finally { _bitmap.UnlockBits(data); }
            }
            finally { context.Unmap(texture, 0); }
        }
        if (tensor != null) { Array.Clear(tensor); ConvertedToTensor = true; return null; }
        if (thirdPersonSupport)
        {
            using var graphics = Graphics.FromImage(_bitmap);
            using var brush = new SolidBrush(Color.Black);
            graphics.FillRectangle(brush, 0, bounds.Height - bounds.Height / 2, bounds.Width / 2, bounds.Height / 2);
        }
        return _bitmap;
    }

    public void Dispose()
    {
        lock (_frameLock) { _disposed = true; }
        try { if (_item != null) _item.Closed -= OnClosed; } catch { }
        // Close the frame pool after releasing any in-flight capture operation.
        CloseSafely(_session); _session = null;
        CloseSafely(_pool); _pool = null;
        _item = null;
        lock (_frameLock)
        {
            CloseSafely(_latestTexture); _latestTexture = null;
            CloseSafely(_bitmap); _bitmap = null;
            CloseSafely(_winrtDevice); _winrtDevice = null;
            CloseSafely(_device); _device = null;
        }
    }
    private static void CloseSafely(IDisposable? resource)
    {
        try { resource?.Dispose(); }
        catch { /* Device/session loss must not interrupt the remaining cleanup. */ }
    }

    private static GraphicsCaptureItem CreateItem(IntPtr monitor)
    {
        const string className = "Windows.Graphics.Capture.GraphicsCaptureItem";
        Marshal.ThrowExceptionForHR(WindowsCreateString(className, className.Length, out var name));
        try
        {
            var iid = typeof(IGraphicsCaptureItemInterop).GUID;
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(name, ref iid, out var factory));
            try
            {
                var itemId = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
                Marshal.ThrowExceptionForHR(factory.CreateForMonitor(monitor, ref itemId, out var pointer));
                try { return MarshalInspectable<GraphicsCaptureItem>.FromAbi(pointer); }
                finally { Marshal.Release(pointer); }
            }
            finally { Marshal.ReleaseComObject(factory); }
        }
        finally { WindowsDeleteString(name); }
    }

    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        [PreserveSig] int CreateForWindow(IntPtr window, ref Guid iid, out IntPtr item);
        [PreserveSig] int CreateForMonitor(IntPtr monitor, ref Guid iid, out IntPtr item);
    }

    [ComImport, Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        [PreserveSig] int GetInterface(ref Guid iid, out IntPtr result);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativePoint(int x, int y) { public readonly int X = x; public readonly int Y = y; }

    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);
    [DllImport("d3d11.dll", ExactSpelling = true)] private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr device, out IntPtr result);
    [DllImport("combase.dll", ExactSpelling = true, CharSet = CharSet.Unicode)] private static extern int WindowsCreateString(string value, int length, out IntPtr result);
    [DllImport("combase.dll", ExactSpelling = true)] private static extern int WindowsDeleteString(IntPtr value);
    [DllImport("combase.dll", ExactSpelling = true)] private static extern int RoGetActivationFactory(IntPtr name, ref Guid iid, out IGraphicsCaptureItemInterop factory);
}


