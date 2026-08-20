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

    /// <summary>
    /// 90° face-turn target. Prime uses the same pulse travel as Turn end, mirrored around Start.
    /// Do not use OppositeEndUs here — that value is for dual tumble and is often 496/2496.
    /// </summary>
    public double QuarterTurnTargetUs(bool prime) => prime ? MirroredEndUs() : EndUs;

    /// <summary>
    /// Opposite pulse for a dual tumble. If OppositeEndUs was saved as servo max/min (2496/256)
    /// on the wrong side of Start, use the mirrored Turn distance instead.
    /// </summary>
    public double EffectiveOppositeUs()
    {
        var turnSign = Math.Sign(EndUs - StartUs);
        if (turnSign == 0)
        {
            turnSign = 1;
        }

        var oppositeSign = Math.Sign(OppositeEndUs - StartUs);
        if (oppositeSign == 0 || oppositeSign == turnSign
            || OppositeEndUs <= 260 || OppositeEndUs >= 2490)
        {
            return MirroredEndUs();
        }

        return Math.Clamp(OppositeEndUs, 256, 2496);
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
    public const int CurrentCalibrationVersion = 15;

    public string? MaestroPort { get; set; }
    public int CameraIndex { get; set; }
    public string? CameraName { get; set; }
    public bool RotatePhotos180 { get; set; }
    public int VideoDurationMs { get; set; } = 1000;
    public int ScanFramesPerFace { get; set; } = 4;
    public int ScanFrameGapMs { get; set; } = 150;
    public int ScanHoldSqueezeUs { get; set; } = 140;
    public int TurnGripSqueezeUs { get; set; } = 50;
    /// <summary>Bottom arm stops this many µs short of In during hug (toward Out) so it doesn't stall against L/R grip.</summary>
    public int HugTopBottomBackoffUs { get; set; } = 150;
    /// <summary>Top arm moves this many µs past In during hug so it sits a bit lower on the cube.</summary>
    public int HugTopExtraUs { get; set; } = 80;
    public int TurnSpeedCap { get; set; } = 30;
    public int TurnAccelerationCap { get; set; } = 50;
    public double FaceMargin { get; set; } = 0.22;
    public double FaceOffsetX { get; set; }
    public double FaceOffsetY { get; set; }
    public double FaceSampleInset { get; set; } = 0.18;
    public bool FaceAutoDetect { get; set; }
    public bool InvertPitch { get; set; }
    public bool InvertYaw { get; set; }
    public bool TestMode { get; set; }
    public int ZenDisplaySeconds { get; set; } = 15;
    public int TumbleSqueezeUs { get; set; } = 80;
    public int PitchExtraUs { get; set; }
    public int TumbleTrimUs { get; set; } = 35;
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

            if (settings.CalibrationVersion < 9)
            {
                settings.ApplyScanGridDefaults();
            }

            if (settings.CalibrationVersion < 10)
            {
                // OppositeEndUs was often saved as 2496 (servo max). Dual tumble needs the mirrored Turn.
                settings.ApplyMatchedOppositeEnds();
            }

            if (settings.CalibrationVersion < 11)
            {
                settings.ApplyScanCaptureDefaults();
            }

            if (settings.CalibrationVersion < 12)
            {
                settings.ApplyTurnGentleDefaults();
            }

            if (settings.CalibrationVersion < 13)
            {
                settings.ApplySofterGripDefaults();
            }

            if (settings.CalibrationVersion < 14)
            {
                if (settings.HugTopBottomBackoffUs <= 0)
                {
                    settings.HugTopBottomBackoffUs = 150;
                }
            }

            if (settings.CalibrationVersion < 15)
            {
                if (settings.HugTopExtraUs <= 0)
                {
                    settings.HugTopExtraUs = 80;
                }
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

    public void ApplyScanGridDefaults()
    {
        if (FaceSampleInset <= 0)
        {
            FaceSampleInset = 0.18;
        }
    }

    public void ApplyScanCaptureDefaults()
    {
        if (ScanFramesPerFace <= 0)
        {
            ScanFramesPerFace = 4;
        }

        if (ScanFrameGapMs <= 0)
        {
            ScanFrameGapMs = 150;
        }

        if (ScanHoldSqueezeUs <= 0)
        {
            ScanHoldSqueezeUs = 140;
        }
    }

    public void ApplySofterGripDefaults()
    {
        if (ScanHoldSqueezeUs >= 180)
        {
            ScanHoldSqueezeUs = 140;
        }

        if (TurnGripSqueezeUs >= 70)
        {
            TurnGripSqueezeUs = 50;
        }

        if (TumbleSqueezeUs >= 120)
        {
            TumbleSqueezeUs = 80;
        }
    }

    public void ApplyTurnGentleDefaults()
    {
        if (TurnGripSqueezeUs <= 0)
        {
            TurnGripSqueezeUs = 50;
        }

        if (TurnSpeedCap <= 0)
        {
            TurnSpeedCap = 30;
        }

        if (TurnAccelerationCap <= 0)
        {
            TurnAccelerationCap = 50;
        }
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

    /// <summary>
    /// Writes the current scan-grid fields into settings.json, keeping every other key already on disk.
    /// </summary>
    public void MergeScanGridIntoFile()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        System.Text.Json.Nodes.JsonObject root;
        if (File.Exists(FilePath))
        {
            try
            {
                root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(FilePath)) as System.Text.Json.Nodes.JsonObject
                       ?? new System.Text.Json.Nodes.JsonObject();
            }
            catch
            {
                root = new System.Text.Json.Nodes.JsonObject();
            }
        }
        else
        {
            root = System.Text.Json.Nodes.JsonNode.Parse(
                       System.Text.Json.JsonSerializer.Serialize(this)) as System.Text.Json.Nodes.JsonObject
                   ?? new System.Text.Json.Nodes.JsonObject();
        }

        root["FaceMargin"] = FaceMargin;
        root["FaceOffsetX"] = FaceOffsetX;
        root["FaceOffsetY"] = FaceOffsetY;
        root["FaceSampleInset"] = FaceSampleInset;
        root["FaceAutoDetect"] = FaceAutoDetect;
        root["RotatePhotos180"] = RotatePhotos180;
        root["CalibrationVersion"] = CurrentCalibrationVersion;

        File.WriteAllText(FilePath, root.ToJsonString(new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }
}
