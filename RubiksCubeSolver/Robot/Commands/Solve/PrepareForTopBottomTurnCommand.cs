using RubiksCubeSolver.Robot.Actuation;

namespace RubiksCubeSolver.Robot.Commands.Solve;

public sealed class PrepareForTopBottomTurnCommand : IRobotCommand
{
    readonly IRobotActuator _robot;

    public PrepareForTopBottomTurnCommand(IRobotActuator robot) => _robot = robot;

    public string Name => "TB hold, RL clear, left/right turners to Start";

    public Task ExecuteAsync(CancellationToken cancellationToken) =>
        _robot.CommandAsync(Name, async ct =>
        {
            await _robot.HoldPitchTurnersStillAsync(ct);
            await _robot.TopBottomInAsync(ct, squeeze: true, squeezeExtraUs: _robot.Settings.ScanHoldSqueezeUs);
            await Task.Delay(Math.Max(400, _robot.Settings.SettleMs * 2), ct);
            await _robot.LeftRightOutAsync(ct, clearOfCube: true);
            await _robot.PitchTurnersToStartAsync(ct);
        }, cancellationToken);
}
