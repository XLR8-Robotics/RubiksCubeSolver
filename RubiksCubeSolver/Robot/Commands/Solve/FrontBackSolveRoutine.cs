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

    public async Task<bool> PitchFrontOntoGripperAsync(CancellationToken cancellationToken)
    {
        var ontoTopOpposite = !_robot.Settings.InvertPitch;
        await _secureRl.ExecuteAsync(cancellationToken);
        await _robot.PitchSpin90Async(cancellationToken, opposite: ontoTopOpposite);
        if (FrontOrBackStillUngripped())
        {
            await _pitchReturn.ExecuteAsync(cancellationToken);
            await _secureRl.ExecuteAsync(cancellationToken);
            await _robot.PitchSpin90Async(cancellationToken, opposite: !ontoTopOpposite);
            await _robot.YawTurnersToStartAsync(cancellationToken);
            return !ontoTopOpposite;
        }

        await _robot.YawTurnersToStartAsync(cancellationToken);
        return ontoTopOpposite;
    }

    public async Task TurnAsync(CubeFace face, bool prime, CancellationToken cancellationToken)
    {
        var station = _robot.Orientation.StationOf(face);
        if (station is RobotStation.Front or RobotStation.Back)
        {
            throw new InvalidOperationException($"Cannot bring {face} to a Top/Bottom gripper.");
        }

        await new GripperQuarterTurnCommand(_robot, station, prime).ExecuteAsync(cancellationToken);
    }

    public async Task RestoreForwardAsync(bool pitchOpposite, CancellationToken cancellationToken)
    {
        _robot.OnCommand?.Invoke("Restore Front to camera (reverse pitch)");
        await _secureRl.ExecuteAsync(cancellationToken);
        await _robot.PitchSpin90Async(cancellationToken, opposite: !pitchOpposite);
        await _hug.ExecuteAsync(cancellationToken);
    }

    bool FrontOrBackStillUngripped() =>
        _robot.Orientation.StationOf(CubeFace.F) is RobotStation.Front or RobotStation.Back
        && _robot.Orientation.StationOf(CubeFace.B) is RobotStation.Front or RobotStation.Back;
}
