using RubiksCubeSolver.Models;
using RubiksCubeSolver.Robot.Actuation;
using RubiksCubeSolver.Robot.Commands.Scan;
using RubiksCubeSolver.Robot.Commands.Shared;

namespace RubiksCubeSolver.Robot.Commands.Solve;

public sealed class SolveCommandSet
{
    public SolveCommandSet(
        IRobotActuator robot,
        FrontBackSolveRoutine frontBack,
        PrepareForTopBottomTurnCommand prepareTopBottom,
        PrepareForLeftRightTurnCommand prepareLeftRight)
    {
        FrontBack = frontBack;
        U = new GrippedFaceTurnCommand("U", prepareTopBottom, new GripperQuarterTurnCommand(robot, RobotStation.Top, prime: false));
        UPrime = new GrippedFaceTurnCommand("U'", prepareTopBottom, new GripperQuarterTurnCommand(robot, RobotStation.Top, prime: true));
        D = new GrippedFaceTurnCommand("D", prepareTopBottom, new GripperQuarterTurnCommand(robot, RobotStation.Bottom, prime: false));
        DPrime = new GrippedFaceTurnCommand("D'", prepareTopBottom, new GripperQuarterTurnCommand(robot, RobotStation.Bottom, prime: true));
        L = new GrippedFaceTurnCommand("L", prepareLeftRight, new GripperQuarterTurnCommand(robot, RobotStation.Left, prime: false));
        LPrime = new GrippedFaceTurnCommand("L'", prepareLeftRight, new GripperQuarterTurnCommand(robot, RobotStation.Left, prime: true));
        R = new GrippedFaceTurnCommand("R", prepareLeftRight, new GripperQuarterTurnCommand(robot, RobotStation.Right, prime: false));
        RPrime = new GrippedFaceTurnCommand("R'", prepareLeftRight, new GripperQuarterTurnCommand(robot, RobotStation.Right, prime: true));
        F = new PitchedFaceTurnCommand("F", frontBack, CubeFace.F, prime: false);
        FPrime = new PitchedFaceTurnCommand("F'", frontBack, CubeFace.F, prime: true);
        B = new PitchedFaceTurnCommand("B", frontBack, CubeFace.B, prime: false);
        BPrime = new PitchedFaceTurnCommand("B'", frontBack, CubeFace.B, prime: true);
    }

    public FrontBackSolveRoutine FrontBack { get; }

    public IRobotCommand U { get; }
    public IRobotCommand UPrime { get; }
    public IRobotCommand D { get; }
    public IRobotCommand DPrime { get; }
    public IRobotCommand L { get; }
    public IRobotCommand LPrime { get; }
    public IRobotCommand R { get; }
    public IRobotCommand RPrime { get; }
    public IRobotCommand F { get; }
    public IRobotCommand FPrime { get; }
    public IRobotCommand B { get; }
    public IRobotCommand BPrime { get; }

    public IRobotCommand For(CubeFace face, bool prime) => (face, prime) switch
    {
        (CubeFace.U, false) => U,
        (CubeFace.U, true) => UPrime,
        (CubeFace.D, false) => D,
        (CubeFace.D, true) => DPrime,
        (CubeFace.L, false) => L,
        (CubeFace.L, true) => LPrime,
        (CubeFace.R, false) => R,
        (CubeFace.R, true) => RPrime,
        (CubeFace.F, false) => F,
        (CubeFace.F, true) => FPrime,
        (CubeFace.B, false) => B,
        _ => BPrime
    };
}
