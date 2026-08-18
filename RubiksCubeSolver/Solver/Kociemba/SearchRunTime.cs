namespace RubiksCubeSolver.Solver.Kociemba;

public static class SearchRunTime
{
    static readonly int[] ax = new int[31];
    static readonly int[] po = new int[31];
    static readonly int[] flip = new int[31];
    static readonly int[] twist = new int[31];
    static readonly int[] slice = new int[31];
    static readonly int[] parity = new int[31];
    static readonly int[] URFtoDLF = new int[31];
    static readonly int[] FRtoBR = new int[31];
    static readonly int[] URtoUL = new int[31];
    static readonly int[] UBtoDF = new int[31];
    static readonly int[] URtoDF = new int[31];
    static readonly int[] minDistPhase1 = new int[31];
    static readonly int[] minDistPhase2 = new int[31];

    static string SolutionToString(int length)
    {
        var s = "";
        for (int i = 0; i < length; i++)
        {
            s += ax[i] switch
            {
                0 => "U",
                1 => "R",
                2 => "F",
                3 => "D",
                4 => "L",
                _ => "B"
            };
            s += po[i] switch
            {
                1 => " ",
                2 => "2 ",
                _ => "' "
            };
        }

        return s;
    }

    public static string Solution(string facelets, int maxDepth = 22, long timeOutMs = 8000)
    {
        if (facelets == "UUUUUUUUURRRRRRRRRFFFFFFFFFDDDDDDDDDLLLLLLLLLBBBBBBBBB")
        {
            return "";
        }

        var count = new int[6];
        try
        {
            for (int i = 0; i < 54; i++)
            {
                count[(int)Enum.Parse(typeof(CubeColor), facelets.Substring(i, 1))]++;
            }
        }
        catch
        {
            return "Error 1";
        }

        for (int i = 0; i < 6; i++)
        {
            if (count[i] != 9)
            {
                return "Error 1";
            }
        }

        var fc = new FaceCube(facelets);
        var cc = fc.toCubieCube();
        int s;
        if ((s = cc.verify()) != 0)
        {
            return "Error " + Math.Abs(s);
        }

        CoordCubeBuildTables.EnsureTables();
        var c = new CoordCubeBuildTables(cc);

        po[0] = 0;
        ax[0] = 0;
        flip[0] = c.flip;
        twist[0] = c.twist;
        parity[0] = c.parity;
        slice[0] = c.FRtoBR / 24;
        URFtoDLF[0] = c.URFtoDLF;
        FRtoBR[0] = c.FRtoBR;
        URtoUL[0] = c.URtoUL;
        UBtoDF[0] = c.UBtoDF;
        minDistPhase1[1] = 1;
        int n = 0;
        bool busy = false;
        int depthPhase1 = 1;
        long tStart = DateTimeHelper.CurrentUnixTimeMillis();

        do
        {
            do
            {
                if (depthPhase1 - n > minDistPhase1[n + 1] && !busy)
                {
                    if (ax[n] is 0 or 3)
                    {
                        ax[++n] = 1;
                    }
                    else
                    {
                        ax[++n] = 0;
                    }

                    po[n] = 1;
                }
                else if (++po[n] > 3)
                {
                    do
                    {
                        if (++ax[n] > 5)
                        {
                            if (DateTimeHelper.CurrentUnixTimeMillis() - tStart > timeOutMs)
                            {
                                return "Error 8";
                            }

                            if (n == 0)
                            {
                                if (depthPhase1 >= maxDepth)
                                {
                                    return "Error 7";
                                }

                                depthPhase1++;
                                ax[n] = 0;
                                po[n] = 1;
                                busy = false;
                                break;
                            }

                            n--;
                            busy = true;
                            break;
                        }

                        po[n] = 1;
                        busy = false;
                    } while (n != 0 && (ax[n - 1] == ax[n] || ax[n - 1] - 3 == ax[n]));
                }
                else
                {
                    busy = false;
                }
            } while (busy);

            int mv = 3 * ax[n] + po[n] - 1;
            flip[n + 1] = CoordCubeBuildTables.flipMove[flip[n], mv];
            twist[n + 1] = CoordCubeBuildTables.twistMove[twist[n], mv];
            slice[n + 1] = CoordCubeBuildTables.FRtoBR_Move[slice[n] * 24, mv] / 24;
            minDistPhase1[n + 1] = Math.Max(
                CoordCubeBuildTables.getPruning(CoordCubeBuildTables.Slice_Flip_Prun, CoordCubeBuildTables.N_SLICE1 * flip[n + 1] + slice[n + 1]),
                CoordCubeBuildTables.getPruning(CoordCubeBuildTables.Slice_Twist_Prun, CoordCubeBuildTables.N_SLICE1 * twist[n + 1] + slice[n + 1]));

            if (minDistPhase1[n + 1] == 0 && n >= depthPhase1 - 5)
            {
                minDistPhase1[n + 1] = 10;
                if (n == depthPhase1 - 1 && (s = TotalDepth(depthPhase1, maxDepth)) >= 0)
                {
                    if (s == depthPhase1 || (ax[depthPhase1 - 1] != ax[depthPhase1] && ax[depthPhase1 - 1] != ax[depthPhase1] + 3))
                    {
                        return SolutionToString(s);
                    }
                }
            }
        } while (true);
    }

    static int TotalDepth(int depthPhase1, int maxDepth)
    {
        int maxDepthPhase2 = Math.Min(10, maxDepth - depthPhase1);
        for (int i = 0; i < depthPhase1; i++)
        {
            int mv = 3 * ax[i] + po[i] - 1;
            URFtoDLF[i + 1] = CoordCubeBuildTables.URFtoDLF_Move[URFtoDLF[i], mv];
            FRtoBR[i + 1] = CoordCubeBuildTables.FRtoBR_Move[FRtoBR[i], mv];
            parity[i + 1] = CoordCubeBuildTables.parityMove[parity[i]][mv];
        }

        int d1 = CoordCubeBuildTables.getPruning(
            CoordCubeBuildTables.Slice_URFtoDLF_Parity_Prun,
            (CoordCubeBuildTables.N_SLICE2 * URFtoDLF[depthPhase1] + FRtoBR[depthPhase1]) * 2 + parity[depthPhase1]);
        if (d1 > maxDepthPhase2)
        {
            return -1;
        }

        for (int i = 0; i < depthPhase1; i++)
        {
            int mv = 3 * ax[i] + po[i] - 1;
            URtoUL[i + 1] = CoordCubeBuildTables.URtoUL_Move[URtoUL[i], mv];
            UBtoDF[i + 1] = CoordCubeBuildTables.UBtoDF_Move[UBtoDF[i], mv];
        }

        URtoDF[depthPhase1] = CoordCubeBuildTables.MergeURtoULandUBtoDF[URtoUL[depthPhase1], UBtoDF[depthPhase1]];

        int d2 = CoordCubeBuildTables.getPruning(
            CoordCubeBuildTables.Slice_URtoDF_Parity_Prun,
            (CoordCubeBuildTables.N_SLICE2 * URtoDF[depthPhase1] + FRtoBR[depthPhase1]) * 2 + parity[depthPhase1]);
        if (d2 > maxDepthPhase2)
        {
            return -1;
        }

        if ((minDistPhase2[depthPhase1] = Math.Max(d1, d2)) == 0)
        {
            return depthPhase1;
        }

        int depthPhase2 = 1;
        int n = depthPhase1;
        bool busy = false;
        po[depthPhase1] = 0;
        ax[depthPhase1] = 0;
        minDistPhase2[n + 1] = 1;

        do
        {
            do
            {
                if (depthPhase1 + depthPhase2 - n > minDistPhase2[n + 1] && !busy)
                {
                    if (ax[n] is 0 or 3)
                    {
                        ax[++n] = 1;
                        po[n] = 2;
                    }
                    else
                    {
                        ax[++n] = 0;
                        po[n] = 1;
                    }
                }
                else if ((ax[n] is 0 or 3) ? ++po[n] > 3 : (po[n] = po[n] + 2) > 3)
                {
                    do
                    {
                        if (++ax[n] > 5)
                        {
                            if (n == depthPhase1)
                            {
                                if (depthPhase2 >= maxDepthPhase2)
                                {
                                    return -1;
                                }

                                depthPhase2++;
                                ax[n] = 0;
                                po[n] = 1;
                                busy = false;
                                break;
                            }

                            n--;
                            busy = true;
                            break;
                        }

                        po[n] = ax[n] is 0 or 3 ? 1 : 2;
                        busy = false;
                    } while (n != depthPhase1 && (ax[n - 1] == ax[n] || ax[n - 1] - 3 == ax[n]));
                }
                else
                {
                    busy = false;
                }
            } while (busy);

            int mv = 3 * ax[n] + po[n] - 1;
            URFtoDLF[n + 1] = CoordCubeBuildTables.URFtoDLF_Move[URFtoDLF[n], mv];
            FRtoBR[n + 1] = CoordCubeBuildTables.FRtoBR_Move[FRtoBR[n], mv];
            parity[n + 1] = CoordCubeBuildTables.parityMove[parity[n]][mv];
            URtoDF[n + 1] = CoordCubeBuildTables.URtoDF_Move[URtoDF[n], mv];
            minDistPhase2[n + 1] = Math.Max(
                CoordCubeBuildTables.getPruning(CoordCubeBuildTables.Slice_URtoDF_Parity_Prun, (CoordCubeBuildTables.N_SLICE2 * URtoDF[n + 1] + FRtoBR[n + 1]) * 2 + parity[n + 1]),
                CoordCubeBuildTables.getPruning(CoordCubeBuildTables.Slice_URFtoDLF_Parity_Prun, (CoordCubeBuildTables.N_SLICE2 * URFtoDLF[n + 1] + FRtoBR[n + 1]) * 2 + parity[n + 1]));
        } while (minDistPhase2[n + 1] != 0);

        return depthPhase1 + depthPhase2;
    }
}
