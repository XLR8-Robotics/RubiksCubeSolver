using OpenCvSharp;
using RubiksCubeSolver.Robot.Scan;

namespace RubiksCubeSolver.Tests;

public class ScanFaceBufferTests
{
    static Scalar S(double v) => new(v, v, v);

    [Fact]
    public void TopBottomHold_SkipsStickersTwoAndEight()
    {
        Assert.Equal(new[] { 0, 2, 3, 4, 5, 6, 8 }, ScanStickerMask.TopBottomHold);
        Assert.DoesNotContain(1, ScanStickerMask.TopBottomHold);
        Assert.DoesNotContain(7, ScanStickerMask.TopBottomHold);
    }

    [Fact]
    public void LeftRightHold_IsOnlyStickersTwoAndEight()
    {
        Assert.Equal(new[] { 1, 7 }, ScanStickerMask.LeftRightHold);
    }

    [Fact]
    public void Write_TopBottomHold_DoesNotStoreObstructedStickers()
    {
        var buffer = new ScanFaceBuffer();
        var incoming = Enumerable.Range(0, 9).Select(i => S(i + 1)).ToArray();

        buffer.Write(incoming, ScanStickerMask.TopBottomHold);

        Assert.False(buffer.Written[1]);
        Assert.False(buffer.Written[7]);
        Assert.Equal(5, buffer.Samples[4].Val0);
        Assert.False(buffer.IsComplete);
    }

    [Fact]
    public void Write_LeftRightHold_FillsOnlyTwoAndEight_WithoutClobberingFourAndSix()
    {
        var buffer = new ScanFaceBuffer();
        var first = Enumerable.Range(0, 9).Select(i => S(10)).ToArray();
        var second = Enumerable.Range(0, 9).Select(i => S(20)).ToArray();

        buffer.Write(first, ScanStickerMask.TopBottomHold);
        buffer.Write(second, ScanStickerMask.LeftRightHold);

        Assert.Equal(10, buffer.Samples[3].Val0);
        Assert.Equal(10, buffer.Samples[5].Val0);
        Assert.Equal(20, buffer.Samples[1].Val0);
        Assert.Equal(20, buffer.Samples[7].Val0);
        Assert.True(buffer.IsComplete);
    }
}
