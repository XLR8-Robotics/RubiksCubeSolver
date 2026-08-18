namespace RubiksCubeSolver.Solver.Kociemba;

/// <summary>
/// Facelet positions in Kociemba order (U, R, F, D, L, B).
/// Algorithm by Herbert Kociemba; C# port adapted from the public Java package.
/// </summary>
public enum Facelet
{
    U1, U2, U3, U4, U5, U6, U7, U8, U9,
    R1, R2, R3, R4, R5, R6, R7, R8, R9,
    F1, F2, F3, F4, F5, F6, F7, F8, F9,
    D1, D2, D3, D4, D5, D6, D7, D8, D9,
    L1, L2, L3, L4, L5, L6, L7, L8, L9,
    B1, B2, B3, B4, B5, B6, B7, B8, B9
}

public enum CubeColor
{
    U, R, F, D, L, B
}

public enum Corner
{
    URF, UFL, ULB, UBR, DFR, DLF, DBL, DRB
}

public enum Edge
{
    UR, UF, UL, UB, DR, DF, DL, DB, FR, FL, BL, BR
}
