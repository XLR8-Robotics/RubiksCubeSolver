using RubiksCubeSolver.Models;
using RubiksCubeSolver.Robot.Actuation;

namespace RubiksCubeSolver.Robot.Commands.Solve;

public sealed class QuarterTurnStationCommand
{
    readonly IRobotActuator _robot;
    readonly PrepareForTopBottomTurnCommand _prepareTopBottom;
    readonly PrepareForLeftRightTurnCommand _prepareLeftRight;

    public QuarterTurnStationCommand(
        IRobotActuator robot,
        PrepareForTopBottomTurnCommand prepareTopBottom,
        PrepareForLeftRightTurnCommand prepareLeftRight)
    {
        _robot = robot;
        _prepareTopBottom = prepareTopBottom;
        _prepareLeftRight = prepareLeftRight;
    }

    public string Name => "Quarter turn station";

    public async Task ExecuteAsync(RobotStation station, CancellationToken cancellationToken)
    {
        if (station is RobotStation.Top or RobotStation.Bottom)
        {
            await _prepareTopBottom.ExecuteAsync(cancellationToken);
        }
        else
        {
            await _prepareLeftRight.ExecuteAsync(cancellationToken);
        }

        var (turner, arm) = station switch
        {
            RobotStation.Right => (_robot.Settings.RightTurner, _robot.Settings.RightArm),
            RobotStation.Left => (_robot.Settings.LeftTurner, _robot.Settings.LeftArm),
            RobotStation.Top => (_robot.Settings.TopTurner, _robot.Settings.TopArm),
            _ => (_robot.Settings.BottomTurner, _robot.Settings.BottomArm)
        };

        _robot.SetArm(arm, inside: true);
        await _robot.WaitAsync(cancellationToken);

        _robot.SetGripper(turner, turned: true);
        await _robot.WaitAsync(cancellationToken);

        _robot.SetArm(arm, inside: false);
        await _robot.WaitAsync(cancellationToken);

        _robot.NeutralGripper(turner);
        await _robot.WaitAsync(cancellationToken);

        _robot.SetArm(arm, inside: true);
        await _robot.WaitAsync(cancellationToken);
    }
}
