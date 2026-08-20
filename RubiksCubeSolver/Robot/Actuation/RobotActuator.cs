using RubiksCubeSolver.Hardware;
using RubiksCubeSolver.Models;

namespace RubiksCubeSolver.Robot.Actuation;

public sealed class RobotActuator : IRobotActuator
{
    const double ServoMinUs = 256;
    const double ServoMaxUs = 2496;

    readonly MaestroController _maestro;

    public RobotActuator(MaestroController maestro, AppSettings settings)
    {
        _maestro = maestro;
        Settings = settings;
        Orientation = CubeOrientation.Home();
    }

    public AppSettings Settings { get; }

    public Action<string>? OnCommand { get; set; }

    public CubeOrientation Orientation { get; private set; }

    public bool YawTurnersHomed { get; set; } = true;

    public bool PitchTurnersHomed { get; set; } = true;

    public CubeFace CurrentCameraFace => Orientation.Front;

    public void ResetOrientation()
    {
        Orientation = CubeOrientation.Home();
        YawTurnersHomed = true;
        PitchTurnersHomed = true;
    }

    public void ConfigureChannels()
    {
        ConfigureGripper(Settings.RightTurner);
        ConfigureGripper(Settings.TopTurner);
        ConfigureGripper(Settings.LeftTurner);
        ConfigureGripper(Settings.BottomTurner);
        ConfigureArm(Settings.RightArm);
        ConfigureArm(Settings.TopArm);
        ConfigureArm(Settings.LeftArm);
        ConfigureArm(Settings.BottomArm);
    }

    public void AllServosOff()
    {
        foreach (byte channel in new byte[]
                 {
                     Settings.RightTurner.Port, Settings.RightArm.Port,
                     Settings.TopTurner.Port, Settings.TopArm.Port,
                     Settings.LeftTurner.Port, Settings.LeftArm.Port,
                     Settings.BottomTurner.Port, Settings.BottomArm.Port
                 })
        {
            _maestro.SetServoOff(channel);
        }
    }

    public async Task CommandAsync(string name, Func<CancellationToken, Task> command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OnCommand?.Invoke(name);
        await command(cancellationToken);
    }

    public Task TopBottomInAsync(CancellationToken cancellationToken, bool squeeze = false, int? squeezeExtraUs = null) =>
        CommandAsync("Top/Bottom in", async ct =>
        {
            SetArm(Settings.TopArm, inside: true, squeeze: squeeze, squeezeExtraUs: squeezeExtraUs);
            SetArm(Settings.BottomArm, inside: true, squeeze: squeeze, squeezeExtraUs: squeezeExtraUs);
            await WaitAsync(ct);
            await Task.Delay(Math.Max(200, Settings.SettleMs), ct);
        }, cancellationToken);

    public Task TopBottomOutAsync(CancellationToken cancellationToken) =>
        CommandAsync("Top/Bottom out", async ct =>
        {
            SetArm(Settings.TopArm, inside: false);
            SetArm(Settings.BottomArm, inside: false);
            await WaitAsync(ct);
        }, cancellationToken);

    public Task LeftRightInAsync(CancellationToken cancellationToken, bool squeeze = true, int? squeezeExtraUs = null) =>
        CommandAsync("Left/Right in", async ct =>
        {
            SetArm(Settings.LeftArm, inside: true, squeeze: squeeze, squeezeExtraUs: squeezeExtraUs);
            SetArm(Settings.RightArm, inside: true, squeeze: squeeze, squeezeExtraUs: squeezeExtraUs);
            await WaitAsync(ct);
            await Task.Delay(Math.Max(200, Settings.SettleMs), ct);
        }, cancellationToken);

    public Task LeftRightOutAsync(CancellationToken cancellationToken, bool clearOfCube = false) =>
        CommandAsync(clearOfCube ? "Left/Right out and clear of cube" : "Left/Right out", async ct =>
        {
            SetArm(Settings.LeftArm, inside: false);
            SetArm(Settings.RightArm, inside: false);
            await WaitAsync(ct);
            if (clearOfCube)
            {
                await WaitUntilArmsNearAsync(Settings.LeftArm, Settings.RightArm, retracted: true, ct);
                await Task.Delay(Math.Max(800, Settings.SettleMs * 4), ct);
            }
        }, cancellationToken);

    public Task HoldPitchTurnersStillAsync(CancellationToken cancellationToken) =>
        CommandAsync("Hold pitch turners still", ct => FreezePairAtCurrentAsync(Settings.LeftTurner, Settings.RightTurner, ct), cancellationToken);

    public Task HoldYawTurnersStillAsync(CancellationToken cancellationToken) =>
        CommandAsync("Hold yaw turners still", ct => FreezePairAtCurrentAsync(Settings.TopTurner, Settings.BottomTurner, ct), cancellationToken);

    public Task PitchTurnersToStartAsync(CancellationToken cancellationToken) =>
        CommandAsync("Pitch turners to Start", async ct =>
        {
            if (ArmNearPose(Settings.LeftArm, retracted: true) == false
                || ArmNearPose(Settings.RightArm, retracted: true) == false)
            {
                OnCommand?.Invoke("Left/Right still on cube — not sending Start");
                SetArm(Settings.LeftArm, inside: false);
                SetArm(Settings.RightArm, inside: false);
                await WaitAsync(ct);
                await WaitUntilArmsNearAsync(Settings.LeftArm, Settings.RightArm, retracted: true, ct);
                await Task.Delay(Math.Max(800, Settings.SettleMs * 4), ct);
            }

            await ReversePairToStartAsync(Settings.LeftTurner, Settings.RightTurner, ct);
            await WaitUntilPairNearStartAsync(Settings.LeftTurner, Settings.RightTurner, ct);
        }, cancellationToken);

    public Task YawTurnersToStartAsync(CancellationToken cancellationToken) =>
        CommandAsync("Yaw turners to Start", ct => ReversePairToStartAsync(Settings.TopTurner, Settings.BottomTurner, ct), cancellationToken);

    public Task PitchSpin90Async(CancellationToken cancellationToken, bool opposite = false) =>
        CommandAsync(opposite ? "Pitch 90° the other way" : "Pitch 90°", async ct =>
        {
            var invert = Settings.InvertPitch ^ opposite;
            if (PairNearStart(Settings.LeftTurner, Settings.RightTurner) != true)
            {
                OnCommand?.Invoke("Pitch turners not at Start — will home before 90°");
            }

            var (targetLeft, targetRight) = PairPitchTumbleTargets(Settings.LeftTurner, Settings.RightTurner, invert, Settings.PitchExtraUs);
            var travelLeft = Math.Abs(targetLeft - Settings.LeftTurner.StartUs);
            var travelRight = Math.Abs(targetRight - Settings.RightTurner.StartUs);
            OnCommand?.Invoke(
                $"Pitch targets Ch{Settings.LeftTurner.Port} {Settings.LeftTurner.StartUs:F0}→{targetLeft:F0} ({travelLeft:F0} µs), " +
                $"Ch{Settings.RightTurner.Port} {Settings.RightTurner.StartUs:F0}→{targetRight:F0} ({travelRight:F0} µs), " +
                $"opposite ends L {Settings.LeftTurner.EffectiveOppositeUs():F0} R {Settings.RightTurner.EffectiveOppositeUs():F0}");

            if (Math.Abs(travelLeft - travelRight) > 25)
            {
                OnCommand?.Invoke("Pitch 90° blocked — left/right travel mismatch");
                return;
            }

            if (SpinWouldBeTooFar(Settings.LeftTurner, Settings.RightTurner, targetLeft, targetRight))
            {
                OnCommand?.Invoke("Pitch 90° blocked (would be ~180°) — turners are not at Start");
                return;
            }

            await SpinPairAsync(Settings.LeftTurner, Settings.RightTurner, invert, ct, Settings.PitchExtraUs, pitchMatchedOpposite: true);
            Orientation.Pitch(invert);
        }, cancellationToken);

    public Task YawSpin90Async(CancellationToken cancellationToken, bool opposite = false) =>
        CommandAsync(opposite ? "Yaw 90° other way" : "Yaw 90°", async ct =>
        {
            var invert = Settings.InvertYaw ^ opposite;
            if (PairNearStart(Settings.TopTurner, Settings.BottomTurner) != true)
            {
                OnCommand?.Invoke("Yaw 90° blocked — top/bottom turners are not at Start");
                return;
            }

            await SpinPairAsync(Settings.TopTurner, Settings.BottomTurner, invert, ct, yawMatchedOpposite: true);
            Orientation.Yaw(invert);
        }, cancellationToken);

    public async Task RetractAllThenHomeTurnersAsync(CancellationToken cancellationToken)
    {
        AllArmsOut();
        await WaitAsync(cancellationToken);
        NeutralGrippers();
        await WaitAsync(cancellationToken);
        YawTurnersHomed = true;
        PitchTurnersHomed = true;
    }

    public async Task SpinPairAsync(
        GripperCalibration a,
        GripperCalibration b,
        bool invertDirection,
        CancellationToken cancellationToken,
        int extraUs = 0,
        bool mirrorPair = true,
        bool pitchMatchedOpposite = false,
        bool yawMatchedOpposite = false,
        bool requireStartBeforeSpin = true)
    {
        if ((pitchMatchedOpposite || yawMatchedOpposite) && requireStartBeforeSpin)
        {
            if (PairNearStart(a, b) != true)
            {
                OnCommand?.Invoke($"Ch{a.Port}/Ch{b.Port} not at Start — homing before dual tumble");
                await ReversePairToStartAsync(a, b, cancellationToken);
                if (PairNearStart(a, b) != true)
                {
                    OnCommand?.Invoke("Dual tumble blocked — turners could not reach Start");
                    return;
                }
            }
        }

        var fromA = a.StartUs;
        var fromB = b.StartUs;
        var (targetA, targetB) = pitchMatchedOpposite
            ? PairPitchTumbleTargets(a, b, invertDirection, extraUs)
            : yawMatchedOpposite
                ? PairDualTumbleTargets(a, b, invertDirection, extraUs)
                : PairTumbleTargets(a, b, invertDirection, extraUs, mirrorPair);

        var travelA = Math.Abs(targetA - fromA);
        var travelB = Math.Abs(targetB - fromB);

        if (pitchMatchedOpposite || yawMatchedOpposite)
        {
            OnCommand?.Invoke(
                $"Ch{a.Port} {fromA:F0}→{targetA:F0} ({travelA:F0} µs), Ch{b.Port} {fromB:F0}→{targetB:F0} ({travelB:F0} µs)");
            if (Math.Abs(travelA - travelB) > 25)
            {
                OnCommand?.Invoke($"Pair travel mismatch — blocking spin (Ch{a.Port} {travelA:F0} vs Ch{b.Port} {travelB:F0})");
                return;
            }
        }

        MatchPairSpeeds(a, b, fromA, fromB, targetA, targetB);
        SetGripperTarget(a, targetA);
        SetGripperTarget(b, targetB);
        await WaitForPairMoveAsync(Math.Max(travelA, travelB), cancellationToken);
        ConfigureGripper(a);
        ConfigureGripper(b);
        SetGripperTarget(a, targetA);
        SetGripperTarget(b, targetB);
        MarkPairHomed(a, b, homed: false);
    }

    public async Task SpinPairYawFromCurrentAsync(
        GripperCalibration a, GripperCalibration b, bool invertDirection, CancellationToken cancellationToken)
    {
        var fromA = _maestro.GetPositionMicroseconds(a.Port) ?? a.StartUs;
        var fromB = _maestro.GetPositionMicroseconds(b.Port) ?? b.StartUs;
        var (targetA, targetB) = PairNearStart(a, b) == true
            ? PairDualTumbleTargets(a, b, invertDirection, 0)
            : (a.StartUs, b.StartUs);
        var travelA = Math.Abs(targetA - fromA);
        var travelB = Math.Abs(targetB - fromB);

        OnCommand?.Invoke(
            $"Ch{a.Port} {fromA:F0}→{targetA:F0} ({travelA:F0} µs), Ch{b.Port} {fromB:F0}→{targetB:F0} ({travelB:F0} µs) [chained]");
        if (Math.Abs(travelA - travelB) > 25)
        {
            OnCommand?.Invoke($"Chained yaw blocked — travel mismatch (Ch{a.Port} {travelA:F0} vs Ch{b.Port} {travelB:F0})");
            return;
        }

        if (!PairTargetsInRange(a, b, targetA, targetB))
        {
            OnCommand?.Invoke($"Chained yaw blocked — target out of servo range (Ch{a.Port} {targetA:F0}, Ch{b.Port} {targetB:F0})");
            return;
        }

        MatchPairSpeeds(a, b, fromA, fromB, targetA, targetB);
        SetGripperTarget(a, targetA);
        SetGripperTarget(b, targetB);
        await WaitForPairMoveAsync(Math.Max(travelA, travelB), cancellationToken);
        ConfigureGripper(a);
        ConfigureGripper(b);
        SetGripperTarget(a, targetA);
        SetGripperTarget(b, targetB);
        MarkPairHomed(a, b, homed: PairNearStart(a, b) == true);
    }

    public async Task ReversePairToStartAsync(GripperCalibration a, GripperCalibration b, CancellationToken cancellationToken)
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
        await Task.Delay(Math.Max(80, Settings.SettleMs), cancellationToken);
        ConfigureGripper(a);
        ConfigureGripper(b);
        MarkPairHomed(a, b, homed: true);
    }

    public async Task WaitUntilPairNearStartAsync(GripperCalibration a, GripperCalibration b, CancellationToken cancellationToken)
    {
        var start = Environment.TickCount64;
        while (Environment.TickCount64 - start < Settings.MovementTimeoutMs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (PairNearStart(a, b) == true)
            {
                return;
            }

            SetGripperTarget(a, a.StartUs);
            SetGripperTarget(b, b.StartUs);
            await Task.Delay(150, cancellationToken);
        }
    }

    public async Task WaitUntilArmsNearAsync(ArmCalibration a, ArmCalibration b, bool retracted, CancellationToken cancellationToken)
    {
        var start = Environment.TickCount64;
        var confirmed = false;
        while (Environment.TickCount64 - start < Settings.MovementTimeoutMs)
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
            await Task.Delay(Math.Max(1000, Settings.SettleMs * 5), cancellationToken);
        }
    }

    public bool? PairNearStart(GripperCalibration a, GripperCalibration b)
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

    public (double TargetA, double TargetB) PairDualTumbleTargets(
        GripperCalibration gripperA, GripperCalibration gripperB, bool invertDirection, int extraUs = 0)
    {
        GripperCalibration turnGripper;
        GripperCalibration oppositeGripper;
        if (!invertDirection)
        {
            turnGripper = gripperA;
            oppositeGripper = gripperB;
        }
        else
        {
            turnGripper = gripperB;
            oppositeGripper = gripperA;
        }

        var turnSign = Math.Sign(turnGripper.EndUs - turnGripper.StartUs);
        if (turnSign == 0) turnSign = 1;
        var oppositeSign = -turnSign;

        var (targetTurn, targetOpposite) = DualTumbleFromStart(turnGripper, oppositeGripper, turnSign, oppositeSign, extraUs);
        return !invertDirection ? (targetTurn, targetOpposite) : (targetOpposite, targetTurn);
    }

    public double? GetPositionMicroseconds(byte port) => _maestro.GetPositionMicroseconds(port);

    public void SetArm(ArmCalibration arm, bool inside, bool squeeze = false, int? squeezeExtraUs = null)
    {
        double microseconds;
        if (!inside)
        {
            microseconds = arm.OutUs;
        }
        else if (squeeze)
        {
            var extra = Math.Max(0, squeezeExtraUs ?? Settings.TumbleSqueezeUs);
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

    public void SetArmMicroseconds(ArmCalibration arm, double microseconds) =>
        _maestro.SetTargetMicroseconds(arm.Port, microseconds);

    public void NeutralGrippers()
    {
        NeutralGripper(Settings.RightTurner);
        NeutralGripper(Settings.TopTurner);
        NeutralGripper(Settings.LeftTurner);
        NeutralGripper(Settings.BottomTurner);
    }

    public void NeutralGripper(GripperCalibration gripper) => SetGripper(gripper, turned: false);

    public void SetGripper(GripperCalibration gripper, bool turned)
    {
        SetGripperTarget(gripper, turned ? gripper.EndUs : gripper.StartUs);
    }

    public void SetGripperQuarterTurn(GripperCalibration gripper, bool prime)
    {
        var target = gripper.QuarterTurnTargetUs(prime);
        if (target < 256)
        {
            OnCommand?.Invoke($"Ch{gripper.Port} refused {target:F0} µs (0 = servo off)");
            target = 256;
        }

        OnCommand?.Invoke(
            $"Ch{gripper.Port} {gripper.StartUs:F0}→{target:F0} ({Math.Abs(target - gripper.StartUs):F0} µs)");
        SetGripperTarget(gripper, target);
    }

    public void SetGripperMicroseconds(GripperCalibration gripper, double microseconds) =>
        SetGripperTarget(gripper, microseconds);

    public void AllArmsOut()
    {
        SetArm(Settings.RightArm, false);
        SetArm(Settings.TopArm, false);
        SetArm(Settings.LeftArm, false);
        SetArm(Settings.BottomArm, false);
    }

    public Task WaitAsync(CancellationToken cancellationToken) =>
        _maestro.WaitUntilIdleAsync(Settings.MovementTimeoutMs, Settings.SettleMs, cancellationToken);

    async Task FreezePairAtCurrentAsync(GripperCalibration a, GripperCalibration b, CancellationToken cancellationToken)
    {
        ConfigureGripper(a);
        ConfigureGripper(b);
        var posA = _maestro.GetPositionMicroseconds(a.Port);
        var posB = _maestro.GetPositionMicroseconds(b.Port);
        if (posA is not null)
        {
            SetGripperTarget(a, posA.Value);
        }

        if (posB is not null)
        {
            SetGripperTarget(b, posB.Value);
        }

        await Task.Delay(Math.Max(80, Settings.SettleMs), cancellationToken);
    }

    bool SpinWouldBeTooFar(GripperCalibration a, GripperCalibration b, double targetA, double targetB)
    {
        var mag = ComputeTumbleMagnitude(a, b, 0);
        var tooFarA = Math.Abs(targetA - a.StartUs) > mag * 1.25;
        var tooFarB = Math.Abs(targetB - b.StartUs) > mag * 1.25;
        return tooFarA || tooFarB;
    }

    bool? ArmNearPose(ArmCalibration arm, bool retracted)
    {
        var pos = _maestro.GetPositionMicroseconds(arm.Port);
        if (pos is null)
        {
            return null;
        }

        var target = retracted ? arm.OutUs : arm.InUs;
        var travel = Math.Max(1, Math.Abs(arm.OutUs - arm.InUs));
        var distTarget = Math.Abs(pos.Value - target);
        return distTarget <= Math.Max(80, travel * 0.12);
    }

    static bool PairTargetsInRange(GripperCalibration a, GripperCalibration b, double targetA, double targetB) =>
        targetA >= ServoMinUs && targetA <= ServoMaxUs
        && targetB >= ServoMinUs && targetB <= ServoMaxUs;

    void MarkPairHomed(GripperCalibration a, GripperCalibration b, bool homed)
    {
        if (ReferenceEquals(a, Settings.TopTurner) || ReferenceEquals(b, Settings.TopTurner)
            || ReferenceEquals(a, Settings.BottomTurner) || ReferenceEquals(b, Settings.BottomTurner))
        {
            YawTurnersHomed = homed;
        }

        if (ReferenceEquals(a, Settings.LeftTurner) || ReferenceEquals(b, Settings.LeftTurner)
            || ReferenceEquals(a, Settings.RightTurner) || ReferenceEquals(b, Settings.RightTurner))
        {
            PitchTurnersHomed = homed;
        }
    }

    (double TargetLeft, double TargetRight) PairPitchTumbleTargets(
        GripperCalibration left, GripperCalibration right, bool invertDirection, int extraUs = 0)
    {
        // Left sets the tumble direction; Right always moves the opposite pulse way by the same µs.
        var leftDir = Math.Sign(left.EndUs - left.StartUs);
        if (leftDir == 0) leftDir = 1;
        if (invertDirection) leftDir = -leftDir;
        var rightDir = -leftDir;

        return DualTumbleFromStart(left, right, leftDir, rightDir, extraUs);
    }

    (double TargetA, double TargetB) DualTumbleFromStart(
        GripperCalibration a, GripperCalibration b, int dirA, int dirB, int extraUs)
    {
        var endA = dirA > 0 ? a.EndUs : a.EffectiveOppositeUs();
        var endB = dirB > 0 ? b.EndUs : b.EffectiveOppositeUs();

        var mag = Math.Min(Math.Abs(endA - a.StartUs), Math.Abs(endB - b.StartUs));
        mag += extraUs - Math.Max(0, Settings.TumbleTrimUs);
        mag = Math.Max(mag, 1);

        var targetA = a.StartUs + dirA * mag;
        var targetB = b.StartUs + dirB * mag;

        if (dirA > 0)
            targetA = Math.Min(targetA, a.EndUs);
        else if (dirA < 0)
            targetA = Math.Max(targetA, a.EffectiveOppositeUs());

        if (dirB > 0)
            targetB = Math.Min(targetB, b.EndUs);
        else if (dirB < 0)
            targetB = Math.Max(targetB, b.EffectiveOppositeUs());

        mag = Math.Min(Math.Abs(targetA - a.StartUs), Math.Abs(targetB - b.StartUs));
        mag = Math.Max(mag, 1);
        return (a.StartUs + dirA * mag, b.StartUs + dirB * mag);
    }

    (double TargetA, double TargetB) PairTumbleTargets(GripperCalibration a, GripperCalibration b, bool invertDirection, int extraUs = 0, bool mirrorPair = true)
    {
        if (mirrorPair)
        {
            return PairDualTumbleTargets(a, b, invertDirection, extraUs);
        }

        var mag = ComputeTumbleMagnitude(a, b, extraUs);
        var signA = Math.Sign(a.EndUs - a.StartUs);
        if (signA == 0) signA = 1;
        var dirA = invertDirection ? -signA : signA;
        var dirB = dirA;
        return EqualOppositePairTargets(a, b, dirA, dirB, mag);
    }

    (double TargetA, double TargetB) EqualOppositePairTargets(
        GripperCalibration gripperA, GripperCalibration gripperB, int dirA, int dirB, double mag)
    {
        mag = Math.Min(mag, MaxTravelToward(gripperA, dirA));
        mag = Math.Min(mag, MaxTravelToward(gripperB, dirB));
        mag = Math.Max(mag, 1);

        var targetA = gripperA.StartUs + dirA * mag;
        var targetB = gripperB.StartUs + dirB * mag;

        if (dirA > 0)
            targetA = Math.Min(targetA, gripperA.EndUs);
        else if (dirA < 0)
            targetA = Math.Max(targetA, gripperA.EffectiveOppositeUs());

        if (dirB > 0)
            targetB = Math.Min(targetB, gripperB.EndUs);
        else if (dirB < 0)
            targetB = Math.Max(targetB, gripperB.EffectiveOppositeUs());

        var actualMag = Math.Min(Math.Abs(targetA - gripperA.StartUs), Math.Abs(targetB - gripperB.StartUs));
        actualMag = Math.Max(actualMag, 1);
        targetA = gripperA.StartUs + dirA * actualMag;
        targetB = gripperB.StartUs + dirB * actualMag;
        return (targetA, targetB);
    }

    static double MaxTravelToward(GripperCalibration gripper, int direction)
    {
        if (direction > 0)
            return Math.Max(0, gripper.EndUs - gripper.StartUs);
        if (direction < 0)
            return Math.Max(0, gripper.StartUs - gripper.EffectiveOppositeUs());
        return 0;
    }

    double ComputeTumbleMagnitude(GripperCalibration a, GripperCalibration b, int extraUs)
    {
        // Cap by Turn travel and Opposite travel so the downward gripper cannot run to servo min (256).
        var turnA = Math.Abs(a.EndUs - a.StartUs);
        var turnB = Math.Abs(b.EndUs - b.StartUs);
        var oppositeA = Math.Abs(a.EffectiveOppositeUs() - a.StartUs);
        var oppositeB = Math.Abs(b.EffectiveOppositeUs() - b.StartUs);
        var mag = Math.Min(Math.Min(turnA, turnB), Math.Min(oppositeA, oppositeB));
        mag += extraUs - Math.Max(0, Settings.TumbleTrimUs);
        return Math.Max(mag, 1);
    }

    void MatchPairSpeeds(GripperCalibration a, GripperCalibration b, double fromA, double fromB, double toA, double toB)
    {
        var distA = Math.Max(1, Math.Abs(toA - fromA));
        var distB = Math.Max(1, Math.Abs(toB - fromB));
        var shorter = Math.Min(distA, distB);
        var baseSpeed = (ushort)Math.Min(a.Speed, b.Speed);
        if (baseSpeed < 1)
        {
            baseSpeed = 28;
        }

        if (Settings.TurnSpeedCap > 0)
        {
            baseSpeed = (ushort)Math.Min(baseSpeed, Settings.TurnSpeedCap);
        }

        var speedA = (ushort)Math.Clamp(Math.Round(baseSpeed * distA / shorter), 1, 255);
        var speedB = (ushort)Math.Clamp(Math.Round(baseSpeed * distB / shorter), 1, 255);
        var accelCap = Settings.TurnAccelerationCap > 0 ? Settings.TurnAccelerationCap : 50;
        var accel = (ushort)Math.Clamp(Math.Min(a.Acceleration, b.Acceleration), 20, accelCap);
        _maestro.SetAcceleration(a.Port, accel);
        _maestro.SetAcceleration(b.Port, accel);
        _maestro.SetSpeed(a.Port, speedA);
        _maestro.SetSpeed(b.Port, speedB);
    }

    async Task WaitForPairMoveAsync(double distanceUs, CancellationToken cancellationToken)
    {
        var minMs = (int)Math.Clamp(distanceUs / 1.2 + 200, 400, Settings.MovementTimeoutMs);
        var start = Environment.TickCount64;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var elapsed = Environment.TickCount64 - start;
            if (elapsed > Settings.MovementTimeoutMs)
            {
                break;
            }

            if (!_maestro.GetMovingState() && elapsed >= minMs)
            {
                break;
            }

            await Task.Delay(20, cancellationToken);
        }

        if (Settings.SettleMs > 0)
        {
            await Task.Delay(Settings.SettleMs, cancellationToken);
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

    void SetGripperTarget(GripperCalibration gripper, double microseconds)
    {
        _maestro.SetTargetMicroseconds(gripper.Port, microseconds);
    }
}
