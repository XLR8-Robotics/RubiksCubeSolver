using OpenCvSharp;
using RubiksCubeSolver.Models;

namespace RubiksCubeSolver.Vision;

public readonly record struct HsvSample(int H, int S, int V);

public static class ColorClassifier
{
    public static StickerColor[] ClassifyFace(Scalar[] samples, ColorHueSplits? splits = null)
    {
        return samples.Select(sample => Guess(sample, splits)).ToArray();
    }

    public static StickerColor[] ClassifyCube(
        IReadOnlyList<Scalar[]> faces, ColorHueSplits? splits = null)
    {
        var all = new List<(int Face, int Index, Vec3d Lab)>();
        for (int f = 0; f < 6; f++)
        {
            for (int i = 0; i < 9; i++)
            {
                all.Add((f, i, ToLab(faces[f][i])));
            }
        }

        var centers = new Vec3d[6];
        for (int f = 0; f < 6; f++)
        {
            centers[f] = ToLab(faces[f][4]);
        }

        var assigned = new int[6][];
        for (int f = 0; f < 6; f++)
        {
            assigned[f] = new int[9];
        }

        foreach (var (face, index, lab) in all)
        {
            assigned[face][index] = Nearest(lab, centers);
        }

        EnforceNineEach(assigned, all, centers);

        var result = new StickerColor[54];
        var palette = new StickerColor[6];
        for (int f = 0; f < 6; f++)
        {
            palette[f] = Guess(faces[f][4], splits);
        }

        palette = DisambiguatePalette(palette, centers);

        for (int f = 0; f < 6; f++)
        {
            for (int i = 0; i < 9; i++)
            {
                result[f * 9 + i] = palette[assigned[f][i]];
            }
        }

        return result;
    }

    public static string ToKociembaString(StickerColor[] stickers)
    {
        var centers = new[]
        {
            stickers[4],
            stickers[13],
            stickers[22],
            stickers[31],
            stickers[40],
            stickers[49]
        };
        var letters = "URFDLB";
        var map = new Dictionary<StickerColor, char>();
        for (int i = 0; i < 6; i++)
        {
            map[centers[i]] = letters[i];
        }

        var chars = new char[54];
        for (int i = 0; i < 54; i++)
        {
            chars[i] = map.TryGetValue(stickers[i], out var letter) ? letter : 'U';
        }

        return new string(chars);
    }

    static StickerColor[] DisambiguatePalette(StickerColor[] palette, Vec3d[] centers)
    {
        var used = new HashSet<StickerColor>();
        var result = new StickerColor[6];
        for (int i = 0; i < 6; i++)
        {
            var color = palette[i];
            if (color == StickerColor.Unknown || used.Contains(color))
            {
                color = GuessFromLab(centers[i], used);
            }

            used.Add(color);
            result[i] = color;
        }

        return result;
    }

    static void EnforceNineEach(int[][] assigned, List<(int Face, int Index, Vec3d Lab)> all, Vec3d[] centers)
    {
        var counts = new int[6];
        foreach (var (face, index, _) in all)
        {
            counts[assigned[face][index]]++;
        }

        for (int guard = 0; guard < 54; guard++)
        {
            var over = Array.FindIndex(counts, c => c > 9);
            var under = Array.FindIndex(counts, c => c < 9);
            if (over < 0 || under < 0)
            {
                break;
            }

            var bestFace = 0;
            var bestIndex = 0;
            var bestDelta = double.MaxValue;
            foreach (var (face, index, lab) in all)
            {
                if (assigned[face][index] != over)
                {
                    continue;
                }

                var delta = Distance(lab, centers[under]) - Distance(lab, centers[over]);
                if (delta < bestDelta)
                {
                    bestDelta = delta;
                    bestFace = face;
                    bestIndex = index;
                }
            }

            assigned[bestFace][bestIndex] = under;
            counts[over]--;
            counts[under]++;
        }
    }

    static int Nearest(Vec3d lab, Vec3d[] centers)
    {
        var best = 0;
        var bestD = double.MaxValue;
        for (int i = 0; i < centers.Length; i++)
        {
            var d = Distance(lab, centers[i]);
            if (d < bestD)
            {
                bestD = d;
                best = i;
            }
        }

        return best;
    }

    static double Distance(Vec3d a, Vec3d b)
    {
        var dl = a.Item0 - b.Item0;
        var da = a.Item1 - b.Item1;
        var db = a.Item2 - b.Item2;
        return dl * dl + da * da + db * db;
    }

    static Vec3d ToLab(Scalar bgr)
    {
        using var src = new Mat(1, 1, MatType.CV_8UC3, bgr);
        using var lab = new Mat();
        Cv2.CvtColor(src, lab, ColorConversionCodes.BGR2Lab);
        var p = lab.Get<Vec3b>(0, 0);
        return new Vec3d(p.Item0, p.Item1, p.Item2);
    }

    public static HsvSample ToHsv(Scalar bgr)
    {
        using var src = new Mat(1, 1, MatType.CV_8UC3, bgr);
        using var hsv = new Mat();
        Cv2.CvtColor(src, hsv, ColorConversionCodes.BGR2HSV);
        var p = hsv.Get<Vec3b>(0, 0);
        return new HsvSample(p.Item0, p.Item1, p.Item2);
    }

    public static StickerColor Guess(Scalar bgr, int redOrangeHueSplit) =>
        Guess(bgr, ColorHueSplits.FromRedOrange(redOrangeHueSplit));

    public static StickerColor Guess(Scalar bgr, ColorHueSplits? splits = null)
    {
        var hsv = ToHsv(bgr);
        var split = (splits ?? new ColorHueSplits()).Normalized();

        if (hsv.S < split.WhiteSaturation && hsv.V > 140)
        {
            return StickerColor.White;
        }

        if (hsv.S < 40 && hsv.V < 80)
        {
            return StickerColor.Unknown;
        }

        return split.MatchHue(hsv.H);
    }

    static StickerColor GuessFromLab(Vec3d lab, HashSet<StickerColor> used)
    {
        foreach (var color in new[]
                 {
                     StickerColor.White, StickerColor.Yellow, StickerColor.Red,
                     StickerColor.Orange, StickerColor.Blue, StickerColor.Green
                 })
        {
            if (!used.Contains(color))
            {
                return color;
            }
        }

        return StickerColor.White;
    }

    public static string DescribeVerifyError(int code) => code switch
    {
        0 => "Cube is solvable.",
        -1 => "Each color must appear on exactly nine stickers.",
        -2 => "Edge cubies are incomplete or duplicated.",
        -3 => "An edge needs to be flipped (scan error).",
        -4 => "Corner cubies are incomplete or duplicated.",
        -5 => "A corner needs to be twisted (scan error).",
        -6 => "Permutation parity is invalid (scan error).",
        _ => $"Unknown verify code {code}."
    };
}
