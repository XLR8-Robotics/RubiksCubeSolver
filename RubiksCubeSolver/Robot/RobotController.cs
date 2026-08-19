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

    public async Task ArmsInHoldAsync(CancellationToken cancellationToken)
    {
        AllArmsIn();
        await WaitAsync(cancellationToken);
    }

    public async Task DisplayAsync(CancellationToken cancellationToken)
    {
        NeutralGrippers();
        SetArm(_settings.BottomArm, inside: true);
        SetArm(_settings.LeftArm, inside: false);
        SetArm(_settings.RightArm, inside: false);
        SetArm(_settings.TopArm, inside: false);
        await WaitAsync(cancellationToken);
    }

    public async Task PreviewPoseAsync(CancellationToken cancellationToken)
    {
        NeutralGrippers();
        AllArmsIn();
        await WaitAsync(cancellationToken);
    }

    public async Task PitchAsync(CancellationToken cancellationToken, bool opposite = false)
    {
        var invert = _settings.InvertPitch ^ opposite;
        await HoldPairAsync(_settings.LeftArm, _settings.RightArm, _settings.TopArm, _settings.BottomArm, cancellationToken);
        await SpinPairAsync(_settings.LeftTurner, _settings.RightTurner, invert, cancellationToken);
        AllArmsIn();
        await WaitAsync(cancellationToken);
        ArmsOut(_settings.LeftArm, _settings.RightArm);
        await WaitAsync(cancellationToken);
        NeutralGrippers();
        await WaitAsync(cancellationToken);
        AllArmsIn();
        await WaitAsync(cancellationToken);
        Orientation.Pitch(invert);
    }

    public async Task YawAsync(CancellationToken cancellationToken, bool opposite = false)
    {
        var invert = _settings.InvertYaw ^ opposite;
        await HoldPairAsync(_settings.TopArm, _settings.BottomArm, _settings.LeftArm, _settings.RightArm, cancellationToken);
        await SpinPairAsync(_settings.TopTurner, _settings.BottomTurner, invert, cancellationToken);
        AllArmsIn();
        await WaitAsync(cancellationToken);
        ArmsOut(_settings.TopArm, _settings.BottomArm);
        await WaitAsync(cancellationToken);
        NeutralGrippers();
        await WaitAsync(cancellationToken);
        AllArmsIn();
        await WaitAsync(cancellationToken);
        Orientation.Yaw(invert);
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

    public IReadOnlyList<(RobotStation Station, CubeMove Move)> CreateGrippedScramble(int moves)
    {
        var list = new List<(RobotStation Station, CubeMove Move)>(moves);
        var rng = new Random();
        var stations = new[] { RobotStation.Right, RobotStation.Top, RobotStation.Left, RobotStation.Bottom };
        RobotStation? last = null;
        for (int i = 0; i < moves; i++)
        {
            RobotStation station;
            do
            {
                station = stations[rng.Next(stations.Length)];
            } while (last is not null && station == last);

            last = station;
            list.Add((station, new CubeMove(Orientation.FaceAt(station), 1)));
        }

        return list;
    }

    public Task QuarterTurnStationOnceAsync(RobotStation station, CancellationToken cancellationToken) =>
        QuarterTurnStationAsync(station, cancellationToken);

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
        var (turner, arm) = station switch
        {
            RobotStation.Right => (_settings.RightTurner, _settings.RightArm),
            RobotStation.Left => (_settings.LeftTurner, _settings.LeftArm),
            RobotStation.Top => (_settings.TopTurner, _settings.TopArm),
            _ => (_settings.BottomTurner, _settings.BottomArm)
        };

        SetArm(arm, inside: true);
        await WaitAsync(cancellationToken);

        SetGripper(turner, turned: true);
        await WaitAsync(cancellationToken);

        SetArm(arm, inside: false);
        await WaitAsync(cancellationToken);

        NeutralGripper(turner);
        await WaitAsync(cancellationToken);

        SetArm(arm, inside: true);
        await WaitAsync(cancellationToken);
    }

    async Task HoldPairAsync(ArmCalibration holdA, ArmCalibration holdB, ArmCalibration clearA, ArmCalibration clearB, CancellationToken cancellationToken, bool squeeze = true)
    {
        SetArm(holdA, inside: true, squeeze: squeeze);
        SetArm(holdB, inside: true, squeeze: squeeze);
        SetArm(clearA, inside: false);
        SetArm(clearB, inside: false);
        await WaitAsync(cancellationToken);
    }

    public Task HoldTopBottomScanAsync(CancellationToken cancellationToken) =>
        HoldPairAsync(_settings.TopArm, _settings.BottomArm, _settings.LeftArm, _settings.RightArm, cancellationToken, squeeze: false);

    public Task HoldLeftRightScanAsync(CancellationToken cancellationToken) =>
        HoldPairAsync(_settings.LeftArm, _settings.RightArm, _settings.TopArm, _settings.BottomArm, cancellationToken, squeeze: false);

    async Task SpinPairAsync(GripperCalibration a, GripperCalibration b, bool invertDirection, CancellationToken cancellationToken)
    {
        var targetA = invertDirection ? a.OppositeEndUs : a.EndUs;
        var targetB = invertDirection ? b.EndUs : b.OppositeEndUs;
        MatchPairSpeeds(a, b, targetA, targetB);
        SetGripperTarget(a, targetA);
        SetGripperTarget(b, targetB);
        await WaitAsync(cancellationToken);
        ConfigureGripper(a);
        ConfigureGripper(b);
    }

    void MatchPairSpeeds(GripperCalibration a, GripperCalibration b, double targetA, double targetB)
    {
        var distA = Math.Max(1, Math.Abs(targetA - a.StartUs));
        var distB = Math.Max(1, Math.Abs(targetB - b.StartUs));
        var shorter = Math.Min(distA, distB);
        const double baseSpeed = 50;
        var speedA = (ushort)Math.Clamp(Math.Round(baseSpeed * distA / shorter), 1, 255);
        var speedB = (ushort)Math.Clamp(Math.Round(baseSpeed * distB / shorter), 1, 255);
        _maestro.SetAcceleration(a.Port, 0);
        _maestro.SetAcceleration(b.Port, 0);
        _maestro.SetSpeed(a.Port, speedA);
        _maestro.SetSpeed(b.Port, speedB);
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

    void SetGripper(GripperCalibration gripper, bool turned)
    {
        SetGripperTarget(gripper, turned ? gripper.EndUs : gripper.StartUs);
    }

    void SetGripperTarget(GripperCalibration gripper, double microseconds)
    {
        _maestro.SetTargetMicroseconds(gripper.Port, microseconds);
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

    void SetArm(ArmCalibration arm, bool inside, bool squeeze = false)
    {
        double microseconds;
        if (!inside)
        {
            microseconds = arm.OutUs;
        }
        else if (squeeze)
        {
            var extra = Math.Max(0, _settings.TumbleSqueezeUs);
            var towardCube = Math.Sign(arm.InUs - arm.OutUs);
            if (towardCube == 0)
            {
                towardCube = -1;
            }

            microseconds = Math.Clamp(arm.InUs + towardCube * extra, 500, 2496);
        }
        else
        {
            microseconds = arm.InUs;
        }

        _maestro.SetTargetMicroseconds(arm.Port, microseconds);
    }

    Task WaitAsync(CancellationToken cancellationToken) =>
        _maestro.WaitUntilIdleAsync(_settings.MovementTimeoutMs, _settings.SettleMs, cancellationToken);

    public void Dispose()
    {
        // Maestro lifetime is owned by the session.
    }
}
