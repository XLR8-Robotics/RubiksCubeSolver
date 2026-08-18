namespace RubiksCubeSolver.Solver.Kociemba;

public class FaceCube
{
    public CubeColor[] f = new CubeColor[54];

    public static Facelet[][] cornerFacelet =
    [
        [Facelet.U9, Facelet.R1, Facelet.F3],
        [Facelet.U7, Facelet.F1, Facelet.L3],
        [Facelet.U1, Facelet.L1, Facelet.B3],
        [Facelet.U3, Facelet.B1, Facelet.R3],
        [Facelet.D3, Facelet.F9, Facelet.R7],
        [Facelet.D1, Facelet.L9, Facelet.F7],
        [Facelet.D7, Facelet.B9, Facelet.L7],
        [Facelet.D9, Facelet.R9, Facelet.B7]
    ];

    public static Facelet[][] edgeFacelet =
    [
        [Facelet.U6, Facelet.R2],
        [Facelet.U8, Facelet.F2],
        [Facelet.U4, Facelet.L2],
        [Facelet.U2, Facelet.B2],
        [Facelet.D6, Facelet.R8],
        [Facelet.D2, Facelet.F8],
        [Facelet.D4, Facelet.L8],
        [Facelet.D8, Facelet.B8],
        [Facelet.F6, Facelet.R4],
        [Facelet.F4, Facelet.L6],
        [Facelet.B6, Facelet.L4],
        [Facelet.B4, Facelet.R6]
    ];

    public static CubeColor[][] cornerColor =
    [
        [CubeColor.U, CubeColor.R, CubeColor.F],
        [CubeColor.U, CubeColor.F, CubeColor.L],
        [CubeColor.U, CubeColor.L, CubeColor.B],
        [CubeColor.U, CubeColor.B, CubeColor.R],
        [CubeColor.D, CubeColor.F, CubeColor.R],
        [CubeColor.D, CubeColor.L, CubeColor.F],
        [CubeColor.D, CubeColor.B, CubeColor.L],
        [CubeColor.D, CubeColor.R, CubeColor.B]
    ];

    public static CubeColor[][] edgeColor =
    [
        [CubeColor.U, CubeColor.R],
        [CubeColor.U, CubeColor.F],
        [CubeColor.U, CubeColor.L],
        [CubeColor.U, CubeColor.B],
        [CubeColor.D, CubeColor.R],
        [CubeColor.D, CubeColor.F],
        [CubeColor.D, CubeColor.L],
        [CubeColor.D, CubeColor.B],
        [CubeColor.F, CubeColor.R],
        [CubeColor.F, CubeColor.L],
        [CubeColor.B, CubeColor.L],
        [CubeColor.B, CubeColor.R]
    ];

    public FaceCube()
        : this("UUUUUUUUURRRRRRRRRFFFFFFFFFDDDDDDDDDLLLLLLLLLBBBBBBBBB")
    {
    }

    public FaceCube(string cubeString)
    {
        for (int i = 0; i < cubeString.Length && i < 54; i++)
        {
            f[i] = (CubeColor)Enum.Parse(typeof(CubeColor), cubeString[i].ToString());
        }
    }

    public string to_fc_String()
    {
        var chars = new char[54];
        for (int i = 0; i < 54; i++)
        {
            chars[i] = f[i].ToString()[0];
        }

        return new string(chars);
    }

    public CubieCube toCubieCube()
    {
        var ccRet = new CubieCube();
        for (int i = 0; i < 8; i++)
        {
            ccRet.cp[i] = Corner.URF;
        }

        for (int i = 0; i < 12; i++)
        {
            ccRet.ep[i] = Edge.UR;
        }

        foreach (Corner i in Enum.GetValues<Corner>())
        {
            byte ori;
            for (ori = 0; ori < 3; ori++)
            {
                if (f[(int)cornerFacelet[(int)i][ori]] is CubeColor.U or CubeColor.D)
                {
                    break;
                }
            }

            var col1 = f[(int)cornerFacelet[(int)i][(ori + 1) % 3]];
            var col2 = f[(int)cornerFacelet[(int)i][(ori + 2) % 3]];

            foreach (Corner j in Enum.GetValues<Corner>())
            {
                if (col1 == cornerColor[(int)j][1] && col2 == cornerColor[(int)j][2])
                {
                    ccRet.cp[(int)i] = j;
                    ccRet.co[(int)i] = (byte)(ori % 3);
                    break;
                }
            }
        }

        foreach (Edge i in Enum.GetValues<Edge>())
        {
            foreach (Edge j in Enum.GetValues<Edge>())
            {
                if (f[(int)edgeFacelet[(int)i][0]] == edgeColor[(int)j][0]
                    && f[(int)edgeFacelet[(int)i][1]] == edgeColor[(int)j][1])
                {
                    ccRet.ep[(int)i] = j;
                    ccRet.eo[(int)i] = 0;
                    break;
                }

                if (f[(int)edgeFacelet[(int)i][0]] == edgeColor[(int)j][1]
                    && f[(int)edgeFacelet[(int)i][1]] == edgeColor[(int)j][0])
                {
                    ccRet.ep[(int)i] = j;
                    ccRet.eo[(int)i] = 1;
                    break;
                }
            }
        }

        return ccRet;
    }
}
