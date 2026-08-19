using RubiksCubeSolver.Models;

namespace RubiksCubeSolver.Robot;

public sealed class CubeOrientation
{
    public CubeFace Up { get; set; } = CubeFace.U;
    public CubeFace Down { get; set; } = CubeFace.D;
    public CubeFace Front { get; set; } = CubeFace.F;
    public CubeFace Back { get; set; } = CubeFace.B;
    public CubeFace Left { get; set; } = CubeFace.L;
    public CubeFace Right { get; set; } = CubeFace.R;

    public static CubeOrientation Home() => new();

    public CubeFace FaceAt(RobotStation station) => station switch
    {
        RobotStation.Right => Right,
        RobotStation.Top => Up,
        RobotStation.Left => Left,
        RobotStation.Bottom => Down,
        RobotStation.Front => Front,
        _ => Back
    };

    public RobotStation StationOf(CubeFace face)
    {
        if (Right == face) return RobotStation.Right;
        if (Up == face) return RobotStation.Top;
        if (Left == face) return RobotStation.Left;
        if (Down == face) return RobotStation.Bottom;
        if (Front == face) return RobotStation.Front;
        return RobotStation.Back;
    }

    public void Pitch(bool invert)
    {
        if (!invert)
        {
            var up = Up;
            Up = Back;
            Back = Down;
            Down = Front;
            Front = up;
        }
        else
        {
            var up = Up;
            Up = Front;
            Front = Down;
            Down = Back;
            Back = up;
        }
    }

    public void Yaw(bool invert)
    {
        if (!invert)
        {
            var front = Front;
            Front = Right;
            Right = Back;
            Back = Left;
            Left = front;
        }
        else
        {
            var front = Front;
            Front = Left;
            Left = Back;
            Back = Right;
            Right = front;
        }
    }
}
