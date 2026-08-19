using RubiksCubeSolver.Models;
using RubiksCubeSolver.Robot.Actuation;

namespace RubiksCubeSolver.Robot.Commands.Solve;

public sealed class QuarterTurnStationCommand
{
    readonly IRobotActuator _robot;

    public QuarterTurnStationCommand(IRobotActuator robot) => _robot = robot;

    public string Name => "Quarter turn station";

    public Task ExecuteAsync(RobotStation station, CancellationToken cancellationToken) =>
        new GripperQuarterTurnCommand(_robot, station, prime: false).ExecuteAsync(cancellationToken);
}
