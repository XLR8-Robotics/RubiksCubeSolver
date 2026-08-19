using RubiksCubeSolver.Hardware;
using RubiksCubeSolver.Models;
using RubiksCubeSolver.Robot.Actuation;
using RubiksCubeSolver.Robot.Commands.Scan;
using RubiksCubeSolver.Robot.Commands.Shared;
using RubiksCubeSolver.Robot.Commands.Solve;

namespace RubiksCubeSolver.Robot;

public sealed class RobotController : IDisposable
{
    readonly IRobotActuator _actuator;
    readonly HugCommand _hug;
    readonly OpenCommand _open;
    readonly ScanExposeTopBottomHoldCommand _exposeTopBottom;
    readonly ScanExposeLeftRightHoldCommand _exposeLeftRight;
    readonly ScanPrepareForYawTurnCommand _prepareYaw;
    readonly ScanTurnRight90Command _turnRight90;
    readonly ScanYawTurnersHomeKeepFaceCommand _yawHomeKeepFace;
    readonly ScanPitchToTopCommand _pitchToTop;
    readonly ScanPitchToBottomCommand _pitchToBottom;
    readonly ScanPitchReturnToFrontCommand _pitchReturn;
    readonly ScanFinishHugCommand _finishHug;
    readonly QuarterTurnStationCommand _quarterTurn;
    readonly TurnCubeFaceCommand _turnCubeFace;
    readonly DisplayCommand _display;

    public RobotController(MaestroController maestro, AppSettings settings)
    {
        _actuator = new RobotActuator(maestro, settings);
        _hug = new HugCommand(_actuator);
        _open = new OpenCommand(_actuator);

        _exposeTopBottom = new ScanExposeTopBottomHoldCommand(_actuator);
        _exposeLeftRight = new ScanExposeLeftRightHoldCommand(_actuator);
        _prepareYaw = new ScanPrepareForYawTurnCommand(_actuator);
        var retractBetween = new ScanRetractTbBetweenTurnsCommand(_actuator);
        _turnRight90 = new ScanTurnRight90Command(_actuator, _prepareYaw, retractBetween);
        _yawHomeKeepFace = new ScanYawTurnersHomeKeepFaceCommand(_actuator);
        var secureRl = new ScanSecureRlThenTbClearCommand(_actuator);
        _pitchToTop = new ScanPitchToTopCommand(_actuator, secureRl);
        _pitchToBottom = new ScanPitchToBottomCommand(_actuator, secureRl);
        _pitchReturn = new ScanPitchReturnToFrontCommand(_actuator);
        _finishHug = new ScanFinishHugCommand(_actuator, _hug);

        _quarterTurn = new QuarterTurnStationCommand(_actuator);
        var frontBack = new FrontBackSolveRoutine(_actuator, _hug, secureRl, _pitchReturn);
        var solveMoves = new SolveCommandSet(_actuator, frontBack);
        _turnCubeFace = new TurnCubeFaceCommand(solveMoves);
        _display = new DisplayCommand(_actuator);
    }

    public Action<string>? OnCommand
    {
        get => _actuator.OnCommand;
        set => _actuator.OnCommand = value;
    }

    public CubeOrientation Orientation => _actuator.Orientation;

    public CubeFace CurrentCameraFace => _actuator.CurrentCameraFace;

    public void ResetOrientation() => _actuator.ResetOrientation();

    public void ConfigureChannels() => _actuator.ConfigureChannels();

    public void AllServosOff() => _actuator.AllServosOff();

    public Task CloseAsync(CancellationToken cancellationToken) => _hug.ExecuteAsync(cancellationToken);

    public Task OpenAsync(CancellationToken cancellationToken) => _open.ExecuteAsync(cancellationToken);

    public Task HugAsync(CancellationToken cancellationToken) => _hug.ExecuteAsync(cancellationToken);

    public Task ArmsInHoldAsync(CancellationToken cancellationToken) => _hug.ArmsOnlyAsync(cancellationToken);

    public Task PreviewPoseAsync(CancellationToken cancellationToken) => HugAsync(cancellationToken);

    public Task DisplayAsync(CancellationToken cancellationToken) => _display.ExecuteAsync(cancellationToken);

    public Task TurnCubeFaceAsync(CubeMove move, CancellationToken cancellationToken) =>
        _turnCubeFace.ExecuteAsync(move, cancellationToken);

    public Task ExecuteSolveSequenceAsync(
        IReadOnlyList<CubeMove> moves,
        Func<CubeMove, CancellationToken, Task>? onStep,
        CancellationToken cancellationToken) =>
        _turnCubeFace.ExecuteSequenceAsync(moves, onStep, cancellationToken);

    public IReadOnlyList<(RobotStation Station, CubeMove Move)> CreateGrippedScramble(int moves) =>
        GrippedScramble.Create(_actuator.Orientation, moves);

    public Task QuarterTurnStationOnceAsync(RobotStation station, CancellationToken cancellationToken) =>
        _quarterTurn.ExecuteAsync(station, cancellationToken);

    public Task ScanExposeTopBottomHoldForPhotoAsync(CancellationToken cancellationToken) =>
        _exposeTopBottom.ExecuteAsync(cancellationToken);

    public Task ScanExposeLeftRightHoldForPhotoAsync(CancellationToken cancellationToken) =>
        _exposeLeftRight.ExecuteAsync(cancellationToken);

    public Task ScanTurnRight90CountAsync(CancellationToken cancellationToken, int count) =>
        _turnRight90.ExecuteAsync(count, cancellationToken);

    public Task ScanYawTurnersHomeKeepFaceAsync(CancellationToken cancellationToken) =>
        _yawHomeKeepFace.ExecuteAsync(cancellationToken);

    public Task ScanYawTurnersHomeAtFrontAsync(CancellationToken cancellationToken) =>
        _yawHomeKeepFace.ExecuteAtFrontAsync(cancellationToken);

    public Task ScanPitchToTopAsync(CancellationToken cancellationToken) =>
        _pitchToTop.ExecuteAsync(cancellationToken);

    public Task ScanPitchToBottomAsync(CancellationToken cancellationToken) =>
        _pitchToBottom.ExecuteAsync(cancellationToken);

    public Task ScanPitchReturnToFrontAsync(CancellationToken cancellationToken) =>
        _pitchReturn.ExecuteAsync(cancellationToken);

    public Task ScanFinishHugAtFrontAsync(CancellationToken cancellationToken) =>
        _finishHug.ExecuteAsync(cancellationToken);

    public async Task<TurnCalibrationResult> AutoCalibrateTurnSettingsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _actuator.OnCommand?.Invoke("Auto calibrate turn settings");

        var log = new List<string>();
        var derived = TurnSettingsCalibrator.DeriveFromCalibration(_actuator.Settings);
        log.AddRange(derived.LogLines);
        _actuator.OnCommand?.Invoke(derived.Summary);

        ResetOrientation();
        var hardwareValidated = false;
        for (int pass = 0; pass < 3; pass++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (ok, elapsedMs, travelFraction) = await ProbeYawTurnAsync(cancellationToken);
            log.Add($"Probe {pass + 1}: {elapsedMs} ms, travel {travelFraction:P0}.");
            await _yawHomeKeepFace.ExecuteCoreAsync(cancellationToken);
            ResetOrientation();

            if (ok)
            {
                hardwareValidated = true;
                log.Add(pass == 0
                    ? "First probe succeeded."
                    : $"Probe succeeded after {pass + 1} attempt(s).");
                break;
            }

            TurnSettingsCalibrator.SoftenForStall(_actuator.Settings, log);
            if (pass == 2)
            {
                log.Add("Probe still strained after 3 passes — using softest derived values.");
            }
        }

        await HugAsync(cancellationToken);
        return TurnSettingsCalibrator.FromSettings(_actuator.Settings, hardwareValidated, log);
    }

    async Task<(bool Ok, long ElapsedMs, double TravelFraction)> ProbeYawTurnAsync(CancellationToken cancellationToken)
    {
        await _prepareYaw.ExecuteAsync(cancellationToken);
        if (_actuator.PairNearStart(_actuator.Settings.TopTurner, _actuator.Settings.BottomTurner) != true)
        {
            _actuator.OnCommand?.Invoke("Turn probe blocked — yaw turners not at Start");
            return (false, 0, 0);
        }

        var invert = _actuator.Settings.InvertYaw;
        var (targetTop, targetBottom) = _actuator.PairDualTumbleTargets(_actuator.Settings.TopTurner, _actuator.Settings.BottomTurner, invert, 0);
        var expectedTravel = Math.Max(
            Math.Abs(targetTop - _actuator.Settings.TopTurner.StartUs),
            Math.Abs(targetBottom - _actuator.Settings.BottomTurner.StartUs));
        var minExpectedMs = (int)Math.Clamp(expectedTravel / 1.2 + 200, 400, _actuator.Settings.MovementTimeoutMs);

        var started = Environment.TickCount64;
        await _actuator.SpinPairAsync(_actuator.Settings.TopTurner, _actuator.Settings.BottomTurner, invert, cancellationToken, yawMatchedOpposite: true);
        var elapsedMs = Environment.TickCount64 - started;

        var posTop = _actuator.GetPositionMicroseconds(_actuator.Settings.TopTurner.Port) ?? _actuator.Settings.TopTurner.StartUs;
        var posBottom = _actuator.GetPositionMicroseconds(_actuator.Settings.BottomTurner.Port) ?? _actuator.Settings.BottomTurner.StartUs;
        var actualTravel = Math.Max(
            Math.Abs(posTop - _actuator.Settings.TopTurner.StartUs),
            Math.Abs(posBottom - _actuator.Settings.BottomTurner.StartUs));
        var travelFraction = expectedTravel > 1 ? actualTravel / expectedTravel : 1;

        var stalled = elapsedMs > minExpectedMs * 2.2 || travelFraction < 0.82;
        if (!stalled)
        {
            _actuator.Orientation.Yaw(invert);
        }

        return (!stalled, elapsedMs, travelFraction);
    }

    public void Dispose()
    {
        // Maestro lifetime is owned by the session.
    }
}
