using RubiksCubeSolver.Models;
using RubiksCubeSolver.Robot.Actuation;

namespace RubiksCubeSolver.Robot.Commands.Solve;

public sealed class DisplayCommand : IRobotCommand
{
    readonly IRobotActuator _robot;

    public DisplayCommand(IRobotActuator robot) => _robot = robot;

    public string Name => "Display TOP: pitch up, bottom holds, sides/top clear";

    public Task ExecuteAsync(CancellationToken cancellationToken) =>
        _robot.CommandAsync(Name, DisplayCoreAsync, cancellationToken);

    async Task DisplayCoreAsync(CancellationToken cancellationToken)
    {
        if (_robot.Orientation.Front != CubeFace.U)
        {
            await _robot.HoldYawTurnersStillAsync(cancellationToken);
            await _robot.LeftRightInAsync(cancellationToken, squeeze: true, squeezeExtraUs: _robot.Settings.ScanHoldSqueezeUs);
            await Task.Delay(Math.Max(400, _robot.Settings.SettleMs * 2), cancellationToken);
            await _robot.TopBottomOutAsync(cancellationToken);
            await _robot.WaitUntilArmsNearAsync(_robot.Settings.TopArm, _robot.Settings.BottomArm, retracted: true, cancellationToken);
            await Task.Delay(Math.Max(800, _robot.Settings.SettleMs * 4), cancellationToken);
            await _robot.HoldYawTurnersStillAsync(cancellationToken);
            await _robot.PitchSpin90Async(cancellationToken, opposite: false);
        }

        _robot.SetArm(_robot.Settings.BottomArm, inside: true);
        await _robot.WaitAsync(cancellationToken);
        await Task.Delay(Math.Max(400, _robot.Settings.SettleMs * 2), cancellationToken);
        _robot.SetArm(_robot.Settings.TopArm, inside: false);
        _robot.SetArm(_robot.Settings.LeftArm, inside: false);
        _robot.SetArm(_robot.Settings.RightArm, inside: false);
        await _robot.WaitAsync(cancellationToken);
        await Task.Delay(Math.Max(400, _robot.Settings.SettleMs * 2), cancellationToken);
    }
}
