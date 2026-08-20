using RubiksCubeSolver.Models;
using RubiksCubeSolver.Robot.Commands.Solve;
using System.IO;
using System.Text.Json.Nodes;

namespace RubiksCubeSolver.Tests;

public class AppSettingsTests
{
    [Fact]
    public void SwapLeftRightStations_PreservesPitchDirection()
    {
        var settings = new AppSettings
        {
            InvertPitch = false,
            RightTurner = new GripperCalibration { Port = 0 },
            RightArm = new ArmCalibration { Port = 1 },
            LeftTurner = new GripperCalibration { Port = 6 },
            LeftArm = new ArmCalibration { Port = 7 }
        };

        settings.SwapLeftRightStations();

        Assert.Equal((byte)6, settings.RightTurner.Port);
        Assert.Equal((byte)7, settings.RightArm.Port);
        Assert.Equal((byte)0, settings.LeftTurner.Port);
        Assert.Equal((byte)1, settings.LeftArm.Port);
        Assert.False(settings.InvertPitch);
    }

    [Theory]
    [InlineData(CubeFace.F, false, true)]
    [InlineData(CubeFace.B, false, false)]
    [InlineData(CubeFace.F, true, false)]
    [InlineData(CubeFace.B, true, true)]
    public void FrontBackPitch_PutsRequestedFaceOnTop(
        CubeFace face, bool invertPitch, bool expectedOpposite)
    {
        Assert.Equal(
            expectedOpposite,
            FrontBackSolveRoutine.OppositePitchToPutOnTop(face, invertPitch));
    }

    [Fact]
    public void MergeScanGridIntoFile_EmptyLayout_OverwritesStaleSavedCustomBoxes()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(
                tempPath,
                """
                {
                  "ScanRectangles": [
                    { "X": 0.1, "Y": 0.1, "Width": 0.1, "Height": 0.1 }
                  ]
                }
                """);

            var settings = new AppSettings
            {
                ScanRectangles = []
            };

            settings.MergeScanGridIntoFile(tempPath);

            var root = JsonNode.Parse(File.ReadAllText(tempPath))!.AsObject();
            var rectangles = root["ScanRectangles"]!.AsArray();
            Assert.Empty(rectangles);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }
}
