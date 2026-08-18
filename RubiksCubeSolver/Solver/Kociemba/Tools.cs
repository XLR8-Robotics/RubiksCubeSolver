using System.IO;

namespace RubiksCubeSolver.Solver.Kociemba;

public static class Tools
{
    public static string TableDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RubiksCubeSolver", "KociembaTables");

    public static int Verify(string s)
    {
        var count = new int[6];
        try
        {
            for (int i = 0; i < 54; i++)
            {
                count[(int)Enum.Parse(typeof(CubeColor), s[i].ToString())]++;
            }
        }
        catch
        {
            return -1;
        }

        for (int i = 0; i < 6; i++)
        {
            if (count[i] != 9)
            {
                return -1;
            }
        }

        var fc = new FaceCube(s);
        var cc = fc.toCubieCube();
        return cc.verify();
    }

    public static string RandomCube()
    {
        var cc = new CubieCube();
        var gen = new Random();
        cc.setFlip((short)gen.Next(CoordCubeBuildTables.N_FLIP));
        cc.setTwist((short)gen.Next(CoordCubeBuildTables.N_TWIST));
        do
        {
            cc.setURFtoDLB(gen.Next(CoordCubeBuildTables.N_URFtoDLB));
            cc.setURtoBR(gen.Next(CoordCubeBuildTables.N_URtoBR));
        } while ((cc.edgeParity() ^ cc.cornerParity()) != 0);

        return cc.toFaceCube().to_fc_String();
    }

    public static void SerializeTable(string filename, short[,] array)
    {
        Directory.CreateDirectory(TableDirectory);
        using var s = File.Create(Path.Combine(TableDirectory, filename + ".bin"));
        using var w = new BinaryWriter(s);
        w.Write(array.GetLength(0));
        w.Write(array.GetLength(1));
        for (int i = 0; i < array.GetLength(0); i++)
        {
            for (int j = 0; j < array.GetLength(1); j++)
            {
                w.Write(array[i, j]);
            }
        }
    }

    public static short[,]? TryDeserializeTable(string filename)
    {
        var path = Path.Combine(TableDirectory, filename + ".bin");
        if (!File.Exists(path))
        {
            return null;
        }

        using var s = File.OpenRead(path);
        using var r = new BinaryReader(s);
        int rows = r.ReadInt32();
        int cols = r.ReadInt32();
        var array = new short[rows, cols];
        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < cols; j++)
            {
                array[i, j] = r.ReadInt16();
            }
        }

        return array;
    }

    public static void SerializeSbyteArray(string filename, sbyte[] array)
    {
        Directory.CreateDirectory(TableDirectory);
        using var s = File.Create(Path.Combine(TableDirectory, filename + ".bin"));
        using var w = new BinaryWriter(s);
        w.Write(array.Length);
        foreach (var b in array)
        {
            w.Write(b);
        }
    }

    public static sbyte[]? TryDeserializeSbyteArray(string filename)
    {
        var path = Path.Combine(TableDirectory, filename + ".bin");
        if (!File.Exists(path))
        {
            return null;
        }

        using var s = File.OpenRead(path);
        using var r = new BinaryReader(s);
        int len = r.ReadInt32();
        var array = new sbyte[len];
        for (int i = 0; i < len; i++)
        {
            array[i] = r.ReadSByte();
        }

        return array;
    }
}
