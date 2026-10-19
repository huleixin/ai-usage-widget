using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CodexUsageWidgetApp;

// Prefer a clipped Windows Composition visual: GPU blur tracks the live desktop
// without screen capture. Older systems retain an optimized software fallback.
internal sealed class GlassBackdrop : IDisposable
{
    private readonly IntPtr _front;
    private readonly object _frameGate = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly ManualResetEvent _stop = new(false);
    private readonly Thread? _worker;
    private IntPtr _nativeGlass;
    private volatile bool _active, _disposed;
    private double _scale = 1;
    private byte[]? _pendingPixels;
    private int _pendingWidth, _pendingHeight;
    private double _pendingLight;
    private long _sequence, _shownSequence, _pendingCapturedAt;
    private WriteableBitmap? _image;
    private byte[]? _displayPixels, _targetPixels;
    private long _lastBlendAt;
    private bool _blendPending;
    private TimeSpan _lastRenderingTime = TimeSpan.MinValue;
    internal event Action<BitmapSource, double>? FrameReady;

    internal GlassBackdrop(IntPtr front)
    {
        _front = front;
        CompositionTarget.Rendering += PresentLatestFrame;
        int nativeError = 0;
        try { _nativeGlass = GlassCreate(front, out nativeError); }
        catch (DllNotFoundException) { nativeError = unchecked((int)0x8007007E); }
        catch (EntryPointNotFoundException) { nativeError = unchecked((int)0x8007007F); }
        if (_nativeGlass != IntPtr.Zero) return;
        _worker = new Thread(CaptureLoop) { IsBackground = true, Name = "Glass background renderer" };
        _worker.Start();
    }

    internal void Sync(Window front)
    {
        if (_disposed) return;
        double scale = PresentationSource.FromVisual(front)?.CompositionTarget?.TransformToDevice.M11 ?? 1;
        if (_nativeGlass != IntPtr.Zero)
        {
            if (front.IsVisible) GlassSync(_nativeGlass, (float)scale);
            return;
        }
        Volatile.Write(ref _scale, scale);
        _active = front.IsVisible && front.WindowState != WindowState.Minimized;
        _wake.Set();
    }

    private void CaptureLoop()
    {
        RenderBuffers? buffers = null;
        try
        {
            while (!_disposed)
            {
                if (!_active) { _wake.WaitOne(200); continue; }
                long cycleStarted = Stopwatch.GetTimestamp();
                try
                {
                    // Screen readback already synchronizes with desktop composition.
                    // An additional DwmFlush adds an avoidable full-frame wait.
                    if (_disposed || !GetWindowRect(_front, out var bounds)) continue;
                    int width = bounds.Right - bounds.Left, height = bounds.Bottom - bounds.Top;
                    if (width < 1 || height < 1) continue;
                    double scale = Volatile.Read(ref _scale);
                    if (buffers is null || buffers.PhysicalWidth != width || buffers.PhysicalHeight != height || Math.Abs(buffers.Scale - scale) > .01)
                    {
                        buffers?.Dispose();
                        buffers = new RenderBuffers(width, height, scale);
                    }
                    long capturedAt = Stopwatch.GetTimestamp();
                    if (!SetWindowDisplayAffinity(_front, 0x11)) throw new Win32Exception(Marshal.GetLastWin32Error());
                    long excludedAt = Stopwatch.GetTimestamp();
                    try { buffers.Capture(bounds); }
                    finally { SetWindowDisplayAffinity(_front, 0); }
                    long capturedEnd = Stopwatch.GetTimestamp();
                    double light = buffers.Filter();
                    long processedAt = Stopwatch.GetTimestamp();
                    lock (_frameGate)
                    {
                        byte[]? recycled = _pendingPixels;
                        _pendingPixels = buffers.Output;
                        buffers.Output = recycled is not null && recycled.Length == buffers.Output.Length ? recycled : new byte[buffers.Output.Length];
                        _pendingWidth = buffers.ImageWidth;
                        _pendingHeight = buffers.ImageHeight;
                        _pendingLight = light;
                        _pendingCapturedAt = capturedAt;
                        _sequence++;
                    }
                }
                catch (Exception error)
                {
                    _ = error;
                    if (_disposed) break;
                    _stop.WaitOne(100);
                }
                double spareMs = 1000.0 / 60 - ElapsedMs(cycleStarted, Stopwatch.GetTimestamp());
                if (spareMs > 1) _stop.WaitOne((int)Math.Floor(spareMs));
            }
        }
        finally
        {
            SetWindowDisplayAffinity(_front, 0);
            buffers?.Dispose();
            _wake.Dispose();
            _stop.Dispose();
        }
    }

    private void PresentLatestFrame(object? sender, EventArgs eventArgs)
    {
        if (_disposed || eventArgs is not RenderingEventArgs rendering || rendering.RenderingTime == _lastRenderingTime) return;
        _lastRenderingTime = rendering.RenderingTime;
        if (_nativeGlass != IntPtr.Zero) return;
        double light;
        lock (_frameGate)
        {
            if (_pendingPixels is null || (_shownSequence == _sequence && !_blendPending)) return;
            bool firstImage = _image is null || _image.PixelWidth != _pendingWidth || _image.PixelHeight != _pendingHeight;
            if (firstImage)
            {
                _image = new WriteableBitmap(_pendingWidth, _pendingHeight, 96, 96, PixelFormats.Bgr32, null);
                _displayPixels = new byte[_pendingPixels.Length];
                _targetPixels = new byte[_pendingPixels.Length];
                Array.Copy(_pendingPixels, _displayPixels, _pendingPixels.Length);
            }
            if (_shownSequence != _sequence)
                Array.Copy(_pendingPixels, _targetPixels!, _pendingPixels.Length);
            _shownSequence = _sequence;
            light = _pendingLight;
            long presentedAt = Stopwatch.GetTimestamp();
            double deltaMs = _lastBlendAt == 0 ? 16.667 : Math.Clamp(ElapsedMs(_lastBlendAt, presentedAt), 1, 60);
            _lastBlendAt = presentedAt;
            int blend = (int)Math.Clamp(Math.Round((1 - Math.Exp(-deltaMs / 12)) * 256), 1, 256);
            bool changed = firstImage;
            _blendPending = false;
            for (int pixel = 0; pixel < _pendingPixels.Length; pixel += 4)
                for (int channel = 0; channel < 3; channel++)
                {
                    int i = pixel + channel;
                    int difference = _targetPixels![i] - _displayPixels![i];
                    if (difference == 0) continue;
                    int step = (Math.Abs(difference) * blend + 255) >> 8;
                    _displayPixels[i] = (byte)(_displayPixels[i] + (difference > 0 ? step : -step));
                    changed = true;
                    if (_displayPixels[i] != _targetPixels[i]) _blendPending = true;
                }
            if (!changed) return;
            _image!.WritePixels(new Int32Rect(0, 0, _pendingWidth, _pendingHeight), _displayPixels!, _pendingWidth * 4, 0);
        }
        FrameReady?.Invoke(_image!, light);
    }

    private sealed class RenderBuffers : IDisposable
    {
        internal readonly int PhysicalWidth, PhysicalHeight, ImageWidth, ImageHeight, CaptureWidth, CaptureHeight;
        internal readonly double Scale;
        internal readonly byte[] Source;
        internal byte[] Output;
        private readonly byte[] _scratch;
        private readonly byte[] _fullCapture;
        private readonly int[] _lensOffsets;
        private readonly int _padding, _radius, _sourceWidth, _sourceHeight;
        private readonly IntPtr _screen, _memory, _bitmap, _original, _bits;
        internal double LastReadMs { get; private set; }

        internal RenderBuffers(int width, int height, double scale)
        {
            PhysicalWidth = width; PhysicalHeight = height; Scale = scale;
            // Blur contains no text: half-resolution sampling preserves its appearance
            // while leaving foreground labels and antialiased corners at full resolution.
            ImageWidth = (width + 1) / 2; ImageHeight = (height + 1) / 2;
            _padding = Math.Max(20, (int)Math.Round(24 * scale));
            _sourceWidth = width + _padding * 2; _sourceHeight = height + _padding * 2;
            CaptureWidth = (_sourceWidth + 1) / 2; CaptureHeight = (_sourceHeight + 1) / 2;
            _radius = Math.Max(2, (int)Math.Round(3 * scale));
            Source = new byte[CaptureWidth * CaptureHeight * 4];
            _fullCapture = new byte[_sourceWidth * _sourceHeight * 4];
            _scratch = new byte[Source.Length];
            Output = new byte[ImageWidth * ImageHeight * 4];
            _lensOffsets = new int[ImageWidth * ImageHeight];
            PrecomputeLens();
            _screen = GetDC(IntPtr.Zero);
            _memory = CreateCompatibleDC(_screen);
            var info = new BitmapInfo { Header = new BitmapHeader { Size = 40, Width = _sourceWidth, Height = -_sourceHeight, Planes = 1, Bits = 32 } };
            _bitmap = CreateDIBSection(_memory, ref info, 0, out _bits, IntPtr.Zero, 0);
            if (_bitmap == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            _original = SelectObject(_memory, _bitmap);
        }

        internal void Capture(Rect bounds)
        {
            long started = Stopwatch.GetTimestamp();
            if (!BitBlt(_memory, 0, 0, _sourceWidth, _sourceHeight, _screen, bounds.Left - _padding, bounds.Top - _padding, 0x00CC0020))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            Marshal.Copy(_bits, _fullCapture, 0, _fullCapture.Length);
            for (int y = 0; y < CaptureHeight; y++)
                for (int x = 0; x < CaptureWidth; x++)
                {
                    int sx = Math.Min(x * 2, _sourceWidth - 1), sy = Math.Min(y * 2, _sourceHeight - 1);
                    int a = (sy * _sourceWidth + sx) * 4;
                    int b = (sy * _sourceWidth + Math.Min(sx + 1, _sourceWidth - 1)) * 4;
                    int c = (Math.Min(sy + 1, _sourceHeight - 1) * _sourceWidth + sx) * 4;
                    int d = (Math.Min(sy + 1, _sourceHeight - 1) * _sourceWidth + Math.Min(sx + 1, _sourceWidth - 1)) * 4;
                    int target = (y * CaptureWidth + x) * 4;
                    for (int channel = 0; channel < 3; channel++)
                        Source[target + channel] = (byte)((_fullCapture[a + channel] + _fullCapture[b + channel] + _fullCapture[c + channel] + _fullCapture[d + channel] + 2) >> 2);
                }
            LastReadMs = ElapsedMs(started, Stopwatch.GetTimestamp());
        }

        private void PrecomputeLens()
        {
            double corner = 24 * Scale;
            for (int y = 0; y < ImageHeight; y++)
                for (int x = 0; x < ImageWidth; x++)
                {
                    double px = (x + .5) * PhysicalWidth / ImageWidth - .5;
                    double py = (y + .5) * PhysicalHeight / ImageHeight - .5;
                    double qx = Math.Abs(px + .5 - PhysicalWidth / 2.0) - (PhysicalWidth / 2.0 - corner);
                    double qy = Math.Abs(py + .5 - PhysicalHeight / 2.0) - (PhysicalHeight / 2.0 - corner);
                    double ox = Math.Max(qx, 0), oy = Math.Max(qy, 0);
                    double distance = Math.Min(Math.Max(qx, qy), 0) + Math.Sqrt(ox * ox + oy * oy) - corner;
                    double bend = Math.Exp(-Math.Max(0, -distance) / (7 * Scale)) * 3.3 * Scale;
                    double sx = _padding + px + bend * (PhysicalWidth / 2.0 - px) / (PhysicalWidth / 2.0);
                    double sy = _padding + py + bend * (PhysicalHeight / 2.0 - py) / (PhysicalHeight / 2.0);
                    int ix = Math.Clamp((int)Math.Round(sx * CaptureWidth / _sourceWidth), 0, CaptureWidth - 1);
                    int iy = Math.Clamp((int)Math.Round(sy * CaptureHeight / _sourceHeight), 0, CaptureHeight - 1);
                    _lensOffsets[y * ImageWidth + x] = (iy * CaptureWidth + ix) * 4;
                }
        }

        internal double Filter()
        {
            double light = 0; int samples = 0;
            for (int y = 12; y < CaptureHeight - 12; y += 5)
                for (int x = 12; x < CaptureWidth - 12; x += 5)
                {
                    int p = (y * CaptureWidth + x) * 4;
                    light += (.0722 * Source[p] + .7152 * Source[p + 1] + .2126 * Source[p + 2]) / 255;
                    samples++;
                }
            for (int pass = 0; pass < 3; pass++)
            {
                BlurHorizontal(Source, _scratch, CaptureWidth, CaptureHeight, _radius);
                BlurVertical(_scratch, Source, CaptureWidth, CaptureHeight, _radius);
            }
            for (int pixel = 0; pixel < _lensOffsets.Length; pixel++)
            {
                int from = _lensOffsets[pixel], to = pixel * 4;
                Output[to] = Source[from]; Output[to + 1] = Source[from + 1]; Output[to + 2] = Source[from + 2];
            }
            return light / Math.Max(1, samples);
        }

        public void Dispose()
        {
            if (_original != IntPtr.Zero) SelectObject(_memory, _original);
            if (_bitmap != IntPtr.Zero) DeleteObject(_bitmap);
            if (_memory != IntPtr.Zero) DeleteDC(_memory);
            if (_screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, _screen);
        }
    }

    private static void BlurHorizontal(byte[] input, byte[] output, int width, int height, int radius)
    {
        int count = radius * 2 + 1;
        for (int y = 0; y < height; y++)
        {
            int row = y * width * 4;
            for (int c = 0; c < 3; c++)
            {
                int sum = 0;
                for (int i = -radius; i <= radius; i++) sum += input[row + Math.Clamp(i, 0, width - 1) * 4 + c];
                for (int x = 0; x < width; x++)
                {
                    output[row + x * 4 + c] = (byte)(sum / count);
                    sum += input[row + Math.Min(width - 1, x + radius + 1) * 4 + c] - input[row + Math.Max(0, x - radius) * 4 + c];
                }
            }
        }
    }
    private static void BlurVertical(byte[] input, byte[] output, int width, int height, int radius)
    {
        int count = radius * 2 + 1;
        for (int x = 0; x < width; x++)
            for (int c = 0; c < 3; c++)
            {
                int sum = 0;
                for (int i = -radius; i <= radius; i++) sum += input[(Math.Clamp(i, 0, height - 1) * width + x) * 4 + c];
                for (int y = 0; y < height; y++)
                {
                    output[(y * width + x) * 4 + c] = (byte)(sum / count);
                    sum += input[(Math.Min(height - 1, y + radius + 1) * width + x) * 4 + c] - input[(Math.Max(0, y - radius) * width + x) * 4 + c];
                }
            }
    }
    private static double ElapsedMs(long from, long to) => (to - from) * 1000.0 / Stopwatch.Frequency;
    public void Dispose()
    {
        if (_disposed) return;
        CompositionTarget.Rendering -= PresentLatestFrame;
        FrameReady = null;
        if (_nativeGlass != IntPtr.Zero)
        {
            GlassDestroy(_nativeGlass);
            _nativeGlass = IntPtr.Zero;
            _wake.Dispose(); _stop.Dispose();
            _disposed = true;
            return;
        }
        _active = false;
        _stop.Set(); _wake.Set();
        _disposed = true;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapHeader { public uint Size; public int Width, Height; public ushort Planes, Bits; public uint Compression, ImageSize; public int XPixels, YPixels; public uint ColorsUsed, ColorsImportant; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo { public BitmapHeader Header; public uint Color; }
    [DllImport("LiquidGlassNative.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr GlassCreate(IntPtr front, out int error);
    [DllImport("LiquidGlassNative.dll", CallingConvention = CallingConvention.Cdecl)] private static extern void GlassSync(IntPtr glass, float scale);
    [DllImport("LiquidGlassNative.dll", CallingConvention = CallingConvention.Cdecl)] private static extern void GlassDestroy(IntPtr glass);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr item);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern bool BitBlt(IntPtr destination, int dx, int dy, int width, int height, IntPtr source, int sx, int sy, uint operation);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr item);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
}
