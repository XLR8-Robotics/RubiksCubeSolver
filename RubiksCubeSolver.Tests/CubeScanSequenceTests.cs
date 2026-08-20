using RubiksCubeSolver.Models;
using RubiksCubeSolver.Robot.Scan;

namespace RubiksCubeSolver.Tests;

public class CubeScanSequenceTests
{
    [Fact]
    public async Task Default_RecordsOpportunisticPhotosAlongYawLoop()
    {
        var session = new RecordingScanSession();

        await CubeScanSequence.RunAsync(session, CubeScanSequence.Default, CancellationToken.None);

        Assert.Equal(
            [
                "expose-tb",
                "capture F tb-hold",
                "turn-r-90",
                "expose-tb",
                "capture R all",
                "home-keep-face",
                "turn-r-90",
                "expose-tb",
                "capture B tb-hold",
                "expose-rl",
                "capture B rl-hold",
                "home-keep-face",
                "turn-r-90",
                "expose-tb",
                "capture L all",
                "home-keep-face",
                "turn-r-90",
                "home-keep-rl",
                "capture F rl-hold",
                "pitch-top",
                "capture-pitched U",
                "pitch-return",
                "pitch-bottom",
                "capture-pitched D",
                "pitch-return",
                "finish-hug"
            ],
            session.Calls);
    }

    sealed class RecordingScanSession : IScanSession
    {
        public List<string> Calls { get; } = [];

        public CubeFace CurrentCameraFace => CubeFace.F;

        public void Log(string message)
        {
        }

        public Task CaptureMaskedAsync(
            CubeFace face, string label, IReadOnlyList<int> stickerIndices, CancellationToken cancellationToken)
        {
            Calls.Add($"capture {face} {MaskName(stickerIndices)}");
            return Task.CompletedTask;
        }

        public Task CapturePitchedFaceAsync(CubeFace face, string label, CancellationToken cancellationToken)
        {
            Calls.Add($"capture-pitched {face}");
            return Task.CompletedTask;
        }

        public Task ScanExposeTopBottomHoldAsync(CancellationToken cancellationToken)
        {
            Calls.Add("expose-tb");
            return Task.CompletedTask;
        }

        public Task ScanExposeLeftRightHoldAsync(CancellationToken cancellationToken)
        {
            Calls.Add("expose-rl");
            return Task.CompletedTask;
        }

        public Task ScanTurnRight90Async(CancellationToken cancellationToken)
        {
            Calls.Add("turn-r-90");
            return Task.CompletedTask;
        }

        public Task ScanYawTurnersHomeKeepFaceAsync(CancellationToken cancellationToken)
        {
            Calls.Add("home-keep-face");
            return Task.CompletedTask;
        }

        public Task ScanYawTurnersHomeAtFrontAsync(CancellationToken cancellationToken)
        {
            Calls.Add("home-at-front");
            return Task.CompletedTask;
        }

        public Task ScanYawTurnersHomeKeepRlHoldAsync(CancellationToken cancellationToken)
        {
            Calls.Add("home-keep-rl");
            return Task.CompletedTask;
        }

        public Task ScanPitchToTopAsync(CancellationToken cancellationToken)
        {
            Calls.Add("pitch-top");
            return Task.CompletedTask;
        }

        public Task ScanPitchToBottomAsync(CancellationToken cancellationToken)
        {
            Calls.Add("pitch-bottom");
            return Task.CompletedTask;
        }

        public Task ScanPitchReturnToFrontAsync(CancellationToken cancellationToken)
        {
            Calls.Add("pitch-return");
            return Task.CompletedTask;
        }

        public Task ScanFinishHugAtFrontAsync(CancellationToken cancellationToken)
        {
            Calls.Add("finish-hug");
            return Task.CompletedTask;
        }

        static string MaskName(IReadOnlyList<int> indices)
        {
            if (indices.SequenceEqual(ScanStickerMask.AllNine))
                return "all";
            if (indices.SequenceEqual(ScanStickerMask.TopBottomHold))
                return "tb-hold";
            if (indices.SequenceEqual(ScanStickerMask.LeftRightHold))
                return "rl-hold";
            return string.Join(',', indices);
        }
    }
}
