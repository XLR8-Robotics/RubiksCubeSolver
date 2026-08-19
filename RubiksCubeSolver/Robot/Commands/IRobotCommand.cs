namespace RubiksCubeSolver.Robot.Commands;

public interface IRobotCommand
{
    string Name { get; }
    Task ExecuteAsync(CancellationToken cancellationToken);
}
