using RubiksCubeSolver.Solver.Kociemba;

namespace RubiksCubeSolver.Models;

public sealed class DigitalCube
{
    static readonly int[][] CameFrom = BuildMoveTables();

    public StickerColor[] Colors { get; } = SolvedWestern();

    public static StickerColor[] SolvedWestern()
    {
        var colors = new StickerColor[54];
        Fill(colors, 0, StickerColor.White);
        Fill(colors, 9, StickerColor.Red);
        Fill(colors, 18, StickerColor.Green);
        Fill(colors, 27, StickerColor.Yellow);
        Fill(colors, 36, StickerColor.Orange);
        Fill(colors, 45, StickerColor.Blue);
        return colors;
    }

    static void Fill(StickerColor[] colors, int start, StickerColor color)
    {
        for (int i = 0; i < 9; i++)
        {
            colors[start + i] = color;
        }
    }

    public void CopyFrom(IReadOnlyList<StickerColor> source)
    {
        for (int i = 0; i < 54; i++)
        {
            Colors[i] = source[i];
        }
    }

    public void ResetSolved()
    {
        Array.Copy(SolvedWestern(), Colors, 54);
    }

    public static IReadOnlyList<CubeMove> RandomScramble(int moves = 20)
    {
        var list = new List<CubeMove>(moves);
        var rng = new Random();
        CubeFace? last = null;
        for (int i = 0; i < moves; i++)
        {
            CubeFace face;
            do
            {
                face = (CubeFace)rng.Next(6);
            } while (last is not null && face == last);

            last = face;
            list.Add(new CubeMove(face, rng.Next(1, 4)));
        }

        return list;
    }

    public void Apply(CubeMove move)
    {
        var perm = CameFrom[(int)move.Face];
        for (int n = 0; n < move.QuarterTurns; n++)
        {
            var next = new StickerColor[54];
            for (int i = 0; i < 54; i++)
            {
                next[i] = Colors[perm[i]];
            }

            Array.Copy(next, Colors, 54);
        }
    }

    static int[][] BuildMoveTables()
    {
        var tables = new int[6][];
        for (int m = 0; m < 6; m++)
        {
            tables[m] = BuildCameFrom(CubieCube.moveCube[m]);
        }

        return tables;
    }

    static int[] BuildCameFrom(CubieCube move)
    {
        var cameFrom = new int[54];
        for (int i = 0; i < 54; i++)
        {
            cameFrom[i] = i;
        }

        for (int i = 0; i < 8; i++)
        {
            var from = (int)move.cp[i];
            int ori = move.co[i];
            for (int k = 0; k < 3; k++)
            {
                int dest = (int)FaceCube.cornerFacelet[i][(k + ori) % 3];
                int src = (int)FaceCube.cornerFacelet[from][k];
                cameFrom[dest] = src;
            }
        }

        for (int i = 0; i < 12; i++)
        {
            var from = (int)move.ep[i];
            int ori = move.eo[i];
            for (int k = 0; k < 2; k++)
            {
                int dest = (int)FaceCube.edgeFacelet[i][(k + ori) % 2];
                int src = (int)FaceCube.edgeFacelet[from][k];
                cameFrom[dest] = src;
            }
        }

        return cameFrom;
    }
}
