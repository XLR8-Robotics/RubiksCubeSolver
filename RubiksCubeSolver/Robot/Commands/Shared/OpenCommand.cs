using RubiksCubeSolver.Robot.Actuation;

namespace RubiksCubeSolver.Robot.Commands.Shared;

public sealed class OpenCommand : IRobotCommand
{
    readonly IRobotActuator _robot;

    public OpenCommand(IRobotActuator robot) => _robot = robot;

    public string Name => "Open arms";

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        _robot.ResetOrientation();
        await _robot.RetractAllThenHomeTurnersAsync(cancellationToken);
    }
}
