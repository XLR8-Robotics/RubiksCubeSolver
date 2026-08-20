using RubiksCubeSolver.Models;

namespace RubiksCubeSolver.Robot.Scan;

public static class CubeScanSequence
{
    public static IReadOnlyList<IScanStep> Default { get; } =
    [
        new TopBottomHoldPhotoStep(CubeFace.F, "FRONT", ScanStickerMask.TopBottomHold),
        new YawTurnTopBottomHoldHomeStep(CubeFace.R, "RIGHT", ScanStickerMask.AllNine),
        new YawTurnBackFillHomeStep(),
        new YawTurnTopBottomHoldHomeStep(CubeFace.L, "LEFT", ScanStickerMask.AllNine),
        new ReturnToFrontFillStep(),
        new PitchPhotoReturnStep(toTop: true, "first pitch"),
        new PitchPhotoReturnStep(toTop: false, "other way"),
        new FinishHugStep()
    ];

    public static async Task RunAsync(
        IScanSession session, IReadOnlyList<IScanStep> steps, CancellationToken cancellationToken)
    {
        foreach (var step in steps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await step.ExecuteAsync(session, cancellationToken);
        }
    }
}

public sealed class TopBottomHoldPhotoStep : IScanStep
{
    readonly CubeFace _face;
    readonly string _label;
    readonly IReadOnlyList<int> _indices;

    public TopBottomHoldPhotoStep(CubeFace face, string label, IReadOnlyList<int> indices)
    {
        _face = face;
        _label = label;
        _indices = indices;
    }

    public string Name => $"{_label} TB hold photo";

    public async Task ExecuteAsync(IScanSession session, CancellationToken cancellationToken)
    {
        session.Log($"{_label}: TB hold photo, skip obstructed stickers");
        await session.ScanExposeTopBottomHoldAsync(cancellationToken);
        await session.CaptureMaskedAsync(_face, _label, _indices, cancellationToken);
        session.Log($"STATE after {_label} photo: CURRENT_FACE={session.CurrentCameraFace}");
    }
}

public sealed class YawTurnTopBottomHoldHomeStep : IScanStep
{
    readonly CubeFace _face;
    readonly string _label;
    readonly IReadOnlyList<int> _indices;

    public YawTurnTopBottomHoldHomeStep(CubeFace face, string label, IReadOnlyList<int> indices)
    {
        _face = face;
        _label = label;
        _indices = indices;
    }

    public string Name => $"{_label} turn, TB hold photo, yaw home";

    public async Task ExecuteAsync(IScanSession session, CancellationToken cancellationToken)
    {
        session.Log($"{_label}: TURN_R_90");
        await session.ScanTurnRight90Async(cancellationToken);
        session.Log($"{_label}: TB hold photo");
        await session.ScanExposeTopBottomHoldAsync(cancellationToken);
        await session.CaptureMaskedAsync(_face, _label, _indices, cancellationToken);
        session.Log($"{_label}: retract TB, unwind yaw turners, re-grip");
        await session.ScanYawTurnersHomeKeepFaceAsync(cancellationToken);
        session.Log($"STATE after {_label} photo: CURRENT_FACE={session.CurrentCameraFace}");
    }
}

public sealed class YawTurnBackFillHomeStep : IScanStep
{
    public string Name => "BACK turn, TB hold, RL fill 2 and 8, yaw home";

    public async Task ExecuteAsync(IScanSession session, CancellationToken cancellationToken)
    {
        session.Log("BACK: TURN_R_90");
        await session.ScanTurnRight90Async(cancellationToken);
        session.Log("BACK: TB hold photo, skip 2 and 8");
        await session.ScanExposeTopBottomHoldAsync(cancellationToken);
        await session.CaptureMaskedAsync(CubeFace.B, "BACK", ScanStickerMask.TopBottomHold, cancellationToken);
        session.Log("BACK: RL hold while TB retract — write 2 and 8");
        await session.ScanExposeLeftRightHoldAsync(cancellationToken);
        await session.CaptureMaskedAsync(CubeFace.B, "BACK", ScanStickerMask.LeftRightHold, cancellationToken);
        session.Log("BACK: unwind yaw turners, re-grip TB");
        await session.ScanYawTurnersHomeKeepFaceAsync(cancellationToken);
        session.Log($"STATE after BACK photos: CURRENT_FACE={session.CurrentCameraFace}");
    }
}

public sealed class ReturnToFrontFillStep : IScanStep
{
    public string Name => "Return to FRONT, keep RL hold, fill 2 and 8";

    public async Task ExecuteAsync(IScanSession session, CancellationToken cancellationToken)
    {
        session.Log("RETURN: TURN_R_90 to FRONT");
        await session.ScanTurnRight90Async(cancellationToken);
        session.Log("RETURN: unwind yaw, keep RL hold, TB clear");
        await session.ScanYawTurnersHomeKeepRlHoldAsync(cancellationToken);
        session.Log("FRONT: RL hold photo — write 2 and 8");
        await session.CaptureMaskedAsync(CubeFace.F, "FRONT", ScanStickerMask.LeftRightHold, cancellationToken);
        session.Log($"STATE after FRONT 2/8: CURRENT_FACE={session.CurrentCameraFace}");
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

        var face = session.CurrentCameraFace;
        session.Log($"Pitch photo uses CURRENT_FACE={face} (command was {(_toTop ? "TOP" : "BOTTOM")})");
        await session.CapturePitchedFaceAsync(face, _label, cancellationToken);
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
