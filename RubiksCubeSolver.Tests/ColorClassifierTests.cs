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
    public void ToHsv_RoundTripsHueNearRequestedValue()
    {
        var hsv = ColorClassifier.ToHsv(BgrFromHsv(10, 200, 200));

        Assert.InRange(hsv.H, 8, 12);
        Assert.InRange(hsv.S, 180, 220);
        Assert.InRange(hsv.V, 180, 220);
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
    public void Normalized_ZeroValues_BecomeDefaultHueRanges()
    {
        var splits = new ColorHueSplits().Normalized();

        Assert.Equal(171, splits.RedHueFrom);
        Assert.Equal(7, splits.RedHueTo);
        Assert.Equal(8, splits.OrangeHueFrom);
        Assert.Equal(17, splits.OrangeHueTo);
        Assert.Equal(18, splits.YellowHueFrom);
        Assert.Equal(37, splits.YellowHueTo);
        Assert.Equal(38, splits.GreenHueFrom);
        Assert.Equal(84, splits.GreenHueTo);
        Assert.Equal(85, splits.BlueHueFrom);
        Assert.Equal(170, splits.BlueHueTo);
        Assert.Equal(8, splits.RedOrange);
        Assert.Equal(50, splits.WhiteSaturation);
    }

    [Fact]
    public void Normalized_LegacySplitPoints_FillFromToRanges()
    {
        var splits = new ColorHueSplits
        {
            RedOrange = 12,
            OrangeYellow = 18,
            YellowGreen = 38,
            GreenBlue = 85,
            BlueRed = 170
        }.Normalized();

        Assert.Equal(171, splits.RedHueFrom);
        Assert.Equal(11, splits.RedHueTo);
        Assert.Equal(12, splits.OrangeHueFrom);
        Assert.Equal(17, splits.OrangeHueTo);
    }

    [Fact]
    public void MatchHue_WrapRedAndLowOrange_Classifies176AsRedAnd7AsOrange()
    {
        var splits = new ColorHueSplits
        {
            RedHueFrom = 170,
            RedHueTo = 4,
            OrangeHueFrom = 5,
            OrangeHueTo = 17,
            YellowHueFrom = 18,
            YellowHueTo = 37,
            GreenHueFrom = 38,
            GreenHueTo = 84,
            BlueHueFrom = 85,
            BlueHueTo = 169
        }.Normalized();

        Assert.Equal(StickerColor.Red, splits.MatchHue(176));
        Assert.Equal(StickerColor.Red, splits.MatchHue(0));
        Assert.Equal(StickerColor.Orange, splits.MatchHue(7));
        Assert.Equal(StickerColor.Orange, splits.MatchHue(5));
    }

    [Fact]
    public void ContainsHue_FromGreaterThanTo_WrapsPast179()
    {
        Assert.True(ColorHueSplits.ContainsHue(176, 170, 4));
        Assert.True(ColorHueSplits.ContainsHue(0, 170, 4));
        Assert.True(ColorHueSplits.ContainsHue(4, 170, 4));
        Assert.False(ColorHueSplits.ContainsHue(5, 170, 4));
        Assert.False(ColorHueSplits.ContainsHue(90, 170, 4));
    }
}
