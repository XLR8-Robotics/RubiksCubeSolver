namespace RubiksCubeSolver.Models;

public enum StickerColor
{
    Unknown,
    White,
    Yellow,
    Red,
    Orange,
    Blue,
    Green
}

public enum CubeFace
{
    U,
    R,
    F,
    D,
    L,
    B
}

public enum RobotStation
{
    Right,
    Top,
    Left,
    Bottom,
    Front,
    Back
}

public readonly record struct CubeMove(CubeFace Face, int QuarterTurns)
{
    public override string ToString()
    {
        var letter = Face.ToString();
        return QuarterTurns switch
        {
            2 => letter + "2",
            3 => letter + "'",
            _ => letter
        };
    }

    public static IReadOnlyList<CubeMove> ParseSequence(string solution)
    {
        var moves = new List<CubeMove>();
        if (string.IsNullOrWhiteSpace(solution) || solution.StartsWith("Error", StringComparison.OrdinalIgnoreCase))
        {
            return moves;
        }

        foreach (var token in solution.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (token is "." or "..")
            {
                continue;
            }

            var face = token[0] switch
            {
                'U' => CubeFace.U,
                'R' => CubeFace.R,
                'F' => CubeFace.F,
                'D' => CubeFace.D,
                'L' => CubeFace.L,
                'B' => CubeFace.B,
                _ => throw new FormatException($"Unknown move '{token}'.")
            };
            var turns = 1;
            if (token.Length > 1)
            {
                turns = token[1] switch
                {
                    '2' => 2,
                    '\'' => 3,
                    _ => 1
                };
            }

            moves.Add(new CubeMove(face, turns));
        }

        return moves;
    }
}
