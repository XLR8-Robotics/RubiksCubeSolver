using RubiksCubeSolver.Models;

namespace RubiksCubeSolver.Robot.Scan;

public interface IScanSession
{
    CubeFace CurrentCameraFace { get; }
    void Log(string message);
    Task CaptureMaskedAsync(
        CubeFace face, string label, IReadOnlyList<int> stickerIndices, CancellationToken cancellationToken);
    Task CapturePitchedFaceAsync(CubeFace face, string label, CancellationToken cancellationToken);
    Task ScanExposeTopBottomHoldAsync(CancellationToken cancellationToken);
    Task ScanExposeLeftRightHoldAsync(CancellationToken cancellationToken);
    Task ScanTurnRight90Async(CancellationToken cancellationToken);
    Task ScanYawTurnersHomeKeepFaceAsync(CancellationToken cancellationToken);
    Task ScanYawTurnersHomeAtFrontAsync(CancellationToken cancellationToken);
    Task ScanYawTurnersHomeKeepRlHoldAsync(CancellationToken cancellationToken);
    Task ScanPitchToTopAsync(CancellationToken cancellationToken);
    Task ScanPitchToBottomAsync(CancellationToken cancellationToken);
    Task ScanPitchReturnToFrontAsync(CancellationToken cancellationToken);
    Task ScanFinishHugAtFrontAsync(CancellationToken cancellationToken);
}

public interface IScanStep
{
    string Name { get; }
    Task ExecuteAsync(IScanSession session, CancellationToken cancellationToken);
}
