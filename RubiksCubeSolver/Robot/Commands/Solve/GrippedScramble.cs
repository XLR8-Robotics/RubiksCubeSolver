using RubiksCubeSolver.Models;

namespace RubiksCubeSolver.Robot.Commands.Solve;

public static class GrippedScramble
{
    public static IReadOnlyList<(RobotStation Station, CubeMove Move)> Create(CubeOrientation orientation, int moves)
    {
        var list = new List<(RobotStation Station, CubeMove Move)>(moves);
        var rng = new Random();
        var stations = new[] { RobotStation.Right, RobotStation.Top, RobotStation.Left, RobotStation.Bottom };
        RobotStation? last = null;
        for (int i = 0; i < moves; i++)
        {
            RobotStation station;
            do
            {
                station = stations[rng.Next(stations.Length)];
            } while (last is not null && station == last);

            last = station;
            list.Add((station, new CubeMove(orientation.FaceAt(station), 1)));
        }

        return list;
    }
}
