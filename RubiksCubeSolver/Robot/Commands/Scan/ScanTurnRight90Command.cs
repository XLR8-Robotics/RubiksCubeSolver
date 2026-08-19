using RubiksCubeSolver.Robot.Actuation;

namespace RubiksCubeSolver.Robot.Commands.Scan;

public sealed class ScanTurnRight90Command : IRobotCommand
{
    readonly IRobotActuator _robot;
    readonly ScanPrepareForYawTurnCommand _prepare;
    readonly ScanRetractTbBetweenTurnsCommand _retractBetween;

    public ScanTurnRight90Command(
        IRobotActuator robot,
        ScanPrepareForYawTurnCommand prepare,
        ScanRetractTbBetweenTurnsCommand retractBetween)
    {
        _robot = robot;
        _prepare = prepare;
        _retractBetween = retractBetween;
    }

    public string Name => "TURN_R_90";

    public Task ExecuteAsync(CancellationToken cancellationToken) => ExecuteAsync(1, cancellationToken);

    public Task ExecuteAsync(int count, CancellationToken cancellationToken) =>
        _robot.CommandAsync(count == 1 ? "TURN_R_90" : $"TURN_R_90 ×{count}", async ct =>
        {
            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    await _retractBetween.ExecuteAsync(ct);
                }

                await _prepare.ExecuteAsync(ct);
                var invert = _robot.Settings.InvertYaw;
                if (i == 0 && _robot.PairNearStart(_robot.Settings.TopTurner, _robot.Settings.BottomTurner) != true)
                {
                    _robot.OnCommand?.Invoke("TURN_R_90 blocked — top/bottom turners are not at Start");
                    return;
                }

                if (i == 0)
                {
                    await _robot.SpinPairAsync(_robot.Settings.TopTurner, _robot.Settings.BottomTurner, invert, ct, yawMatchedOpposite: true);
                }
                else
                {
                    await _robot.SpinPairYawFromCurrentAsync(_robot.Settings.TopTurner, _robot.Settings.BottomTurner, invert, ct);
                }

                _robot.Orientation.Yaw(invert);
            }
        }, cancellationToken);
}
