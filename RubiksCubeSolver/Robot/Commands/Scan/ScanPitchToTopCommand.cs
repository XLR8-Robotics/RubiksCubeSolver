using RubiksCubeSolver.Robot.Actuation;

namespace RubiksCubeSolver.Robot.Commands.Scan;

public sealed class ScanPitchToTopCommand : IRobotCommand
{
    readonly IRobotActuator _robot;
    readonly ScanSecureRlThenTbClearCommand _secure;

    public ScanPitchToTopCommand(IRobotActuator robot, ScanSecureRlThenTbClearCommand secure)
    {
        _robot = robot;
        _secure = secure;
    }

    public string Name => "Pitch to TOP (RL secure, TB clear)";

    public Task ExecuteAsync(CancellationToken cancellationToken) =>
        _robot.CommandAsync(Name, async ct =>
        {
            await _secure.ExecuteAsync(ct);
            await _robot.PitchSpin90Async(ct, opposite: true);
        }, cancellationToken);
}
