using RubiksCubeSolver.Models;
using RubiksCubeSolver.Robot.Actuation;
using RubiksCubeSolver.Robot.Commands.Scan;
using RubiksCubeSolver.Robot.Commands.Shared;

namespace RubiksCubeSolver.Robot.Commands.Solve;

public sealed class FrontBackSolveRoutine
{
    readonly IRobotActuator _robot;
    readonly HugCommand _hug;
    readonly ScanSecureRlThenTbClearCommand _secureRl;
    readonly ScanPitchReturnToFrontCommand _pitchReturn;
    bool _ontoTopOpposite;

    public FrontBackSolveRoutine(
        IRobotActuator robot,
        HugCommand hug,
        ScanSecureRlThenTbClearCommand secureRl,
        ScanPitchReturnToFrontCommand pitchReturn)
    {
        _robot = robot;
        _hug = hug;
        _secureRl = secureRl;
        _pitchReturn = pitchReturn;
    }

    public void Log(string message) => _robot.OnCommand?.Invoke(message);

    public bool FaceIsOnTop(CubeFace face) =>
        _robot.Orientation.StationOf(face) is RobotStation.Top;

    public async Task PitchFaceOntoTopAsync(CubeFace face, CancellationToken cancellationToken)
    {
        if (face is not CubeFace.F and not CubeFace.B)
        {
            throw new ArgumentOutOfRangeException(nameof(face), face, "Only F/B are pitched onto Top.");
        }

        if (FaceIsOnTop(face))
        {
            await PrepareTopThenResetPitchClawsAsync(cancellationToken);
            return;
        }

        var opposite = OppositePitchToPutOnTop(face, _robot.Settings.InvertPitch);
        _ontoTopOpposite = opposite;
        Log($"{face} → Top (pitch {(opposite ? "other way" : "90°")})");
        await _secureRl.ExecuteAsync(cancellationToken);
        await _robot.PitchSpin90Async(cancellationToken, opposite);
        AlignSoftwareWithPitchedFace(face, opposite);

        await PrepareTopThenResetPitchClawsAsync(cancellationToken);
    }

    public Task TurnAsUAsync(bool prime, CancellationToken cancellationToken) =>
        new GripperQuarterTurnCommand(_robot, RobotStation.Top, prime).ExecuteAsync(cancellationToken);

    public async Task RestoreForwardAsync(CancellationToken cancellationToken)
    {
        Log("After F/B: Left/Right pull back and rewind");
        await RetractAndRewindPitchClawsAsync(cancellationToken);

        if (_robot.Orientation.StationOf(CubeFace.F) is RobotStation.Front
            && _robot.Orientation.StationOf(CubeFace.B) is RobotStation.Back)
        {
            await _hug.ArmsOnlyAsync(cancellationToken);
            return;
        }

        Log("Pitch Front back to camera");
        await _secureRl.ExecuteAsync(cancellationToken);
        await _robot.PitchSpin90Async(cancellationToken, opposite: !_ontoTopOpposite);

        Log("Left/Right pull back and rewind after restore pitch");
        await PrepareTopThenResetPitchClawsAsync(cancellationToken);
    }

    async Task PrepareTopThenResetPitchClawsAsync(CancellationToken cancellationToken)
    {
        if (_robot.PairNearStart(_robot.Settings.TopTurner, _robot.Settings.BottomTurner) != true)
        {
            await _robot.TopBottomOutAsync(cancellationToken);
            await Task.Delay(ArmClearMs(_robot.Settings.TopArm), cancellationToken);
            await _robot.YawTurnersToStartAsync(cancellationToken);
            await _robot.WaitUntilPairNearStartAsync(_robot.Settings.TopTurner, _robot.Settings.BottomTurner, cancellationToken);
        }

        await _robot.TopBottomInAsync(cancellationToken, squeeze: false);
        await Task.Delay(Math.Max(400, _robot.Settings.SettleMs * 2), cancellationToken);

        await RetractAndRewindPitchClawsAsync(cancellationToken);
        await _hug.ArmsOnlyAsync(cancellationToken);
    }

    async Task RetractAndRewindPitchClawsAsync(CancellationToken cancellationToken)
    {
        await _robot.LeftRightOutAsync(cancellationToken, clearOfCube: true);
        await Task.Delay(ArmClearMs(_robot.Settings.LeftArm), cancellationToken);
        await _robot.PitchTurnersToStartAsync(cancellationToken);
        await _robot.WaitUntilPairNearStartAsync(_robot.Settings.LeftTurner, _robot.Settings.RightTurner, cancellationToken);
    }

    int ArmClearMs(ArmCalibration arm) =>
        (int)Math.Clamp(Math.Abs(arm.OutUs - arm.InUs) / 1.2 + 400, 800, _robot.Settings.MovementTimeoutMs);

    public static bool OppositePitchToPutOnTop(CubeFace face, bool invertPitch) =>
        face is CubeFace.F ? !invertPitch : invertPitch;

    void AlignSoftwareWithPitchedFace(CubeFace face, bool opposite)
    {
        if (FaceIsOnTop(face))
        {
            return;
        }

        var invert = _robot.Settings.InvertPitch ^ opposite;
        _robot.Orientation.Pitch(!invert);
        _robot.Orientation.Pitch(!invert);
    }
}
