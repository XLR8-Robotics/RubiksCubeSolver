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

        var face = CalibratedFaceRect(work.Width, work.Height, settings);
        var preview = work.Clone();
        var samples = SampleAndDraw(preview, face, settings.FaceSampleInset, draw: true);
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

        var face = CalibratedFaceRect(preview.Width, preview.Height, settings);
        samples = SampleAndDraw(preview, face, settings.FaceSampleInset, draw: true);
        return preview;
    }

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

    static Scalar[] SampleAndDraw(Mat bgr, Rect face, double sampleInset, bool draw)
    {
        var samples = new Scalar[9];
        var inset = Math.Clamp(sampleInset, 0.04, 0.42);
        var cell = face.Width / 3.0;
        var pad = cell * inset;
        var thickness = Math.Max(2, face.Width / 90);
        var rois = new Rect[9];
        for (int r = 0; r < 3; r++)
        {
            for (int c = 0; c < 3; c++)
            {
                var x = (int)Math.Round(face.X + c * cell + pad);
                var y = (int)Math.Round(face.Y + r * cell + pad);
                var w = Math.Max(4, (int)Math.Round(cell - pad * 2));
                var roi = ClampRect(new Rect(x, y, w, w), bgr.Width, bgr.Height);
                rois[r * 3 + c] = roi;
                if (roi.Width > 1 && roi.Height > 1)
                {
                    using var patch = bgr.SubMat(roi);
                    samples[r * 3 + c] = Cv2.Mean(patch);
                }
            }
        }

        if (draw)
        {
            Cv2.Rectangle(bgr, face, new Scalar(0, 200, 255), thickness);
            for (int i = 1; i < 3; i++)
            {
                var x = (int)Math.Round(face.X + i * cell);
                var y = (int)Math.Round(face.Y + i * cell);
                Cv2.Line(bgr, new Point(x, face.Y), new Point(x, face.Y + face.Height), new Scalar(0, 180, 220), 1);
                Cv2.Line(bgr, new Point(face.X, y), new Point(face.X + face.Width, y), new Scalar(0, 180, 220), 1);
            }

            foreach (var roi in rois)
            {
                if (roi.Width > 1 && roi.Height > 1)
                {
                    Cv2.Rectangle(bgr, roi, new Scalar(0, 255, 255), thickness);
                }
            }
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
                Cv2.Rectangle(warped, roi, new Scalar(0, 255, 255), 2);
            }
        }

        using var left = new Mat();
        var leftWidth = Math.Max(1, warped.Rows * work.Cols / Math.Max(1, work.Rows));
        Cv2.Resize(work, left, new Size(leftWidth, warped.Rows));
        var composed = new Mat();
        Cv2.HConcat(left, warped, composed);
        warped.Dispose();
        return new SampledFace { Samples = samples, Preview = composed };
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

    public static Scalar[] AverageSamples(Scalar[] first, Scalar[] second)
    {
        var merged = new Scalar[9];
        for (int i = 0; i < 9; i++)
        {
            merged[i] = Average(first[i], second[i]);
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
