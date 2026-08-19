using RubiksCubeSolver.Models;
using RubiksCubeSolver.Robot.Commands.Scan;
using RubiksCubeSolver.Robot.Commands.Shared;

namespace RubiksCubeSolver.Robot.Commands.Solve;

public sealed class GrippedFaceTurnCommand : IRobotCommand
{
    readonly IRobotCommand _prepare;
    readonly GripperQuarterTurnCommand _turn;

    public GrippedFaceTurnCommand(string name, IRobotCommand prepare, GripperQuarterTurnCommand turn)
    {
        Name = name;
        _prepare = prepare;
        _turn = turn;
    }

    public string Name { get; }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await _prepare.ExecuteAsync(cancellationToken);
        await _turn.ExecuteAsync(cancellationToken);
    }
}

public sealed class PitchedFaceTurnCommand : IRobotCommand
{
    readonly FrontBackSolveRoutine _routine;
    readonly CubeFace _face;
    readonly bool _prime;

    public PitchedFaceTurnCommand(string name, FrontBackSolveRoutine routine, CubeFace face, bool prime)
    {
        Name = name;
        _routine = routine;
        _face = face;
        _prime = prime;
    }

    public string Name { get; }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var pitchOpposite = await _routine.PitchFrontOntoGripperAsync(cancellationToken);
        await _routine.TurnAsync(_face, _prime, cancellationToken);
        await _routine.RestoreForwardAsync(pitchOpposite, cancellationToken);
    }
}
