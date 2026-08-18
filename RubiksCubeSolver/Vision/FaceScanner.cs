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
    public static SampledFace Sample(Mat bgr, double margin)
    {
        margin = Math.Clamp(margin, 0.05, 0.4);
        using var work = bgr.Clone();
        var quad = TryFindFaceQuad(work) ?? DefaultQuad(work.Width, work.Height, margin);
        var warped = WarpFace(work, quad, 300);
        DrawQuad(work, quad);

        var samples = new Scalar[9];
        const int size = 300;
        const int cell = size / 3;
        const int inset = 18;
        for (int r = 0; r < 3; r++)
        {
            for (int c = 0; c < 3; c++)
            {
                var x = c * cell + inset;
                var y = r * cell + inset;
                var w = cell - inset * 2;
                var roi = new Rect(x, y, w, w);
                using var patch = warped.SubMat(roi);
                samples[r * 3 + c] = Cv2.Mean(patch);
                Cv2.Rectangle(warped, roi, new Scalar(0, 255, 255), 2);
            }
        }

        var composed = new Mat();
        Cv2.HConcat([work, warped], composed);
        warped.Dispose();
        return new SampledFace { Samples = samples, Preview = composed };
    }

    static Point2f[] DefaultQuad(int width, int height, double margin)
    {
        var x0 = (float)(width * margin);
        var y0 = (float)(height * margin);
        var x1 = (float)(width * (1 - margin));
        var y1 = (float)(height * (1 - margin));
        return [new(x0, y0), new(x1, y0), new(x1, y1), new(x0, y1)];
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
            var score = area * squareness;
            if (squareness > 0.7 && score > bestScore)
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
