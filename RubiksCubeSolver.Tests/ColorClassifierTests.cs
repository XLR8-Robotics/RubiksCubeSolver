using OpenCvSharp;
using RubiksCubeSolver.Models;
using RubiksCubeSolver.Vision;

namespace RubiksCubeSolver.Tests;

public class ColorClassifierTests
{
    static Scalar BgrFromHsv(byte hue, byte saturation = 200, byte value = 200)
    {
        using var hsv = new Mat(1, 1, MatType.CV_8UC3, new Scalar(hue, saturation, value));
        using var bgr = new Mat();
        Cv2.CvtColor(hsv, bgr, ColorConversionCodes.HSV2BGR);
        var pixel = bgr.Get<Vec3b>(0, 0);
        return new Scalar(pixel.Item0, pixel.Item1, pixel.Item2);
    }

    [Fact]
    public void Guess_DefaultSplit_TreatsHue10AsOrange()
    {
        Assert.Equal(StickerColor.Orange, ColorClassifier.Guess(BgrFromHsv(10)));
    }

    [Fact]
    public void Guess_RaisedSplit_TreatsHue10AsRed()
    {
        Assert.Equal(StickerColor.Red, ColorClassifier.Guess(BgrFromHsv(10), 12));
    }

    [Fact]
    public void Guess_LoweredSplit_TreatsHue6AsOrange()
    {
        Assert.Equal(StickerColor.Orange, ColorClassifier.Guess(BgrFromHsv(6), 4));
    }
}
