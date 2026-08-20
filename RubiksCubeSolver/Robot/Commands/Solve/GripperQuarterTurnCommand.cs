using RubiksCubeSolver.Models;
using RubiksCubeSolver.Robot.Actuation;

namespace RubiksCubeSolver.Robot.Commands.Solve;

public sealed class GripperQuarterTurnCommand : IRobotCommand
{
    readonly IRobotActuator _robot;
    readonly RobotStation _station;
    readonly bool _prime;

    public GripperQuarterTurnCommand(IRobotActuator robot, RobotStation station, bool prime)
    {
        _robot = robot;
        _station = station;
        _prime = prime;
    }

    public string Name => $"{_station} {(_prime ? "90° '" : "90°")}";

    public Task ExecuteAsync(CancellationToken cancellationToken) =>
        _robot.CommandAsync(Name, TurnThenRewindAsync, cancellationToken);

    async Task TurnThenRewindAsync(CancellationToken cancellationToken)
    {
        var (turner, arm) = _station switch
        {
            RobotStation.Right => (_robot.Settings.RightTurner, _robot.Settings.RightArm),
            RobotStation.Left => (_robot.Settings.LeftTurner, _robot.Settings.LeftArm),
            RobotStation.Top => (_robot.Settings.TopTurner, _robot.Settings.TopArm),
            _ => (_robot.Settings.BottomTurner, _robot.Settings.BottomArm)
        };

        var turnTarget = turner.QuarterTurnTargetUs(_prime);
        var turnMs = MoveMs(Math.Abs(turnTarget - turner.StartUs));
        var liftMs = MoveMs(Math.Abs(arm.OutUs - arm.InUs));

        _robot.SetArm(arm, inside: true);
        await Task.Delay(Math.Max(200, _robot.Settings.SettleMs), cancellationToken);

        _robot.SetGripperQuarterTurn(turner, _prime);
        await HoldPulseAsync(turner, turnTarget, turnMs, cancellationToken);

        _robot.OnCommand?.Invoke("Hold 90° while arm retracts");
        _robot.SetArm(arm, inside: false);
        await HoldPulseAsync(turner, turnTarget, liftMs + 500, cancellationToken);

        _robot.OnCommand?.Invoke("Arm clear — rewind gripper");
        _robot.NeutralGripper(turner);
        await HoldPulseAsync(turner, turner.StartUs, turnMs, cancellationToken);

        SetArmBackIn(arm);
        await Task.Delay(liftMs, cancellationToken);
    }

    async Task HoldPulseAsync(
        GripperCalibration turner, double pulseUs, int durationMs, CancellationToken cancellationToken)
    {
        var started = Environment.TickCount64;
        while (Environment.TickCount64 - started < durationMs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _robot.SetGripperMicroseconds(turner, pulseUs);
            await Task.Delay(50, cancellationToken);
        }

        _robot.SetGripperMicroseconds(turner, pulseUs);
    }

    int MoveMs(double travelUs) =>
        (int)Math.Clamp(travelUs / 1.2 + 300, 500, _robot.Settings.MovementTimeoutMs);

    void SetArmBackIn(ArmCalibration arm)
    {
        if (_station is RobotStation.Top)
        {
            _robot.SetArm(arm, inside: true, squeeze: true, squeezeExtraUs: _robot.Settings.HugTopExtraUs);
            return;
        }

        _robot.SetArm(arm, inside: true);
    }
}
