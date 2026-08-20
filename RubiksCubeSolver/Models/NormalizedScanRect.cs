namespace RubiksCubeSolver.Models;

public sealed record NormalizedScanRect
{
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }

    public NormalizedScanRect()
    {
    }

    public NormalizedScanRect(double x, double y, double width, double height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }
}
