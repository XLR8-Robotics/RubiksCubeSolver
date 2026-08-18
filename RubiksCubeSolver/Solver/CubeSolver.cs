using RubiksCubeSolver.Models;
using RubiksCubeSolver.Solver.Kociemba;

namespace RubiksCubeSolver.Solver;

public static class CubeSolver
{
    public static void Warmup()
    {
        CoordCubeBuildTables.EnsureTables();
        const string fTurn = "UUUUUULLLURRURRURRFFFFFFFFFRRRDDDDDDLLDLLDLLDBBBBBBBBB";
        var solution = SearchRunTime.Solution(fTurn);
        if (solution.StartsWith("Error", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Solver self-test failed: " + solution);
        }
    }

    public static IReadOnlyList<CubeMove> Solve(string facelets)
    {
        var result = SearchRunTime.Solution(facelets);
        if (result.StartsWith("Error", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(Describe(result));
        }

        return CubeMove.ParseSequence(result);
    }

    static string Describe(string error) => error switch
    {
        "Error 1" => "The scanned cube does not have nine stickers of each color.",
        "Error 2" => "Not all 12 edges exist exactly once.",
        "Error 3" => "An edge is flipped — usually a color-scan error.",
        "Error 4" => "Not all 8 corners exist exactly once.",
        "Error 5" => "A corner is twisted — usually a color-scan error.",
        "Error 6" => "The cube has invalid permutation parity.",
        "Error 7" => "No solution exists within the move limit.",
        "Error 8" => "The solver timed out.",
        _ => error
    };
}
