using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using RubiksCubeSolver.Hardware;
using RubiksCubeSolver.Models;
using RubiksCubeSolver.Robot;
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

    public MainViewModel()
    {
        Settings = AppSettings.Load();
        Stickers = new ObservableCollection<StickerCell>(Enumerable.Range(0, 54).Select(i => new StickerCell(i)));
        ScanPreviewStickers = new ObservableCollection<StickerCell>(Enumerable.Range(0, 9).Select(i => new StickerCell(i)));
        ApplyStickers(_digital.Colors);
        RefreshDevices();
        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _previewTimer.Tick += (_, _) => TickPreview();
        TestMode = Settings.TestMode;
        HugSqueezeTop = Settings.HugSqueezeTopUs;
        HugSqueezeBottom = Settings.HugSqueezeBottomUs;
        HugSqueezeLeft = Settings.HugSqueezeLeftUs;
        HugSqueezeRight = Settings.HugSqueezeRightUs;
        if (!TestMode)
        {
            AppendLog("Ready. Pick your webcam, connect the Mini Maestro Command Port, then Load a cube.");
            AppendLog("Set the Maestro serial mode to USB Dual Port in Pololu Maestro Control Center.");
            AppendLog("Turn on Test mode to scramble and solve the digital cube with no hardware.");
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

    [ObservableProperty] bool hugTuneLive;
    [ObservableProperty] int hugSqueezeTop = 50;
    [ObservableProperty] int hugSqueezeBottom = 140;
    [ObservableProperty] int hugSqueezeLeft = 140;
    [ObservableProperty] int hugSqueezeRight = 140;

    partial void OnHugSqueezeTopChanged(int value) => HugSqueezeChanged(HugArm.Top, value);
    partial void OnHugSqueezeBottomChanged(int value) => HugSqueezeChanged(HugArm.Bottom, value);
    partial void OnHugSqueezeLeftChanged(int value) => HugSqueezeChanged(HugArm.Left, value);
    partial void OnHugSqueezeRightChanged(int value) => HugSqueezeChanged(HugArm.Right, value);

    void HugSqueezeChanged(HugArm arm, int value)
    {
        value = Math.Clamp(value, 0, 250);
        switch (arm)
        {
            case HugArm.Top: Settings.HugSqueezeTopUs = value; break;
            case HugArm.Bottom: Settings.HugSqueezeBottomUs = value; break;
            case HugArm.Left: Settings.HugSqueezeLeftUs = value; break;
            case HugArm.Right: Settings.HugSqueezeRightUs = value; break;
        }

        ApplyLiveHugIfTuning(arm);
    }

    void ApplyLiveHugIfTuning(HugArm arm)
    {
        if (!HugTuneLive || TestMode || _robot is null || !_maestro.IsConnected)
        {
            return;
        }

        try
        {
            _robot.ApplyLiveHugArm(arm);
        }
        catch (Exception ex)
        {
            AppendLog("Hug tune: " + ex.Message);
        }
    }

    [RelayCommand]
    public async Task StartHugTuneAsync()
    {
        if (TestMode)
        {
            AppendLog("Test mode: hug tune needs hardware.");
            return;
        }

        try
        {
            EnsureRobot();
            HugTuneLive = true;
            AppendLog("Hug tune live — move sliders; each arm updates immediately. Top often ~50 µs. Save settings when done.");
            await _robot!.HugAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            HugTuneLive = false;
            AppendLog("Hug tune failed: " + ex.Message);
        }
    }

    [RelayCommand]
    public void StopHugTune()
    {
        HugTuneLive = false;
        AppendLog("Hug tune off (sliders still edit saved values).");
    }

    [RelayCommand]
    public async Task ReapplyHugPressuresAsync()
    {
        if (TestMode || _robot is null)
        {
            return;
        }

        try
        {
            EnsureRobot();
            _robot.ApplyHugPressures();
            await _maestro.WaitUntilIdleAsync(Settings.MovementTimeoutMs, Settings.SettleMs, CancellationToken.None);
            AppendLog("Hug pressures reapplied to all four arms.");
        }
        catch (Exception ex)
        {
            AppendLog("Reapply hug: " + ex.Message);
        }
    }

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
        }
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
            AppendLog("Connected. Use Load to open the arms, insert the cube, then Start.");
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
    public async Task LoadAsync()
    {
        await RunExclusiveAsync("Opening arms", async ct =>
        {
            if (TestMode)
            {
                _digital.ResetSolved();
                ApplyStickers(_digital.Colors);
                SolutionText = "";
                AppendLog("Test mode: digital cube reset to solved. Click Start to scramble and solve.");
                return;
            }

            EnsureRobot();
            await _robot!.LoadAsync(ct);
            AppendLog("Arms open. Insert the cube with the Rubik's logo (white face) toward the camera, then click Start.");
        });
    }

    [RelayCommand]
    public async Task UnloadAsync()
    {
        await RunExclusiveAsync("Unloading", async ct =>
        {
            if (TestMode)
            {
                _digital.ResetSolved();
                ApplyStickers(_digital.Colors);
                AppendLog("Test mode: digital cube released (reset to solved).");
                return;
            }

            EnsureRobot();
            await _robot!.UnloadAsync(ct);
            AppendLog("Cube released.");
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
                var sampled = FaceScanner.Sample(frame, Settings);
                ShowFrame(sampled.Preview);
                sampled.Preview.Dispose();
                AppendLog($"Test photo captured ({_webcam.Resolution}).");
            }

            await _robot.LoadAsync(ct);
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
        Settings.MergeScanGridIntoFile();
        AppendLog($"Scan grid saved to settings.json (inset {Settings.FaceMargin:F2}, right {Settings.FaceOffsetX:F2}, down {Settings.FaceOffsetY:F2}, box {Settings.FaceSampleInset:F2}).");
    }

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
        AppendLog("Load convention: white/logo face toward the camera (this becomes Front for the scan).");
        AppendLog("Hugging cube...");
        await _robot.HugAsync(cancellationToken);
        var faces = await ScanAllFacesAsync(cancellationToken);
        var stickers = ColorClassifier.ClassifyCube(faces);
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
                await _robot.UnloadAsync(cancellationToken);
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
        for (int i = 0; i < moves.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Progress = progressStart + progressSpan * (i + 1) / Math.Max(1, moves.Count);
            StatusText = $"{verb} {moves[i]} ({i + 1}/{moves.Count})";
            AppendLog($"{verb} {i + 1}/{moves.Count}: {moves[i]}");
            Task robotMove = TestMode || _robot is null
                ? Task.CompletedTask
                : _robot.TurnCubeFaceAsync(moves[i], cancellationToken);
            await PlayDigitalMoveAsync(moves[i], cancellationToken);
            await robotMove;
        }
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

            for (int i = 0; i < moves.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                Progress = (i + 1) / (double)moves.Count;
                StatusText = $"Executing {moves[i]} ({i + 1}/{moves.Count})";
                AppendLog($"Move {i + 1}/{moves.Count}: {moves[i]}");
                Task robotMove = TestMode ? Task.CompletedTask : _robot!.TurnCubeFaceAsync(moves[i], ct);
                await PlayDigitalMoveAsync(moves[i], ct);
                await robotMove;
            }

            AppendLog(TestMode ? "Test mode: cube solved digitally from the net." : "Cube solved from manual net.");
        });
    }

    async Task<List<Scalar[]>> ScanAllFacesAsync(CancellationToken cancellationToken)
    {
        var map = new Dictionary<CubeFace, Scalar[]>();
        var frameCount = Math.Clamp(Settings.ScanFramesPerFace, 1, 12);
        var frameGapMs = Math.Clamp(Settings.ScanFrameGapMs, 40, 1000);
        AppendLog($"Scan: F → R → B → L chained ({frameCount} frames/hold). Turner home after each side, no FRONT rewind.");

        void LogState(string step)
        {
            AppendLog($"STATE after {step}: CURRENT_FACE={_robot!.CurrentCameraFace}");
        }

        async Task TurnOnceDualPhotoTurnerHomeAsync(CubeFace face, string label)
        {
            AppendLog($"{label}: TURN_R_90");
            await _robot!.ScanTurnRight90CountAsync(cancellationToken, 1);
            AppendLog($"{label}: dual hold photo (TB hold + RL hold, merge)");
            await CaptureFaceDualHoldAsync(face, label);
            AppendLog($"{label}: retract TB, unwind yaw turners, re-grip");
            await _robot.ScanYawTurnersHomeKeepFaceAsync(cancellationToken);
            LogState($"{label} photo");
        }

        async Task<Scalar[]> GrabFaceAsync(int settleMs)
        {
            var frames = new List<Scalar[]>(frameCount);
            for (int i = 0; i < frameCount; i++)
            {
                using var frame = await _webcam.GrabSettledAsync(settleMs, Settings.RotatePhotos180, cancellationToken)
                                  ?? throw new InvalidOperationException("Camera frame was empty.");
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

        async Task CaptureFaceAsync(CubeFace face, string label)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (map.ContainsKey(face))
            {
                throw new InvalidOperationException($"Face {face} was already scanned — sequence error at {label}.");
            }

            StatusText = $"Photographing {face}";
            AppendLog($"PHOTO_{label}: CURRENT_FACE={_robot!.CurrentCameraFace}, taking {frameCount} frames.");
            var samples = await GrabFaceAsync(Settings.VideoDurationMs);
            map[face] = samples;
            ApplyFace(face, samples.Select(ColorClassifier.Guess).ToArray());
            AppendLog($"Stored {face} ({label}).");
        }

        async Task CaptureFaceDualHoldAsync(CubeFace face, string label)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (map.ContainsKey(face))
            {
                throw new InvalidOperationException($"Face {face} was already scanned — sequence error at {label}.");
            }

            StatusText = $"Photographing {face} (TB hold)";
            AppendLog($"PHOTO_{label}: TB hold, CURRENT_FACE={_robot!.CurrentCameraFace}, {frameCount} frames.");
            await _robot.ScanExposeTopBottomHoldForPhotoAsync(cancellationToken);
            var topBottomHold = await GrabFaceAsync(Settings.VideoDurationMs);

            StatusText = $"Photographing {face} (RL hold)";
            AppendLog($"PHOTO_{label}: RL hold, {frameCount} frames.");
            await _robot.ScanExposeLeftRightHoldForPhotoAsync(cancellationToken);
            var leftRightHold = await GrabFaceAsync(Settings.VideoDurationMs);

            var samples = FaceScanner.MergeDualHold(topBottomHold, leftRightHold);
            map[face] = samples;
            ApplyFace(face, samples.Select(ColorClassifier.Guess).ToArray());
            AppendLog($"Stored {face} ({label}) — merged TB+RL holds.");
        }

        _robot!.ResetOrientation();
        AppendLog("START: CURRENT_FACE=FRONT, RL_IN, TB_IN, turners reset.");

        AppendLog("FRONT: dual hold photo (TB hold + RL hold, merge)");
        await CaptureFaceDualHoldAsync(CubeFace.F, "FRONT");
        var frontCenter = ColorClassifier.Guess(map[CubeFace.F][4]);
        if (frontCenter == StickerColor.White)
        {
            AppendLog("Front center read as White — matches logo-face load.");
        }
        else
        {
            AppendLog($"Front center read as {frontCenter}, not White. Load with the logo (white) face toward the camera.");
        }

        LogState("FRONT photo");

        await TurnOnceDualPhotoTurnerHomeAsync(CubeFace.R, "RIGHT");
        await TurnOnceDualPhotoTurnerHomeAsync(CubeFace.B, "BACK");
        await TurnOnceDualPhotoTurnerHomeAsync(CubeFace.L, "LEFT");

        AppendLog("RETURN: TURN_R_90 to FRONT (no photo — pitch phase)");
        await _robot.ScanTurnRight90CountAsync(cancellationToken, 1);
        AppendLog("RETURN: retract TB, unwind yaw turners at FRONT");
        await _robot.ScanYawTurnersHomeAtFrontAsync(cancellationToken);
        LogState("back at FRONT for pitch");

        AppendLog("TOP: RL secure, TB clear, pitch up");
        await _robot.ScanPitchToTopAsync(cancellationToken);
        await CaptureFaceAsync(CubeFace.U, "TOP");
        LogState("TOP photo");

        AppendLog("BOTTOM: 2× pitch 90° (TOP→BACK→BOTTOM), retract + unwind between");
        await _robot.ScanPitchTopToBottomAsync(cancellationToken);
        await CaptureFaceAsync(CubeFace.D, "BOTTOM");
        LogState("BOTTOM photo");

        AppendLog("FINISH: 1× pitch 90° BOTTOM → FRONT, hug");
        await _robot.ScanPitchRestoreFrontAsync(cancellationToken);
        await _robot.ScanFinishHugAtFrontAsync(cancellationToken);
        LogState("scan complete");

        if (_robot.CurrentCameraFace != CubeFace.F)
        {
            AppendLog($"WARNING: scan ended with {_robot.CurrentCameraFace} at camera (expected FRONT).");
        }

        return
        [
            map[CubeFace.U],
            map[CubeFace.R],
            map[CubeFace.F],
            map[CubeFace.D],
            map[CubeFace.L],
            map[CubeFace.B]
        ];
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

    void ApplyFace(CubeFace face, StickerColor[] nine)
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
        void Write()
        {
            for (int i = 0; i < 9; i++)
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

        using var overlay = FaceScanner.OverlayLive(frame, Settings, out var samples);
        var bitmap = overlay.ToBitmapSource();
        bitmap.Freeze();
        CameraImage = bitmap;
        for (int i = 0; i < 9 && i < samples.Length; i++)
        {
            ScanPreviewStickers[i].Color = ColorClassifier.Guess(samples[i]);
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
