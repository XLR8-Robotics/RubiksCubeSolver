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

        var opposite = OppositePitchToPutOnTop(face);
        Log($"{face} → Top (pitch {(opposite ? "other way" : "90°")})");
        await _secureRl.ExecuteAsync(cancellationToken);
        await _robot.PitchSpin90Async(cancellationToken, opposite);

        if (!FaceIsOnTop(face))
        {
            Log($"{face} not on Top after pitch — reversing and pitching the other way");
            await _pitchReturn.ExecuteAsync(cancellationToken);
            await _secureRl.ExecuteAsync(cancellationToken);
            await _robot.PitchSpin90Async(cancellationToken, opposite: !opposite);
        }

        if (!FaceIsOnTop(face))
        {
            throw new InvalidOperationException($"Could not bring {face} onto the Top gripper.");
        }

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

        var undoOpposite = FaceIsOnTop(CubeFace.F)
            ? _robot.Settings.InvertPitch
            : !_robot.Settings.InvertPitch;

        Log("Pitch Front back to camera");
        await _secureRl.ExecuteAsync(cancellationToken);
        await _robot.PitchSpin90Async(cancellationToken, undoOpposite);

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

    bool OppositePitchToPutOnTop(CubeFace face) =>
        face is CubeFace.F ? !_robot.Settings.InvertPitch : _robot.Settings.InvertPitch;
}
