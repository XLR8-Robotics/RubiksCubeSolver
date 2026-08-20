using RubiksCubeSolver.Models;
using RubiksCubeSolver.Robot;

namespace RubiksCubeSolver.Tests;

public class CubeOrientationTests
{
    [Theory]
    [InlineData(false, CubeFace.U)]
    [InlineData(true, CubeFace.D)]
    public void Pitch_FromHome_PutsExpectedFaceAtCamera(bool invert, CubeFace expectedFront)
    {
        var orientation = CubeOrientation.Home();

        orientation.Pitch(invert);

        Assert.Equal(expectedFront, orientation.Front);
    }

    [Theory]
    [InlineData(true, false, CubeFace.D)]
    [InlineData(false, false, CubeFace.U)]
    [InlineData(true, true, CubeFace.U)]
    [InlineData(false, true, CubeFace.D)]
    public void ScanPitchCommand_MatchesPitchSpinInvert(
        bool toTop, bool invertPitch, CubeFace expectedAtCamera)
    {
        var opposite = toTop;
        var invert = invertPitch ^ opposite;
        var orientation = CubeOrientation.Home();

        orientation.Pitch(invert);

        Assert.Equal(expectedAtCamera, orientation.Front);
    }
}
