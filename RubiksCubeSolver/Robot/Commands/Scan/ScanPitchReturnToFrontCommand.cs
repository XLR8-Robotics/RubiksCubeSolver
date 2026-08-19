using RubiksCubeSolver.Models;
using RubiksCubeSolver.Robot.Actuation;

namespace RubiksCubeSolver.Robot.Commands.Scan;

public sealed class ScanPitchReturnToFrontCommand : IRobotCommand
{
    readonly IRobotActuator _robot;

    public ScanPitchReturnToFrontCommand(IRobotActuator robot) => _robot = robot;

    public string Name => "RL hold, TB clear, pitch unwind → FRONT";

    public Task ExecuteAsync(CancellationToken cancellationToken) =>
        _robot.CommandAsync(Name, ReturnCoreAsync, cancellationToken);

    async Task ReturnCoreAsync(CancellationToken cancellationToken)
    {
        await _robot.HoldYawTurnersStillAsync(cancellationToken);
        await _robot.LeftRightInAsync(cancellationToken, squeeze: true, squeezeExtraUs: _robot.Settings.ScanHoldSqueezeUs);
        await Task.Delay(Math.Max(400, _robot.Settings.SettleMs * 2), cancellationToken);
        await _robot.TopBottomOutAsync(cancellationToken);
        await _robot.WaitUntilArmsNearAsync(_robot.Settings.TopArm, _robot.Settings.BottomArm, retracted: true, cancellationToken);
        await Task.Delay(Math.Max(400, _robot.Settings.SettleMs * 2), cancellationToken);

        var faceAtCamera = _robot.Orientation.Front;
        await _robot.ReversePairToStartAsync(_robot.Settings.LeftTurner, _robot.Settings.RightTurner, cancellationToken);
        await _robot.WaitUntilPairNearStartAsync(_robot.Settings.LeftTurner, _robot.Settings.RightTurner, cancellationToken);
        _robot.PitchTurnersHomed = true;

        if (faceAtCamera == CubeFace.U)
        {
            _robot.Orientation.Pitch(true);
        }
        else if (faceAtCamera == CubeFace.D)
        {
            _robot.Orientation.Pitch(false);
        }

        await _robot.HoldPitchTurnersStillAsync(cancellationToken);
    }
}
