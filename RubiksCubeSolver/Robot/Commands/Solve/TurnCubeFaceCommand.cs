using RubiksCubeSolver.Models;
using RubiksCubeSolver.Robot.Actuation;
using RubiksCubeSolver.Robot.Commands.Scan;
using RubiksCubeSolver.Robot.Commands.Shared;

namespace RubiksCubeSolver.Robot.Commands.Solve;

public sealed class TurnCubeFaceCommand
{
    readonly IRobotActuator _robot;
    readonly HugCommand _hug;
    readonly ScanSecureRlThenTbClearCommand _secureRl;
    readonly ScanPitchReturnToFrontCommand _pitchReturn;
    readonly QuarterTurnStationCommand _quarterTurn;

    public TurnCubeFaceCommand(
        IRobotActuator robot,
        HugCommand hug,
        ScanSecureRlThenTbClearCommand secureRl,
        ScanPitchReturnToFrontCommand pitchReturn,
        QuarterTurnStationCommand quarterTurn)
    {
        _robot = robot;
        _hug = hug;
        _secureRl = secureRl;
        _pitchReturn = pitchReturn;
        _quarterTurn = quarterTurn;
    }

    public async Task ExecuteAsync(CubeMove move, CancellationToken cancellationToken)
    {
        var station = _robot.Orientation.StationOf(move.Face);
        var restoreFront = false;
        var restoreOpposite = false;
        if (station is RobotStation.Front or RobotStation.Back)
        {
            _robot.OnCommand?.Invoke($"{move}: pitch Front onto Top, reset idle turners, turn, then Front back to camera");
            restoreOpposite = await DockFrontOrBackOnGripperAsync(move.Face, cancellationToken);
            station = _robot.Orientation.StationOf(move.Face);
            if (station is RobotStation.Front or RobotStation.Back)
            {
                throw new InvalidOperationException($"Cannot bring {move.Face} to a Top/Bottom gripper.");
            }

            restoreFront = true;
        }

        for (int i = 0; i < move.QuarterTurns; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _quarterTurn.ExecuteAsync(station, cancellationToken);
        }

        if (restoreFront)
        {
            await _secureRl.ExecuteAsync(cancellationToken);
            await _robot.PitchSpin90Async(cancellationToken, opposite: !restoreOpposite);
            await _hug.ExecuteAsync(cancellationToken);
        }
    }

    async Task<bool> DockFrontOrBackOnGripperAsync(CubeFace face, CancellationToken cancellationToken)
    {
        var ontoTopOpposite = !_robot.Settings.InvertPitch;
        await _secureRl.ExecuteAsync(cancellationToken);
        await _robot.PitchSpin90Async(cancellationToken, opposite: ontoTopOpposite);
        if (_robot.Orientation.StationOf(face) is not RobotStation.Front and not RobotStation.Back)
        {
            return ontoTopOpposite;
        }

        await _pitchReturn.ExecuteAsync(cancellationToken);
        await _secureRl.ExecuteAsync(cancellationToken);
        await _robot.PitchSpin90Async(cancellationToken, opposite: !ontoTopOpposite);
        return !ontoTopOpposite;
    }
}
