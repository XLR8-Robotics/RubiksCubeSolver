namespace RubiksCubeSolver.Solver.Kociemba;

internal class CoordCubeBuildTables
{
    internal const short N_TWIST = 2187;
    internal const short N_FLIP = 2048;
    internal const short N_SLICE1 = 495;
    internal const short N_SLICE2 = 24;
    internal const short N_PARITY = 2;
    internal const short N_URFtoDLF = 20160;
    internal const short N_FRtoBR = 11880;
    internal const short N_URtoUL = 1320;
    internal const short N_UBtoDF = 1320;
    internal const short N_URtoDF = 20160;
    internal const int N_URFtoDLB = 40320;
    internal const int N_URtoBR = 479001600;
    internal const short N_MOVE = 18;

    internal short twist;
    internal short flip;
    internal short parity;
    internal short FRtoBR;
    internal short URFtoDLF;
    internal short URtoUL;
    internal short UBtoDF;
    internal int URtoDF;

    internal static bool TablesReady { get; private set; }

    internal CoordCubeBuildTables(CubieCube c)
    {
        twist = c.getTwist();
        flip = c.getFlip();
        parity = c.cornerParity();
        FRtoBR = c.getFRtoBR();
        URFtoDLF = c.getURFtoDLF();
        URtoUL = c.getURtoUL();
        UBtoDF = c.getUBtoDF();
        URtoDF = c.getURtoDF();
    }

    internal static short[,] twistMove = new short[N_TWIST, N_MOVE];
    internal static short[,] flipMove = new short[N_FLIP, N_MOVE];
    internal static short[][] parityMove =
    [
        [1, 0, 1, 1, 0, 1, 1, 0, 1, 1, 0, 1, 1, 0, 1, 1, 0, 1],
        [0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1, 0]
    ];
    internal static short[,] FRtoBR_Move = new short[N_FRtoBR, N_MOVE];
    internal static short[,] URFtoDLF_Move = new short[N_URFtoDLF, N_MOVE];
    internal static short[,] URtoDF_Move = new short[N_URtoDF, N_MOVE];
    internal static short[,] URtoUL_Move = new short[N_URtoUL, N_MOVE];
    internal static short[,] UBtoDF_Move = new short[N_UBtoDF, N_MOVE];
    internal static short[,] MergeURtoULandUBtoDF = new short[336, 336];
    internal static sbyte[] Slice_URFtoDLF_Parity_Prun = new sbyte[N_SLICE2 * N_URFtoDLF * N_PARITY / 2];
    internal static sbyte[] Slice_URtoDF_Parity_Prun = new sbyte[N_SLICE2 * N_URtoDF * N_PARITY / 2];
    internal static sbyte[] Slice_Twist_Prun = new sbyte[N_SLICE1 * N_TWIST / 2 + 1];
    internal static sbyte[] Slice_Flip_Prun = new sbyte[N_SLICE1 * N_FLIP / 2];

    public static void EnsureTables()
    {
        _ = twistMove[0, 0];
    }

    static CoordCubeBuildTables()
    {
        if (TryLoadCached())
        {
            TablesReady = true;
            return;
        }

        GenerateTables();
        SaveCached();
        TablesReady = true;
    }

    static bool TryLoadCached()
    {
        try
        {
            var twist = Tools.TryDeserializeTable("twist");
            var flip = Tools.TryDeserializeTable("flip");
            var fr = Tools.TryDeserializeTable("FRtoBR");
            var urf = Tools.TryDeserializeTable("URFtoDLF");
            var urdf = Tools.TryDeserializeTable("URtoDF");
            var urul = Tools.TryDeserializeTable("URtoUL");
            var ubdf = Tools.TryDeserializeTable("UBtoDF");
            var merge = Tools.TryDeserializeTable("MergeURtoULandUBtoDF");
            var p1 = Tools.TryDeserializeSbyteArray("Slice_URFtoDLF_Parity_Prun");
            var p2 = Tools.TryDeserializeSbyteArray("Slice_URtoDF_Parity_Prun");
            var p3 = Tools.TryDeserializeSbyteArray("Slice_Twist_Prun");
            var p4 = Tools.TryDeserializeSbyteArray("Slice_Flip_Prun");
            if (twist is null || flip is null || fr is null || urf is null || urdf is null
                || urul is null || ubdf is null || merge is null
                || p1 is null || p2 is null || p3 is null || p4 is null)
            {
                return false;
            }

            twistMove = twist;
            flipMove = flip;
            FRtoBR_Move = fr;
            URFtoDLF_Move = urf;
            URtoDF_Move = urdf;
            URtoUL_Move = urul;
            UBtoDF_Move = ubdf;
            MergeURtoULandUBtoDF = merge;
            Slice_URFtoDLF_Parity_Prun = p1;
            Slice_URtoDF_Parity_Prun = p2;
            Slice_Twist_Prun = p3;
            Slice_Flip_Prun = p4;
            return true;
        }
        catch
        {
            return false;
        }
    }

    static void SaveCached()
    {
        try
        {
            Tools.SerializeTable("twist", twistMove);
            Tools.SerializeTable("flip", flipMove);
            Tools.SerializeTable("FRtoBR", FRtoBR_Move);
            Tools.SerializeTable("URFtoDLF", URFtoDLF_Move);
            Tools.SerializeTable("URtoDF", URtoDF_Move);
            Tools.SerializeTable("URtoUL", URtoUL_Move);
            Tools.SerializeTable("UBtoDF", UBtoDF_Move);
            Tools.SerializeTable("MergeURtoULandUBtoDF", MergeURtoULandUBtoDF);
            Tools.SerializeSbyteArray("Slice_URFtoDLF_Parity_Prun", Slice_URFtoDLF_Parity_Prun);
            Tools.SerializeSbyteArray("Slice_URtoDF_Parity_Prun", Slice_URtoDF_Parity_Prun);
            Tools.SerializeSbyteArray("Slice_Twist_Prun", Slice_Twist_Prun);
            Tools.SerializeSbyteArray("Slice_Flip_Prun", Slice_Flip_Prun);
        }
        catch
        {
            // Caching is optional; solving still works in-memory.
        }
    }

    static void GenerateTables()
    {
        var a = new CubieCube();
        for (short i = 0; i < N_TWIST; i++)
        {
            a.setTwist(i);
            for (int j = 0; j < 6; j++)
            {
                for (int k = 0; k < 3; k++)
                {
                    a.cornerMultiply(CubieCube.moveCube[j]);
                    twistMove[i, 3 * j + k] = a.getTwist();
                }

                a.cornerMultiply(CubieCube.moveCube[j]);
            }
        }

        a = new CubieCube();
        for (short i = 0; i < N_FLIP; i++)
        {
            a.setFlip(i);
            for (int j = 0; j < 6; j++)
            {
                for (int k = 0; k < 3; k++)
                {
                    a.edgeMultiply(CubieCube.moveCube[j]);
                    flipMove[i, 3 * j + k] = a.getFlip();
                }

                a.edgeMultiply(CubieCube.moveCube[j]);
            }
        }

        a = new CubieCube();
        for (short i = 0; i < N_FRtoBR; i++)
        {
            a.setFRtoBR(i);
            for (int j = 0; j < 6; j++)
            {
                for (int k = 0; k < 3; k++)
                {
                    a.edgeMultiply(CubieCube.moveCube[j]);
                    FRtoBR_Move[i, 3 * j + k] = a.getFRtoBR();
                }

                a.edgeMultiply(CubieCube.moveCube[j]);
            }
        }

        a = new CubieCube();
        for (short i = 0; i < N_URFtoDLF; i++)
        {
            a.setURFtoDLF(i);
            for (int j = 0; j < 6; j++)
            {
                for (int k = 0; k < 3; k++)
                {
                    a.cornerMultiply(CubieCube.moveCube[j]);
                    URFtoDLF_Move[i, 3 * j + k] = a.getURFtoDLF();
                }

                a.cornerMultiply(CubieCube.moveCube[j]);
            }
        }

        a = new CubieCube();
        for (short i = 0; i < N_URtoDF; i++)
        {
            a.setURtoDF(i);
            for (int j = 0; j < 6; j++)
            {
                for (int k = 0; k < 3; k++)
                {
                    a.edgeMultiply(CubieCube.moveCube[j]);
                    URtoDF_Move[i, 3 * j + k] = (short)a.getURtoDF();
                }

                a.edgeMultiply(CubieCube.moveCube[j]);
            }
        }

        a = new CubieCube();
        for (short i = 0; i < N_URtoUL; i++)
        {
            a.setURtoUL(i);
            for (int j = 0; j < 6; j++)
            {
                for (int k = 0; k < 3; k++)
                {
                    a.edgeMultiply(CubieCube.moveCube[j]);
                    URtoUL_Move[i, 3 * j + k] = a.getURtoUL();
                }

                a.edgeMultiply(CubieCube.moveCube[j]);
            }
        }

        a = new CubieCube();
        for (short i = 0; i < N_UBtoDF; i++)
        {
            a.setUBtoDF(i);
            for (int j = 0; j < 6; j++)
            {
                for (int k = 0; k < 3; k++)
                {
                    a.edgeMultiply(CubieCube.moveCube[j]);
                    UBtoDF_Move[i, 3 * j + k] = a.getUBtoDF();
                }

                a.edgeMultiply(CubieCube.moveCube[j]);
            }
        }

        for (short uRtoUL = 0; uRtoUL < 336; uRtoUL++)
        {
            for (short uBtoDF = 0; uBtoDF < 336; uBtoDF++)
            {
                MergeURtoULandUBtoDF[uRtoUL, uBtoDF] = (short)CubieCube.getURtoDF(uRtoUL, uBtoDF);
            }
        }

        for (int i = 0; i < Slice_URFtoDLF_Parity_Prun.Length; i++)
        {
            Slice_URFtoDLF_Parity_Prun[i] = -1;
        }

        int depth = 0;
        setPruning(Slice_URFtoDLF_Parity_Prun, 0, 0);
        int done = 1;
        while (done != N_SLICE2 * N_URFtoDLF * N_PARITY)
        {
            for (int i = 0; i < N_SLICE2 * N_URFtoDLF * N_PARITY; i++)
            {
                int par = i % 2;
                int urf = (i / 2) / N_SLICE2;
                int slice = (i / 2) % N_SLICE2;
                if (getPruning(Slice_URFtoDLF_Parity_Prun, i) == depth)
                {
                    for (int j = 0; j < 18; j++)
                    {
                        switch (j)
                        {
                            case 3:
                            case 5:
                            case 6:
                            case 8:
                            case 12:
                            case 14:
                            case 15:
                            case 17:
                                continue;
                            default:
                                int newSlice = FRtoBR_Move[slice, j];
                                int newURFtoDLF = URFtoDLF_Move[urf, j];
                                int newParity = parityMove[par][j];
                                if (getPruning(Slice_URFtoDLF_Parity_Prun, (N_SLICE2 * newURFtoDLF + newSlice) * 2 + newParity) == 0x0f)
                                {
                                    setPruning(Slice_URFtoDLF_Parity_Prun, (N_SLICE2 * newURFtoDLF + newSlice) * 2 + newParity, (sbyte)(depth + 1));
                                    done++;
                                }

                                break;
                        }
                    }
                }
            }

            depth++;
        }

        for (int i = 0; i < Slice_URtoDF_Parity_Prun.Length; i++)
        {
            Slice_URtoDF_Parity_Prun[i] = -1;
        }

        depth = 0;
        setPruning(Slice_URtoDF_Parity_Prun, 0, 0);
        done = 1;
        while (done != N_SLICE2 * N_URtoDF * N_PARITY)
        {
            for (int i = 0; i < N_SLICE2 * N_URtoDF * N_PARITY; i++)
            {
                int par = i % 2;
                int urdf = (i / 2) / N_SLICE2;
                int slice = (i / 2) % N_SLICE2;
                if (getPruning(Slice_URtoDF_Parity_Prun, i) == depth)
                {
                    for (int j = 0; j < 18; j++)
                    {
                        switch (j)
                        {
                            case 3:
                            case 5:
                            case 6:
                            case 8:
                            case 12:
                            case 14:
                            case 15:
                            case 17:
                                continue;
                            default:
                                int newSlice = FRtoBR_Move[slice, j];
                                int newURtoDF = URtoDF_Move[urdf, j];
                                int newParity = parityMove[par][j];
                                if (getPruning(Slice_URtoDF_Parity_Prun, (N_SLICE2 * newURtoDF + newSlice) * 2 + newParity) == 0x0f)
                                {
                                    setPruning(Slice_URtoDF_Parity_Prun, (N_SLICE2 * newURtoDF + newSlice) * 2 + newParity, (sbyte)(depth + 1));
                                    done++;
                                }

                                break;
                        }
                    }
                }
            }

            depth++;
        }

        for (int i = 0; i < Slice_Twist_Prun.Length; i++)
        {
            Slice_Twist_Prun[i] = -1;
        }

        depth = 0;
        setPruning(Slice_Twist_Prun, 0, 0);
        done = 1;
        while (done != N_SLICE1 * N_TWIST)
        {
            for (int i = 0; i < N_SLICE1 * N_TWIST; i++)
            {
                int twist = i / N_SLICE1;
                int slice = i % N_SLICE1;
                if (getPruning(Slice_Twist_Prun, i) == depth)
                {
                    for (int j = 0; j < 18; j++)
                    {
                        int newSlice = FRtoBR_Move[slice * 24, j] / 24;
                        int newTwist = twistMove[twist, j];
                        if (getPruning(Slice_Twist_Prun, N_SLICE1 * newTwist + newSlice) == 0x0f)
                        {
                            setPruning(Slice_Twist_Prun, N_SLICE1 * newTwist + newSlice, (sbyte)(depth + 1));
                            done++;
                        }
                    }
                }
            }

            depth++;
        }

        for (int i = 0; i < Slice_Flip_Prun.Length; i++)
        {
            Slice_Flip_Prun[i] = -1;
        }

        depth = 0;
        setPruning(Slice_Flip_Prun, 0, 0);
        done = 1;
        while (done != N_SLICE1 * N_FLIP)
        {
            for (int i = 0; i < N_SLICE1 * N_FLIP; i++)
            {
                int flip = i / N_SLICE1;
                int slice = i % N_SLICE1;
                if (getPruning(Slice_Flip_Prun, i) == depth)
                {
                    for (int j = 0; j < 18; j++)
                    {
                        int newSlice = FRtoBR_Move[slice * 24, j] / 24;
                        int newFlip = flipMove[flip, j];
                        if (getPruning(Slice_Flip_Prun, N_SLICE1 * newFlip + newSlice) == 0x0f)
                        {
                            setPruning(Slice_Flip_Prun, N_SLICE1 * newFlip + newSlice, (sbyte)(depth + 1));
                            done++;
                        }
                    }
                }
            }

            depth++;
        }
    }

    internal static void setPruning(sbyte[] table, int index, sbyte value)
    {
        if ((index & 1) == 0)
        {
            table[index / 2] &= unchecked((sbyte)(0xf0 | value));
        }
        else
        {
            table[index / 2] &= (sbyte)(0x0f | (value << 4));
        }
    }

    internal static sbyte getPruning(sbyte[] table, int index)
    {
        if ((index & 1) == 0)
        {
            return (sbyte)(table[index / 2] & 0x0f);
        }

        return (sbyte)((table[index / 2] & 0xf0) >> 4);
    }
}
