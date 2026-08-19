using RubiksCubeSolver.Models;
using RubiksCubeSolver.Robot.Actuation;

namespace RubiksCubeSolver.Robot.Commands.Solve;

public sealed class GripperQuarterTurnCommand : IRobotCommand
{
    readonly IRobotActuator _robot;
    readonly RobotStation _station;
    readonly bool _prime;

    public GripperQuarterTurnCommand(IRobotActuator robot, RobotStation station, bool prime)
    {
        _robot = robot;
        _station = station;
        _prime = prime;
    }

    public string Name => $"{_station} {(_prime ? "90° '" : "90°")}";

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var (turner, arm) = _station switch
        {
            RobotStation.Right => (_robot.Settings.RightTurner, _robot.Settings.RightArm),
            RobotStation.Left => (_robot.Settings.LeftTurner, _robot.Settings.LeftArm),
            RobotStation.Top => (_robot.Settings.TopTurner, _robot.Settings.TopArm),
            _ => (_robot.Settings.BottomTurner, _robot.Settings.BottomArm)
        };

        _robot.SetArm(arm, inside: true);
        await _robot.WaitAsync(cancellationToken);

        _robot.SetGripperQuarterTurn(turner, _prime);
        await _robot.WaitAsync(cancellationToken);

        _robot.SetArm(arm, inside: false);
        await _robot.WaitAsync(cancellationToken);

        _robot.NeutralGripper(turner);
        await _robot.WaitAsync(cancellationToken);

        _robot.SetArm(arm, inside: true);
        await _robot.WaitAsync(cancellationToken);
    }
}
