using System.IO;

namespace RubiksCubeSolver.Models;

public sealed class GripperCalibration
{
    public byte Port { get; set; }
    public int StartUs { get; set; }
    public int EndUs { get; set; }
    public ushort Speed { get; set; } = 40;
    public ushort Acceleration { get; set; } = 110;
}

public sealed class ArmCalibration
{
    public byte Port { get; set; }
    public int InUs { get; set; }
    public int OutUs { get; set; }
    public ushort Speed { get; set; } = 50;
    public ushort Acceleration { get; set; } = 80;
}

public sealed class AppSettings
{
    public string? MaestroPort { get; set; }
    public int CameraIndex { get; set; }
    public string? CameraName { get; set; }
    public bool RotatePhotos180 { get; set; }
    public int VideoDurationMs { get; set; } = 1000;
    public double FaceMargin { get; set; } = 0.22;
    public bool InvertPitch { get; set; }
    public bool InvertYaw { get; set; }
    public bool TestMode { get; set; }
    public int SettleMs { get; set; } = 120;
    public int MovementTimeoutMs { get; set; } = 4000;

    public GripperCalibration RightTurner { get; set; } = new() { Port = 0, StartUs = 998, EndUs = 1700 };
    public ArmCalibration RightArm { get; set; } = new() { Port = 1, InUs = 1850, OutUs = 992 };
    public GripperCalibration TopTurner { get; set; } = new() { Port = 2, StartUs = 998, EndUs = 1700 };
    public ArmCalibration TopArm { get; set; } = new() { Port = 3, InUs = 2000, OutUs = 992 };
    public GripperCalibration LeftTurner { get; set; } = new() { Port = 6, StartUs = 1040, EndUs = 1700 };
    public ArmCalibration LeftArm { get; set; } = new() { Port = 7, InUs = 1800, OutUs = 992 };
    public GripperCalibration BottomTurner { get; set; } = new() { Port = 8, StartUs = 1110, EndUs = 1774 };
    public ArmCalibration BottomArm { get; set; } = new() { Port = 9, InUs = 1700, OutUs = 1040 };

    public static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RubiksCubeSolver", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                return System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch
        {
            // Fall through to defaults from the user's Maestro calibration.
        }

        return new AppSettings();
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
