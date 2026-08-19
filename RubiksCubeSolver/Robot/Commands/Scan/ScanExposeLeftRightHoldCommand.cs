using RubiksCubeSolver.Robot.Actuation;

namespace RubiksCubeSolver.Robot.Commands.Scan;

public sealed class ScanExposeLeftRightHoldCommand : IRobotCommand
{
    readonly IRobotActuator _robot;

    public ScanExposeLeftRightHoldCommand(IRobotActuator robot) => _robot = robot;

    public string Name => "RL_IN (squeeze), TB_OUT";

    public Task ExecuteAsync(CancellationToken cancellationToken) =>
        _robot.CommandAsync(Name, async ct =>
        {
            await _robot.LeftRightInAsync(ct, squeeze: true, squeezeExtraUs: _robot.Settings.ScanHoldSqueezeUs);
            await _robot.TopBottomOutAsync(ct);
            await _robot.HoldYawTurnersStillAsync(ct);
            await Task.Delay(Math.Max(400, _robot.Settings.SettleMs * 2), ct);
        }, cancellationToken);
}
