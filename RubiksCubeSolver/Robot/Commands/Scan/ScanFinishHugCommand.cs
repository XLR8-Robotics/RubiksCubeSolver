using RubiksCubeSolver.Robot.Actuation;
using RubiksCubeSolver.Robot.Commands.Shared;

namespace RubiksCubeSolver.Robot.Commands.Scan;

public sealed class ScanFinishHugCommand : IRobotCommand
{
    readonly IRobotActuator _robot;
    readonly HugCommand _hug;

    public ScanFinishHugCommand(IRobotActuator robot, HugCommand hug)
    {
        _robot = robot;
        _hug = hug;
    }

    public string Name => "Scan finish: TB_IN, RL_IN hug";

    public Task ExecuteAsync(CancellationToken cancellationToken) =>
        _robot.CommandAsync(Name, ct => _hug.ArmsOnlyAsync(ct), cancellationToken);
}
