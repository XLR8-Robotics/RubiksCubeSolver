using System.IO;

namespace RubiksCubeSolver.Models;

public sealed class GripperCalibration
{
    public byte Port { get; set; }
    public double StartUs { get; set; }
    public double EndUs { get; set; }
    public double OppositeEndUs { get; set; } = 496;
    public ushort Speed { get; set; } = 40;
    public ushort Acceleration { get; set; } = 110;

    public double MirroredEndUs()
    {
        var target = StartUs - (EndUs - StartUs);
        return Math.Clamp(target, 256, 2496);
    }
}

public sealed class ArmCalibration
{
    public byte Port { get; set; }
    public double InUs { get; set; }
    public double OutUs { get; set; }
    public ushort Speed { get; set; } = 50;
    public ushort Acceleration { get; set; } = 80;
}

public sealed class AppSettings
{
    public const int CurrentCalibrationVersion = 8;

    public string? MaestroPort { get; set; }
    public int CameraIndex { get; set; }
    public string? CameraName { get; set; }
    public bool RotatePhotos180 { get; set; }
    public int VideoDurationMs { get; set; } = 1000;
    public double FaceMargin { get; set; } = 0.22;
    public bool InvertPitch { get; set; }
    public bool InvertYaw { get; set; }
    public bool TestMode { get; set; }
    public int ZenDisplaySeconds { get; set; } = 15;
    public int TumbleSqueezeUs { get; set; } = 150;
    public int PitchExtraUs { get; set; }
    public int SettleMs { get; set; } = 120;
    public int MovementTimeoutMs { get; set; } = 4000;
    public int CalibrationVersion { get; set; }

    public GripperCalibration RightTurner { get; set; } = new() { Port = 0, StartUs = 1036, EndUs = 1700, OppositeEndUs = 496 };
    public ArmCalibration RightArm { get; set; } = new() { Port = 1, InUs = 1446, OutUs = 2496 };
    public GripperCalibration TopTurner { get; set; } = new() { Port = 2, StartUs = 992, EndUs = 1700, OppositeEndUs = 496 };
    public ArmCalibration TopArm { get; set; } = new() { Port = 3, InUs = 963, OutUs = 2233 };
    public GripperCalibration LeftTurner { get; set; } = new() { Port = 6, StartUs = 1026.25, EndUs = 1700, OppositeEndUs = 496 };
    public ArmCalibration LeftArm { get; set; } = new() { Port = 7, InUs = 1597.75, OutUs = 2496 };
    public GripperCalibration BottomTurner { get; set; } = new() { Port = 8, StartUs = 1079.50, EndUs = 1774, OppositeEndUs = 496 };
    public ArmCalibration BottomArm { get; set; } = new() { Port = 9, InUs = 1408, OutUs = 2496 };

    public static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RubiksCubeSolver", "settings.json");

    public static AppSettings Load()
    {
        AppSettings settings;
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            else
            {
                settings = new AppSettings();
            }
        }
        catch
        {
            settings = new AppSettings();
        }

        if (settings.CalibrationVersion < CurrentCalibrationVersion)
        {
            if (settings.CalibrationVersion < 2)
            {
                settings.ApplyCenteredHome();
            }

            if (settings.CalibrationVersion < 3)
            {
                settings.ApplyRightArmTravel();
            }

            if (settings.CalibrationVersion < 4)
            {
                settings.ApplyTopAndLeftArmTravel();
            }

            if (settings.CalibrationVersion < 5)
            {
                settings.ApplyBottomArmOpen();
            }

            if (settings.CalibrationVersion < 6)
            {
                settings.ApplyTumbleOtherWay();
            }

            if (settings.CalibrationVersion < 8)
            {
                settings.ApplyMatchedOppositeEnds();
            }

            settings.CalibrationVersion = CurrentCalibrationVersion;
            settings.Save();
        }

        return settings;
    }

    public void ApplyCenteredHome()
    {
        RightTurner.Port = 0;
        RightTurner.StartUs = 1036;
        RightTurner.OppositeEndUs = 496;
        RightArm.Port = 1;
        RightArm.InUs = 1446;
        RightArm.OutUs = 2496;
        TopTurner.Port = 2;
        TopTurner.StartUs = 992;
        TopTurner.OppositeEndUs = 496;
        TopArm.Port = 3;
        TopArm.InUs = 963;
        TopArm.OutUs = 2233;
        LeftTurner.Port = 6;
        LeftTurner.StartUs = 1026.25;
        LeftTurner.OppositeEndUs = 496;
        LeftArm.Port = 7;
        LeftArm.InUs = 1597.75;
        LeftArm.OutUs = 2496;
        BottomTurner.Port = 8;
        BottomTurner.StartUs = 1079.50;
        BottomTurner.OppositeEndUs = 496;
        BottomArm.Port = 9;
        BottomArm.InUs = 1408;
        BottomArm.OutUs = 2496;
    }

    public void ApplyRightArmTravel()
    {
        RightArm.Port = 1;
        RightArm.InUs = 1446;
        RightArm.OutUs = 2496;
    }

    public void ApplyTopAndLeftArmTravel()
    {
        TopArm.Port = 3;
        TopArm.InUs = 963;
        TopArm.OutUs = 2233;
        LeftArm.Port = 7;
        LeftArm.OutUs = 2496;
    }

    public void ApplyBottomArmOpen()
    {
        BottomArm.Port = 9;
        BottomArm.OutUs = 2496;
    }

    public void ApplyTumbleOtherWay() => ApplyMatchedOppositeEnds();

    public void ApplyMatchedOppositeEnds()
    {
        RightTurner.OppositeEndUs = RightTurner.MirroredEndUs();
        TopTurner.OppositeEndUs = TopTurner.MirroredEndUs();
        LeftTurner.OppositeEndUs = LeftTurner.MirroredEndUs();
        BottomTurner.OppositeEndUs = BottomTurner.MirroredEndUs();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var json = System.Text.Json.JsonSerializer.Serialize(this, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(FilePath, json);
    }
}
