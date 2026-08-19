using RubiksCubeSolver.Models;
using RubiksCubeSolver.Robot.Actuation;

namespace RubiksCubeSolver.Robot.Commands.Shared;

public sealed class HugCommand : IRobotCommand
{
    readonly IRobotActuator _robot;

    public HugCommand(IRobotActuator robot) => _robot = robot;

    public string Name => "Hug cube";

    public Task ExecuteAsync(CancellationToken cancellationToken) =>
        _robot.CommandAsync(Name, EnsureHuggedAsync, cancellationToken);

    public Task ArmsOnlyAsync(CancellationToken cancellationToken) =>
        HugCubeArmsAsync(cancellationToken);

    async Task EnsureHuggedAsync(CancellationToken cancellationToken)
    {
        await HomeTurnersForHugAsync(cancellationToken);
        await HugCubeArmsAsync(cancellationToken);
    }

    async Task HugCubeArmsAsync(CancellationToken cancellationToken)
    {
        SetArmHugTopBottom();
        await _robot.WaitAsync(cancellationToken);
        await Task.Delay(Math.Max(400, _robot.Settings.SettleMs * 2), cancellationToken);
        _robot.SetArm(_robot.Settings.LeftArm, inside: true);
        _robot.SetArm(_robot.Settings.RightArm, inside: true);
        await _robot.WaitAsync(cancellationToken);
    }

    void SetArmHugTopBottom()
    {
        _robot.SetArm(
            _robot.Settings.TopArm,
            inside: true,
            squeeze: true,
            squeezeExtraUs: _robot.Settings.HugTopExtraUs);
        SetArmRelaxedIn(_robot.Settings.BottomArm, _robot.Settings.HugTopBottomBackoffUs);
    }

    void SetArmRelaxedIn(ArmCalibration arm, int backoffUs)
    {
        var towardOut = Math.Sign(arm.OutUs - arm.InUs);
        if (towardOut == 0)
        {
            towardOut = 1;
        }

        var target = arm.InUs + towardOut * Math.Max(0, backoffUs);
        if (towardOut > 0)
        {
            target = Math.Min(target, arm.OutUs - 40);
        }
        else
        {
            target = Math.Max(target, arm.OutUs + 40);
        }

        _robot.SetArmMicroseconds(arm, target);
    }

    async Task HomeTurnersForHugAsync(CancellationToken cancellationToken)
    {
        if (_robot.PairNearStart(_robot.Settings.LeftTurner, _robot.Settings.RightTurner) != true)
        {
            await _robot.LeftRightInAsync(cancellationToken, squeeze: false);
            await Task.Delay(Math.Max(400, _robot.Settings.SettleMs * 2), cancellationToken);
            await _robot.TopBottomOutAsync(cancellationToken);
            await _robot.WaitUntilArmsNearAsync(_robot.Settings.TopArm, _robot.Settings.BottomArm, retracted: true, cancellationToken);
            await Task.Delay(Math.Max(800, _robot.Settings.SettleMs * 4), cancellationToken);
            await _robot.PitchTurnersToStartAsync(cancellationToken);
            await _robot.TopBottomOutAsync(cancellationToken);
        }

        if (_robot.PairNearStart(_robot.Settings.TopTurner, _robot.Settings.BottomTurner) != true)
        {
            await _robot.LeftRightOutAsync(cancellationToken, clearOfCube: true);
            await Task.Delay(Math.Max(400, _robot.Settings.SettleMs * 2), cancellationToken);
            SetArmHugTopBottom();
            await _robot.WaitAsync(cancellationToken);
            await Task.Delay(Math.Max(400, _robot.Settings.SettleMs * 2), cancellationToken);
            await _robot.YawTurnersToStartAsync(cancellationToken);
            await _robot.LeftRightInAsync(cancellationToken, squeeze: false);
        }

        _robot.PitchTurnersHomed = true;
        _robot.YawTurnersHomed = true;
    }
}
