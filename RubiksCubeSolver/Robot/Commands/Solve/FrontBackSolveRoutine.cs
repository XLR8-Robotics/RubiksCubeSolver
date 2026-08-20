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
            await ResetYawThenHugAsync(cancellationToken);
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

        await ResetYawThenHugAsync(cancellationToken);
    }

    public Task TurnAsUAsync(bool prime, CancellationToken cancellationToken) =>
        new GripperQuarterTurnCommand(_robot, RobotStation.Top, prime).ExecuteAsync(cancellationToken);

    public async Task RestoreForwardAsync(CancellationToken cancellationToken)
    {
        if (_robot.Orientation.StationOf(CubeFace.F) is RobotStation.Front
            && _robot.Orientation.StationOf(CubeFace.B) is RobotStation.Back)
        {
            await _hug.ExecuteAsync(cancellationToken);
            return;
        }

        Log("Restore Front to camera");
        await _pitchReturn.ExecuteAsync(cancellationToken);
        await _hug.ExecuteAsync(cancellationToken);
    }

    async Task ResetYawThenHugAsync(CancellationToken cancellationToken)
    {
        if (_robot.PairNearStart(_robot.Settings.TopTurner, _robot.Settings.BottomTurner) != true)
        {
            await _robot.TopBottomOutAsync(cancellationToken);
            await _robot.WaitUntilArmsNearAsync(_robot.Settings.TopArm, _robot.Settings.BottomArm, retracted: true, cancellationToken);
            await _robot.YawTurnersToStartAsync(cancellationToken);
            await _robot.WaitUntilPairNearStartAsync(_robot.Settings.TopTurner, _robot.Settings.BottomTurner, cancellationToken);
        }

        await _hug.ArmsOnlyAsync(cancellationToken);
    }

    bool OppositePitchToPutOnTop(CubeFace face)
    {
        // Pitch(true) puts Front on Top; Pitch(false) puts Back on Top.
        // PitchSpin90 invert = InvertPitch ^ opposite.
        return face is CubeFace.F ? !_robot.Settings.InvertPitch : _robot.Settings.InvertPitch;
    }
}
