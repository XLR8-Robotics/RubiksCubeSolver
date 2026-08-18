using RubiksCubeSolver.Hardware;
using RubiksCubeSolver.Models;

namespace RubiksCubeSolver.Robot;

public sealed class CubeOrientation
{
    public CubeFace Up { get; set; } = CubeFace.U;
    public CubeFace Down { get; set; } = CubeFace.D;
    public CubeFace Front { get; set; } = CubeFace.F;
    public CubeFace Back { get; set; } = CubeFace.B;
    public CubeFace Left { get; set; } = CubeFace.L;
    public CubeFace Right { get; set; } = CubeFace.R;

    public static CubeOrientation Home() => new();

    public CubeFace FaceAt(RobotStation station) => station switch
    {
        RobotStation.Right => Right,
        RobotStation.Top => Up,
        RobotStation.Left => Left,
        RobotStation.Bottom => Down,
        RobotStation.Front => Front,
        _ => Back
    };

    public RobotStation StationOf(CubeFace face)
    {
        if (Right == face) return RobotStation.Right;
        if (Up == face) return RobotStation.Top;
        if (Left == face) return RobotStation.Left;
        if (Down == face) return RobotStation.Bottom;
        if (Front == face) return RobotStation.Front;
        return RobotStation.Back;
    }

    public void Pitch(bool invert)
    {
        if (!invert)
        {
            var up = Up;
            Up = Back;
            Back = Down;
            Down = Front;
            Front = up;
        }
        else
        {
            var up = Up;
            Up = Front;
            Front = Down;
            Down = Back;
            Back = up;
        }
    }

    public void Yaw(bool invert)
    {
        if (!invert)
        {
            var front = Front;
            Front = Right;
            Right = Back;
            Back = Left;
            Left = front;
        }
        else
        {
            var front = Front;
            Front = Left;
            Left = Back;
            Back = Right;
            Right = front;
        }
    }
}

public sealed class RobotController : IDisposable
{
    readonly MaestroController _maestro;
    readonly AppSettings _settings;

    public RobotController(MaestroController maestro, AppSettings settings)
    {
        _maestro = maestro;
        _settings = settings;
        Orientation = CubeOrientation.Home();
    }

    public CubeOrientation Orientation { get; private set; }

    public void ResetOrientation() => Orientation = CubeOrientation.Home();

    public void ConfigureChannels()
    {
        ConfigureGripper(_settings.RightTurner);
        ConfigureGripper(_settings.TopTurner);
        ConfigureGripper(_settings.LeftTurner);
        ConfigureGripper(_settings.BottomTurner);
        ConfigureArm(_settings.RightArm);
        ConfigureArm(_settings.TopArm);
        ConfigureArm(_settings.LeftArm);
        ConfigureArm(_settings.BottomArm);
    }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        ResetOrientation();
        NeutralGrippers();
        AllArmsOut();
        await WaitAsync(cancellationToken);
    }

    public Task UnloadAsync(CancellationToken cancellationToken) => LoadAsync(cancellationToken);

    public async Task HugAsync(CancellationToken cancellationToken)
    {
        NeutralGrippers();
        AllArmsIn();
        await WaitAsync(cancellationToken);
    }

    public async Task PreviewPoseAsync(CancellationToken cancellationToken)
    {
        NeutralGrippers();
        AllArmsIn();
        await WaitAsync(cancellationToken);
    }

    public async Task PitchAsync(CancellationToken cancellationToken)
    {
        await HoldLeftRightAsync(cancellationToken);
        TurnGrippers(_settings.LeftTurner, _settings.RightTurner, turned: true);
        await WaitAsync(cancellationToken);
        AllArmsIn();
        await WaitAsync(cancellationToken);
        ArmsOut(_settings.LeftArm, _settings.RightArm);
        await WaitAsync(cancellationToken);
        NeutralGrippers();
        await WaitAsync(cancellationToken);
        AllArmsIn();
        await WaitAsync(cancellationToken);
        Orientation.Pitch(_settings.InvertPitch);
    }

    public async Task YawAsync(CancellationToken cancellationToken)
    {
        await HoldTopBottomAsync(cancellationToken);
        TurnGrippers(_settings.TopTurner, _settings.BottomTurner, turned: true);
        await WaitAsync(cancellationToken);
        AllArmsIn();
        await WaitAsync(cancellationToken);
        ArmsOut(_settings.TopArm, _settings.BottomArm);
        await WaitAsync(cancellationToken);
        NeutralGrippers();
        await WaitAsync(cancellationToken);
        AllArmsIn();
        await WaitAsync(cancellationToken);
        Orientation.Yaw(_settings.InvertYaw);
    }

    public async Task TurnCubeFaceAsync(CubeMove move, CancellationToken cancellationToken)
    {
        await BringToGripperAsync(move.Face, cancellationToken);
        var station = Orientation.StationOf(move.Face);
        for (int i = 0; i < move.QuarterTurns; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await QuarterTurnStationAsync(station, cancellationToken);
        }
    }

    public async Task BringToGripperAsync(CubeFace face, CancellationToken cancellationToken)
    {
        var station = Orientation.StationOf(face);
        if (station is RobotStation.Right or RobotStation.Top or RobotStation.Left or RobotStation.Bottom)
        {
            return;
        }

        await PitchAsync(cancellationToken);
    }

    public void AllServosOff()
    {
        foreach (byte channel in new byte[]
                 {
                     _settings.RightTurner.Port, _settings.RightArm.Port,
                     _settings.TopTurner.Port, _settings.TopArm.Port,
                     _settings.LeftTurner.Port, _settings.LeftArm.Port,
                     _settings.BottomTurner.Port, _settings.BottomArm.Port
                 })
        {
            _maestro.SetServoOff(channel);
        }
    }

    async Task QuarterTurnStationAsync(RobotStation station, CancellationToken cancellationToken)
    {
        var (turner, arm, oppositeArm, adjA, adjB) = station switch
        {
            RobotStation.Right => (_settings.RightTurner, _settings.RightArm, _settings.LeftArm, _settings.TopArm, _settings.BottomArm),
            RobotStation.Left => (_settings.LeftTurner, _settings.LeftArm, _settings.RightArm, _settings.TopArm, _settings.BottomArm),
            RobotStation.Top => (_settings.TopTurner, _settings.TopArm, _settings.BottomArm, _settings.LeftArm, _settings.RightArm),
            _ => (_settings.BottomTurner, _settings.BottomArm, _settings.TopArm, _settings.LeftArm, _settings.RightArm)
        };

        SetArm(oppositeArm, inside: true);
        SetArm(adjA, inside: false);
        SetArm(adjB, inside: false);
        SetArm(arm, inside: true);
        NeutralGripper(turner);
        await WaitAsync(cancellationToken);

        SetGripper(turner, turned: true);
        await WaitAsync(cancellationToken);

        SetArm(adjA, inside: true);
        SetArm(adjB, inside: true);
        await WaitAsync(cancellationToken);

        SetArm(arm, inside: false);
        await WaitAsync(cancellationToken);
        NeutralGripper(turner);
        await WaitAsync(cancellationToken);
        SetArm(arm, inside: true);
        await WaitAsync(cancellationToken);
    }

    async Task HoldLeftRightAsync(CancellationToken cancellationToken)
    {
        NeutralGrippers();
        SetArm(_settings.LeftArm, true);
        SetArm(_settings.RightArm, true);
        SetArm(_settings.TopArm, false);
        SetArm(_settings.BottomArm, false);
        await WaitAsync(cancellationToken);
    }

    async Task HoldTopBottomAsync(CancellationToken cancellationToken)
    {
        NeutralGrippers();
        SetArm(_settings.TopArm, true);
        SetArm(_settings.BottomArm, true);
        SetArm(_settings.LeftArm, false);
        SetArm(_settings.RightArm, false);
        await WaitAsync(cancellationToken);
    }

    void ConfigureGripper(GripperCalibration gripper)
    {
        _maestro.SetSpeed(gripper.Port, gripper.Speed);
        _maestro.SetAcceleration(gripper.Port, gripper.Acceleration);
    }

    void ConfigureArm(ArmCalibration arm)
    {
        _maestro.SetSpeed(arm.Port, arm.Speed);
        _maestro.SetAcceleration(arm.Port, arm.Acceleration);
    }

    void NeutralGrippers()
    {
        NeutralGripper(_settings.RightTurner);
        NeutralGripper(_settings.TopTurner);
        NeutralGripper(_settings.LeftTurner);
        NeutralGripper(_settings.BottomTurner);
    }

    void NeutralGripper(GripperCalibration gripper) => SetGripper(gripper, turned: false);

    void TurnGrippers(GripperCalibration a, GripperCalibration b, bool turned)
    {
        SetGripper(a, turned);
        SetGripper(b, turned);
    }

    void SetGripper(GripperCalibration gripper, bool turned)
    {
        _maestro.SetTargetMicroseconds(gripper.Port, turned ? gripper.EndUs : gripper.StartUs);
    }

    void AllArmsIn()
    {
        SetArm(_settings.RightArm, true);
        SetArm(_settings.TopArm, true);
        SetArm(_settings.LeftArm, true);
        SetArm(_settings.BottomArm, true);
    }

    void AllArmsOut()
    {
        SetArm(_settings.RightArm, false);
        SetArm(_settings.TopArm, false);
        SetArm(_settings.LeftArm, false);
        SetArm(_settings.BottomArm, false);
    }

    void ArmsOut(params ArmCalibration[] arms)
    {
        foreach (var arm in arms)
        {
            SetArm(arm, false);
        }
    }

    void SetArm(ArmCalibration arm, bool inside)
    {
        _maestro.SetTargetMicroseconds(arm.Port, inside ? arm.InUs : arm.OutUs);
    }

    Task WaitAsync(CancellationToken cancellationToken) =>
        _maestro.WaitUntilIdleAsync(_settings.MovementTimeoutMs, _settings.SettleMs, cancellationToken);

    public void Dispose()
    {
        // Maestro lifetime is owned by the session.
    }
}
