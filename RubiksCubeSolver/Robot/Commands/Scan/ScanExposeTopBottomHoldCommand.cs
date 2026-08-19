using RubiksCubeSolver.Robot.Actuation;

namespace RubiksCubeSolver.Robot.Commands.Scan;

public sealed class ScanExposeTopBottomHoldCommand : IRobotCommand
{
    readonly IRobotActuator _robot;

    public ScanExposeTopBottomHoldCommand(IRobotActuator robot) => _robot = robot;

    public string Name => "TB_IN (squeeze), RL_OUT";

    public Task ExecuteAsync(CancellationToken cancellationToken) =>
        _robot.CommandAsync(Name, async ct =>
        {
            await _robot.TopBottomInAsync(ct, squeeze: true, squeezeExtraUs: _robot.Settings.ScanHoldSqueezeUs);
            await _robot.LeftRightOutAsync(ct);
            await _robot.HoldPitchTurnersStillAsync(ct);
            await Task.Delay(Math.Max(400, _robot.Settings.SettleMs * 2), ct);
        }, cancellationToken);
}
