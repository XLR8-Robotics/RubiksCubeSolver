using RubiksCubeSolver.Robot.Actuation;

namespace RubiksCubeSolver.Robot.Commands.Scan;

public sealed class ScanRetractTbBetweenTurnsCommand : IRobotCommand
{
    readonly IRobotActuator _robot;

    public ScanRetractTbBetweenTurnsCommand(IRobotActuator robot) => _robot = robot;

    public string Name => "TB_OUT, hold yaw, TB_IN (between turns)";

    public Task ExecuteAsync(CancellationToken cancellationToken) =>
        _robot.CommandAsync(Name, async ct =>
        {
            await _robot.TopBottomOutAsync(ct);
            await Task.Delay(Math.Max(500, _robot.Settings.SettleMs * 3), ct);
            await _robot.HoldYawTurnersStillAsync(ct);
            await _robot.TopBottomInAsync(ct, squeeze: true, squeezeExtraUs: _robot.Settings.TurnGripSqueezeUs);
            await _robot.LeftRightOutAsync(ct);
            await _robot.HoldPitchTurnersStillAsync(ct);
            await Task.Delay(Math.Max(300, _robot.Settings.SettleMs * 2), ct);
        }, cancellationToken);
}
