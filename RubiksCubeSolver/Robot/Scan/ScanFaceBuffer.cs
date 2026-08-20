using OpenCvSharp;

namespace RubiksCubeSolver.Robot.Scan;

public sealed class ScanFaceBuffer
{
    public Scalar[] Samples { get; } = new Scalar[9];

    public bool[] Written { get; } = new bool[9];

    public bool IsComplete => Written.All(written => written);

    public void Write(Scalar[] incoming, IReadOnlyList<int> indices)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        ArgumentNullException.ThrowIfNull(indices);
        if (incoming.Length != 9)
            throw new ArgumentException("A face must contain nine samples.", nameof(incoming));

        foreach (var index in indices)
        {
            if (index is < 0 or > 8)
                throw new ArgumentOutOfRangeException(nameof(indices), index, "Sticker index must be 0 through 8.");

            Samples[index] = incoming[index];
            Written[index] = true;
        }
    }
}
