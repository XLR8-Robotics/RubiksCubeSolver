using RubiksCubeSolver.Models;
using RubiksCubeSolver.Robot.Actuation;

namespace RubiksCubeSolver.Robot.Commands.Solve;

public sealed class GripperQuarterTurnCommand : IRobotCommand
{
    const double StartToleranceUs = 50;

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

        _robot.SetArm(arm, inside: true);
        await _robot.WaitAsync(cancellationToken);

        _robot.SetGripperQuarterTurn(turner, _prime);
        await _robot.WaitAsync(cancellationToken);
        await Task.Delay(Math.Max(200, _robot.Settings.SettleMs * 2), cancellationToken);

        _robot.SetArm(arm, inside: false);
        await _robot.WaitAsync(cancellationToken);
        await _robot.WaitUntilArmsNearAsync(arm, arm, retracted: true, cancellationToken);
        await Task.Delay(Math.Max(800, _robot.Settings.SettleMs * 4), cancellationToken);

        _robot.NeutralGripper(turner);
        await _robot.WaitAsync(cancellationToken);
        await WaitUntilGripperNearStartAsync(turner, cancellationToken);

        SetArmBackIn(arm);
        await _robot.WaitAsync(cancellationToken);
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

    async Task WaitUntilGripperNearStartAsync(GripperCalibration turner, CancellationToken cancellationToken)
    {
        var start = Environment.TickCount64;
        while (Environment.TickCount64 - start < _robot.Settings.MovementTimeoutMs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (GripperNearStart(turner))
            {
                return;
            }

            _robot.NeutralGripper(turner);
            await Task.Delay(150, cancellationToken);
        }
    }

    bool GripperNearStart(GripperCalibration turner)
    {
        var pos = _robot.GetPositionMicroseconds(turner.Port);
        return pos is null || Math.Abs(pos.Value - turner.StartUs) <= StartToleranceUs;
    }
}
