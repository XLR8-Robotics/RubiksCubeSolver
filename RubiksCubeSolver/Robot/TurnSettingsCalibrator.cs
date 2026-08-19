using RubiksCubeSolver.Models;

namespace RubiksCubeSolver.Robot;

public sealed record TurnCalibrationResult(
    int TurnGripSqueezeUs,
    int TurnSpeedCap,
    int TurnAccelerationCap,
    int TumbleTrimUs,
    int TumbleSqueezeUs,
    int ScanHoldSqueezeUs,
    bool HardwareValidated,
    IReadOnlyList<string> LogLines)
{
    public string Summary =>
        $"Turn grip {TurnGripSqueezeUs} µs, speed cap {TurnSpeedCap}, accel cap {TurnAccelerationCap}, " +
        $"trim {TumbleTrimUs} µs, tumble squeeze {TumbleSqueezeUs} µs, scan hold {ScanHoldSqueezeUs} µs" +
        (HardwareValidated ? " (hardware check OK)" : " (derived from calibration only)");
}

public static class TurnSettingsCalibrator
{
    public static TurnCalibrationResult DeriveFromCalibration(AppSettings settings)
    {
        var arms = new[] { settings.RightArm, settings.TopArm, settings.LeftArm, settings.BottomArm };
        var turners = new[] { settings.RightTurner, settings.TopTurner, settings.LeftTurner, settings.BottomTurner };

        var armTravel = arms.Average(a => Math.Abs(a.InUs - a.OutUs));
        var quarterTurn = turners.Average(t =>
            Math.Min(Math.Abs(t.EndUs - t.StartUs), Math.Abs(t.EffectiveOppositeUs() - t.StartUs)));
        var minSpeed = turners.Min(t => t.Speed);
        var minAccel = turners.Min(t => t.Acceleration);

        var turnGrip = ClampInt(armTravel * 0.055, 55, 110);
        var scanHold = ClampInt(turnGrip * 2.3, 160, 250);
        var tumbleSqueeze = ClampInt(turnGrip * 1.55, 90, 160);
        var speedCap = ClampInt(minSpeed * 0.72, 24, 34);
        var accelCap = ClampInt(minAccel * 0.42, 38, 55);
        var trim = ClampInt(quarterTurn * 0.028, 28, 48);

        Apply(settings, turnGrip, speedCap, accelCap, trim, tumbleSqueeze, scanHold);

        var log = new List<string>
        {
            $"Derived from arm travel {armTravel:F0} µs and 90° turn {quarterTurn:F0} µs.",
            $"Turn grip {turnGrip}, speed cap {speedCap}, accel cap {accelCap}, trim {trim}."
        };

        return new TurnCalibrationResult(turnGrip, speedCap, accelCap, trim, tumbleSqueeze, scanHold, false, log);
    }

    public static void SoftenForStall(AppSettings settings, ICollection<string> log)
    {
        settings.TurnSpeedCap = Math.Max(18, settings.TurnSpeedCap - 4);
        settings.TurnGripSqueezeUs = Math.Max(45, settings.TurnGripSqueezeUs - 12);
        settings.TumbleTrimUs = Math.Min(60, settings.TumbleTrimUs + 4);
        log.Add(
            $"Stall detected — softened to grip {settings.TurnGripSqueezeUs}, speed {settings.TurnSpeedCap}, trim {settings.TumbleTrimUs}.");
    }

    public static TurnCalibrationResult FromSettings(AppSettings settings, bool hardwareValidated, IReadOnlyList<string> log) =>
        new(
            settings.TurnGripSqueezeUs,
            settings.TurnSpeedCap,
            settings.TurnAccelerationCap,
            settings.TumbleTrimUs,
            settings.TumbleSqueezeUs,
            settings.ScanHoldSqueezeUs,
            hardwareValidated,
            log);

    static void Apply(AppSettings settings, int turnGrip, int speedCap, int accelCap, int trim, int tumbleSqueeze, int scanHold)
    {
        settings.TurnGripSqueezeUs = turnGrip;
        settings.TurnSpeedCap = speedCap;
        settings.TurnAccelerationCap = accelCap;
        settings.TumbleTrimUs = trim;
        settings.TumbleSqueezeUs = tumbleSqueeze;
        settings.ScanHoldSqueezeUs = scanHold;
    }

    static int ClampInt(double value, int min, int max) => (int)Math.Clamp(Math.Round(value), min, max);
}
