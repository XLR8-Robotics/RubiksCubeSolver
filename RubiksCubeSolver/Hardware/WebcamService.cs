using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using System.Windows.Media.Imaging;

namespace RubiksCubeSolver.Hardware;

public sealed class WebcamService : IDisposable
{
    readonly object _gate = new();
    VideoCapture? _capture;
    int _index = -1;

    public bool IsOpen
    {
        get
        {
            lock (_gate)
            {
                return _capture is not null && _capture.IsOpened();
            }
        }
    }

    public string Resolution { get; private set; } = "not open";

    public bool Open(int index)
    {
        lock (_gate)
        {
            CloseUnlocked();
            var capture = new VideoCapture(index, VideoCaptureAPIs.DSHOW);
            if (!capture.IsOpened())
            {
                capture.Dispose();
                capture = new VideoCapture(index);
            }

            if (!capture.IsOpened())
            {
                capture.Dispose();
                return false;
            }

            capture.Set(VideoCaptureProperties.FrameWidth, 1280);
            capture.Set(VideoCaptureProperties.FrameHeight, 720);
            _capture = capture;
            _index = index;
            var width = (int)capture.Get(VideoCaptureProperties.FrameWidth);
            var height = (int)capture.Get(VideoCaptureProperties.FrameHeight);
            Resolution = $"{width}x{height}";
            return true;
        }
    }

    public Mat? Grab(bool rotate180)
    {
        lock (_gate)
        {
            if (_capture is null || !_capture.IsOpened())
            {
                return null;
            }

            var frame = new Mat();
            if (!_capture.Read(frame) || frame.Empty())
            {
                frame.Dispose();
                return null;
            }

            if (rotate180)
            {
                Cv2.Rotate(frame, frame, RotateFlags.Rotate180);
            }

            return frame;
        }
    }

    public BitmapSource? GrabBitmap(bool rotate180)
    {
        using var mat = Grab(rotate180);
        return mat is null ? null : mat.ToBitmapSource();
    }

    public async Task<Mat?> GrabSettledAsync(int warmupMs, bool rotate180, CancellationToken cancellationToken)
    {
        var deadline = Environment.TickCount64 + Math.Max(0, warmupMs);
        Mat? last = null;
        while (Environment.TickCount64 < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            last?.Dispose();
            last = Grab(rotate180);
            await Task.Delay(40, cancellationToken);
        }

        last?.Dispose();
        return Grab(rotate180);
    }

    public void Close()
    {
        lock (_gate)
        {
            CloseUnlocked();
        }
    }

    void CloseUnlocked()
    {
        _capture?.Release();
        _capture?.Dispose();
        _capture = null;
        _index = -1;
        Resolution = "not open";
    }

    public void Dispose() => Close();
}
