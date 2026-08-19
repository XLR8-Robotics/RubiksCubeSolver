using RubiksCubeSolver.Models;

namespace RubiksCubeSolver.Robot.Commands.Solve;

public sealed class TurnCubeFaceCommand
{
    readonly SolveCommandSet _moves;

    public TurnCubeFaceCommand(SolveCommandSet moves) => _moves = moves;

    public Task ExecuteAsync(CubeMove move, CancellationToken cancellationToken) =>
        ExecuteSequenceAsync([move], onStep: null, cancellationToken);

    public async Task ExecuteSequenceAsync(
        IReadOnlyList<CubeMove> moves,
        Func<CubeMove, CancellationToken, Task>? onStep,
        CancellationToken cancellationToken)
    {
        var steps = ExpandDoubles(moves);
        for (int i = 0; i < steps.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var move = steps[i];
            var prime = move.QuarterTurns == 3;
            if (move.Face is CubeFace.F or CubeFace.B)
            {
                var pitchOpposite = await _moves.FrontBack.PitchFrontOntoGripperAsync(cancellationToken);
                while (true)
                {
                    await _moves.FrontBack.TurnAsync(move.Face, prime, cancellationToken);
                    if (onStep is not null)
                    {
                        await onStep(move, cancellationToken);
                    }

                    if (i + 1 >= steps.Count || steps[i + 1].Face is not CubeFace.F and not CubeFace.B)
                    {
                        break;
                    }

                    _moves.FrontBack.Log($"Peek: next {steps[i + 1]} is also F/B — turn before restoring forward");

                    i++;
                    move = steps[i];
                    prime = move.QuarterTurns == 3;
                }

                await _moves.FrontBack.RestoreForwardAsync(pitchOpposite, cancellationToken);
                continue;
            }

            await _moves.For(move.Face, prime).ExecuteAsync(cancellationToken);
            if (onStep is not null)
            {
                await onStep(move, cancellationToken);
            }
        }
    }

    static List<CubeMove> ExpandDoubles(IReadOnlyList<CubeMove> moves)
    {
        var steps = new List<CubeMove>(moves.Count);
        foreach (var move in moves)
        {
            if (move.QuarterTurns == 2)
            {
                steps.Add(new CubeMove(move.Face, 1));
                steps.Add(new CubeMove(move.Face, 1));
            }
            else
            {
                steps.Add(move);
            }
        }

        return steps;
    }
}
