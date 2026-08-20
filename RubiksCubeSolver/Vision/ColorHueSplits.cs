namespace RubiksCubeSolver.Vision;

public sealed class ColorHueSplits
{
    public const int DefaultRedOrange = 8;
    public const int DefaultOrangeYellow = 18;
    public const int DefaultYellowGreen = 38;
    public const int DefaultGreenBlue = 85;
    public const int DefaultBlueRed = 170;
    public const int DefaultWhiteSaturation = 50;

    public int RedOrange { get; set; } = DefaultRedOrange;
    public int OrangeYellow { get; set; } = DefaultOrangeYellow;
    public int YellowGreen { get; set; } = DefaultYellowGreen;
    public int GreenBlue { get; set; } = DefaultGreenBlue;
    public int BlueRed { get; set; } = DefaultBlueRed;
    public int WhiteSaturation { get; set; } = DefaultWhiteSaturation;

    public ColorHueSplits Normalized()
    {
        var redOrange = Pick(RedOrange, DefaultRedOrange);
        var orangeYellow = Pick(OrangeYellow, DefaultOrangeYellow);
        var yellowGreen = Pick(YellowGreen, DefaultYellowGreen);
        var greenBlue = Pick(GreenBlue, DefaultGreenBlue);
        var blueRed = Pick(BlueRed, DefaultBlueRed);
        var white = Pick(WhiteSaturation, DefaultWhiteSaturation);

        redOrange = Math.Clamp(redOrange, 2, 40);
        orangeYellow = Math.Clamp(orangeYellow, redOrange + 1, 55);
        yellowGreen = Math.Clamp(yellowGreen, orangeYellow + 1, 80);
        greenBlue = Math.Clamp(greenBlue, yellowGreen + 1, 155);
        blueRed = Math.Clamp(blueRed, greenBlue + 1, 178);
        white = Math.Clamp(white, 20, 120);

        return new ColorHueSplits
        {
            RedOrange = redOrange,
            OrangeYellow = orangeYellow,
            YellowGreen = yellowGreen,
            GreenBlue = greenBlue,
            BlueRed = blueRed,
            WhiteSaturation = white
        };
    }

    public static ColorHueSplits FromRedOrange(int redOrange) =>
        new ColorHueSplits { RedOrange = redOrange }.Normalized();

    static int Pick(int value, int fallback) => value == 0 ? fallback : value;
}
