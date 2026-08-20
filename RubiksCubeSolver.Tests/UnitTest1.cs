using RubiksCubeSolver.Models;
using RubiksCubeSolver.Robot.Commands.Solve;

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
}
