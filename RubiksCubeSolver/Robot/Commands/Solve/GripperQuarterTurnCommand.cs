using RubiksCubeSolver.Models;
using RubiksCubeSolver.Robot.Actuation;

namespace RubiksCubeSolver.Robot.Commands.Solve;

public sealed class GripperQuarterTurnCommand : IRobotCommand
{
    const double PulseToleranceUs = 50;

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

        _robot.SetArm(arm, inside: true);
        await WaitUntilArmNearAsync(arm, retracted: false, cancellationToken);

        _robot.SetGripperQuarterTurn(turner, _prime);
        await WaitUntilGripperNearAsync(turner, turnTarget, cancellationToken);

        await RetractWhileHoldingAsync(turner, arm, turnTarget, cancellationToken);

        _robot.NeutralGripper(turner);
        await WaitUntilGripperNearAsync(turner, turner.StartUs, cancellationToken);

        SetArmBackIn(arm);
        await WaitUntilArmNearAsync(arm, retracted: false, cancellationToken);
    }

    async Task RetractWhileHoldingAsync(
        GripperCalibration turner, ArmCalibration arm, double holdUs, CancellationToken cancellationToken)
    {
        var started = Environment.TickCount64;
        while (Environment.TickCount64 - started < _robot.Settings.MovementTimeoutMs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _robot.SetGripperMicroseconds(turner, holdUs);
            _robot.SetArm(arm, inside: false);
            if (ArmNear(arm, retracted: true))
            {
                break;
            }

            await Task.Delay(40, cancellationToken);
        }

        _robot.SetGripperMicroseconds(turner, holdUs);
        await Task.Delay(Math.Max(300, _robot.Settings.SettleMs * 2), cancellationToken);
        _robot.SetGripperMicroseconds(turner, holdUs);
    }

    async Task WaitUntilGripperNearAsync(GripperCalibration turner, double targetUs, CancellationToken cancellationToken)
    {
        var travel = Math.Abs(targetUs - ( _robot.GetPositionMicroseconds(turner.Port) ?? turner.StartUs));
        var minMs = (int)Math.Clamp(travel / 1.2 + 250, 400, _robot.Settings.MovementTimeoutMs);
        var started = Environment.TickCount64;
        while (Environment.TickCount64 - started < _robot.Settings.MovementTimeoutMs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _robot.SetGripperMicroseconds(turner, targetUs);

            var elapsed = Environment.TickCount64 - started;
            var pos = _robot.GetPositionMicroseconds(turner.Port);
            if (elapsed >= minMs)
            {
                if (pos is null || Math.Abs(pos.Value - targetUs) <= PulseToleranceUs)
                {
                    return;
                }

                if (elapsed >= minMs + 400)
                {
                    return;
                }
            }

            await Task.Delay(40, cancellationToken);
        }
    }

    async Task WaitUntilArmNearAsync(ArmCalibration arm, bool retracted, CancellationToken cancellationToken)
    {
        var started = Environment.TickCount64;
        while (Environment.TickCount64 - started < _robot.Settings.MovementTimeoutMs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _robot.SetArm(arm, inside: !retracted);
            if (ArmNear(arm, retracted))
            {
                return;
            }

            await Task.Delay(40, cancellationToken);
        }
    }

    bool ArmNear(ArmCalibration arm, bool retracted)
    {
        var pos = _robot.GetPositionMicroseconds(arm.Port);
        if (pos is null)
        {
            return false;
        }

        var target = retracted ? arm.OutUs : arm.InUs;
        var travel = Math.Max(1, Math.Abs(arm.OutUs - arm.InUs));
        return Math.Abs(pos.Value - target) <= Math.Max(50, travel * 0.08);
    }

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
