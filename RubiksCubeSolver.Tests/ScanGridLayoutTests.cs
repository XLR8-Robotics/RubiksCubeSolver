using RubiksCubeSolver.Models;
using RubiksCubeSolver.Vision;

namespace RubiksCubeSolver.Tests;

public class ScanGridLayoutTests
{
    [Fact]
    public void CreateRegular_ReturnsNineRowMajorRectangles()
    {
        var layout = ScanGridLayout.CreateRegular(0.2, 0, 0, 0.1, 1280, 720);

        Assert.Equal(9, layout.Count);
        Assert.True(layout[0].X < layout[1].X);
        Assert.True(layout[0].Y < layout[3].Y);
        Assert.Equal(layout[0].Width, layout[8].Width, 8);
        Assert.Equal(layout[0].Height, layout[8].Height, 8);
    }

    [Fact]
    public void CreateRegular_WideFrame_ProducesSquarePixelSamples()
    {
        var layout = ScanGridLayout.CreateRegular(0.2, 0, 0, 0.1, 1280, 720);

        var pixels = ScanGridLayout.ToPixelRects(layout, 1280, 720);

        Assert.All(pixels, rect => Assert.InRange(Math.Abs(rect.Width - rect.Height), 0, 1));
    }

    [Fact]
    public void ToPixelRects_UsesSourceFrameDimensions()
    {
        var layout = new[]
        {
            new NormalizedScanRect(0.25, 0.20, 0.50, 0.40)
        };

        var pixels = ScanGridLayout.ToPixelRects(layout, 1280, 720);

        Assert.Single(pixels);
        Assert.Equal(new OpenCvSharp.Rect(320, 144, 640, 288), pixels[0]);
    }

    [Fact]
    public void ToPixelRects_EnforcesFourPixelMinimum()
    {
        var layout = new[] { new NormalizedScanRect(0.5, 0.5, 0.001, 0.001) };

        var pixels = ScanGridLayout.ToPixelRects(layout, 640, 480);

        Assert.Equal(4, pixels[0].Width);
        Assert.Equal(4, pixels[0].Height);
    }

    [Fact]
    public void ValidateOrRegular_InvalidSavedLayout_ReturnsRegularLayout()
    {
        var invalid = new[] { new NormalizedScanRect(double.NaN, 0, 0.1, 0.1) };

        var result = ScanGridLayout.ValidateOrRegular(invalid, 0.22, 0, 0, 0.18, 1280, 720);

        Assert.Equal(9, result.Count);
        Assert.All(result, rect => Assert.True(ScanGridLayout.IsValid(rect)));
    }

    [Fact]
    public void ValidateOrRegular_NearBoundarySavedLayout_IsPreserved()
    {
        var saved = ScanGridLayout.CreateRegular(0.2, 0, 0, 0.1, 1280, 720).ToArray();
        var edge = saved[8];
        saved[8] = edge with
        {
            X = Math.BitIncrement(1 - edge.Width),
            Y = Math.BitIncrement(1 - edge.Height)
        };

        var result = ScanGridLayout.ValidateOrRegular(saved, 0.12, 0.09, -0.07, 0.18, 1280, 720);

        Assert.True(ScanGridLayout.IsValid(saved[8]));
        Assert.Equal(saved, result);
    }

    [Fact]
    public void MoveAll_ClampsEntireLayoutInsideFrame()
    {
        var layout = ScanGridLayout.CreateRegular(0.2, 0, 0, 0.1, 1280, 720);

        var moved = ScanGridLayout.MoveAll(layout, 1, 1);

        Assert.Equal(1, moved.Max(rect => rect.X + rect.Width), 8);
        Assert.Equal(1, moved.Max(rect => rect.Y + rect.Height), 8);
    }

    [Fact]
    public void ScaleAll_PreservesLayoutCenterAndRelativeArrangement()
    {
        var layout = ScanGridLayout.CreateRegular(0.2, 0, 0, 0.1, 1280, 720);

        var scaled = ScanGridLayout.ScaleAll(layout, 0.5);

        Assert.Equal(layout[4].X + layout[4].Width / 2,
            scaled[4].X + scaled[4].Width / 2, 8);
        Assert.True(scaled[0].Width < layout[0].Width);
    }

    [Fact]
    public void ScaleAll_NearBoundaryLayout_DoesNotThrowWhenClampRangeInverts()
    {
        var layout = new[]
        {
            new NormalizedScanRect(
                -5e-13,
                0.25,
                ScanGridLayout.MinimumNormalizedSize,
                0.10)
        };

        var scaled = ScanGridLayout.ScaleAll(layout, 1.5);

        Assert.True(ScanGridLayout.IsValid(layout[0]));
        Assert.Equal(layout, scaled);
    }

    [Fact]
    public void MoveAndResizeOne_OnlyChangeSelectedRectangle()
    {
        var layout = ScanGridLayout.CreateRegular(0.2, 0, 0, 0.1, 1280, 720);

        var moved = ScanGridLayout.MoveOne(layout, 4, 0.02, -0.01);
        var resized = ScanGridLayout.ResizeOne(moved, 4, 0.03, 0.02);

        Assert.Equal(layout[0], resized[0]);
        Assert.NotEqual(layout[4], resized[4]);
        Assert.True(resized[4].Width > moved[4].Width);
    }

    [Fact]
    public void AppSettings_MissingCustomLayout_GeneratesRegularLayout()
    {
        var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
            """{"FaceMargin":0.2,"FaceOffsetX":0.1,"FaceOffsetY":0.0,"FaceSampleInset":0.1}""")!;

        var result = settings.GetScanRectangles(1280, 720);

        Assert.Equal(9, result.Count);
        var pixels = ScanGridLayout.ToPixelRects(result, 1280, 720);
        Assert.All(pixels, rect => Assert.InRange(Math.Abs(rect.Width - rect.Height), 0, 1));
    }

    [Fact]
    public void AppSettings_CustomLayout_RoundTripsThroughJson()
    {
        var settings = new AppSettings
        {
            ScanRectangles = ScanGridLayout.CreateRegular(0.2, 0, 0, 0.1, 1280, 720).ToList()
        };

        var json = System.Text.Json.JsonSerializer.Serialize(settings);
        var restored = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json)!;

        Assert.Equal(settings.ScanRectangles, restored.ScanRectangles);
    }

    [Fact]
    public void ResetScanRectangles_ReplacesIndividualAdjustmentsWithRegularLayout()
    {
        var settings = new AppSettings
        {
            ScanRectangles = Enumerable.Repeat(
                new NormalizedScanRect(0.1, 0.1, 0.05, 0.05), 9).ToList()
        };

        settings.ResetScanRectangles(1280, 720);

        Assert.Equal(9, settings.ScanRectangles!.Count);
        Assert.NotEqual(settings.ScanRectangles[0], settings.ScanRectangles[1]);
    }

    [Fact]
    public void IsValid_RejectsMateriallyOutOfBoundsRectangles()
    {
        Assert.False(ScanGridLayout.IsValid(new NormalizedScanRect(-0.001, 0.1, 0.2, 0.2)));
        Assert.False(ScanGridLayout.IsValid(new NormalizedScanRect(0.8001, 0.1, 0.2, 0.2)));
        Assert.False(ScanGridLayout.IsValid(new NormalizedScanRect(0.1, 0.8001, 0.2, 0.2)));
    }

    [Fact]
    public void ManualPixelRects_UsesSavedCustomRectangles()
    {
        var settings = new AppSettings
        {
            ScanRectangles =
            [
                new(0.1, 0.1, 0.1, 0.1), new(0.2, 0.1, 0.1, 0.1), new(0.3, 0.1, 0.1, 0.1),
                new(0.1, 0.2, 0.1, 0.1), new(0.2, 0.2, 0.1, 0.1), new(0.3, 0.2, 0.1, 0.1),
                new(0.1, 0.3, 0.1, 0.1), new(0.2, 0.3, 0.1, 0.1), new(0.3, 0.3, 0.1, 0.1)
            ]
        };

        var result = FaceScanner.ManualPixelRects(1000, 500, settings);

        Assert.Equal(new OpenCvSharp.Rect(100, 50, 100, 50), result[0]);
        Assert.Equal(new OpenCvSharp.Rect(300, 150, 100, 50), result[8]);
    }
}
