using OpenCvSharp;
using RubiksCubeSolver.Models;

namespace RubiksCubeSolver.Vision;

public sealed class SampledFace
{
    public required Scalar[] Samples { get; init; }
    public required Mat Preview { get; init; }
}

public static class FaceScanner
{
    public static SampledFace Sample(Mat bgr, AppSettings settings)
    {
        using var work = EnsureBgr(bgr);
        if (work.Empty() || work.Rows < 2 || work.Cols < 2)
        {
            throw new InvalidOperationException("Camera frame was empty or too small to scan.");
        }

        var autoQuad = settings.FaceAutoDetect ? TryFindFaceQuad(work) : null;
        if (autoQuad is not null)
        {
            return SampleWarped(work, autoQuad, settings.FaceSampleInset);
        }

        var preview = work.Clone();
        var samples = SampleAndDraw(preview, ManualPixelRects(work.Width, work.Height, settings), draw: true);
        return new SampledFace { Samples = samples, Preview = preview };
    }

    public static Mat OverlayLive(Mat bgr, AppSettings settings, out Scalar[] samples)
    {
        using var work = EnsureBgr(bgr);
        var preview = work.Clone();
        if (preview.Empty() || preview.Rows < 2 || preview.Cols < 2)
        {
            samples = new Scalar[9];
            return preview;
        }

        if (settings.FaceAutoDetect)
        {
            var quad = TryFindFaceQuad(work);
            if (quad is not null)
            {
                samples = SampleWarpedPreview(preview, quad, settings.FaceSampleInset);
                return preview;
            }

            Cv2.PutText(preview, "Auto-find: no square face detected", new Point(12, 28),
                HersheyFonts.HersheySimplex, 0.65, new Scalar(80, 80, 255), 2);
        }

        samples = SampleAndDraw(preview, ManualPixelRects(preview.Width, preview.Height, settings), draw: true);
        return preview;
    }

    /// <summary>
    /// Detect the cube face square in the camera image and derive manual grid slider values.
    /// </summary>
    public static bool TryCalibrateGrid(Mat bgr, out double margin, out double offsetX, out double offsetY)
    {
        margin = 0.22;
        offsetX = 0;
        offsetY = 0;

        using var work = EnsureBgr(bgr);
        var quad = TryFindFaceQuad(work);
        if (quad is null)
        {
            return false;
        }

        var cx = quad.Average(p => p.X);
        var cy = quad.Average(p => p.Y);
        var side = 0.0;
        for (int i = 0; i < 4; i++)
        {
            var dx = quad[(i + 1) % 4].X - quad[i].X;
            var dy = quad[(i + 1) % 4].Y - quad[i].Y;
            side += Math.Sqrt(dx * dx + dy * dy);
        }

        side /= 4;
        offsetX = Math.Clamp(cx / work.Width - 0.5, -0.35, 0.35);
        offsetY = Math.Clamp(cy / work.Height - 0.5, -0.35, 0.35);
        margin = Math.Clamp((1 - side / Math.Min(work.Width, work.Height)) / 2, 0.04, 0.42);
        return true;
    }

    public static Rect[] ManualPixelRects(int width, int height, AppSettings settings) =>
        ScanGridLayout.ToPixelRects(settings.GetScanRectangles(width, height), width, height);

    public static Rect CalibratedFaceRect(int width, int height, AppSettings settings)
    {
        var margin = Math.Clamp(settings.FaceMargin, 0, 0.42);
        var offsetX = Math.Clamp(settings.FaceOffsetX, -0.4, 0.4);
        var offsetY = Math.Clamp(settings.FaceOffsetY, -0.4, 0.4);
        var side = Math.Min(width, height) * (1 - 2 * margin);
        side = Math.Max(side, 36);
        var cx = width * (0.5 + offsetX);
        var cy = height * (0.5 + offsetY);
        var half = side / 2;
        cx = Math.Clamp(cx, half, width - half);
        cy = Math.Clamp(cy, half, height - half);
        var x = (int)Math.Round(cx - half);
        var y = (int)Math.Round(cy - half);
        var size = (int)Math.Round(side);
        size = Math.Max(30, Math.Min(size, Math.Min(width - x, height - y)));
        return new Rect(x, y, size, size);
    }

    static Scalar[] SampleAndDraw(Mat bgr, IReadOnlyList<Rect> rois, bool draw)
    {
        if (rois.Count != 9)
            throw new ArgumentException("A scan layout must contain nine rectangles.", nameof(rois));

        var samples = new Scalar[9];
        var thickness = Math.Max(2, Math.Min(bgr.Width, bgr.Height) / 90);
        for (var i = 0; i < rois.Count; i++)
        {
            var roi = ClampRect(rois[i], bgr.Width, bgr.Height);
            using var patch = bgr.SubMat(roi);
            samples[i] = Cv2.Mean(patch);
            if (draw)
                Cv2.Rectangle(bgr, roi, new Scalar(0, 255, 255), thickness);
        }

        return samples;
    }

    static Rect ClampRect(Rect roi, int width, int height)
    {
        var x = Math.Clamp(roi.X, 0, Math.Max(0, width - 1));
        var y = Math.Clamp(roi.Y, 0, Math.Max(0, height - 1));
        var w = Math.Clamp(roi.Width, 1, width - x);
        var h = Math.Clamp(roi.Height, 1, height - y);
        return new Rect(x, y, w, h);
    }

    static SampledFace SampleWarped(Mat work, Point2f[] quad, double sampleInset)
    {
        var warped = WarpFace(work, quad, 300);
        DrawQuad(work, quad);
        var samples = SampleWarpedGrid(warped, sampleInset, draw: true);
        using var left = new Mat();
        var leftWidth = Math.Max(1, warped.Rows * work.Cols / Math.Max(1, work.Rows));
        Cv2.Resize(work, left, new Size(leftWidth, warped.Rows));
        var composed = new Mat();
        Cv2.HConcat(left, warped, composed);
        warped.Dispose();
        return new SampledFace { Samples = samples, Preview = composed };
    }

    static Scalar[] SampleWarpedPreview(Mat preview, Point2f[] quad, double sampleInset)
    {
        using var warped = WarpFace(preview, quad, 300);
        var samples = SampleWarpedGrid(warped, sampleInset, draw: false);
        DrawQuad(preview, quad);
        const int thumb = 120;
        using var thumbMat = new Mat();
        Cv2.Resize(warped, thumbMat, new Size(thumb, thumb));
        var x = Math.Clamp((int)quad.Min(p => p.X), 8, Math.Max(8, preview.Width - thumb - 8));
        var y = Math.Clamp((int)quad.Min(p => p.Y), 8, Math.Max(8, preview.Height - thumb - 8));
        var roi = new Rect(x, y, Math.Min(thumb, preview.Width - x), Math.Min(thumb, preview.Height - y));
        using var resized = new Mat();
        Cv2.Resize(thumbMat, resized, roi.Size);
        resized.CopyTo(preview[roi]);
        Cv2.PutText(preview, "Auto", new Point(x + 6, y + 22),
            HersheyFonts.HersheySimplex, 0.65, new Scalar(0, 255, 255), 2);
        return samples;
    }

    static Scalar[] SampleWarpedGrid(Mat warped, double sampleInset, bool draw)
    {
        var samples = new Scalar[9];
        const int size = 300;
        const int cell = size / 3;
        var inset = (int)Math.Round(cell * Math.Clamp(sampleInset, 0.04, 0.42));
        for (int r = 0; r < 3; r++)
        {
            for (int c = 0; c < 3; c++)
            {
                var x = c * cell + inset;
                var y = r * cell + inset;
                var w = Math.Max(4, cell - inset * 2);
                var roi = new Rect(x, y, w, w);
                using var patch = warped.SubMat(roi);
                samples[r * 3 + c] = Cv2.Mean(patch);
                if (draw)
                {
                    Cv2.Rectangle(warped, roi, new Scalar(0, 255, 255), 2);
                }
            }
        }

        return samples;
    }

    static Mat EnsureBgr(Mat src)
    {
        var bgr = new Mat();
        switch (src.Channels())
        {
            case 1:
                Cv2.CvtColor(src, bgr, ColorConversionCodes.GRAY2BGR);
                break;
            case 4:
                Cv2.CvtColor(src, bgr, ColorConversionCodes.BGRA2BGR);
                break;
            default:
                bgr = src.Clone();
                break;
        }

        return bgr;
    }

    static Point2f[]? TryFindFaceQuad(Mat bgr)
    {
        using var gray = new Mat();
        Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY);
        Cv2.GaussianBlur(gray, gray, new Size(5, 5), 0);
        using var edges = new Mat();
        Cv2.Canny(gray, edges, 50, 150);
        Cv2.Dilate(edges, edges, Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3)));

        Cv2.FindContours(edges, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        var imageArea = bgr.Width * bgr.Height;
        Point[]? best = null;
        double bestScore = 0;
        foreach (var contour in contours)
        {
            var area = Cv2.ContourArea(contour);
            if (area < imageArea * 0.08 || area > imageArea * 0.92)
            {
                continue;
            }

            var peri = Cv2.ArcLength(contour, true);
            var approx = Cv2.ApproxPolyDP(contour, 0.04 * peri, true);
            if (approx.Length != 4 || !Cv2.IsContourConvex(approx))
            {
                continue;
            }

            var rect = Cv2.MinAreaRect(approx);
            var squareness = Math.Min(rect.Size.Width, rect.Size.Height) / Math.Max(rect.Size.Width, rect.Size.Height);
            var centerDist = Math.Sqrt(
                Math.Pow(rect.Center.X - bgr.Width * 0.5, 2) +
                Math.Pow(rect.Center.Y - bgr.Height * 0.5, 2));
            var score = area * squareness / (1 + centerDist * 0.002);
            if (squareness > 0.65 && score > bestScore)
            {
                bestScore = score;
                best = approx;
            }
        }

        if (best is null)
        {
            return null;
        }

        return OrderQuad(best.Select(p => new Point2f(p.X, p.Y)).ToArray());
    }

    static Point2f[] OrderQuad(Point2f[] pts)
    {
        var ordered = new Point2f[4];
        var sum = pts.Select(p => p.X + p.Y).ToArray();
        var diff = pts.Select(p => p.X - p.Y).ToArray();
        ordered[0] = pts[IndexOfMin(sum)];
        ordered[2] = pts[IndexOfMax(sum)];
        ordered[1] = pts[IndexOfMax(diff)];
        ordered[3] = pts[IndexOfMin(diff)];
        return ordered;
    }

    static int IndexOfMin(float[] values)
    {
        var i = 0;
        for (int n = 1; n < values.Length; n++)
        {
            if (values[n] < values[i])
            {
                i = n;
            }
        }

        return i;
    }

    static int IndexOfMax(float[] values)
    {
        var i = 0;
        for (int n = 1; n < values.Length; n++)
        {
            if (values[n] > values[i])
            {
                i = n;
            }
        }

        return i;
    }

    static Mat WarpFace(Mat bgr, Point2f[] quad, int size)
    {
        var dest = new Point2f[]
        {
            new(0, 0), new(size - 1, 0), new(size - 1, size - 1), new(0, size - 1)
        };
        using var matrix = Cv2.GetPerspectiveTransform(quad, dest);
        var warped = new Mat();
        Cv2.WarpPerspective(bgr, warped, matrix, new Size(size, size));
        return warped;
    }

    static void DrawQuad(Mat bgr, Point2f[] quad)
    {
        for (int i = 0; i < 4; i++)
        {
            Cv2.Line(bgr, (Point)quad[i], (Point)quad[(i + 1) % 4], new Scalar(0, 200, 255), 2);
        }
    }

    public static Scalar[] AverageSamples(Scalar[] first, Scalar[] second)
    {
        var merged = new Scalar[9];
        for (int i = 0; i < 9; i++)
        {
            merged[i] = Average(first[i], second[i]);
        }

        return merged;
    }

    public static Scalar[] AverageSamples(IReadOnlyList<Scalar[]> frames)
    {
        if (frames.Count == 0)
        {
            throw new ArgumentException("At least one frame is required.", nameof(frames));
        }

        var merged = frames[0];
        for (int i = 1; i < frames.Count; i++)
        {
            merged = AverageSamples(merged, frames[i]);
        }

        return merged;
    }

    public static Scalar[] MergeDualHold(Scalar[] topBottomHold, Scalar[] leftRightHold)
    {
        var merged = new Scalar[9];
        for (int i = 0; i < 9; i++)
        {
            merged[i] = i switch
            {
                1 or 7 => leftRightHold[i],
                3 or 5 => topBottomHold[i],
                4 => Average(topBottomHold[i], leftRightHold[i]),
                _ => HigherChroma(topBottomHold[i], leftRightHold[i])
            };
        }

        return merged;
    }

    static Scalar Average(Scalar a, Scalar b) =>
        new((a.Val0 + b.Val0) / 2, (a.Val1 + b.Val1) / 2, (a.Val2 + b.Val2) / 2);

    static Scalar HigherChroma(Scalar a, Scalar b) => Chroma(a) >= Chroma(b) ? a : b;

    static double Chroma(Scalar bgr)
    {
        var max = Math.Max(bgr.Val0, Math.Max(bgr.Val1, bgr.Val2));
        var min = Math.Min(bgr.Val0, Math.Min(bgr.Val1, bgr.Val2));
        return max - min;
    }

    public static Scalar[] RotateSamples(Scalar[] samples, int ccw90Turns)
    {
        var grid = samples.ToArray();
        ccw90Turns = ((ccw90Turns % 4) + 4) % 4;
        for (int t = 0; t < ccw90Turns; t++)
        {
            var next = new Scalar[9];
            for (int r = 0; r < 3; r++)
            {
                for (int c = 0; c < 3; c++)
                {
                    next[r * 3 + c] = grid[c * 3 + (2 - r)];
                }
            }

            grid = next;
        }

        return grid;
    }
}
