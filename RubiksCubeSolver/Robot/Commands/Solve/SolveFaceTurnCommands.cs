using RubiksCubeSolver.Models;

namespace RubiksCubeSolver.Robot.Commands.Solve;

public sealed class GrippedFaceTurnCommand : IRobotCommand
{
    readonly GripperQuarterTurnCommand _turn;

    public GrippedFaceTurnCommand(string name, GripperQuarterTurnCommand turn)
    {
        Name = name;
        _turn = turn;
    }

    public string Name { get; }

    public Task ExecuteAsync(CancellationToken cancellationToken) =>
        _turn.ExecuteAsync(cancellationToken);
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
        await _routine.PitchFaceOntoTopAsync(_face, cancellationToken);
        await _routine.TurnAsUAsync(_prime, cancellationToken);
        await _routine.RestoreForwardAsync(cancellationToken);
    }
}
