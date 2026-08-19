using RubiksCubeSolver.Robot.Actuation;

namespace RubiksCubeSolver.Robot.Commands.Scan;

public sealed class ScanYawTurnersHomeKeepFaceCommand : IRobotCommand
{
    readonly IRobotActuator _robot;

    public ScanYawTurnersHomeKeepFaceCommand(IRobotActuator robot) => _robot = robot;

    public string Name => "RL_IN secure, TB_OUT, yaw home, TB_IN, RL_OUT";

    public Task ExecuteAsync(CancellationToken cancellationToken) =>
        _robot.CommandAsync(Name, HomeCoreAsync, cancellationToken);

    public Task ExecuteAtFrontAsync(CancellationToken cancellationToken) =>
        _robot.CommandAsync("RL secure, TB clear, yaw home at FRONT", HomeCoreAsync, cancellationToken);

    public Task ExecuteCoreAsync(CancellationToken cancellationToken) =>
        HomeCoreAsync(cancellationToken);

    async Task HomeCoreAsync(CancellationToken cancellationToken)
    {
        await _robot.HoldPitchTurnersStillAsync(cancellationToken);
        await _robot.LeftRightInAsync(cancellationToken, squeeze: true, squeezeExtraUs: _robot.Settings.ScanHoldSqueezeUs);
        await Task.Delay(Math.Max(400, _robot.Settings.SettleMs * 2), cancellationToken);
        await _robot.TopBottomOutAsync(cancellationToken);
        await _robot.WaitUntilArmsNearAsync(_robot.Settings.TopArm, _robot.Settings.BottomArm, retracted: true, cancellationToken);
        await Task.Delay(Math.Max(800, _robot.Settings.SettleMs * 4), cancellationToken);
        await _robot.YawTurnersToStartAsync(cancellationToken);
        await _robot.TopBottomInAsync(cancellationToken, squeeze: false);
        await _robot.LeftRightOutAsync(cancellationToken);
    }
}
