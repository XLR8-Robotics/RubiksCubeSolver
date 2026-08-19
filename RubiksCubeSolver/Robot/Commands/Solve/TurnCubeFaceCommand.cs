using RubiksCubeSolver.Models;
using RubiksCubeSolver.Robot.Actuation;
using RubiksCubeSolver.Robot.Commands.Scan;
using RubiksCubeSolver.Robot.Commands.Shared;

namespace RubiksCubeSolver.Robot.Commands.Solve;

public sealed class TurnCubeFaceCommand
{
    readonly IRobotActuator _robot;
    readonly HugCommand _hug;
    readonly ScanPitchToTopCommand _pitchToTop;
    readonly ScanPitchToBottomCommand _pitchToBottom;
    readonly ScanPitchReturnToFrontCommand _pitchReturn;
    readonly QuarterTurnStationCommand _quarterTurn;

    public TurnCubeFaceCommand(
        IRobotActuator robot,
        HugCommand hug,
        ScanPitchToTopCommand pitchToTop,
        ScanPitchToBottomCommand pitchToBottom,
        ScanPitchReturnToFrontCommand pitchReturn,
        QuarterTurnStationCommand quarterTurn)
    {
        _robot = robot;
        _hug = hug;
        _pitchToTop = pitchToTop;
        _pitchToBottom = pitchToBottom;
        _pitchReturn = pitchReturn;
        _quarterTurn = quarterTurn;
    }

    public async Task ExecuteAsync(CubeMove move, CancellationToken cancellationToken)
    {
        var station = _robot.Orientation.StationOf(move.Face);
        var restoreFront = false;
        if (station is RobotStation.Front or RobotStation.Back)
        {
            _robot.OnCommand?.Invoke($"{move}: pitch F/B onto Top/Bottom, turn, then Front back to camera");
            await _pitchToTop.ExecuteAsync(cancellationToken);
            station = _robot.Orientation.StationOf(move.Face);
            if (station is RobotStation.Front or RobotStation.Back)
            {
                await _pitchReturn.ExecuteAsync(cancellationToken);
                await _pitchToBottom.ExecuteAsync(cancellationToken);
                station = _robot.Orientation.StationOf(move.Face);
            }

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
            await _pitchReturn.ExecuteAsync(cancellationToken);
            await _hug.ArmsOnlyAsync(cancellationToken);
        }
    }
}
