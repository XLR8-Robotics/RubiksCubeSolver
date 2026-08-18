namespace RubiksCubeSolver.Hardware;

public sealed class CameraDevice
{
    public required int Index { get; init; }
    public required string Name { get; init; }

    public override string ToString() => $"{Index}: {Name}";
}
