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
        await SpinPairAsync(_settings.LeftTurner, _settings.RightTurner, invert, cancellationToken, _settings.PitchExtraUs, pitchMatchedOpposite: true);
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
        await SpinPairAsync(_settings.TopTurner, _settings.BottomTurner, invert, cancellationToken, yawMatchedOpposite: true);
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

    async Task HoldPairAsync(ArmCalibration holdA, ArmCalibration holdB, ArmCalibration clearA, ArmCalibration clearB, CancellationToken cancellationToken, bool squeeze = true, int? squeezeExtraUs = null)
    {
        SetArm(holdA, inside: true, squeeze: squeeze, squeezeExtraUs: squeezeExtraUs);
        SetArm(holdB, inside: true, squeeze: squeeze, squeezeExtraUs: squeezeExtraUs);
        await WaitAsync(cancellationToken);
        SetArm(clearA, inside: false);
        SetArm(clearB, inside: false);
        await WaitAsync(cancellationToken);
    }

    public Task TopBottomInAsync(CancellationToken cancellationToken, bool squeeze = true, int? squeezeExtraUs = null) =>
        CommandAsync("Top/Bottom in", async ct =>
        {
            SetArm(_settings.TopArm, inside: true, squeeze: squeeze, squeezeExtraUs: squeezeExtraUs);
            SetArm(_settings.BottomArm, inside: true, squeeze: squeeze, squeezeExtraUs: squeezeExtraUs);
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

    public Task LeftRightInAsync(CancellationToken cancellationToken, bool squeeze = true, int? squeezeExtraUs = null) =>
        CommandAsync("Left/Right in", async ct =>
        {
            SetArm(_settings.LeftArm, inside: true, squeeze: squeeze, squeezeExtraUs: squeezeExtraUs);
            SetArm(_settings.RightArm, inside: true, squeeze: squeeze, squeezeExtraUs: squeezeExtraUs);
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
                await WaitUntilArmsNearAsync(_settings.LeftArm, _settings.RightArm, retracted: true, ct);
                await Task.Delay(Math.Max(800, _settings.SettleMs * 4), ct);
            }
        }, cancellationToken);

    public Task HoldPitchTurnersStillAsync(CancellationToken cancellationToken) =>
        CommandAsync("Hold pitch turners still", ct => FreezePairAtCurrentAsync(_settings.LeftTurner, _settings.RightTurner, ct), cancellationToken);

    public Task HoldYawTurnersStillAsync(CancellationToken cancellationToken) =>
        CommandAsync("Hold yaw turners still", ct => FreezePairAtCurrentAsync(_settings.TopTurner, _settings.BottomTurner, ct), cancellationToken);

    public Task PitchTurnersToStartAsync(CancellationToken cancellationToken) =>
        CommandAsync("Pitch turners to Start", async ct =>
        {
            if (ArmNearPose(_settings.LeftArm, retracted: true) == false
                || ArmNearPose(_settings.RightArm, retracted: true) == false)
            {
                OnCommand?.Invoke("Left/Right still on cube — not sending Start");
                SetArm(_settings.LeftArm, inside: false);
                SetArm(_settings.RightArm, inside: false);
                await WaitAsync(ct);
                await WaitUntilArmsNearAsync(_settings.LeftArm, _settings.RightArm, retracted: true, ct);
                await Task.Delay(Math.Max(800, _settings.SettleMs * 4), ct);
            }

            await ReversePairToStartAsync(_settings.LeftTurner, _settings.RightTurner, ct);
            await WaitUntilPairNearStartAsync(_settings.LeftTurner, _settings.RightTurner, ct);
        }, cancellationToken);

    public Task YawTurnersToStartAsync(CancellationToken cancellationToken) =>
        CommandAsync("Yaw turners to Start", ct => ReversePairToStartAsync(_settings.TopTurner, _settings.BottomTurner, ct), cancellationToken);

    public Task PitchSpin90Async(CancellationToken cancellationToken, bool opposite = false) =>
        CommandAsync(opposite ? "Pitch 90° the other way" : "Pitch 90°", async ct =>
        {
            var invert = _settings.InvertPitch ^ opposite;
            if (PairNearStart(_settings.LeftTurner, _settings.RightTurner) != true)
            {
                OnCommand?.Invoke("Pitch turners not at Start — will home before 90°");
            }

            var (targetLeft, targetRight) = PairPitchTumbleTargets(_settings.LeftTurner, _settings.RightTurner, invert, _settings.PitchExtraUs);
            var travelLeft = Math.Abs(targetLeft - _settings.LeftTurner.StartUs);
            var travelRight = Math.Abs(targetRight - _settings.RightTurner.StartUs);
            OnCommand?.Invoke(
                $"Pitch targets Ch{_settings.LeftTurner.Port} {_settings.LeftTurner.StartUs:F0}→{targetLeft:F0} ({travelLeft:F0} µs), " +
                $"Ch{_settings.RightTurner.Port} {_settings.RightTurner.StartUs:F0}→{targetRight:F0} ({travelRight:F0} µs), " +
                $"opposite ends L {_settings.LeftTurner.EffectiveOppositeUs():F0} R {_settings.RightTurner.EffectiveOppositeUs():F0}");

            if (Math.Abs(travelLeft - travelRight) > 25)
            {
                OnCommand?.Invoke("Pitch 90° blocked — left/right travel mismatch");
                return;
            }

            if (SpinWouldBeTooFar(_settings.LeftTurner, _settings.RightTurner, targetLeft, targetRight))
            {
                OnCommand?.Invoke("Pitch 90° blocked (would be ~180°) — turners are not at Start");
                return;
            }

            await SpinPairAsync(_settings.LeftTurner, _settings.RightTurner, invert, ct, _settings.PitchExtraUs, pitchMatchedOpposite: true);
            Orientation.Pitch(invert);
        }, cancellationToken);

    public Task YawSpin90Async(CancellationToken cancellationToken, bool opposite = false) =>
        CommandAsync(opposite ? "Yaw 90° other way" : "Yaw 90°", async ct =>
        {
            var invert = _settings.InvertYaw ^ opposite;
            if (PairNearStart(_settings.TopTurner, _settings.BottomTurner) != true)
            {
                OnCommand?.Invoke("Yaw 90° blocked — top/bottom turners are not at Start");
                return;
            }

            await SpinPairAsync(_settings.TopTurner, _settings.BottomTurner, invert, ct, yawMatchedOpposite: true);
            Orientation.Yaw(invert);
        }, cancellationToken);

    public async Task SequencePitchResetAsync(CancellationToken cancellationToken, bool resetCubeOrientation = false)
    {
        await HoldPitchTurnersStillAsync(cancellationToken);
        await HoldYawTurnersStillAsync(cancellationToken);
        await LeftRightInAsync(cancellationToken, squeeze: true);
        await TopBottomOutAsync(cancellationToken);
        await WaitUntilArmsNearAsync(_settings.TopArm, _settings.BottomArm, retracted: true, cancellationToken);
        await Task.Delay(Math.Max(800, _settings.SettleMs * 4), cancellationToken);
        await LeftRightOutAsync(cancellationToken, clearOfCube: true);
        await PitchTurnersToStartAsync(cancellationToken);
        await LeftRightInAsync(cancellationToken, squeeze: true);
        await TopBottomOutAsync(cancellationToken);
        if (resetCubeOrientation)
        {
            ResetOrientation();
        }
    }

    public async Task SequencePitch90Async(CancellationToken cancellationToken, bool opposite = false)
    {
        if (!PitchTurnersHomed || PairNearStart(_settings.LeftTurner, _settings.RightTurner) == false)
        {
            await SequencePitchResetAsync(cancellationToken);
        }

        await LeftRightInAsync(cancellationToken, squeeze: true);
        await TopBottomOutAsync(cancellationToken);
        await WaitUntilArmsNearAsync(_settings.TopArm, _settings.BottomArm, retracted: true, cancellationToken);
        await HoldYawTurnersStillAsync(cancellationToken);
        await PitchSpin90Async(cancellationToken, opposite);
    }

    public async Task SequenceYawResetAsync(CancellationToken cancellationToken, bool resetCubeOrientation = false)
    {
        await HoldPitchTurnersStillAsync(cancellationToken);
        await LeftRightInAsync(cancellationToken, squeeze: false);
        await TopBottomOutAsync(cancellationToken);
        await Task.Delay(Math.Max(800, _settings.SettleMs * 4), cancellationToken);
        await YawTurnersToStartAsync(cancellationToken);
        await TopBottomInAsync(cancellationToken, squeeze: false);
        await LeftRightOutAsync(cancellationToken);
        if (resetCubeOrientation)
        {
            ResetOrientation();
        }
    }

    public async Task SequenceYaw90Async(CancellationToken cancellationToken, bool opposite = false)
    {
        await TopBottomInAsync(cancellationToken, squeeze: false);
        await LeftRightOutAsync(cancellationToken);
        await HoldPitchTurnersStillAsync(cancellationToken);
        await YawSpin90Async(cancellationToken, opposite);
    }

    public async Task SequenceHandoffToPitchAsync(CancellationToken cancellationToken)
    {
        await LeftRightInAsync(cancellationToken, squeeze: true);
        await TopBottomOutAsync(cancellationToken);
        await Task.Delay(Math.Max(800, _settings.SettleMs * 4), cancellationToken);
        await HoldPitchTurnersStillAsync(cancellationToken);
        await YawTurnersToStartAsync(cancellationToken);
    }

    public async Task SequenceScanHugAsync(CancellationToken cancellationToken)
    {
        await TopBottomInAsync(cancellationToken, squeeze: false);
        await LeftRightInAsync(cancellationToken, squeeze: false);
    }

    public Task HoldTopBottomScanAsync(CancellationToken cancellationToken) =>
        HoldPairAsync(_settings.TopArm, _settings.BottomArm, _settings.LeftArm, _settings.RightArm, cancellationToken,
            squeeze: true, squeezeExtraUs: _settings.ScanHoldSqueezeUs);

    public Task HoldLeftRightScanAsync(CancellationToken cancellationToken) =>
        HoldPairAsync(_settings.LeftArm, _settings.RightArm, _settings.TopArm, _settings.BottomArm, cancellationToken,
            squeeze: true, squeezeExtraUs: _settings.ScanHoldSqueezeUs);

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

    public Task ScanExposeTopBottomHoldForPhotoAsync(CancellationToken cancellationToken) =>
        CommandAsync("TB_IN (squeeze), RL_OUT", async ct =>
        {
            await TopBottomInAsync(ct, squeeze: true, squeezeExtraUs: _settings.ScanHoldSqueezeUs);
            await LeftRightOutAsync(ct);
            await HoldPitchTurnersStillAsync(ct);
            await Task.Delay(Math.Max(400, _settings.SettleMs * 2), ct);
        }, cancellationToken);

    public Task ScanExposeLeftRightHoldForPhotoAsync(CancellationToken cancellationToken) =>
        CommandAsync("RL_IN (squeeze), TB_OUT", async ct =>
        {
            await LeftRightInAsync(ct, squeeze: true, squeezeExtraUs: _settings.ScanHoldSqueezeUs);
            await TopBottomOutAsync(ct);
            await HoldYawTurnersStillAsync(ct);
            await Task.Delay(Math.Max(400, _settings.SettleMs * 2), ct);
        }, cancellationToken);

    public Task ScanExposeForFacePhotoAsync(CubeFace face, CancellationToken cancellationToken) =>
        ScanExposeTopBottomHoldForPhotoAsync(cancellationToken);

    public Task ScanExposeSideForPhotoAsync(CancellationToken cancellationToken) =>
        ScanExposeTopBottomHoldForPhotoAsync(cancellationToken);

    public Task ScanPrepareForYawTurnAsync(CancellationToken cancellationToken) =>
        CommandAsync("TB_IN (squeeze), RL_OUT — yaw grip", async ct =>
        {
            await TopBottomInAsync(ct, squeeze: true, squeezeExtraUs: _settings.ScanHoldSqueezeUs);
            await LeftRightOutAsync(ct);
            await HoldPitchTurnersStillAsync(ct);
            await Task.Delay(Math.Max(300, _settings.SettleMs * 2), ct);
        }, cancellationToken);

    public Task ScanYawResetAfterPhotoAsync(CancellationToken cancellationToken) =>
        CommandAsync("TB_OUT, yaw turners reset, TB_IN", ct =>
            SequenceYawResetAsync(ct, resetCubeOrientation: true), cancellationToken);

    public Task ScanYawTurnersHomeKeepFaceAsync(CancellationToken cancellationToken) =>
        CommandAsync("RL_IN, TB_OUT, yaw home (keep face)", ct =>
            SequenceYawResetAsync(ct, resetCubeOrientation: false), cancellationToken);

    public Task ScanRetractTbBetweenTurnsAsync(CancellationToken cancellationToken) =>
        CommandAsync("TB_OUT, hold yaw, TB_IN (between turns)", async ct =>
        {
            await TopBottomOutAsync(ct);
            await Task.Delay(Math.Max(500, _settings.SettleMs * 3), ct);
            await HoldYawTurnersStillAsync(ct);
            await TopBottomInAsync(ct, squeeze: true, squeezeExtraUs: _settings.ScanHoldSqueezeUs);
            await LeftRightOutAsync(ct);
            await HoldPitchTurnersStillAsync(ct);
            await Task.Delay(Math.Max(300, _settings.SettleMs * 2), ct);
        }, cancellationToken);

    public Task ScanTurnRight90CountAsync(CancellationToken cancellationToken, int count) =>
        CommandAsync(count == 1 ? "TURN_R_90" : $"TURN_R_90 ×{count}", async ct =>
        {
            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    await ScanRetractTbBetweenTurnsAsync(ct);
                }

                await ScanPrepareForYawTurnAsync(ct);
                var invert = _settings.InvertYaw;
                if (i == 0 && PairNearStart(_settings.TopTurner, _settings.BottomTurner) != true)
                {
                    OnCommand?.Invoke("TURN_R_90 blocked — top/bottom turners are not at Start");
                    return;
                }

                if (i == 0)
                {
                    await SpinPairAsync(_settings.TopTurner, _settings.BottomTurner, invert, ct, yawMatchedOpposite: true);
                }
                else
                {
                    await SpinPairYawFromCurrentAsync(_settings.TopTurner, _settings.BottomTurner, invert, ct);
                }

                Orientation.Yaw(invert);
            }
        }, cancellationToken);

    public Task ScanChainedTurnRight90Async(CancellationToken cancellationToken, bool firstInChain) =>
        ScanTurnRight90CountAsync(cancellationToken, 1);

    public Task ScanHandoffToPitchAsync(CancellationToken cancellationToken) =>
        CommandAsync("Handoff to pitch (RL_IN, TB_OUT, yaw home)", ct => SequenceHandoffToPitchAsync(ct), cancellationToken);

    public Task ScanPitchToTopAsync(CancellationToken cancellationToken) =>
        CommandAsync("Pitch to TOP", ct => SequencePitch90Async(ct, opposite: false), cancellationToken);

    public Task ScanPitchToBottomAsync(CancellationToken cancellationToken) =>
        CommandAsync("Pitch to BOTTOM", ct => SequencePitch90Async(ct, opposite: true), cancellationToken);

    public Task ScanRestoreFrontAfterPitchAsync(CancellationToken cancellationToken) =>
        CommandAsync("Restore FRONT after pitch", ct => SequencePitchResetAsync(ct, resetCubeOrientation: true), cancellationToken);

    public Task ScanFinishAtFrontAsync(CancellationToken cancellationToken) =>
        CommandAsync("Scan finish: FRONT, RL_IN, TB_IN", async ct =>
        {
            if (Orientation.Front != CubeFace.F)
            {
                OnCommand?.Invoke($"Expected FRONT at camera before finish (currently {Orientation.Front})");
            }

            await SequencePitchResetAsync(ct, resetCubeOrientation: true);
            await LeftRightInAsync(ct, squeeze: false);
            await TopBottomInAsync(ct, squeeze: false);
        }, cancellationToken);

    public CubeFace CurrentCameraFace => Orientation.Front;

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

    async Task SpinPairAsync(GripperCalibration a, GripperCalibration b, bool invertDirection, CancellationToken cancellationToken, int extraUs = 0, bool mirrorPair = true, bool pitchMatchedOpposite = false, bool yawMatchedOpposite = false, bool requireStartBeforeSpin = true)
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

    async Task SpinPairYawFromCurrentAsync(
        GripperCalibration a, GripperCalibration b, bool invertDirection, CancellationToken cancellationToken)
    {
        var fromA = _maestro.GetPositionMicroseconds(a.Port) ?? a.StartUs;
        var fromB = _maestro.GetPositionMicroseconds(b.Port) ?? b.StartUs;
        var (startTargetA, startTargetB) = PairDualTumbleTargets(a, b, invertDirection, 0);
        var deltaA = startTargetA - a.StartUs;
        var deltaB = startTargetB - b.StartUs;
        var targetA = fromA + deltaA;
        var targetB = fromB + deltaB;
        var travelA = Math.Abs(deltaA);
        var travelB = Math.Abs(deltaB);

        OnCommand?.Invoke(
            $"Ch{a.Port} {fromA:F0}→{targetA:F0} ({travelA:F0} µs), Ch{b.Port} {fromB:F0}→{targetB:F0} ({travelB:F0} µs) [chained]");
        if (Math.Abs(travelA - travelB) > 25)
        {
            OnCommand?.Invoke($"Chained yaw blocked — travel mismatch (Ch{a.Port} {travelA:F0} vs Ch{b.Port} {travelB:F0})");
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
        MarkPairHomed(a, b, homed: false);
    }

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

        await Task.Delay(Math.Max(80, _settings.SettleMs), cancellationToken);
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

    async Task WaitUntilPairNearStartAsync(GripperCalibration a, GripperCalibration b, CancellationToken cancellationToken)
    {
        var start = Environment.TickCount64;
        while (Environment.TickCount64 - start < _settings.MovementTimeoutMs)
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

    bool SpinWouldBeTooFar(GripperCalibration a, GripperCalibration b, double targetA, double targetB)
    {
        var mag = ComputeTumbleMagnitude(a, b, 0);
        var tooFarA = Math.Abs(targetA - a.StartUs) > mag * 1.25;
        var tooFarB = Math.Abs(targetB - b.StartUs) > mag * 1.25;
        return tooFarA || tooFarB;
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

    (double TargetA, double TargetB) PairDualTumbleTargets(
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

    (double TargetA, double TargetB) DualTumbleFromStart(
        GripperCalibration a, GripperCalibration b, int dirA, int dirB, int extraUs)
    {
        var endA = dirA > 0 ? a.EndUs : a.EffectiveOppositeUs();
        var endB = dirB > 0 ? b.EndUs : b.EffectiveOppositeUs();

        var mag = Math.Min(Math.Abs(endA - a.StartUs), Math.Abs(endB - b.StartUs));
        mag += extraUs - Math.Max(0, _settings.TumbleTrimUs);
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
        mag += extraUs - Math.Max(0, _settings.TumbleTrimUs);
        return Math.Max(mag, 1);
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

    void SetArm(ArmCalibration arm, bool inside, bool squeeze = false, int? squeezeExtraUs = null)
    {
        double microseconds;
        if (!inside)
        {
            microseconds = arm.OutUs;
        }
        else if (squeeze)
        {
            var extra = Math.Max(0, squeezeExtraUs ?? _settings.TumbleSqueezeUs);
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
