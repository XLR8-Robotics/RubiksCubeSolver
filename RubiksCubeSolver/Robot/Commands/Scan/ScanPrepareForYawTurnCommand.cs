using RubiksCubeSolver.Robot.Actuation;

namespace RubiksCubeSolver.Robot.Commands.Scan;

public sealed class ScanPrepareForYawTurnCommand : IRobotCommand
{
    readonly IRobotActuator _robot;

    public ScanPrepareForYawTurnCommand(IRobotActuator robot) => _robot = robot;

    public string Name => "TB_IN (turn grip), RL_OUT — yaw grip";

    public Task ExecuteAsync(CancellationToken cancellationToken) =>
        _robot.CommandAsync(Name, async ct =>
        {
            await _robot.TopBottomInAsync(ct, squeeze: true, squeezeExtraUs: _robot.Settings.TurnGripSqueezeUs);
            await _robot.LeftRightOutAsync(ct);
            await _robot.HoldPitchTurnersStillAsync(ct);
            await Task.Delay(Math.Max(400, _robot.Settings.SettleMs * 3), ct);
        }, cancellationToken);
}
