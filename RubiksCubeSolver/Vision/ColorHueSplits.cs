using RubiksCubeSolver.Models;

namespace RubiksCubeSolver.Vision;

public sealed class ColorHueSplits
{
    public const int DefaultRedOrange = 8;
    public const int DefaultOrangeYellow = 18;
    public const int DefaultYellowGreen = 38;
    public const int DefaultGreenBlue = 85;
    public const int DefaultBlueRed = 170;
    public const int DefaultWhiteSaturation = 50;

    public int RedOrange { get; set; }
    public int OrangeYellow { get; set; }
    public int YellowGreen { get; set; }
    public int GreenBlue { get; set; }
    public int BlueRed { get; set; }
    public int WhiteSaturation { get; set; } = DefaultWhiteSaturation;

    public int RedHueFrom { get; set; }
    public int RedHueTo { get; set; }
    public int OrangeHueFrom { get; set; }
    public int OrangeHueTo { get; set; }
    public int YellowHueFrom { get; set; }
    public int YellowHueTo { get; set; }
    public int GreenHueFrom { get; set; }
    public int GreenHueTo { get; set; }
    public int BlueHueFrom { get; set; }
    public int BlueHueTo { get; set; }

    public ColorHueSplits Normalized()
    {
        if (!HasExplicitRanges())
        {
            if (RedOrange != 0 || OrangeYellow != 0 || YellowGreen != 0 || GreenBlue != 0 || BlueRed != 0)
                ApplyLegacySplits();
            else
                ApplyDefaultRanges();
        }

        RedHueFrom = ClampHue(RedHueFrom);
        RedHueTo = ClampHue(RedHueTo);
        OrangeHueFrom = ClampHue(OrangeHueFrom);
        OrangeHueTo = ClampHue(OrangeHueTo);
        YellowHueFrom = ClampHue(YellowHueFrom);
        YellowHueTo = ClampHue(YellowHueTo);
        GreenHueFrom = ClampHue(GreenHueFrom);
        GreenHueTo = ClampHue(GreenHueTo);
        BlueHueFrom = ClampHue(BlueHueFrom);
        BlueHueTo = ClampHue(BlueHueTo);
        WhiteSaturation = WhiteSaturation == 0
            ? DefaultWhiteSaturation
            : Math.Clamp(WhiteSaturation, 20, 120);

        RedOrange = OrangeHueFrom;
        OrangeYellow = YellowHueFrom;
        YellowGreen = GreenHueFrom;
        GreenBlue = BlueHueFrom;
        BlueRed = BlueHueTo;
        return this;
    }

    public static ColorHueSplits FromRedOrange(int redOrange)
    {
        var splits = new ColorHueSplits { RedOrange = redOrange };
        splits.ApplyLegacySplits();
        return splits.Normalized();
    }

    public StickerColor MatchHue(int hue)
    {
        hue = ClampHue(hue);
        if (ContainsHue(hue, OrangeHueFrom, OrangeHueTo))
            return StickerColor.Orange;
        if (ContainsHue(hue, YellowHueFrom, YellowHueTo))
            return StickerColor.Yellow;
        if (ContainsHue(hue, GreenHueFrom, GreenHueTo))
            return StickerColor.Green;
        if (ContainsHue(hue, BlueHueFrom, BlueHueTo))
            return StickerColor.Blue;
        if (ContainsHue(hue, RedHueFrom, RedHueTo))
            return StickerColor.Red;
        return StickerColor.Unknown;
    }

    public static bool ContainsHue(int hue, int from, int to)
    {
        hue = ClampHue(hue);
        from = ClampHue(from);
        to = ClampHue(to);
        return from <= to
            ? hue >= from && hue <= to
            : hue >= from || hue <= to;
    }

    public static int ClampHue(int value) => Math.Clamp(value, 0, 179);

    public bool HasExplicitRanges() =>
        RedHueFrom != 0 || RedHueTo != 0
        || OrangeHueFrom != 0 || OrangeHueTo != 0
        || YellowHueFrom != 0 || YellowHueTo != 0
        || GreenHueFrom != 0 || GreenHueTo != 0
        || BlueHueFrom != 0 || BlueHueTo != 0;

    void ApplyDefaultRanges()
    {
        RedHueFrom = 171;
        RedHueTo = 7;
        OrangeHueFrom = 8;
        OrangeHueTo = 17;
        YellowHueFrom = 18;
        YellowHueTo = 37;
        GreenHueFrom = 38;
        GreenHueTo = 84;
        BlueHueFrom = 85;
        BlueHueTo = 170;
    }

    void ApplyLegacySplits()
    {
        var redOrange = Pick(RedOrange, DefaultRedOrange);
        var orangeYellow = Pick(OrangeYellow, DefaultOrangeYellow);
        var yellowGreen = Pick(YellowGreen, DefaultYellowGreen);
        var greenBlue = Pick(GreenBlue, DefaultGreenBlue);
        var blueRed = Pick(BlueRed, DefaultBlueRed);

        RedHueFrom = ClampHue(blueRed + 1);
        RedHueTo = ClampHue(redOrange - 1);
        OrangeHueFrom = ClampHue(redOrange);
        OrangeHueTo = ClampHue(orangeYellow - 1);
        YellowHueFrom = ClampHue(orangeYellow);
        YellowHueTo = ClampHue(yellowGreen - 1);
        GreenHueFrom = ClampHue(yellowGreen);
        GreenHueTo = ClampHue(greenBlue - 1);
        BlueHueFrom = ClampHue(greenBlue);
        BlueHueTo = ClampHue(blueRed);
    }

    static int Pick(int value, int fallback) => value == 0 ? fallback : value;
}
