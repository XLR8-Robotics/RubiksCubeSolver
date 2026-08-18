namespace RubiksCubeSolver.Solver.Kociemba;

internal static class DateTimeHelper
{
    public static long CurrentUnixTimeMillis() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
