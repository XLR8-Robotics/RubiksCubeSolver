using RubiksCubeSolver.Robot.Actuation;

namespace RubiksCubeSolver.Robot.Commands.Scan;

public sealed class ScanSecureRlThenTbClearCommand : IRobotCommand
{
    readonly IRobotActuator _robot;

    public ScanSecureRlThenTbClearCommand(IRobotActuator robot) => _robot = robot;

    public string Name => "RL_IN secure, then TB_OUT clear";

    public Task ExecuteAsync(CancellationToken cancellationToken) =>
        _robot.CommandAsync(Name, async ct =>
        {
            await _robot.LeftRightInAsync(ct, squeeze: true, squeezeExtraUs: _robot.Settings.ScanHoldSqueezeUs);
            await Task.Delay(Math.Max(400, _robot.Settings.SettleMs * 2), ct);
            await _robot.TopBottomOutAsync(ct);
            await _robot.WaitUntilArmsNearAsync(_robot.Settings.TopArm, _robot.Settings.BottomArm, retracted: true, ct);
            await Task.Delay(Math.Max(800, _robot.Settings.SettleMs * 4), ct);
            await _robot.HoldYawTurnersStillAsync(ct);
        }, cancellationToken);
}
