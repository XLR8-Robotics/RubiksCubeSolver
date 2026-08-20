namespace RubiksCubeSolver.Robot.Scan;

public static class ScanStickerMask
{
    public static IReadOnlyList<int> AllNine { get; } = [0, 1, 2, 3, 4, 5, 6, 7, 8];

    public static IReadOnlyList<int> TopBottomHold { get; } = [0, 2, 3, 4, 5, 6, 8];

    public static IReadOnlyList<int> LeftRightHold { get; } = [1, 7];
}
