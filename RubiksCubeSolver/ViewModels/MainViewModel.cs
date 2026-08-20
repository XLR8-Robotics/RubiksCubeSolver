using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using RubiksCubeSolver.Hardware;
using RubiksCubeSolver.Models;
using RubiksCubeSolver.Robot;
using RubiksCubeSolver.Robot.Scan;
using RubiksCubeSolver.Solver;
using RubiksCubeSolver.Solver.Kociemba;
using RubiksCubeSolver.Vision;
using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace RubiksCubeSolver.ViewModels;

public sealed class StickerCell : ObservableObject
{
    public StickerCell(int index) => Index = index;

    public int Index { get; }

    StickerColor _color = StickerColor.Unknown;
    public StickerColor Color
    {
        get => _color;
        set
        {
            if (SetProperty(ref _color, value))
            {
                OnPropertyChanged(nameof(Brush));
            }
        }
    }

    public Brush Brush => StickerPalette.BrushFor(Color);

    int _hue;
    public int Hue
    {
        get => _hue;
        set
        {
            if (SetProperty(ref _hue, value))
                OnPropertyChanged(nameof(SampleReadout));
        }
    }

    int _saturation;
    public int Saturation
    {
        get => _saturation;
        set
        {
            if (SetProperty(ref _saturation, value))
                OnPropertyChanged(nameof(SampleReadout));
        }
    }

    int _value;
    public int Value
    {
        get => _value;
        set
        {
            if (SetProperty(ref _value, value))
                OnPropertyChanged(nameof(SampleReadout));
        }
    }

    public int Number => Index + 1;

    public string SampleReadout => $"H {Hue}  S {Saturation}  V {Value}";

    public void ApplySample(StickerColor color, HsvSample hsv)
    {
        Color = color;
        Hue = hsv.H;
        Saturation = hsv.S;
        Value = hsv.V;
    }
}

public static class StickerPalette
{
    public static Brush BrushFor(StickerColor color) => color switch
    {
        StickerColor.White => new SolidColorBrush(Colors.White),
        StickerColor.Yellow => new SolidColorBrush(Color.FromRgb(255, 213, 0)),
        StickerColor.Red => new SolidColorBrush(Color.FromRgb(196, 30, 58)),
        StickerColor.Orange => new SolidColorBrush(Color.FromRgb(255, 88, 0)),
        StickerColor.Blue => new SolidColorBrush(Color.FromRgb(0, 81, 186)),
        StickerColor.Green => new SolidColorBrush(Color.FromRgb(0, 158, 96)),
        _ => new SolidColorBrush(Color.FromRgb(52, 58, 70))
    };

    public static StickerColor Next(StickerColor color) => color switch
    {
        StickerColor.Unknown => StickerColor.White,
        StickerColor.White => StickerColor.Yellow,
        StickerColor.Yellow => StickerColor.Red,
        StickerColor.Red => StickerColor.Orange,
        StickerColor.Orange => StickerColor.Blue,
        StickerColor.Blue => StickerColor.Green,
        _ => StickerColor.Unknown
    };
}

public partial class MainViewModel : ObservableObject
{
    readonly MaestroController _maestro = new();
    readonly WebcamService _webcam = new();
    readonly DispatcherTimer _previewTimer;
    readonly StringBuilder _log = new();
    CancellationTokenSource? _runCts;
    RobotController? _robot;

    readonly DigitalCube _digital = new();
    bool _ignoreCameraChange;
    bool _regeneratingScanLayout;

    public MainViewModel() : this(AppSettings.Load(), runStartupTasks: true)
    {
    }

    internal MainViewModel(AppSettings settings, bool runStartupTasks)
    {
        Settings = settings;
        Stickers = new ObservableCollection<StickerCell>(Enumerable.Range(0, 54).Select(i => new StickerCell(i)));
        ScanPreviewStickers = new ObservableCollection<StickerCell>(Enumerable.Range(0, 9).Select(i => new StickerCell(i)));
        ScanRectangles = new ObservableCollection<NormalizedScanRect>(
            Settings.ScanRectangles is { Count: 9 } && Settings.ScanRectangles.All(ScanGridLayout.IsValid)
                ? Settings.ScanRectangles
                : []);
        ApplyStickers(_digital.Colors);
        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _previewTimer.Tick += (_, _) => TickPreview();
        TestMode = Settings.TestMode;
        if (runStartupTasks)
        {
            RefreshDevices();
        }

        if (!TestMode)
        {
            AppendLog("Ready. Pick your webcam, connect the Mini Maestro Command Port, then Open the arms and insert a cube.");
            AppendLog("Set the Maestro serial mode to USB Dual Port in Pololu Maestro Control Center.");
            AppendLog("Turn on Test mode to scramble and solve the digital cube with no hardware.");
        }

        if (!runStartupTasks)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                AppendLog("Building Kociemba solver tables (first run can take a minute)...");
                CubeSolver.Warmup();
                AppendLog("Solver tables ready.");
            }
            catch (Exception ex)
            {
                AppendLog("Solver table build failed: " + ex.Message);
            }
        });
    }

    public AppSettings Settings { get; }
    public ObservableCollection<StickerCell> Stickers { get; }
    public ObservableCollection<StickerCell> ScanPreviewStickers { get; }
    public ObservableCollection<NormalizedScanRect> ScanRectangles { get; }
    public ObservableCollection<SerialDevice> SerialPorts { get; } = [];
    public ObservableCollection<CameraDevice> Cameras { get; } = [];
    public Func<CubeMove, Action, CancellationToken, Task>? AnimateDigitalMove { get; set; }

    [ObservableProperty] SerialDevice? selectedPort;
    [ObservableProperty] CameraDevice? selectedCamera;
    [ObservableProperty] BitmapSource? cameraImage;
    [ObservableProperty] string statusText = "Idle";
    [ObservableProperty] string logText = "";
    [ObservableProperty] string solutionText = "";
    [ObservableProperty] string connectionText = "Disconnected";
    [ObservableProperty] bool isBusy;
    [ObservableProperty] bool testMode;
    [ObservableProperty] bool zenModeRunning;
    [ObservableProperty] double progress;
    [ObservableProperty] string cameraResolution = "";
    [ObservableProperty] ScanGridEditMode scanGridEditMode = ScanGridEditMode.MoveGrid;
    [ObservableProperty] int cameraFrameWidth;
    [ObservableProperty] int cameraFrameHeight;

    public bool CanOperate => !IsBusy;

    public double FaceMargin
    {
        get => Settings.FaceMargin;
        set
        {
            var next = Math.Clamp(value, 0, 0.42);
            if (Math.Abs(Settings.FaceMargin - next) < 0.0005)
            {
                return;
            }

            Settings.FaceMargin = next;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FaceGridSize));
            RegenerateRegularScanLayout();
        }
    }

    public double FaceGridSize
    {
        get => Math.Clamp(1 - 2 * Settings.FaceMargin, 0.16, 1);
        set
        {
            var size = Math.Clamp(value, 0.16, 1);
            FaceMargin = (1 - size) / 2;
        }
    }

    public double FaceOffsetX
    {
        get => Settings.FaceOffsetX;
        set
        {
            var next = Math.Clamp(value, -0.35, 0.35);
            if (Math.Abs(Settings.FaceOffsetX - next) < 0.0005)
            {
                return;
            }

            Settings.FaceOffsetX = next;
            OnPropertyChanged();
            RegenerateRegularScanLayout();
        }
    }

    public double FaceOffsetY
    {
        get => Settings.FaceOffsetY;
        set
        {
            var next = Math.Clamp(value, -0.35, 0.35);
            if (Math.Abs(Settings.FaceOffsetY - next) < 0.0005)
            {
                return;
            }

            Settings.FaceOffsetY = next;
            OnPropertyChanged();
            RegenerateRegularScanLayout();
        }
    }

    public double FaceSampleInset
    {
        get => Settings.FaceSampleInset;
        set
        {
            var next = Math.Clamp(value, 0.04, 0.42);
            if (Math.Abs(Settings.FaceSampleInset - next) < 0.0005)
            {
                return;
            }

            Settings.FaceSampleInset = next;
            OnPropertyChanged();
            RegenerateRegularScanLayout();
        }
    }

    public int RedHueFrom
    {
        get => HueSplits.RedHueFrom;
        set => SetHueRange(nameof(RedHueFrom), splits => splits.RedHueFrom = value);
    }

    public int RedHueTo
    {
        get => HueSplits.RedHueTo;
        set => SetHueRange(nameof(RedHueTo), splits => splits.RedHueTo = value);
    }

    public int OrangeHueFrom
    {
        get => HueSplits.OrangeHueFrom;
        set => SetHueRange(nameof(OrangeHueFrom), splits => splits.OrangeHueFrom = value);
    }

    public int OrangeHueTo
    {
        get => HueSplits.OrangeHueTo;
        set => SetHueRange(nameof(OrangeHueTo), splits => splits.OrangeHueTo = value);
    }

    public int YellowHueFrom
    {
        get => HueSplits.YellowHueFrom;
        set => SetHueRange(nameof(YellowHueFrom), splits => splits.YellowHueFrom = value);
    }

    public int YellowHueTo
    {
        get => HueSplits.YellowHueTo;
        set => SetHueRange(nameof(YellowHueTo), splits => splits.YellowHueTo = value);
    }

    public int GreenHueFrom
    {
        get => HueSplits.GreenHueFrom;
        set => SetHueRange(nameof(GreenHueFrom), splits => splits.GreenHueFrom = value);
    }

    public int GreenHueTo
    {
        get => HueSplits.GreenHueTo;
        set => SetHueRange(nameof(GreenHueTo), splits => splits.GreenHueTo = value);
    }

    public int BlueHueFrom
    {
        get => HueSplits.BlueHueFrom;
        set => SetHueRange(nameof(BlueHueFrom), splits => splits.BlueHueFrom = value);
    }

    public int BlueHueTo
    {
        get => HueSplits.BlueHueTo;
        set => SetHueRange(nameof(BlueHueTo), splits => splits.BlueHueTo = value);
    }

    public int WhiteSaturation
    {
        get => HueSplits.WhiteSaturation;
        set => SetHueRange(nameof(WhiteSaturation), splits => splits.WhiteSaturation = value);
    }

    ColorHueSplits HueSplits => Settings.EnsureColorHueSplits();

    void SetHueRange(string propertyName, Action<ColorHueSplits> assign)
    {
        var current = HueSplits;
        assign(current);
        var next = current.Normalized();
        Settings.ColorHueSplits = next;
        Settings.RedOrangeHueSplit = next.RedOrange;
        OnPropertyChanged(propertyName);
        OnPropertyChanged(nameof(RedHueFrom));
        OnPropertyChanged(nameof(RedHueTo));
        OnPropertyChanged(nameof(OrangeHueFrom));
        OnPropertyChanged(nameof(OrangeHueTo));
        OnPropertyChanged(nameof(YellowHueFrom));
        OnPropertyChanged(nameof(YellowHueTo));
        OnPropertyChanged(nameof(GreenHueFrom));
        OnPropertyChanged(nameof(GreenHueTo));
        OnPropertyChanged(nameof(BlueHueFrom));
        OnPropertyChanged(nameof(BlueHueTo));
        OnPropertyChanged(nameof(WhiteSaturation));
    }

    public bool FaceAutoDetect
    {
        get => Settings.FaceAutoDetect;
        set
        {
            if (Settings.FaceAutoDetect == value)
            {
                return;
            }

            Settings.FaceAutoDetect = value;
            OnPropertyChanged();
        }
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanOperate));

    partial void OnTestModeChanged(bool value)
    {
        Settings.TestMode = value;
        _maestro.Simulate = value;
        Settings.Save();
        if (value)
        {
            _previewTimer.Stop();
            _webcam.Close();
            _maestro.Disconnect();
            _robot = null;
            ConnectionText = "Test mode — digital only";
            AppendLog("Test mode on. Start will scramble and solve the digital cube with no Maestro, webcam, or servos.");
        }
        else
        {
            ConnectionText = "Disconnected";
            AppendLog("Test mode off. Connect the Mini Maestro and webcam to use the robot.");
        }
    }

    [RelayCommand]
    public void RefreshDevices()
    {
        SerialPorts.Clear();
        foreach (var port in DeviceEnumerator.ListSerialPorts())
        {
            SerialPorts.Add(port);
        }

        SelectedPort = SerialPorts.FirstOrDefault(p => p.IsMaestroCommandPort)
                       ?? SerialPorts.FirstOrDefault(p => p.PortName == Settings.MaestroPort)
                       ?? SerialPorts.FirstOrDefault();

        Cameras.Clear();
        _ignoreCameraChange = true;
        try
        {
            foreach (var camera in DeviceEnumerator.ListCameras())
            {
                Cameras.Add(camera);
            }

            SelectedCamera = Cameras.FirstOrDefault(c =>
                                 !string.IsNullOrWhiteSpace(Settings.CameraName)
                                 && c.Name.Equals(Settings.CameraName, StringComparison.OrdinalIgnoreCase))
                             ?? Cameras.FirstOrDefault(c => c.Index == Settings.CameraIndex)
                             ?? Cameras.FirstOrDefault();
            if (Cameras.Count == 0)
            {
                AppendLog("No webcams found. Plug in the robot camera and click Refresh devices.");
            }
        }
        finally
        {
            _ignoreCameraChange = false;
        }
    }

    [RelayCommand]
    public void Connect()
    {
        try
        {
            if (TestMode)
            {
                _maestro.Simulate = true;
                ConnectionText = "Test mode — digital only";
                AppendLog("Test mode: hardware connect skipped. Click Start to run a digital scramble and solve.");
                return;
            }

            _maestro.Simulate = false;
            if (SelectedPort is null)
            {
                AppendLog("Select the Mini Maestro Command Port.");
                return;
            }

            _maestro.Connect(SelectedPort.PortName);
            Settings.MaestroPort = SelectedPort.PortName;

            Settings.CameraIndex = SelectedCamera?.Index ?? 0;
            Settings.CameraName = SelectedCamera?.Name;
            if (!OpenSelectedCamera())
            {
                AppendLog(SelectedCamera is null
                    ? "No webcam selected. Plug in a camera and click Refresh devices."
                    : $"Could not open webcam '{SelectedCamera.Name}'.");
            }

            _robot = new RobotController(_maestro, Settings);
            _robot.OnCommand = message =>
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher is null || dispatcher.CheckAccess())
                {
                    StatusText = message;
                    AppendLog(message);
                    return;
                }

                dispatcher.Invoke(() =>
                {
                    StatusText = message;
                    AppendLog(message);
                });
            };
            _robot.ConfigureChannels();
            ConnectionText = $"Connected ({SelectedPort.PortName})";
            Settings.Save();
            AppendLog("Connected. Use Open to release the arms, insert the cube, Close to hug it, then Start.");
        }
        catch (Exception ex)
        {
            AppendLog("Connect failed: " + ex.Message);
            ConnectionText = "Disconnected";
        }
    }

    [RelayCommand]
    public void Disconnect()
    {
        _previewTimer.Stop();
        _robot = null;
        _maestro.Disconnect();
        _webcam.Close();
        ConnectionText = "Disconnected";
        AppendLog("Disconnected.");
    }

    [RelayCommand]
    public async Task CloseAsync()
    {
        await RunExclusiveAsync("Closing arms", async ct =>
        {
            if (TestMode)
            {
                AppendLog("Test mode: hugged (no hardware).");
                return;
            }

            EnsureRobot();
            await _robot!.CloseAsync(ct);
            AppendLog("Cube hugged. Click Start to scan and solve.");
        });
    }

    [RelayCommand]
    public async Task OpenAsync()
    {
        await RunExclusiveAsync("Opening arms", async ct =>
        {
            if (TestMode)
            {
                _digital.ResetSolved();
                ApplyStickers(_digital.Colors);
                SolutionText = "";
                AppendLog("Test mode: digital cube released (reset to solved).");
                return;
            }

            EnsureRobot();
            await _robot!.OpenAsync(ct);
            AppendLog("Arms open. Insert or remove the cube with the Rubik's logo (white face) toward the camera.");
        });
    }

    [RelayCommand]
    public async Task CameraPreviewPoseAsync()
    {
        await RunExclusiveAsync("Camera pose", async ct =>
        {
            if (TestMode)
            {
                AppendLog("Test mode: camera pose skipped.");
                return;
            }

            EnsureRobot();
            await _robot!.PreviewPoseAsync(ct);
            using var frame = await _webcam.GrabSettledAsync(Settings.VideoDurationMs, Settings.RotatePhotos180, ct);
            if (frame is not null)
            {
                ShowFrame(frame);
                PrepareManualLayoutForFrame(frame.Width, frame.Height);
                var sampled = FaceScanner.Sample(frame, Settings);
                ShowFrame(sampled.Preview);
                sampled.Preview.Dispose();
                AppendLog($"Test photo captured ({_webcam.Resolution}).");
            }

            await _robot.OpenAsync(ct);
        });
    }

    [RelayCommand]
    public void Stop()
    {
        _runCts?.Cancel();
        AppendLog("Stop requested.");
    }

    [RelayCommand]
    public void ServosOff()
    {
        if (TestMode)
        {
            AppendLog("Test mode: servos off skipped.");
            return;
        }

        try
        {
            EnsureRobot();
            _robot!.AllServosOff();
            AppendLog("All servo pulses off.");
        }
        catch (Exception ex)
        {
            AppendLog(ex.Message);
        }
    }

    [RelayCommand]
    public void ResetScanGrid()
    {
        Settings.FaceMargin = 0.22;
        Settings.FaceOffsetX = 0;
        Settings.FaceOffsetY = 0;
        Settings.FaceSampleInset = 0.18;
        Settings.FaceAutoDetect = false;
        OnPropertyChanged(nameof(FaceMargin));
        OnPropertyChanged(nameof(FaceGridSize));
        OnPropertyChanged(nameof(FaceOffsetX));
        OnPropertyChanged(nameof(FaceOffsetY));
        OnPropertyChanged(nameof(FaceSampleInset));
        OnPropertyChanged(nameof(FaceAutoDetect));
        ScanRectangles.Clear();
        SyncScanRectanglesToSettings();
        RegenerateRegularScanLayout();
        SyncScanRectanglesToSettings();
        AppendLog("Scan grid reset to a centered square. Press Keep these settings to save it.");
    }

    [RelayCommand]
    public async Task AutoCalibrateScanGridAsync()
    {
        if (!_webcam.IsOpen)
        {
            AppendLog("Auto calibrate: connect the webcam first.");
            return;
        }

        using var frame = _webcam.Grab(Settings.RotatePhotos180);
        if (frame is null)
        {
            AppendLog("Auto calibrate: camera frame was empty.");
            return;
        }

        if (FaceScanner.TryCalibrateGrid(frame, out var margin, out var offsetX, out var offsetY))
        {
            Settings.FaceMargin = margin;
            Settings.FaceOffsetX = offsetX;
            Settings.FaceOffsetY = offsetY;
            OnPropertyChanged(nameof(FaceMargin));
            OnPropertyChanged(nameof(FaceGridSize));
            OnPropertyChanged(nameof(FaceOffsetX));
            OnPropertyChanged(nameof(FaceOffsetY));
            RegenerateRegularScanLayout();
            AppendLog($"Auto calibrate: grid set (size {FaceGridSize:F2}, right {offsetX:F2}, down {offsetY:F2}). Press Keep these settings to save.");
            TickPreview();
            return;
        }

        AppendLog("Auto calibrate: could not find a square face. Hug the cube, improve lighting, or enable Auto-find during scans.");
        await Task.CompletedTask;
    }

    [RelayCommand]
    public async Task HugForScanGridAsync()
    {
        await RunExclusiveAsync("Hug for scan grid", async ct =>
        {
            if (TestMode)
            {
                AppendLog("Test mode: hug skipped. Line up the grid on the webcam still.");
                return;
            }

            EnsureRobot();
            await _robot!.HugAsync(ct);
            await Task.Delay(Math.Max(400, Settings.VideoDurationMs / 2), ct);
            AppendLog("Cube hugged. Running auto calibrate...");
            await AutoCalibrateScanGridAsync();
        });
    }

    [RelayCommand]
    public void SaveScanGrid()
    {
        SyncScanRectanglesToSettings();
        Settings.MergeScanGridIntoFile();
        AppendLog(
            $"Scan grid saved with {ScanRectangles.Count} custom boxes " +
            $"(right {Settings.FaceOffsetX:F2}, down {Settings.FaceOffsetY:F2}, " +
            $"color H red {RedHueFrom}-{RedHueTo}, orange {OrangeHueFrom}-{OrangeHueTo}, " +
            $"yellow {YellowHueFrom}-{YellowHueTo}, green {GreenHueFrom}-{GreenHueTo}, " +
            $"blue {BlueHueFrom}-{BlueHueTo}, white S {WhiteSaturation}).");
    }

    public void ReplaceScanRectangles(IReadOnlyList<NormalizedScanRect> rectangles)
    {
        ScanRectangles.Clear();
        foreach (var rectangle in rectangles)
        {
            ScanRectangles.Add(rectangle);
        }
    }

    public void MoveScanLayout(double dx, double dy) =>
        ReplaceScanRectangles(ScanGridLayout.MoveAll(ScanRectangles, dx, dy));

    public void ScaleScanLayout(double factor) =>
        ReplaceScanRectangles(ScanGridLayout.ScaleAll(ScanRectangles, factor));

    public void MoveScanRectangle(int index, double dx, double dy) =>
        ReplaceScanRectangles(ScanGridLayout.MoveOne(ScanRectangles, index, dx, dy));

    public void ResizeScanRectangle(int index, double dw, double dh) =>
        ReplaceScanRectangles(ScanGridLayout.ResizeOne(ScanRectangles, index, dw, dh));

    internal void SyncScanRectanglesToSettings() =>
        Settings.ScanRectangles = ScanRectangles.ToList();

    [ObservableProperty]
    string _turnCalibrationSummary = "Run auto calibrate turns with a cube loaded (or test mode for math-only).";

    [RelayCommand]
    public async Task AutoCalibrateTurnSettingsAsync()
    {
        if (TestMode)
        {
            var derived = TurnSettingsCalibrator.DeriveFromCalibration(Settings);
            TurnCalibrationSummary = derived.Summary;
            foreach (var line in derived.LogLines)
            {
                AppendLog($"Turn auto-cal: {line}");
            }

            AppendLog($"Turn auto-cal: {derived.Summary}. Press Save settings to keep.");
            return;
        }

        await RunExclusiveAsync("Auto calibrate turns", async ct =>
        {
            EnsureRobot();
            AppendLog("Turn auto-cal: hugging cube, then test turn...");
            await _robot!.HugAsync(ct);
            var result = await _robot.AutoCalibrateTurnSettingsAsync(ct);
            TurnCalibrationSummary = result.Summary;
            foreach (var line in result.LogLines)
            {
                AppendLog($"Turn auto-cal: {line}");
            }

            AppendLog($"Turn auto-cal: {result.Summary}. Press Save settings to keep.");
        });
    }

    [RelayCommand]
    public async Task HugAndAutoCalibrateTurnsAsync()
    {
        if (TestMode)
        {
            await AutoCalibrateTurnSettingsAsync();
            return;
        }

        await RunExclusiveAsync("Hug and auto calibrate turns", async ct =>
        {
            EnsureRobot();
            await _robot!.HugAsync(ct);
            await Task.Delay(Math.Max(400, Settings.SettleMs * 3), ct);
            AppendLog("Cube hugged. Running turn auto-calibrate...");
            var result = await _robot.AutoCalibrateTurnSettingsAsync(ct);
            TurnCalibrationSummary = result.Summary;
            foreach (var line in result.LogLines)
            {
                AppendLog($"Turn auto-cal: {line}");
            }

            AppendLog($"Turn auto-cal: {result.Summary}. Press Save settings to keep.");
        });
    }

    [RelayCommand]
    public void SaveSettings()
    {
        Settings.CameraIndex = SelectedCamera?.Index ?? 0;
        Settings.CameraName = SelectedCamera?.Name;
        Settings.MaestroPort = SelectedPort?.PortName;
        Settings.Save();
        AppendLog("Settings saved.");
    }

    partial void OnSelectedCameraChanged(CameraDevice? value)
    {
        if (_ignoreCameraChange || value is null)
        {
            return;
        }

        Settings.CameraIndex = value.Index;
        Settings.CameraName = value.Name;
        if (_webcam.IsOpen || ConnectionText.StartsWith("Connected", StringComparison.OrdinalIgnoreCase))
        {
            OpenSelectedCamera();
        }
    }

    [RelayCommand]
    public void ApplyCamera() => OpenSelectedCamera();

    bool OpenSelectedCamera()
    {
        if (TestMode)
        {
            AppendLog("Test mode: webcam is not used.");
            return true;
        }

        if (SelectedCamera is null)
        {
            return false;
        }

        if (!_webcam.Open(SelectedCamera.Index))
        {
            CameraResolution = "not open";
            return false;
        }

        CameraResolution = _webcam.Resolution;
        AppendLog($"Webcam '{SelectedCamera.Name}' opened at {_webcam.Resolution}.");
        _previewTimer.Start();
        return true;
    }

    [RelayCommand]
    public void ResetDigitalCube()
    {
        _digital.ResetSolved();
        ApplyStickers(_digital.Colors);
        SolutionText = "";
        AppendLog("Digital cube reset to solved (white front, red top).");
    }

    [RelayCommand]
    public void CycleSticker(StickerCell? cell)
    {
        if (cell is null)
        {
            return;
        }

        cell.Color = StickerPalette.Next(cell.Color);
        _digital.CopyFrom(Stickers.Select(s => s.Color).ToArray());
    }

    [RelayCommand]
    public async Task StartAsync()
    {
        await RunExclusiveAsync("Solving", SolveAsync);
    }

    [RelayCommand]
    public async Task ScrambleAsync()
    {
        await RunExclusiveAsync("Scrambling", ExecuteScrambleAsync);
    }

    [RelayCommand]
    public async Task ZenModeAsync()
    {
        await RunExclusiveAsync("Zen mode", RunZenModeAsync);
    }

    [RelayCommand]
    public async Task TestSolveMoveAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        var moves = CubeMove.ParseSequence(token);
        if (moves.Count != 1)
        {
            AppendLog($"Unknown solve command '{token}'.");
            return;
        }

        await RunExclusiveAsync($"Test {token}", async ct =>
        {
            AppendLog($"Test solve command: {token}");
            if (TestMode)
            {
                await PlayDigitalMoveAsync(moves[0], ct);
                return;
            }

            EnsureRobot();
            await _robot!.TurnCubeFaceAsync(moves[0], ct);
            await PlayDigitalMoveAsync(moves[0], ct);
        });
    }

    async Task SolveAsync(CancellationToken cancellationToken)
    {
        if (TestMode)
        {
            await SolveDigitallyAsync(cancellationToken);
            return;
        }

        var stickers = await ScanCubeAsync(cancellationToken);
        await SolveFromStickersAsync(stickers, cancellationToken, unloadOnError: true);
    }

    async Task RunZenModeAsync(CancellationToken cancellationToken)
    {
        ZenModeRunning = true;
        AppendLog("Zen mode on. Scan, solve, display, wait, scramble, repeat. Stop to end.");
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await ZenCycleAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    AppendLog("Zen mode: " + ex.Message);
                    StatusText = "Zen mode retry";
                    if (!TestMode && _robot is not null)
                    {
                        try
                        {
                            await _robot.HugAsync(cancellationToken);
                        }
                        catch
                        {
                            // Keep the loop alive.
                        }
                    }

                    await Task.Delay(2000, cancellationToken);
                }
            }
        }
        finally
        {
            ZenModeRunning = false;
            AppendLog("Zen mode ended.");
        }
    }

    async Task ZenCycleAsync(CancellationToken cancellationToken)
    {
        var stickers = await ScanCubeAsync(cancellationToken);
        if (DigitalCube.IsSolved(stickers))
        {
            AppendLog("Zen mode: scanned cube is solved. Scrambling before solving.");
            await ExecuteScrambleAsync(cancellationToken);
            stickers = await ScanCubeAsync(cancellationToken);
            if (DigitalCube.IsSolved(stickers))
            {
                AppendLog("Zen mode: still solved after scramble. Scrambling again.");
                await ExecuteScrambleAsync(cancellationToken);
                stickers = await ScanCubeAsync(cancellationToken);
            }
        }

        await SolveFromStickersAsync(stickers, cancellationToken, unloadOnError: false);
        await DisplaySolvedAsync(cancellationToken);
        await CountdownAsync(Math.Max(1, Settings.ZenDisplaySeconds), cancellationToken);
        AppendLog("Zen mode: scrambling for the next round.");
        await ExecuteScrambleAsync(cancellationToken);
    }

    async Task<StickerColor[]> ScanCubeAsync(CancellationToken cancellationToken)
    {
        if (TestMode)
        {
            ApplyStickers(_digital.Colors);
            AppendLog("Test mode: using the digital cube instead of a camera scan.");
            return _digital.Colors.ToArray();
        }

        EnsureRobot();
        _robot!.ResetOrientation();
        AppendLog("Insert convention: white/logo face toward the camera (this becomes Front for the scan).");
        AppendLog("Hugging cube...");
        await _robot.HugAsync(cancellationToken);
        var faces = await ScanAllFacesAsync(cancellationToken);
        var stickers = ColorClassifier.ClassifyCube(faces, HueSplits);
        var facelets = ColorClassifier.ToKociembaString(stickers);
        AppendLog("Scanned facelet string: " + facelets);
        ApplyStickers(stickers);
        _digital.CopyFrom(stickers);
        return stickers;
    }

    async Task SolveFromStickersAsync(StickerColor[] stickers, CancellationToken cancellationToken, bool unloadOnError)
    {
        _digital.CopyFrom(stickers);
        ApplyStickers(stickers);
        if (DigitalCube.IsSolved(stickers))
        {
            AppendLog("Cube is already solved.");
            StatusText = "Solved";
            return;
        }

        var facelets = ColorClassifier.ToKociembaString(stickers);
        AppendLog("Facelet string: " + facelets);
        var verify = Tools.Verify(facelets);
        if (verify != 0)
        {
            if (unloadOnError && !TestMode && _robot is not null)
            {
                await _robot.OpenAsync(cancellationToken);
            }

            throw new InvalidOperationException("Unsolvable cube. " + ColorClassifier.DescribeVerifyError(verify)
                + (unloadOnError ? " Click stickers on the net to correct colors, then use Solve Net." : ""));
        }

        AppendLog("Computing solution...");
        var moves = CubeSolver.Solve(facelets);
        SolutionText = string.Join(' ', moves);
        AppendLog($"Solution ({moves.Count} moves): {SolutionText}");
        await ExecuteMovesAsync(moves, "Solving", cancellationToken, progressStart: 0, progressSpan: 1);
        if (!TestMode)
        {
            await _robot!.HugAsync(cancellationToken);
        }

        AppendLog("Cube solved.");
        StatusText = "Solved";
    }

    async Task ExecuteScrambleAsync(CancellationToken cancellationToken)
    {
        if (TestMode)
        {
            var digital = DigitalCube.RandomScramble(20, singleQuarterTurns: true);
            SolutionText = "Scramble: " + string.Join(' ', digital);
            AppendLog(SolutionText);
            await ExecuteMovesAsync(digital, "Scrambling", cancellationToken, progressStart: 0, progressSpan: 1);
            AppendLog("Scramble complete.");
            StatusText = "Scrambled";
            return;
        }

        EnsureRobot();
        AppendLog("Holding cube to scramble...");
        await _robot!.ArmsInHoldAsync(cancellationToken);

        var steps = _robot.CreateGrippedScramble(20);
        var scramble = steps.Select(step => step.Move).ToList();
        SolutionText = "Scramble: " + string.Join(' ', scramble);
        AppendLog(SolutionText + " (one 90° gripper turn at a time; no pitch/yaw)");

        for (int i = 0; i < steps.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Progress = (i + 1) / (double)steps.Count;
            StatusText = $"Scrambling {scramble[i]} ({i + 1}/{steps.Count})";
            AppendLog($"Scrambling {i + 1}/{steps.Count}: {scramble[i]}");
            var robotMove = _robot.QuarterTurnStationOnceAsync(steps[i].Station, cancellationToken);
            await PlayDigitalMoveAsync(scramble[i], cancellationToken);
            await robotMove;
        }

        await _robot.ArmsInHoldAsync(cancellationToken);
        AppendLog("Scramble complete.");
        StatusText = "Scrambled";
    }

    async Task ExecuteMovesAsync(IReadOnlyList<CubeMove> moves, string verb, CancellationToken cancellationToken, double progressStart, double progressSpan)
    {
        if (TestMode || _robot is null)
        {
            for (int i = 0; i < moves.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Progress = progressStart + progressSpan * (i + 1) / Math.Max(1, moves.Count);
                StatusText = $"{verb} {moves[i]} ({i + 1}/{moves.Count})";
                AppendLog($"{verb} {i + 1}/{moves.Count}: {moves[i]}");
                await PlayDigitalMoveAsync(moves[i], cancellationToken);
            }

            return;
        }

        var completed = 0;
        await _robot.ExecuteSolveSequenceAsync(moves, async (step, ct) =>
        {
            completed++;
            Progress = progressStart + progressSpan * completed / Math.Max(1, moves.Count);
            StatusText = $"{verb} {step} ({completed})";
            AppendLog($"{verb} {completed}: {step}");
            await PlayDigitalMoveAsync(step, ct);
        }, cancellationToken);
    }

    async Task DisplaySolvedAsync(CancellationToken cancellationToken)
    {
        if (TestMode)
        {
            AppendLog("Test mode: display pose skipped (left, right, and top would retract).");
            StatusText = "Displaying";
            return;
        }

        EnsureRobot();
        AppendLog("Display pose: pitch TOP toward camera, bottom holds, left/right/top clear.");
        await _robot!.DisplayAsync(cancellationToken);
        StatusText = "Displaying";
    }

    async Task CountdownAsync(int seconds, CancellationToken cancellationToken)
    {
        for (int left = seconds; left > 0; left--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StatusText = ZenModeRunning ? $"Zen display {left}s" : $"Waiting {left}s";
            Progress = 1 - left / (double)seconds;
            await Task.Delay(1000, cancellationToken);
        }
    }

    [RelayCommand]
    public async Task SolveNetAsync()
    {
        await RunExclusiveAsync("Solving from net", async ct =>
        {
            var stickers = Stickers.Select(s => s.Color).ToArray();
            _digital.CopyFrom(stickers);
            var facelets = ColorClassifier.ToKociembaString(stickers);
            var verify = Tools.Verify(facelets);
            if (verify != 0)
            {
                throw new InvalidOperationException(ColorClassifier.DescribeVerifyError(verify));
            }

            var moves = CubeSolver.Solve(facelets);
            SolutionText = string.Join(' ', moves);
            AppendLog($"Solution ({moves.Count} moves): {SolutionText}");
            if (!TestMode)
            {
                EnsureRobot();
                await _robot!.HugAsync(ct);
            }

            await ExecuteMovesAsync(moves, "Executing", ct, progressStart: 0, progressSpan: 1);

            AppendLog(TestMode ? "Test mode: cube solved digitally from the net." : "Cube solved from manual net.");
        });
    }

    async Task<List<Scalar[]>> ScanAllFacesAsync(CancellationToken cancellationToken)
    {
        var map = new Dictionary<CubeFace, ScanFaceBuffer>();
        var frameCount = Math.Clamp(Settings.ScanFramesPerFace, 1, 12);
        var frameGapMs = Math.Clamp(Settings.ScanFrameGapMs, 40, 1000);
        AppendLog($"Scan: F → R → B → L opportunistic fill, then pitch U/D ({frameCount} frames/hold).");

        async Task<Scalar[]> GrabFaceAsync(int settleMs)
        {
            var frames = new List<Scalar[]>(frameCount);
            for (int i = 0; i < frameCount; i++)
            {
                using var frame = await _webcam.GrabSettledAsync(settleMs, Settings.RotatePhotos180, cancellationToken)
                                  ?? throw new InvalidOperationException("Camera frame was empty.");
                PrepareManualLayoutForFrame(frame.Width, frame.Height);
                var sampled = FaceScanner.Sample(frame, Settings);
                ShowFrame(sampled.Preview);
                frames.Add(sampled.Samples);
                sampled.Preview.Dispose();
                if (i < frameCount - 1)
                {
                    await Task.Delay(frameGapMs, cancellationToken);
                }
            }

            return FaceScanner.AverageSamples(frames);
        }

        async Task CaptureMaskedAsync(
            CubeFace face, string label, IReadOnlyList<int> indices, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (!map.TryGetValue(face, out var buffer))
            {
                buffer = new ScanFaceBuffer();
                map[face] = buffer;
            }

            var stickerNumbers = string.Join(',', indices.Select(index => index + 1));
            StatusText = $"Photographing {face}";
            AppendLog(
                $"PHOTO_{label}: CURRENT_FACE={_robot!.CurrentCameraFace}, stickers [{stickerNumbers}], {frameCount} frames.");
            var samples = await GrabFaceAsync(Settings.VideoDurationMs);
            buffer.Write(samples, indices);
            ApplyFace(face, samples.Select(ColorClassifier.Guess).ToArray(), indices);
            AppendLog($"Stored {face} stickers [{stickerNumbers}] ({label}).");

            if (face == CubeFace.F && indices.Contains(4))
            {
                var frontCenter = ColorClassifier.Guess(samples[4], HueSplits);
                if (frontCenter == StickerColor.White)
                {
                    AppendLog("Front center read as White — matches logo-face load.");
                }
                else
                {
                    AppendLog($"Front center read as {frontCenter}, not White. Load with the logo (white) face toward the camera.");
                }
            }
        }

        async Task CaptureFaceAsync(CubeFace face, string label, CancellationToken ct) =>
            await CaptureMaskedAsync(face, label, ScanStickerMask.AllNine, ct);

        async Task CapturePitchedFaceAsync(CubeFace face, string label, CancellationToken ct)
        {
            var atCamera = _robot.CurrentCameraFace;
            if (atCamera is CubeFace.U or CubeFace.D && atCamera != face)
            {
                AppendLog($"Pitch capture remapped {face} → {atCamera} (face at camera).");
                face = atCamera;
            }

            var name = face switch
            {
                CubeFace.U => "TOP",
                CubeFace.D => "BOTTOM",
                _ => face.ToString()
            };
            if (face is not CubeFace.U and not CubeFace.D)
            {
                throw new InvalidOperationException(
                    $"Pitch {label} left {face} at the camera — expected TOP or BOTTOM.");
            }

            await CaptureFaceAsync(face, name, ct);
            AppendLog($"STATE after {name} photo: CURRENT_FACE={_robot.CurrentCameraFace}");
        }

        _robot!.ResetOrientation();
        AppendLog("START: CURRENT_FACE=FRONT, RL_IN, TB_IN, turners reset.");

        var session = new CubeScanSession(
            this,
            _robot,
            CaptureMaskedAsync,
            CapturePitchedFaceAsync);
        await CubeScanSequence.RunAsync(session, CubeScanSequence.Default, cancellationToken);

        if (_robot.CurrentCameraFace != CubeFace.F)
        {
            AppendLog($"WARNING: scan ended with {_robot.CurrentCameraFace} at camera (expected FRONT).");
        }

        foreach (var face in new[] { CubeFace.U, CubeFace.R, CubeFace.F, CubeFace.D, CubeFace.L, CubeFace.B })
        {
            if (!map.TryGetValue(face, out var buffer) || !buffer.IsComplete)
            {
                throw new InvalidOperationException(
                    $"Scan missed stickers on {face} — every face must be fully photographed.");
            }
        }

        return
        [
            map[CubeFace.U].Samples,
            map[CubeFace.R].Samples,
            map[CubeFace.F].Samples,
            map[CubeFace.D].Samples,
            map[CubeFace.L].Samples,
            map[CubeFace.B].Samples
        ];
    }

    sealed class CubeScanSession : IScanSession
    {
        readonly MainViewModel _viewModel;
        readonly RobotController _robot;
        readonly Func<CubeFace, string, IReadOnlyList<int>, CancellationToken, Task> _captureMasked;
        readonly Func<CubeFace, string, CancellationToken, Task> _capturePitched;

        public CubeScanSession(
            MainViewModel viewModel,
            RobotController robot,
            Func<CubeFace, string, IReadOnlyList<int>, CancellationToken, Task> captureMasked,
            Func<CubeFace, string, CancellationToken, Task> capturePitched)
        {
            _viewModel = viewModel;
            _robot = robot;
            _captureMasked = captureMasked;
            _capturePitched = capturePitched;
        }

        public CubeFace CurrentCameraFace => _robot.CurrentCameraFace;

        public void Log(string message) => _viewModel.AppendLog(message);

        public Task CaptureMaskedAsync(
            CubeFace face, string label, IReadOnlyList<int> stickerIndices, CancellationToken cancellationToken) =>
            _captureMasked(face, label, stickerIndices, cancellationToken);

        public Task CapturePitchedFaceAsync(CubeFace face, string label, CancellationToken cancellationToken) =>
            _capturePitched(face, label, cancellationToken);

        public Task ScanExposeTopBottomHoldAsync(CancellationToken cancellationToken) =>
            _robot.ScanExposeTopBottomHoldForPhotoAsync(cancellationToken);

        public Task ScanExposeLeftRightHoldAsync(CancellationToken cancellationToken) =>
            _robot.ScanExposeLeftRightHoldForPhotoAsync(cancellationToken);

        public Task ScanTurnRight90Async(CancellationToken cancellationToken) =>
            _robot.ScanTurnRight90CountAsync(cancellationToken, 1);

        public Task ScanYawTurnersHomeKeepFaceAsync(CancellationToken cancellationToken) =>
            _robot.ScanYawTurnersHomeKeepFaceAsync(cancellationToken);

        public Task ScanYawTurnersHomeAtFrontAsync(CancellationToken cancellationToken) =>
            _robot.ScanYawTurnersHomeAtFrontAsync(cancellationToken);

        public Task ScanYawTurnersHomeKeepRlHoldAsync(CancellationToken cancellationToken) =>
            _robot.ScanYawTurnersHomeKeepRlHoldAsync(cancellationToken);

        public Task ScanPitchToTopAsync(CancellationToken cancellationToken) =>
            _robot.ScanPitchToTopAsync(cancellationToken);

        public Task ScanPitchToBottomAsync(CancellationToken cancellationToken) =>
            _robot.ScanPitchToBottomAsync(cancellationToken);

        public Task ScanPitchReturnToFrontAsync(CancellationToken cancellationToken) =>
            _robot.ScanPitchReturnToFrontAsync(cancellationToken);

        public Task ScanFinishHugAtFrontAsync(CancellationToken cancellationToken) =>
            _robot.ScanFinishHugAtFrontAsync(cancellationToken);
    }

    void ApplyStickers(StickerColor[] stickers)
    {
        void Write()
        {
            for (int i = 0; i < 54; i++)
            {
                Stickers[i].Color = stickers[i];
            }
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            Write();
        }
        else
        {
            dispatcher.Invoke(Write);
        }
    }

    void ApplyFace(CubeFace face, StickerColor[] nine, IReadOnlyList<int>? indices = null)
    {
        var start = face switch
        {
            CubeFace.U => 0,
            CubeFace.R => 9,
            CubeFace.F => 18,
            CubeFace.D => 27,
            CubeFace.L => 36,
            _ => 45
        };
        var copy = nine.ToArray();
        var slots = indices ?? ScanStickerMask.AllNine;
        void Write()
        {
            foreach (var i in slots)
            {
                Stickers[start + i].Color = copy[i];
            }
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            Write();
        }
        else
        {
            dispatcher.Invoke(Write);
        }
    }

    async Task PlayDigitalMoveAsync(CubeMove move, CancellationToken cancellationToken)
    {
        void Commit()
        {
            _digital.Apply(move);
            ApplyStickers(_digital.Colors);
        }

        if (AnimateDigitalMove is null)
        {
            Commit();
            return;
        }

        await AnimateDigitalMove(move, Commit, cancellationToken);
    }

    async Task SolveDigitallyAsync(CancellationToken cancellationToken)
    {
        AppendLog("Test mode: scrambling the digital cube...");
        _digital.ResetSolved();
        ApplyStickers(_digital.Colors);
        SolutionText = "";
        var scramble = DigitalCube.RandomScramble(20);
        AppendLog("Scramble: " + string.Join(' ', scramble));
        for (int i = 0; i < scramble.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Progress = (i + 1) / (double)(scramble.Count * 2);
            StatusText = $"Scrambling {scramble[i]} ({i + 1}/{scramble.Count})";
            await PlayDigitalMoveAsync(scramble[i], cancellationToken);
        }

        var facelets = ColorClassifier.ToKociembaString(_digital.Colors);
        AppendLog("Facelet string: " + facelets);
        var verify = Tools.Verify(facelets);
        if (verify != 0)
        {
            throw new InvalidOperationException("Test scramble was invalid. " + ColorClassifier.DescribeVerifyError(verify));
        }

        AppendLog("Computing solution...");
        var moves = CubeSolver.Solve(facelets);
        SolutionText = string.Join(' ', moves);
        AppendLog($"Solution ({moves.Count} moves): {SolutionText}");

        for (int i = 0; i < moves.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Progress = 0.5 + (i + 1) / (double)(moves.Count * 2);
            StatusText = $"Solving {moves[i]} ({i + 1}/{moves.Count})";
            AppendLog($"Move {i + 1}/{moves.Count}: {moves[i]}");
            await PlayDigitalMoveAsync(moves[i], cancellationToken);
        }

        AppendLog("Test mode: digital cube solved.");
        StatusText = "Solved (test mode)";
    }

    void EnsureRobot()
    {
        if (TestMode)
        {
            return;
        }

        if (_robot is null)
        {
            throw new InvalidOperationException("Connect the Maestro and camera first, or turn on Test mode.");
        }

        if (!_maestro.IsConnected)
        {
            throw new InvalidOperationException("Mini Maestro is not connected.");
        }
    }

    async Task RunExclusiveAsync(string title, Func<CancellationToken, Task> work)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusText = title;
        Progress = 0;
        _runCts = new CancellationTokenSource();
        try
        {
            await work(_runCts.Token);
        }
        catch (OperationCanceledException)
        {
            AppendLog("Stopped.");
            StatusText = "Stopped";
            try
            {
                _robot?.AllServosOff();
            }
            catch
            {
                // Ignore.
            }
        }
        catch (Exception ex)
        {
            AppendLog("Error: " + ex.Message);
            StatusText = "Error";
            MessageBox.Show(ex.Message, "Rubik's Cube Solver", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsBusy = false;
            _runCts.Dispose();
            _runCts = null;
            OnPropertyChanged(nameof(CanOperate));
        }
    }

    void TickPreview()
    {
        if (IsBusy || !_webcam.IsOpen)
        {
            return;
        }

        using var frame = _webcam.Grab(Settings.RotatePhotos180);
        if (frame is null)
        {
            return;
        }

        PrepareManualLayoutForFrame(frame.Width, frame.Height);
        using var overlay = FaceScanner.OverlayLive(frame, Settings, out var samples);
        var bitmap = overlay.ToBitmapSource();
        bitmap.Freeze();
        CameraImage = bitmap;
        for (int i = 0; i < 9 && i < samples.Length; i++)
        {
            ScanPreviewStickers[i].ApplySample(
                ColorClassifier.Guess(samples[i], HueSplits),
                ColorClassifier.ToHsv(samples[i]));
        }
    }

    internal void PrepareManualLayoutForFrame(int frameWidth, int frameHeight)
    {
        EnsureScanLayoutForFrame(frameWidth, frameHeight);
        SyncScanRectanglesToSettings();
    }

    void EnsureScanLayoutForFrame(int frameWidth, int frameHeight)
    {
        CameraFrameWidth = frameWidth;
        CameraFrameHeight = frameHeight;
        if (ScanRectangles.Count != 9)
        {
            RegenerateRegularScanLayout();
        }
    }

    void RegenerateRegularScanLayout()
    {
        if (CameraFrameWidth < 1 || CameraFrameHeight < 1 || _regeneratingScanLayout)
        {
            return;
        }

        _regeneratingScanLayout = true;
        try
        {
            ReplaceScanRectangles(ScanGridLayout.CreateRegular(
                Settings.FaceMargin,
                Settings.FaceOffsetX,
                Settings.FaceOffsetY,
                Settings.FaceSampleInset,
                CameraFrameWidth,
                CameraFrameHeight));
        }
        finally
        {
            _regeneratingScanLayout = false;
        }
    }

    void ShowFrame(Mat frame)
    {
        var dispatcher = Application.Current.Dispatcher;
        dispatcher.Invoke(() =>
        {
            var bitmap = frame.ToBitmapSource();
            bitmap.Freeze();
            CameraImage = bitmap;
        });
    }

    public void AppendLog(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        void Write()
        {
            _log.AppendLine(line);
            if (_log.Length > 20000)
            {
                _log.Remove(0, _log.Length - 16000);
            }

            LogText = _log.ToString();
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            Write();
        }
        else
        {
            dispatcher.Invoke(Write);
        }
    }

    public void Closing()
    {
        _runCts?.Cancel();
        _previewTimer.Stop();
        try
        {
            _robot?.AllServosOff();
        }
        catch
        {
            // Ignore.
        }

        _webcam.Dispose();
        _maestro.Dispose();
        Settings.Save();
    }
}
