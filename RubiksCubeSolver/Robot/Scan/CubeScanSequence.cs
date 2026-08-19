using RubiksCubeSolver.Models;

namespace RubiksCubeSolver.Robot.Scan;

public static class CubeScanSequence
{
    public static IReadOnlyList<IScanStep> Default { get; } =
    [
        new DualHoldPhotoStep(CubeFace.F, "FRONT"),
        new YawTurnDualHoldHomeStep(CubeFace.R, "RIGHT"),
        new YawTurnDualHoldHomeStep(CubeFace.B, "BACK"),
        new YawTurnDualHoldHomeStep(CubeFace.L, "LEFT"),
        new ReturnToFrontForPitchStep(),
        new PitchPhotoReturnStep(toTop: true, "first pitch"),
        new PitchPhotoReturnStep(toTop: false, "other way"),
        new FinishHugStep()
    ];

    public static async Task RunAsync(IScanSession session, IReadOnlyList<IScanStep> steps, CancellationToken cancellationToken)
    {
        foreach (var step in steps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await step.ExecuteAsync(session, cancellationToken);
        }
    }
}

public sealed class DualHoldPhotoStep : IScanStep
{
    readonly CubeFace _face;
    readonly string _label;

    public DualHoldPhotoStep(CubeFace face, string label)
    {
        _face = face;
        _label = label;
    }

    public string Name => $"{_label} dual hold photo";

    public async Task ExecuteAsync(IScanSession session, CancellationToken cancellationToken)
    {
        session.Log($"{_label}: dual hold photo (TB hold + RL hold, merge)");
        await session.CaptureDualHoldAsync(_face, _label, cancellationToken);
        session.Log($"STATE after {_label} photo: CURRENT_FACE={session.CurrentCameraFace}");
    }
}

public sealed class YawTurnDualHoldHomeStep : IScanStep
{
    readonly CubeFace _face;
    readonly string _label;

    public YawTurnDualHoldHomeStep(CubeFace face, string label)
    {
        _face = face;
        _label = label;
    }

    public string Name => $"{_label} turn, dual hold, yaw home";

    public async Task ExecuteAsync(IScanSession session, CancellationToken cancellationToken)
    {
        session.Log($"{_label}: TURN_R_90");
        await session.ScanTurnRight90Async(cancellationToken);
        session.Log($"{_label}: dual hold photo (TB hold + RL hold, merge)");
        await session.CaptureDualHoldAsync(_face, _label, cancellationToken);
        session.Log($"{_label}: retract TB, unwind yaw turners, re-grip");
        await session.ScanYawTurnersHomeKeepFaceAsync(cancellationToken);
        session.Log($"STATE after {_label} photo: CURRENT_FACE={session.CurrentCameraFace}");
    }
}

public sealed class ReturnToFrontForPitchStep : IScanStep
{
    public string Name => "Return to FRONT for pitch";

    public async Task ExecuteAsync(IScanSession session, CancellationToken cancellationToken)
    {
        session.Log("RETURN: TURN_R_90 to FRONT (no photo — pitch phase)");
        await session.ScanTurnRight90Async(cancellationToken);
        session.Log("RETURN: retract TB, unwind yaw turners at FRONT");
        await session.ScanYawTurnersHomeAtFrontAsync(cancellationToken);
        session.Log($"STATE after back at FRONT for pitch: CURRENT_FACE={session.CurrentCameraFace}");
    }
}

public sealed class PitchPhotoReturnStep : IScanStep
{
    readonly bool _toTop;
    readonly string _label;

    public PitchPhotoReturnStep(bool toTop, string label)
    {
        _toTop = toTop;
        _label = label;
    }

    public string Name => _toTop ? "Pitch TOP photo and return" : "Pitch BOTTOM photo and return";

    public async Task ExecuteAsync(IScanSession session, CancellationToken cancellationToken)
    {
        if (_toTop)
        {
            session.Log("U/D: pitch 90°, photo, unwind back to FRONT (keep RL hold)");
            await session.ScanPitchToTopAsync(cancellationToken);
        }
        else
        {
            session.Log("U/D: pitch 90° the other way, photo, unwind back to FRONT");
            await session.ScanPitchToBottomAsync(cancellationToken);
        }

        await session.CapturePitchedFaceAsync(_toTop ? CubeFace.U : CubeFace.D, _label, cancellationToken);
        await session.ScanPitchReturnToFrontAsync(cancellationToken);
        var after = _toTop ? "back at FRONT after first pitch" : "back at FRONT after second pitch";
        session.Log($"STATE after {after}: CURRENT_FACE={session.CurrentCameraFace}");
    }
}

public sealed class FinishHugStep : IScanStep
{
    public string Name => "Finish hug at FRONT";

    public async Task ExecuteAsync(IScanSession session, CancellationToken cancellationToken)
    {
        session.Log("FINISH: hug at FRONT, then solve");
        await session.ScanFinishHugAtFrontAsync(cancellationToken);
        session.Log($"STATE after scan complete: CURRENT_FACE={session.CurrentCameraFace}");
    }
}
