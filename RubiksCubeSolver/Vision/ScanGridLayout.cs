using OpenCvSharp;
using RubiksCubeSolver.Models;

namespace RubiksCubeSolver.Vision;

public static class ScanGridLayout
{
    public const double MinimumNormalizedSize = 0.005;
    const double BoundaryEpsilon = 1e-12;

    public static IReadOnlyList<NormalizedScanRect> CreateRegular(
        double margin, double offsetX, double offsetY, double sampleInset,
        int frameWidth, int frameHeight)
    {
        if (frameWidth < 1 || frameHeight < 1)
            throw new ArgumentOutOfRangeException(nameof(frameWidth));

        margin = Math.Clamp(margin, 0, 0.42);
        sampleInset = Math.Clamp(sampleInset, 0.04, 0.42);
        var sidePixels = Math.Min(frameWidth, frameHeight) * (1 - 2 * margin);
        var faceWidth = sidePixels / frameWidth;
        var faceHeight = sidePixels / frameHeight;
        var left = Math.Clamp(0.5 + offsetX - faceWidth / 2, 0, 1 - faceWidth);
        var top = Math.Clamp(0.5 + offsetY - faceHeight / 2, 0, 1 - faceHeight);
        var cellWidth = faceWidth / 3;
        var cellHeight = faceHeight / 3;
        var padX = cellWidth * sampleInset;
        var padY = cellHeight * sampleInset;
        var sampleWidth = cellWidth - 2 * padX;
        var sampleHeight = cellHeight - 2 * padY;
        var result = new List<NormalizedScanRect>(9);

        for (var row = 0; row < 3; row++)
        for (var column = 0; column < 3; column++)
        {
            result.Add(new NormalizedScanRect(
                left + column * cellWidth + padX,
                top + row * cellHeight + padY,
                sampleWidth,
                sampleHeight));
        }

        return result;
    }

    public static Rect[] ToPixelRects(
        IReadOnlyList<NormalizedScanRect> layout, int frameWidth, int frameHeight)
    {
        if (frameWidth < 1 || frameHeight < 1)
            throw new ArgumentOutOfRangeException(nameof(frameWidth));

        return layout.Select(rect =>
        {
            var x = Math.Clamp((int)Math.Round(rect.X * frameWidth), 0, frameWidth - 1);
            var y = Math.Clamp((int)Math.Round(rect.Y * frameHeight), 0, frameHeight - 1);
            var minimumWidth = Math.Min(4, frameWidth - x);
            var minimumHeight = Math.Min(4, frameHeight - y);
            var width = Math.Clamp(
                (int)Math.Round(rect.Width * frameWidth), minimumWidth, frameWidth - x);
            var height = Math.Clamp(
                (int)Math.Round(rect.Height * frameHeight), minimumHeight, frameHeight - y);
            return new Rect(x, y, width, height);
        }).ToArray();
    }

    public static bool IsValid(NormalizedScanRect rect) =>
        double.IsFinite(rect.X) && double.IsFinite(rect.Y) &&
        double.IsFinite(rect.Width) && double.IsFinite(rect.Height) &&
        rect.X >= -BoundaryEpsilon && rect.Y >= -BoundaryEpsilon &&
        rect.Width >= MinimumNormalizedSize &&
        rect.Height >= MinimumNormalizedSize &&
        rect.Width <= 1 + BoundaryEpsilon &&
        rect.Height <= 1 + BoundaryEpsilon &&
        rect.X + rect.Width <= 1 + BoundaryEpsilon &&
        rect.Y + rect.Height <= 1 + BoundaryEpsilon;

    public static IReadOnlyList<NormalizedScanRect> ValidateOrRegular(
        IReadOnlyList<NormalizedScanRect>? saved,
        double margin, double offsetX, double offsetY, double sampleInset,
        int frameWidth, int frameHeight)
    {
        return saved is { Count: 9 } && saved.All(IsValid)
            ? saved.Select(Clone).ToArray()
            : CreateRegular(margin, offsetX, offsetY, sampleInset, frameWidth, frameHeight);
    }

    public static IReadOnlyList<NormalizedScanRect> MoveAll(
        IReadOnlyList<NormalizedScanRect> layout, double dx, double dy)
    {
        var minX = layout.Min(rect => rect.X);
        var minY = layout.Min(rect => rect.Y);
        var maxX = layout.Max(rect => rect.X + rect.Width);
        var maxY = layout.Max(rect => rect.Y + rect.Height);
        dx = Math.Clamp(dx, -minX, 1 - maxX);
        dy = Math.Clamp(dy, -minY, 1 - maxY);
        return layout.Select(rect => rect with { X = rect.X + dx, Y = rect.Y + dy }).ToArray();
    }

    public static IReadOnlyList<NormalizedScanRect> ScaleAll(
        IReadOnlyList<NormalizedScanRect> layout, double factor)
    {
        var minX = layout.Min(rect => rect.X);
        var minY = layout.Min(rect => rect.Y);
        var maxX = layout.Max(rect => rect.X + rect.Width);
        var maxY = layout.Max(rect => rect.Y + rect.Height);
        var centerX = (minX + maxX) / 2;
        var centerY = (minY + maxY) / 2;
        var minimumFactor = Math.Max(
            MinimumNormalizedSize / layout.Min(rect => rect.Width),
            MinimumNormalizedSize / layout.Min(rect => rect.Height));
        var maximumFactor = Math.Min(
            Math.Min(centerX / Math.Max(centerX - minX, double.Epsilon),
                (1 - centerX) / Math.Max(maxX - centerX, double.Epsilon)),
            Math.Min(centerY / Math.Max(centerY - minY, double.Epsilon),
                (1 - centerY) / Math.Max(maxY - centerY, double.Epsilon)));
        if (minimumFactor > maximumFactor)
        {
            return layout.Select(Clone).ToArray();
        }

        factor = Math.Clamp(factor, minimumFactor, maximumFactor);

        return layout.Select(rect => new NormalizedScanRect(
            centerX + (rect.X - centerX) * factor,
            centerY + (rect.Y - centerY) * factor,
            rect.Width * factor,
            rect.Height * factor)).ToArray();
    }

    public static IReadOnlyList<NormalizedScanRect> MoveOne(
        IReadOnlyList<NormalizedScanRect> layout, int index, double dx, double dy)
    {
        var result = layout.Select(Clone).ToArray();
        var rect = result[index];
        result[index] = rect with
        {
            X = Math.Clamp(rect.X + dx, 0, 1 - rect.Width),
            Y = Math.Clamp(rect.Y + dy, 0, 1 - rect.Height)
        };
        return result;
    }

    public static IReadOnlyList<NormalizedScanRect> ResizeOne(
        IReadOnlyList<NormalizedScanRect> layout, int index, double dw, double dh)
    {
        var result = layout.Select(Clone).ToArray();
        var rect = result[index];
        result[index] = rect with
        {
            Width = Math.Clamp(rect.Width + dw, MinimumNormalizedSize, 1 - rect.X),
            Height = Math.Clamp(rect.Height + dh, MinimumNormalizedSize, 1 - rect.Y)
        };
        return result;
    }

    static NormalizedScanRect Clone(NormalizedScanRect rect) =>
        new(rect.X, rect.Y, rect.Width, rect.Height);
}
