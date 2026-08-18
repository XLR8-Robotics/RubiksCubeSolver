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
        ApplyStickers(_digital.Colors);
        RefreshDevices();
        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _previewTimer.Tick += (_, _) => TickPreview();
        TestMode = Settings.TestMode;
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
    [ObservableProperty] double progress;
    [ObservableProperty] string cameraResolution = "";

    public bool CanOperate => !IsBusy;

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
            AppendLog("Arms open. Insert the cube, then click Start.");
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
                var sampled = FaceScanner.Sample(frame, Settings.FaceMargin);
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

    async Task SolveAsync(CancellationToken cancellationToken)
    {
        if (TestMode)
        {
            await SolveDigitallyAsync(cancellationToken);
            return;
        }

        EnsureRobot();
        _robot!.ResetOrientation();
        AppendLog("Hugging cube...");
        await _robot.HugAsync(cancellationToken);

        var faces = await ScanAllFacesAsync(cancellationToken);
        var stickers = ColorClassifier.ClassifyCube(faces);
        ApplyStickers(stickers);
        _digital.CopyFrom(stickers);

        var facelets = ColorClassifier.ToKociembaString(stickers);
        AppendLog("Facelet string: " + facelets);
        var verify = Tools.Verify(facelets);
        if (verify != 0)
        {
            await _robot.UnloadAsync(cancellationToken);
            throw new InvalidOperationException("Unsolvable cube. " + ColorClassifier.DescribeVerifyError(verify)
                + " Click stickers on the net to correct colors, then use Solve Net.");
        }

        AppendLog("Computing solution...");
        var moves = CubeSolver.Solve(facelets);
        SolutionText = string.Join(' ', moves);
        AppendLog($"Solution ({moves.Count} moves): {SolutionText}");

        for (int i = 0; i < moves.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Progress = (i + 1) / (double)moves.Count;
            StatusText = $"Executing {moves[i]} ({i + 1}/{moves.Count})";
            AppendLog($"Move {i + 1}/{moves.Count}: {moves[i]}");
            var robotMove = _robot.TurnCubeFaceAsync(moves[i], cancellationToken);
            await PlayDigitalMoveAsync(moves[i], cancellationToken);
            await robotMove;
        }

        await _robot.HugAsync(cancellationToken);
        AppendLog("Cube solved.");
        StatusText = "Solved";
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

        async Task Capture(CubeFace homeFace, int rotateCcw)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StatusText = $"Photographing {homeFace}";
            AppendLog($"Photographing {homeFace} (two frames)...");
            Scalar[]? samples = null;
            for (int n = 0; n < 2; n++)
            {
                using var frame = await _webcam.GrabSettledAsync(n == 0 ? Settings.VideoDurationMs : 250, Settings.RotatePhotos180, cancellationToken)
                                  ?? throw new InvalidOperationException("Camera frame was empty.");
                var sampled = FaceScanner.Sample(frame, Settings.FaceMargin);
                ShowFrame(sampled.Preview);
                samples = FaceScanner.RotateSamples(sampled.Samples, rotateCcw);
                sampled.Preview.Dispose();
            }

            map[homeFace] = samples!;
            var guessed = samples!.Select(ColorClassifier.Guess).ToArray();
            ApplyFace(homeFace, guessed);
        }

        await Capture(CubeFace.F, 0);
        await _robot!.PitchAsync(cancellationToken);
        await Capture(CubeFace.U, 0);
        await _robot.PitchAsync(cancellationToken);
        await Capture(CubeFace.B, 2);
        await _robot.PitchAsync(cancellationToken);
        await Capture(CubeFace.D, 0);
        await _robot.PitchAsync(cancellationToken);
        await _robot.YawAsync(cancellationToken);
        await Capture(CubeFace.R, 0);
        await _robot.YawAsync(cancellationToken);
        await _robot.YawAsync(cancellationToken);
        await Capture(CubeFace.L, 0);
        await _robot.YawAsync(cancellationToken);

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

        var bitmap = _webcam.GrabBitmap(Settings.RotatePhotos180);
        if (bitmap is not null)
        {
            bitmap.Freeze();
            CameraImage = bitmap;
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
