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
    public void Guess_RaisedRedOrange_TreatsHue10AsRed()
    {
        Assert.Equal(StickerColor.Red, ColorClassifier.Guess(BgrFromHsv(10), new ColorHueSplits { RedOrange = 12 }));
    }

    [Fact]
    public void Guess_LoweredRedOrange_TreatsHue6AsOrange()
    {
        Assert.Equal(StickerColor.Orange, ColorClassifier.Guess(BgrFromHsv(6), new ColorHueSplits { RedOrange = 4 }));
    }

    [Fact]
    public void Guess_RaisedOrangeYellow_TreatsHue25AsOrange()
    {
        Assert.Equal(
            StickerColor.Orange,
            ColorClassifier.Guess(BgrFromHsv(25), new ColorHueSplits { OrangeYellow = 28 }));
    }

    [Fact]
    public void Guess_RaisedYellowGreen_TreatsHue50AsYellow()
    {
        Assert.Equal(
            StickerColor.Yellow,
            ColorClassifier.Guess(BgrFromHsv(50), new ColorHueSplits { YellowGreen = 55 }));
    }

    [Fact]
    public void Guess_RaisedGreenBlue_TreatsHue90AsGreen()
    {
        Assert.Equal(
            StickerColor.Green,
            ColorClassifier.Guess(BgrFromHsv(90), new ColorHueSplits { GreenBlue = 110 }));
    }

    [Fact]
    public void Guess_LoweredBlueRed_TreatsHue165AsRed()
    {
        Assert.Equal(
            StickerColor.Red,
            ColorClassifier.Guess(BgrFromHsv(165), new ColorHueSplits { BlueRed = 160 }));
    }

    [Fact]
    public void Guess_RaisedWhiteSaturation_TreatsPaleHueAsWhite()
    {
        Assert.Equal(
            StickerColor.White,
            ColorClassifier.Guess(BgrFromHsv(20, 70, 200), new ColorHueSplits { WhiteSaturation = 80 }));
    }

    [Fact]
    public void Normalized_ZeroValues_BecomeDefaults()
    {
        var splits = new ColorHueSplits().Normalized();

        Assert.Equal(8, splits.RedOrange);
        Assert.Equal(18, splits.OrangeYellow);
        Assert.Equal(38, splits.YellowGreen);
        Assert.Equal(85, splits.GreenBlue);
        Assert.Equal(170, splits.BlueRed);
        Assert.Equal(50, splits.WhiteSaturation);
    }

    [Fact]
    public void Normalized_KeepsBoundariesStrictlyIncreasing()
    {
        var splits = new ColorHueSplits
        {
            RedOrange = 40,
            OrangeYellow = 30,
            YellowGreen = 20,
            GreenBlue = 10,
            BlueRed = 5
        }.Normalized();

        Assert.True(splits.RedOrange < splits.OrangeYellow);
        Assert.True(splits.OrangeYellow < splits.YellowGreen);
        Assert.True(splits.YellowGreen < splits.GreenBlue);
        Assert.True(splits.GreenBlue < splits.BlueRed);
    }
}
