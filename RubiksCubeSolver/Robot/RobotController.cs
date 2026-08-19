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

    public Action<string>? OnCommand { get; set; }

    public CubeOrientation Orientation { get; private set; }

    public void ResetOrientation()
    {
        Orientation = CubeOrientation.Home();
        YawTurnersHomed = true;
        PitchTurnersHomed = true;
    }

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
        await RetractAllThenHomeTurnersAsync(cancellationToken);
    }

    public Task UnloadAsync(CancellationToken cancellationToken) => LoadAsync(cancellationToken);

    public async Task HugAsync(CancellationToken cancellationToken)
    {
        await RetractAllThenHomeTurnersAsync(cancellationToken);
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
        await RetractAllThenHomeTurnersAsync(cancellationToken);
        SetArm(_settings.BottomArm, inside: true);
        SetArm(_settings.LeftArm, inside: false);
        SetArm(_settings.RightArm, inside: false);
        SetArm(_settings.TopArm, inside: false);
        await WaitAsync(cancellationToken);
    }

    public async Task PreviewPoseAsync(CancellationToken cancellationToken)
    {
        await HugAsync(cancellationToken);
    }

    public async Task PitchAsync(CancellationToken cancellationToken, bool opposite = false)
    {
        var invert = _settings.InvertPitch ^ opposite;
        await HoldPairAsync(_settings.LeftArm, _settings.RightArm, _settings.TopArm, _settings.BottomArm, cancellationToken);
        await SpinPairAsync(_settings.LeftTurner, _settings.RightTurner, invert, cancellationToken, _settings.PitchExtraUs);
        AllArmsIn();
        await WaitAsync(cancellationToken);
        await RetractThenHomeTurnersAsync(_settings.LeftArm, _settings.RightArm, _settings.LeftTurner, _settings.RightTurner, cancellationToken);
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
        await RetractThenHomeTurnersAsync(_settings.TopArm, _settings.BottomArm, _settings.TopTurner, _settings.BottomTurner, cancellationToken);
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
        await WaitAsync(cancellationToken);
        SetArm(clearA, inside: false);
        SetArm(clearB, inside: false);
        await WaitAsync(cancellationToken);
    }

    public Task TopBottomInAsync(CancellationToken cancellationToken, bool squeeze = true) =>
        CommandAsync("Top/Bottom in", async ct =>
        {
            SetArm(_settings.TopArm, inside: true, squeeze: squeeze);
            SetArm(_settings.BottomArm, inside: true, squeeze: squeeze);
            await WaitAsync(ct);
            await Task.Delay(Math.Max(200, _settings.SettleMs), ct);
        }, cancellationToken);

    public Task TopBottomOutAsync(CancellationToken cancellationToken) =>
        CommandAsync("Top/Bottom out", async ct =>
        {
            SetArm(_settings.TopArm, inside: false);
            SetArm(_settings.BottomArm, inside: false);
            await WaitAsync(ct);
        }, cancellationToken);

    public Task LeftRightInAsync(CancellationToken cancellationToken, bool squeeze = true) =>
        CommandAsync("Left/Right in", async ct =>
        {
            SetArm(_settings.LeftArm, inside: true, squeeze: squeeze);
            SetArm(_settings.RightArm, inside: true, squeeze: squeeze);
            await WaitAsync(ct);
            await Task.Delay(Math.Max(200, _settings.SettleMs), ct);
        }, cancellationToken);

    public Task LeftRightOutAsync(CancellationToken cancellationToken, bool clearOfCube = false) =>
        CommandAsync(clearOfCube ? "Left/Right out and clear of cube" : "Left/Right out", async ct =>
        {
            SetArm(_settings.LeftArm, inside: false);
            SetArm(_settings.RightArm, inside: false);
            await WaitAsync(ct);
            if (clearOfCube)
            {
                await Task.Delay(Math.Max(1200, _settings.SettleMs * 6), ct);
            }
        }, cancellationToken);

    public Task PitchTurnersToStartAsync(CancellationToken cancellationToken) =>
        CommandAsync("Pitch turners to Start", ct => ReversePairToStartAsync(_settings.LeftTurner, _settings.RightTurner, ct), cancellationToken);

    public Task YawTurnersToStartAsync(CancellationToken cancellationToken) =>
        CommandAsync("Yaw turners to Start", ct => ReversePairToStartAsync(_settings.TopTurner, _settings.BottomTurner, ct), cancellationToken);

    public Task PitchSpin90Async(CancellationToken cancellationToken, bool opposite = false) =>
        CommandAsync(opposite ? "Pitch 90° the other way" : "Pitch 90°", async ct =>
        {
            var invert = _settings.InvertPitch ^ opposite;
            await SpinPairAsync(_settings.LeftTurner, _settings.RightTurner, invert, ct, _settings.PitchExtraUs);
            Orientation.Pitch(invert);
        }, cancellationToken);

    public Task YawSpin90Async(CancellationToken cancellationToken, bool opposite = false) =>
        CommandAsync(opposite ? "Yaw 90° other way" : "Yaw 90°", async ct =>
        {
            var invert = _settings.InvertYaw ^ opposite;
            await SpinPairAsync(_settings.TopTurner, _settings.BottomTurner, invert, ct);
            Orientation.Yaw(invert);
        }, cancellationToken);

    public async Task SequencePitchResetAsync(CancellationToken cancellationToken)
    {
        await TopBottomInAsync(cancellationToken, squeeze: true);
        await LeftRightOutAsync(cancellationToken, clearOfCube: true);
        await PitchTurnersToStartAsync(cancellationToken);
        await LeftRightInAsync(cancellationToken, squeeze: true);
        await TopBottomOutAsync(cancellationToken);
    }

    public async Task SequencePitch90Async(CancellationToken cancellationToken, bool opposite = false)
    {
        await LeftRightInAsync(cancellationToken, squeeze: true);
        await TopBottomOutAsync(cancellationToken);
        await PitchSpin90Async(cancellationToken, opposite);
    }

    public async Task SequenceYawResetAsync(CancellationToken cancellationToken)
    {
        await LeftRightInAsync(cancellationToken, squeeze: false);
        await TopBottomOutAsync(cancellationToken);
        await Task.Delay(Math.Max(800, _settings.SettleMs * 4), cancellationToken);
        await YawTurnersToStartAsync(cancellationToken);
        await TopBottomInAsync(cancellationToken, squeeze: false);
        await LeftRightOutAsync(cancellationToken);
    }

    public async Task SequenceYaw90Async(CancellationToken cancellationToken, bool opposite = false)
    {
        await TopBottomInAsync(cancellationToken, squeeze: false);
        await LeftRightOutAsync(cancellationToken);
        await YawSpin90Async(cancellationToken, opposite);
    }

    public async Task SequenceHandoffToPitchAsync(CancellationToken cancellationToken)
    {
        await LeftRightInAsync(cancellationToken, squeeze: true);
        await TopBottomOutAsync(cancellationToken);
        await Task.Delay(Math.Max(800, _settings.SettleMs * 4), cancellationToken);
        await YawTurnersToStartAsync(cancellationToken);
    }

    public async Task SequenceScanHugAsync(CancellationToken cancellationToken)
    {
        await TopBottomInAsync(cancellationToken, squeeze: false);
        await LeftRightInAsync(cancellationToken, squeeze: false);
    }

    public Task HoldTopBottomScanAsync(CancellationToken cancellationToken) =>
        HoldPairAsync(_settings.TopArm, _settings.BottomArm, _settings.LeftArm, _settings.RightArm, cancellationToken, squeeze: false);

    public Task HoldLeftRightScanAsync(CancellationToken cancellationToken) =>
        HoldPairAsync(_settings.LeftArm, _settings.RightArm, _settings.TopArm, _settings.BottomArm, cancellationToken, squeeze: false);

    public Task YawScanAsync(CancellationToken cancellationToken, bool opposite = false) =>
        SequenceYaw90Async(cancellationToken, opposite);

    public Task ResetYawTurnersForScanAsync(CancellationToken cancellationToken) =>
        SequenceYawResetAsync(cancellationToken);

    public Task ResetPitchTurnersForScanAsync(CancellationToken cancellationToken) =>
        SequencePitchResetAsync(cancellationToken);

    public Task HandoffToLeftRightParkTopBottomAsync(CancellationToken cancellationToken) =>
        SequenceHandoffToPitchAsync(cancellationToken);

    public Task PitchScanAsync(CancellationToken cancellationToken, bool opposite = false) =>
        SequencePitch90Async(cancellationToken, opposite);

    public Task FinishScanHugAsync(CancellationToken cancellationToken) =>
        SequenceScanHugAsync(cancellationToken);

    async Task CommandAsync(string name, Func<CancellationToken, Task> command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OnCommand?.Invoke(name);
        await command(cancellationToken);
    }

    async Task RetractAllThenHomeTurnersAsync(CancellationToken cancellationToken)
    {
        AllArmsOut();
        await WaitAsync(cancellationToken);
        NeutralGrippers();
        await WaitAsync(cancellationToken);
        YawTurnersHomed = true;
        PitchTurnersHomed = true;
    }

    async Task RetractThenHomeTurnersAsync(
        ArmCalibration armA, ArmCalibration armB,
        GripperCalibration turnerA, GripperCalibration turnerB,
        CancellationToken cancellationToken)
    {
        SetArm(armA, inside: false);
        SetArm(armB, inside: false);
        await WaitAsync(cancellationToken);
        await WaitUntilArmsNearAsync(armA, armB, retracted: true, cancellationToken);
        await Task.Delay(Math.Max(800, _settings.SettleMs * 4), cancellationToken);
        await ReversePairToStartAsync(turnerA, turnerB, cancellationToken);
    }

    bool YawTurnersHomed { get; set; } = true;
    bool PitchTurnersHomed { get; set; } = true;

    const double ServoMinUs = 256;
    const double ServoMaxUs = 2496;

    async Task SpinPairAsync(GripperCalibration a, GripperCalibration b, bool invertDirection, CancellationToken cancellationToken, int extraUs = 0)
    {
        var (targetA, targetB) = PairTumbleTargets(a, b, invertDirection, extraUs);
        MatchPairSpeeds(a, b, a.StartUs, b.StartUs, targetA, targetB);
        SetGripperTarget(a, targetA);
        SetGripperTarget(b, targetB);
        await WaitForPairMoveAsync(Math.Max(Math.Abs(targetA - a.StartUs), Math.Abs(targetB - b.StartUs)), cancellationToken);
        MarkPairHomed(a, b, homed: false);
    }

    async Task ReversePairToStartAsync(GripperCalibration a, GripperCalibration b, CancellationToken cancellationToken)
    {
        var fromA = _maestro.GetPositionMicroseconds(a.Port) ?? a.StartUs;
        var fromB = _maestro.GetPositionMicroseconds(b.Port) ?? b.StartUs;
        var dist = Math.Max(Math.Abs(fromA - a.StartUs), Math.Abs(fromB - b.StartUs));
        if (dist < 40)
        {
            dist = Math.Max(Math.Abs(a.EndUs - a.StartUs), Math.Abs(b.EndUs - b.StartUs));
        }

        MatchPairSpeeds(a, b, fromA, fromB, a.StartUs, b.StartUs);
        SetGripperTarget(a, a.StartUs);
        SetGripperTarget(b, b.StartUs);
        await WaitForPairMoveAsync(dist, cancellationToken);

        for (int attempt = 0; attempt < 3 && PairNearStart(a, b) == false; attempt++)
        {
            SetGripperTarget(a, a.StartUs);
            SetGripperTarget(b, b.StartUs);
            await WaitForPairMoveAsync(Math.Max(120, dist * 0.25), cancellationToken);
        }

        SetGripperTarget(a, a.StartUs);
        SetGripperTarget(b, b.StartUs);
        await Task.Delay(Math.Max(80, _settings.SettleMs), cancellationToken);
        ConfigureGripper(a);
        ConfigureGripper(b);
        MarkPairHomed(a, b, homed: true);
    }

    async Task WaitUntilArmsNearAsync(ArmCalibration a, ArmCalibration b, bool retracted, CancellationToken cancellationToken)
    {
        var start = Environment.TickCount64;
        var confirmed = false;
        while (Environment.TickCount64 - start < _settings.MovementTimeoutMs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var readyA = ArmNearPose(a, retracted);
            var readyB = ArmNearPose(b, retracted);
            if (readyA == true && readyB == true)
            {
                confirmed = true;
                break;
            }

            if (readyA == false || readyB == false)
            {
                SetArm(a, inside: !retracted, squeeze: !retracted);
                SetArm(b, inside: !retracted, squeeze: !retracted);
            }

            await Task.Delay(150, cancellationToken);
        }

        if (!confirmed)
        {
            await Task.Delay(Math.Max(1000, _settings.SettleMs * 5), cancellationToken);
        }
    }

    bool? ArmNearPose(ArmCalibration arm, bool retracted)
    {
        var pos = _maestro.GetPositionMicroseconds(arm.Port);
        if (pos is null)
        {
            return null;
        }

        var target = retracted ? arm.OutUs : arm.InUs;
        var other = retracted ? arm.InUs : arm.OutUs;
        var travel = Math.Max(1, Math.Abs(arm.OutUs - arm.InUs));
        var distTarget = Math.Abs(pos.Value - target);
        var distOther = Math.Abs(pos.Value - other);
        return distTarget <= Math.Max(80, travel * 0.15) || distTarget + 40 < distOther;
    }

    bool? PairNearStart(GripperCalibration a, GripperCalibration b)
    {
        var posA = _maestro.GetPositionMicroseconds(a.Port);
        var posB = _maestro.GetPositionMicroseconds(b.Port);
        if (posA is null || posB is null)
        {
            return null;
        }

        const double toleranceUs = 50;
        return Math.Abs(posA.Value - a.StartUs) <= toleranceUs
               && Math.Abs(posB.Value - b.StartUs) <= toleranceUs;
    }

    void MarkPairHomed(GripperCalibration a, GripperCalibration b, bool homed)
    {
        if (ReferenceEquals(a, _settings.TopTurner) || ReferenceEquals(b, _settings.TopTurner)
            || ReferenceEquals(a, _settings.BottomTurner) || ReferenceEquals(b, _settings.BottomTurner))
        {
            YawTurnersHomed = homed;
        }

        if (ReferenceEquals(a, _settings.LeftTurner) || ReferenceEquals(b, _settings.LeftTurner)
            || ReferenceEquals(a, _settings.RightTurner) || ReferenceEquals(b, _settings.RightTurner))
        {
            PitchTurnersHomed = homed;
        }
    }

    (double TargetA, double TargetB) PairTumbleTargets(GripperCalibration a, GripperCalibration b, bool invertDirection, int extraUs = 0)
    {
        var mag = Math.Min(Math.Abs(a.EndUs - a.StartUs), Math.Abs(b.EndUs - b.StartUs));
        mag += extraUs - Math.Max(0, _settings.TumbleTrimUs);
        mag = Math.Min(mag, a.StartUs - ServoMinUs);
        mag = Math.Min(mag, b.StartUs - ServoMinUs);
        mag = Math.Min(mag, ServoMaxUs - a.StartUs);
        mag = Math.Min(mag, ServoMaxUs - b.StartUs);
        mag = Math.Max(mag, 1);

        var signA = Math.Sign(a.EndUs - a.StartUs);
        var signB = Math.Sign(b.EndUs - b.StartUs);
        if (signA == 0) signA = 1;
        if (signB == 0) signB = 1;

        var targetA = a.StartUs + (invertDirection ? -signA : signA) * mag;
        var targetB = b.StartUs + (invertDirection ? signB : -signB) * mag;
        return (Math.Clamp(targetA, ServoMinUs, ServoMaxUs), Math.Clamp(targetB, ServoMinUs, ServoMaxUs));
    }

    void MatchPairSpeeds(GripperCalibration a, GripperCalibration b, double fromA, double fromB, double toA, double toB)
    {
        var distA = Math.Max(1, Math.Abs(toA - fromA));
        var distB = Math.Max(1, Math.Abs(toB - fromB));
        var shorter = Math.Min(distA, distB);
        const double baseSpeed = 50;
        var speedA = (ushort)Math.Clamp(Math.Round(baseSpeed * distA / shorter), 1, 255);
        var speedB = (ushort)Math.Clamp(Math.Round(baseSpeed * distB / shorter), 1, 255);
        _maestro.SetAcceleration(a.Port, 0);
        _maestro.SetAcceleration(b.Port, 0);
        _maestro.SetSpeed(a.Port, speedA);
        _maestro.SetSpeed(b.Port, speedB);
    }

    async Task WaitForPairMoveAsync(double distanceUs, CancellationToken cancellationToken)
    {
        var minMs = (int)Math.Clamp(distanceUs / 1.2 + 200, 400, _settings.MovementTimeoutMs);
        var start = Environment.TickCount64;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var elapsed = Environment.TickCount64 - start;
            if (elapsed > _settings.MovementTimeoutMs)
            {
                break;
            }

            if (!_maestro.GetMovingState() && elapsed >= minMs)
            {
                break;
            }

            await Task.Delay(20, cancellationToken);
        }

        if (_settings.SettleMs > 0)
        {
            await Task.Delay(_settings.SettleMs, cancellationToken);
        }
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
