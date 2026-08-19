using RubiksCubeSolver.Robot.Actuation;

namespace RubiksCubeSolver.Robot.Commands.Solve;

public sealed class PrepareForLeftRightTurnCommand : IRobotCommand
{
    readonly IRobotActuator _robot;

    public PrepareForLeftRightTurnCommand(IRobotActuator robot) => _robot = robot;

    public string Name => "RL hold, TB clear, top/bottom turners to Start";

    public Task ExecuteAsync(CancellationToken cancellationToken) =>
        _robot.CommandAsync(Name, async ct =>
        {
            await _robot.HoldYawTurnersStillAsync(ct);
            await _robot.LeftRightInAsync(ct, squeeze: true, squeezeExtraUs: _robot.Settings.ScanHoldSqueezeUs);
            await Task.Delay(Math.Max(400, _robot.Settings.SettleMs * 2), ct);
            await _robot.TopBottomOutAsync(ct);
            await _robot.WaitUntilArmsNearAsync(_robot.Settings.TopArm, _robot.Settings.BottomArm, retracted: true, ct);
            await Task.Delay(Math.Max(800, _robot.Settings.SettleMs * 4), ct);
            await _robot.YawTurnersToStartAsync(ct);
        }, cancellationToken);
}
