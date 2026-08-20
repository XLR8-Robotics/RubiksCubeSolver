# Interactive Scan Grid Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the operator move and resize the complete scan grid or any individual sampling box by dragging directly on the live camera feed.

**Architecture:** Store nine sampling rectangles in normalized camera coordinates and centralize all generation, validation, movement, resizing, and pixel conversion in a UI-independent layout class. `FaceScanner` and a focused WPF overlay consume the same layout, while `MainViewModel` owns the editable in-memory copy and persists it through `AppSettings`.

**Tech Stack:** C# 14, .NET 10 Windows, WPF, CommunityToolkit.Mvvm, OpenCvSharp, System.Text.Json, xUnit.

## Global Constraints

- Preserve compatibility with settings files that do not contain custom rectangles.
- Keep automatic perspective-warp detection on its existing regular-grid behavior.
- Apply custom rectangles only to the manual-grid scan path.
- Store coordinates relative to the camera frame, independent of window size and DPI.
- Display and sample the exact same rectangles.
- Do not add third-party dependencies.
- Do not commit unless the user explicitly requests a commit.

---

## File Structure

- Create `RubiksCubeSolver/Models/NormalizedScanRect.cs`: persisted normalized rectangle value.
- Create `RubiksCubeSolver/Vision/ScanGridLayout.cs`: regular layout generation, validation, transformations, and pixel conversion.
- Create `RubiksCubeSolver/Controls/ScanGridEditor.cs`: WPF rendering, letterbox-aware hit testing, and pointer interaction.
- Create `RubiksCubeSolver.Tests/ScanGridLayoutTests.cs`: layout behavior and compatibility tests.
- Create `RubiksCubeSolver.Tests/ScanGridEditorGeometryTests.cs`: rendered-image coordinate conversion tests.
- Modify `RubiksCubeSolver/Models/AppSettings.cs`: custom rectangle persistence.
- Modify `RubiksCubeSolver/Vision/FaceScanner.cs`: use the shared pixel rectangles for manual preview and sampling.
- Modify `RubiksCubeSolver/ViewModels/MainViewModel.cs`: edit state, commands, layout lifecycle, and saving.
- Modify `RubiksCubeSolver/MainWindow.xaml`: toolbar and interactive overlay.
- Modify `RubiksCubeSolver/MainWindow.xaml.cs`: route editor events to the view model.

### Task 1: Normalized Rectangle and Layout Engine

**Files:**
- Create: `RubiksCubeSolver/Models/NormalizedScanRect.cs`
- Create: `RubiksCubeSolver/Vision/ScanGridLayout.cs`
- Create: `RubiksCubeSolver.Tests/ScanGridLayoutTests.cs`

**Interfaces:**
- Produces: `NormalizedScanRect(double X, double Y, double Width, double Height)`
- Produces: `ScanGridLayout.CreateRegular(double margin, double offsetX, double offsetY, double sampleInset, int frameWidth, int frameHeight)`
- Produces: `ScanGridLayout.ValidateOrRegular(...)`
- Produces: `ScanGridLayout.ToPixelRects(...)`, `MoveAll(...)`, `ScaleAll(...)`, `MoveOne(...)`, and `ResizeOne(...)`

- [ ] **Step 1: Write failing tests for regular layout generation and pixel conversion**

Create `RubiksCubeSolver.Tests/ScanGridLayoutTests.cs`:

```csharp
using RubiksCubeSolver.Models;
using RubiksCubeSolver.Vision;

namespace RubiksCubeSolver.Tests;

public class ScanGridLayoutTests
{
    [Fact]
    public void CreateRegular_ReturnsNineRowMajorRectangles()
    {
        var layout = ScanGridLayout.CreateRegular(0.2, 0, 0, 0.1, 1280, 720);

        Assert.Equal(9, layout.Count);
        Assert.True(layout[0].X < layout[1].X);
        Assert.True(layout[0].Y < layout[3].Y);
        Assert.Equal(layout[0].Width, layout[8].Width, 8);
        Assert.Equal(layout[0].Height, layout[8].Height, 8);
    }

    [Fact]
    public void CreateRegular_WideFrame_ProducesSquarePixelSamples()
    {
        var layout = ScanGridLayout.CreateRegular(0.2, 0, 0, 0.1, 1280, 720);

        var pixels = ScanGridLayout.ToPixelRects(layout, 1280, 720);

        Assert.All(pixels, rect => Assert.InRange(Math.Abs(rect.Width - rect.Height), 0, 1));
    }

    [Fact]
    public void ToPixelRects_UsesSourceFrameDimensions()
    {
        var layout = new[]
        {
            new NormalizedScanRect(0.25, 0.20, 0.50, 0.40)
        };

        var pixels = ScanGridLayout.ToPixelRects(layout, 1280, 720);

        Assert.Single(pixels);
        Assert.Equal(new OpenCvSharp.Rect(320, 144, 640, 288), pixels[0]);
    }

    [Fact]
    public void ToPixelRects_EnforcesFourPixelMinimum()
    {
        var layout = new[] { new NormalizedScanRect(0.5, 0.5, 0.001, 0.001) };

        var pixels = ScanGridLayout.ToPixelRects(layout, 640, 480);

        Assert.Equal(4, pixels[0].Width);
        Assert.Equal(4, pixels[0].Height);
    }
}
```

- [ ] **Step 2: Run the focused tests and confirm the missing-type failure**

Run:

```powershell
dotnet test "RubiksCubeSolver.Tests\RubiksCubeSolver.Tests.csproj" --filter "FullyQualifiedName~ScanGridLayoutTests"
```

Expected: FAIL to compile because `NormalizedScanRect` and `ScanGridLayout` do not exist.

- [ ] **Step 3: Implement the rectangle model and regular layout**

Create `RubiksCubeSolver/Models/NormalizedScanRect.cs`:

```csharp
namespace RubiksCubeSolver.Models;

public sealed record NormalizedScanRect
{
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }

    public NormalizedScanRect()
    {
    }

    public NormalizedScanRect(double x, double y, double width, double height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }
}
```

Create the initial `RubiksCubeSolver/Vision/ScanGridLayout.cs`:

```csharp
using OpenCvSharp;
using RubiksCubeSolver.Models;

namespace RubiksCubeSolver.Vision;

public static class ScanGridLayout
{
    public const double MinimumNormalizedSize = 0.005;

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
}
```

- [ ] **Step 4: Run the focused tests**

Run the Task 1 command from Step 2.

Expected: PASS, 4 tests.

- [ ] **Step 5: Add failing tests for validation and transformations**

Append to `ScanGridLayoutTests`:

```csharp
[Fact]
public void ValidateOrRegular_InvalidSavedLayout_ReturnsRegularLayout()
{
    var invalid = new[] { new NormalizedScanRect(double.NaN, 0, 0.1, 0.1) };

    var result = ScanGridLayout.ValidateOrRegular(invalid, 0.22, 0, 0, 0.18, 1280, 720);

    Assert.Equal(9, result.Count);
    Assert.All(result, rect => Assert.True(ScanGridLayout.IsValid(rect)));
}

[Fact]
public void MoveAll_ClampsEntireLayoutInsideFrame()
{
    var layout = ScanGridLayout.CreateRegular(0.2, 0, 0, 0.1, 1280, 720);

    var moved = ScanGridLayout.MoveAll(layout, 1, 1);

    Assert.Equal(1, moved.Max(rect => rect.X + rect.Width), 8);
    Assert.Equal(1, moved.Max(rect => rect.Y + rect.Height), 8);
}

[Fact]
public void ScaleAll_PreservesLayoutCenterAndRelativeArrangement()
{
    var layout = ScanGridLayout.CreateRegular(0.2, 0, 0, 0.1, 1280, 720);

    var scaled = ScanGridLayout.ScaleAll(layout, 0.5);

    Assert.Equal(layout[4].X + layout[4].Width / 2,
        scaled[4].X + scaled[4].Width / 2, 8);
    Assert.True(scaled[0].Width < layout[0].Width);
}

[Fact]
public void MoveAndResizeOne_OnlyChangeSelectedRectangle()
{
    var layout = ScanGridLayout.CreateRegular(0.2, 0, 0, 0.1, 1280, 720);

    var moved = ScanGridLayout.MoveOne(layout, 4, 0.02, -0.01);
    var resized = ScanGridLayout.ResizeOne(moved, 4, 0.03, 0.02);

    Assert.Equal(layout[0], resized[0]);
    Assert.NotEqual(layout[4], resized[4]);
    Assert.True(resized[4].Width > moved[4].Width);
}
```

- [ ] **Step 6: Run tests and confirm missing-method failures**

Run the Task 1 command.

Expected: FAIL to compile because transformation and validation methods are absent.

- [ ] **Step 7: Implement validation and transformations**

Add to `ScanGridLayout`:

```csharp
public static bool IsValid(NormalizedScanRect rect) =>
    double.IsFinite(rect.X) && double.IsFinite(rect.Y) &&
    double.IsFinite(rect.Width) && double.IsFinite(rect.Height) &&
    rect.X >= 0 && rect.Y >= 0 &&
    rect.Width >= MinimumNormalizedSize &&
    rect.Height >= MinimumNormalizedSize &&
    rect.X + rect.Width <= 1 &&
    rect.Y + rect.Height <= 1;

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
```

- [ ] **Step 8: Run Task 1 tests**

Run the Task 1 command.

Expected: PASS, 8 tests.

### Task 2: Persist and Resolve Custom Layouts

**Files:**
- Modify: `RubiksCubeSolver/Models/AppSettings.cs:67-100, 373-407`
- Modify: `RubiksCubeSolver.Tests/ScanGridLayoutTests.cs`

**Interfaces:**
- Consumes: `NormalizedScanRect` and `ScanGridLayout.ValidateOrRegular`
- Produces: `AppSettings.ScanRectangles`
- Produces: `AppSettings.GetScanRectangles(int frameWidth, int frameHeight)`
- Produces: `AppSettings.ResetScanRectangles(int frameWidth, int frameHeight)`

- [ ] **Step 1: Add failing compatibility and serialization tests**

Append to `ScanGridLayoutTests`:

```csharp
[Fact]
public void AppSettings_MissingCustomLayout_GeneratesRegularLayout()
{
    var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
        """{"FaceMargin":0.2,"FaceOffsetX":0.1,"FaceOffsetY":0.0,"FaceSampleInset":0.1}""")!;

    var result = settings.GetScanRectangles(1280, 720);

    Assert.Equal(9, result.Count);
    var pixels = ScanGridLayout.ToPixelRects(result, 1280, 720);
    Assert.All(pixels, rect => Assert.InRange(Math.Abs(rect.Width - rect.Height), 0, 1));
}

[Fact]
public void AppSettings_CustomLayout_RoundTripsThroughJson()
{
    var settings = new AppSettings
    {
        ScanRectangles = ScanGridLayout.CreateRegular(0.2, 0, 0, 0.1, 1280, 720).ToList()
    };

    var json = System.Text.Json.JsonSerializer.Serialize(settings);
    var restored = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json)!;

    Assert.Equal(settings.ScanRectangles, restored.ScanRectangles);
}

[Fact]
public void ResetScanRectangles_ReplacesIndividualAdjustmentsWithRegularLayout()
{
    var settings = new AppSettings
    {
        ScanRectangles = Enumerable.Repeat(
            new NormalizedScanRect(0.1, 0.1, 0.05, 0.05), 9).ToList()
    };

    settings.ResetScanRectangles(1280, 720);

    Assert.Equal(9, settings.ScanRectangles!.Count);
    Assert.NotEqual(settings.ScanRectangles[0], settings.ScanRectangles[1]);
}
```

- [ ] **Step 2: Run tests and confirm missing-member failures**

Run the Task 1 test command.

Expected: FAIL to compile because `ScanRectangles` and `GetScanRectangles` do not exist.

- [ ] **Step 3: Add custom-layout settings**

Add `using RubiksCubeSolver.Vision;` to `AppSettings.cs`, then add beside the existing face settings:

```csharp
public List<NormalizedScanRect>? ScanRectangles { get; set; }

public IReadOnlyList<NormalizedScanRect> GetScanRectangles(int frameWidth, int frameHeight) =>
    ScanGridLayout.ValidateOrRegular(
        ScanRectangles, FaceMargin, FaceOffsetX, FaceOffsetY, FaceSampleInset,
        frameWidth, frameHeight);

public void ResetScanRectangles(int frameWidth, int frameHeight)
{
    ScanRectangles = ScanGridLayout.CreateRegular(
        FaceMargin, FaceOffsetX, FaceOffsetY, FaceSampleInset,
        frameWidth, frameHeight).ToList();
}
```

In `MergeScanGridIntoFile`, add:

```csharp
root["ScanRectangles"] = System.Text.Json.JsonSerializer.SerializeToNode(ScanRectangles);
```

- [ ] **Step 4: Run Task 2 tests**

Run the Task 1 test command.

Expected: PASS, 11 tests.

### Task 3: Use One Layout for Manual Sampling and Drawing

**Files:**
- Modify: `RubiksCubeSolver/Vision/FaceScanner.cs:14-31, 34-59, 114-160`
- Modify: `RubiksCubeSolver.Tests/ScanGridLayoutTests.cs`

**Interfaces:**
- Consumes: `AppSettings.GetScanRectangles(int frameWidth, int frameHeight)` and `ScanGridLayout.ToPixelRects(...)`
- Produces: `FaceScanner.ManualPixelRects(int width, int height, AppSettings settings)`
- Changes: manual `Sample` and `OverlayLive` paths use those rectangles.

- [ ] **Step 1: Write a failing scanner rectangle test**

Append to `ScanGridLayoutTests`:

```csharp
[Fact]
public void ManualPixelRects_UsesSavedCustomRectangles()
{
    var settings = new AppSettings
    {
        ScanRectangles =
        [
            new(0.1, 0.1, 0.1, 0.1), new(0.2, 0.1, 0.1, 0.1), new(0.3, 0.1, 0.1, 0.1),
            new(0.1, 0.2, 0.1, 0.1), new(0.2, 0.2, 0.1, 0.1), new(0.3, 0.2, 0.1, 0.1),
            new(0.1, 0.3, 0.1, 0.1), new(0.2, 0.3, 0.1, 0.1), new(0.3, 0.3, 0.1, 0.1)
        ]
    };

    var result = FaceScanner.ManualPixelRects(1000, 500, settings);

    Assert.Equal(new OpenCvSharp.Rect(100, 50, 100, 50), result[0]);
    Assert.Equal(new OpenCvSharp.Rect(300, 150, 100, 50), result[8]);
}
```

- [ ] **Step 2: Run tests and confirm the missing-method failure**

Run the Task 1 test command.

Expected: FAIL to compile because `ManualPixelRects` does not exist.

- [ ] **Step 3: Refactor manual scanning to consume exact rectangles**

Add to `FaceScanner`:

```csharp
public static Rect[] ManualPixelRects(int width, int height, AppSettings settings) =>
    ScanGridLayout.ToPixelRects(settings.GetScanRectangles(width, height), width, height);
```

Replace manual calls to `CalibratedFaceRect` plus `SampleAndDraw` in `Sample` and `OverlayLive` with:

```csharp
var preview = work.Clone();
var samples = SampleAndDraw(preview, ManualPixelRects(work.Width, work.Height, settings), draw: true);
```

Replace the rectangle-generating `SampleAndDraw` method with:

```csharp
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
```

Keep `CalibratedFaceRect` because auto-calibration tests or callers may still use it; remove only the old regular-grid drawing loops that are no longer called.

- [ ] **Step 4: Run scanner and existing tests**

Run:

```powershell
dotnet test "RubiksCubeSolver.Tests\RubiksCubeSolver.Tests.csproj"
```

Expected: PASS for all tests.

### Task 4: View-Model Layout Lifecycle and Edit Commands

**Files:**
- Modify: `RubiksCubeSolver/ViewModels/MainViewModel.cs:111-223, 448-523`
- Create: `RubiksCubeSolver/Models/ScanGridEditMode.cs`

**Interfaces:**
- Produces: `ScanGridEditMode { MoveGrid, ResizeGrid, MoveBoxes, ResizeBoxes }`
- Produces: `ObservableCollection<NormalizedScanRect> ScanRectangles`
- Produces: `ReplaceScanRectangles(...)`, `MoveScanLayout(...)`, `ScaleScanLayout(...)`, `MoveScanRectangle(...)`, and `ResizeScanRectangle(...)`

- [ ] **Step 1: Add edit mode and initialize editable rectangles**

Create `RubiksCubeSolver/Models/ScanGridEditMode.cs`:

```csharp
namespace RubiksCubeSolver.Models;

public enum ScanGridEditMode
{
    MoveGrid,
    ResizeGrid,
    MoveBoxes,
    ResizeBoxes
}
```

Add to the view model constructor after `ScanPreviewStickers` initialization:

```csharp
ScanRectangles = new ObservableCollection<NormalizedScanRect>(
    Settings.ScanRectangles is { Count: 9 }
        && Settings.ScanRectangles.All(ScanGridLayout.IsValid)
        ? Settings.ScanRectangles
        : []);
```

Add view-model members:

```csharp
public ObservableCollection<NormalizedScanRect> ScanRectangles { get; }

[ObservableProperty]
ScanGridEditMode scanGridEditMode = ScanGridEditMode.MoveGrid;

[ObservableProperty]
int selectedScanRectangle;

public void ReplaceScanRectangles(IReadOnlyList<NormalizedScanRect> rectangles)
{
    ScanRectangles.Clear();
    foreach (var rectangle in rectangles)
        ScanRectangles.Add(rectangle);
}

public void MoveScanLayout(double dx, double dy) =>
    ReplaceScanRectangles(ScanGridLayout.MoveAll(ScanRectangles, dx, dy));

public void ScaleScanLayout(double factor) =>
    ReplaceScanRectangles(ScanGridLayout.ScaleAll(ScanRectangles, factor));

public void MoveScanRectangle(int index, double dx, double dy) =>
    ReplaceScanRectangles(ScanGridLayout.MoveOne(ScanRectangles, index, dx, dy));

public void ResizeScanRectangle(int index, double dw, double dh) =>
    ReplaceScanRectangles(ScanGridLayout.ResizeOne(ScanRectangles, index, dw, dh));
```

- [ ] **Step 2: Make sliders, reset, and auto-calibrate regenerate the regular layout**

At the end of setters for `FaceMargin`, `FaceOffsetX`, `FaceOffsetY`, and `FaceSampleInset`, call:

```csharp
RegenerateRegularScanLayout();
```

Add:

```csharp
void RegenerateRegularScanLayout()
{
    if (CameraFrameWidth < 1 || CameraFrameHeight < 1 || _regeneratingScanLayout)
        return;

    _regeneratingScanLayout = true;
    try
    {
        ReplaceScanRectangles(ScanGridLayout.CreateRegular(
            Settings.FaceMargin,
            Settings.FaceOffsetX,
            Settings.FaceOffsetY,
            Settings.FaceSampleInset,
            CameraFrameWidth,
            CameraFrameHeight));
    }
    finally
    {
        _regeneratingScanLayout = false;
    }
}
```

After setting all base values in `ResetScanGrid` and successful `AutoCalibrateScanGridAsync`, call `RegenerateRegularScanLayout()` once. Add the regeneration guard field:

```csharp
bool _regeneratingScanLayout;
```

The method should return immediately when the flag is already true and reset it in `finally`.

- [ ] **Step 3: Persist the in-memory rectangles**

Change `SaveScanGrid` to:

```csharp
[RelayCommand]
public void SaveScanGrid()
{
    Settings.ScanRectangles = ScanRectangles.ToList();
    Settings.MergeScanGridIntoFile();
    AppendLog(
        $"Scan grid saved with {ScanRectangles.Count} custom boxes " +
        $"(right {Settings.FaceOffsetX:F2}, down {Settings.FaceOffsetY:F2}).");
}
```

Before every manual `FaceScanner.Sample` or `FaceScanner.OverlayLive` call, keep `Settings.ScanRectangles` synchronized without writing disk:

```csharp
Settings.ScanRectangles = ScanRectangles.ToList();
```

In `TickPreview`, set `CameraFrameWidth` and `CameraFrameHeight` from the frame, call `RegenerateRegularScanLayout()` when `ScanRectangles.Count != 9`, then synchronize settings before `OverlayLive`. In `GrabFaceAsync`, synchronize settings before `FaceScanner.Sample`. This delays first-time regular-layout generation until the real camera aspect ratio is known.

- [ ] **Step 4: Build and run tests**

Run:

```powershell
dotnet test "RubiksCubeSolver.Tests\RubiksCubeSolver.Tests.csproj"
```

Expected: build succeeds and all tests pass.

### Task 5: Letterbox-Aware Interactive Overlay

**Files:**
- Create: `RubiksCubeSolver/Controls/ScanGridEditor.cs`
- Create: `RubiksCubeSolver.Tests/ScanGridEditorGeometryTests.cs`

**Interfaces:**
- Produces: `ScanGridEditorGeometry.ImageBounds(...)`
- Produces: `ScanGridEditorGeometry.ToNormalized(...)`
- Produces: `ScanGridEditor` events `MoveGridRequested`, `ScaleGridRequested`, `MoveBoxRequested`, `ResizeBoxRequested`, and `SelectedIndexChanged`

- [ ] **Step 1: Write failing coordinate tests**

Create `RubiksCubeSolver.Tests/ScanGridEditorGeometryTests.cs`:

```csharp
using RubiksCubeSolver.Controls;
using System.Windows;

namespace RubiksCubeSolver.Tests;

public class ScanGridEditorGeometryTests
{
    [Fact]
    public void ImageBounds_WideFrameInsideSquareControl_AddsVerticalLetterbox()
    {
        var bounds = ScanGridEditorGeometry.ImageBounds(
            new Size(500, 500), new Size(1000, 500));

        Assert.Equal(new Rect(0, 125, 500, 250), bounds);
    }

    [Fact]
    public void ToNormalized_MapsRenderedImageCenterToHalf()
    {
        var bounds = new Rect(0, 125, 500, 250);

        var result = ScanGridEditorGeometry.ToNormalized(new Point(250, 250), bounds);

        Assert.Equal(new Point(0.5, 0.5), result);
    }

    [Fact]
    public void ToNormalized_PointInLetterbox_ReturnsNull()
    {
        var bounds = new Rect(0, 125, 500, 250);

        Assert.Null(ScanGridEditorGeometry.ToNormalized(new Point(250, 50), bounds));
    }
}
```

- [ ] **Step 2: Run tests and confirm missing-type failures**

Run:

```powershell
dotnet test "RubiksCubeSolver.Tests\RubiksCubeSolver.Tests.csproj" --filter "FullyQualifiedName~ScanGridEditorGeometryTests"
```

Expected: FAIL to compile because `ScanGridEditorGeometry` does not exist.

- [ ] **Step 3: Implement pure coordinate conversion**

Create the beginning of `RubiksCubeSolver/Controls/ScanGridEditor.cs`:

```csharp
using RubiksCubeSolver.Models;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace RubiksCubeSolver.Controls;

public static class ScanGridEditorGeometry
{
    public static Rect ImageBounds(Size control, Size source)
    {
        if (control.Width <= 0 || control.Height <= 0 ||
            source.Width <= 0 || source.Height <= 0)
            return Rect.Empty;

        var scale = Math.Min(control.Width / source.Width, control.Height / source.Height);
        var width = source.Width * scale;
        var height = source.Height * scale;
        return new Rect((control.Width - width) / 2, (control.Height - height) / 2, width, height);
    }

    public static Point? ToNormalized(Point point, Rect imageBounds)
    {
        if (imageBounds.IsEmpty || !imageBounds.Contains(point))
            return null;

        return new Point(
            (point.X - imageBounds.X) / imageBounds.Width,
            (point.Y - imageBounds.Y) / imageBounds.Height);
    }
}
```

- [ ] **Step 4: Run geometry tests**

Run the Task 5 command.

Expected: PASS, 3 tests.

- [ ] **Step 5: Implement the editor control**

Continue `ScanGridEditor.cs` with a sealed `FrameworkElement` that:

```csharp
public sealed class ScanGridEditor : FrameworkElement
{
    public ObservableCollection<NormalizedScanRect>? Rectangles
    {
        get => (ObservableCollection<NormalizedScanRect>?)GetValue(RectanglesProperty);
        set => SetValue(RectanglesProperty, value);
    }

    public static readonly DependencyProperty RectanglesProperty =
        DependencyProperty.Register(nameof(Rectangles),
            typeof(ObservableCollection<NormalizedScanRect>),
            typeof(ScanGridEditor),
            new FrameworkPropertyMetadata(null,
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnRectanglesChanged));

    public ScanGridEditMode EditMode
    {
        get => (ScanGridEditMode)GetValue(EditModeProperty);
        set => SetValue(EditModeProperty, value);
    }

    public static readonly DependencyProperty EditModeProperty =
        DependencyProperty.Register(nameof(EditMode), typeof(ScanGridEditMode),
            typeof(ScanGridEditor), new FrameworkPropertyMetadata(ScanGridEditMode.MoveGrid));

    public Size SourceSize { get; set; }
    public int SelectedIndex { get; private set; }

    public event Action<double, double>? MoveGridRequested;
    public event Action<double>? ScaleGridRequested;
    public event Action<int, double, double>? MoveBoxRequested;
    public event Action<int, double, double>? ResizeBoxRequested;
    public event Action<int>? SelectedIndexChanged;
}
```

Implement `OnRender` to map every normalized rectangle through `ImageBounds`, draw a transparent hit area, a yellow outline, labels 1–9, a brighter selected outline, and a bottom-right resize handle in resize modes. Implement `OnMouseLeftButtonDown`, `OnMouseMove`, and `OnMouseLeftButtonUp` with mouse capture:

- Ignore a starting point outside the rendered image bounds.
- Hit-test boxes from index 8 down to 0.
- Store the starting normalized point.
- In move modes, emit normalized `dx` and `dy`.
- In `ResizeGrid`, emit `factor = Math.Max(0.05, 1 + Math.Max(dx, dy) * 2)`.
- In `ResizeBoxes`, emit normalized `dx` and `dy` as width and height deltas.
- Update the stored point after each emitted delta to make events incremental.
- Release mouse capture on mouse-up or lost capture.
- Use `Cursors.SizeAll` for move modes and `Cursors.SizeNWSE` for resize modes.

Use `OnRectanglesChanged` to detach and attach `CollectionChanged`; call `InvalidateVisual()` from the handler so view-model replacement redraws immediately.

- [ ] **Step 6: Build and run all tests**

Run:

```powershell
dotnet test "RubiksCubeSolver.Tests\RubiksCubeSolver.Tests.csproj"
```

Expected: build succeeds and all tests pass.

### Task 6: Wire the Overlay into the Scan Grid Tab

**Files:**
- Modify: `RubiksCubeSolver/MainWindow.xaml:165-260`
- Modify: `RubiksCubeSolver/MainWindow.xaml.cs:1-32`
- Modify: `RubiksCubeSolver/ViewModels/MainViewModel.cs:120-130, 1321-1342`

**Interfaces:**
- Consumes: `ScanGridEditor`, `ScanRectangles`, and view-model edit methods.
- Produces: visible mode buttons and direct manipulation on the live feed.

- [ ] **Step 1: Register the controls namespace and add mode buttons**

Add to the root `Window` element in `MainWindow.xaml`:

```xml
xmlns:controls="clr-namespace:RubiksCubeSolver.Controls"
```

Replace the live-grid image container with:

```xml
<DockPanel>
    <UniformGrid DockPanel.Dock="Top" Rows="1" Margin="0,0,0,8">
        <RadioButton Content="Move Grid" GroupName="ScanEditMode"
                     IsChecked="True" Click="MoveGridMode_Click"/>
        <RadioButton Content="Resize Grid" GroupName="ScanEditMode"
                     Click="ResizeGridMode_Click"/>
        <RadioButton Content="Move Boxes" GroupName="ScanEditMode"
                     Click="MoveBoxesMode_Click"/>
        <RadioButton Content="Resize Boxes" GroupName="ScanEditMode"
                     Click="ResizeBoxesMode_Click"/>
    </UniformGrid>
    <TextBlock DockPanel.Dock="Top" TextWrapping="Wrap"
               Foreground="{StaticResource MutedBrush}" FontSize="12"
               Margin="0,0,0,8"
               Text="Drag directly on the camera feed. Individual boxes are numbered 1–9. Sliders, Reset, and Auto calibrate rebuild a regular layout."/>
    <Border Background="#0C0E12" CornerRadius="8" ClipToBounds="True">
        <Grid>
            <Image x:Name="ScanGridCameraImage"
                   Source="{Binding CameraImage}" Stretch="Uniform"/>
            <controls:ScanGridEditor x:Name="ScanGridEditor"
                    Rectangles="{Binding ScanRectangles}"
                    EditMode="{Binding ScanGridEditMode}"/>
        </Grid>
    </Border>
</DockPanel>
```

Keep the existing panel header, sticker preview, sliders, checkboxes, and action buttons.

- [ ] **Step 2: Connect editor events in code-behind**

Add `using RubiksCubeSolver.Models;` to `MainWindow.xaml.cs`. In the constructor after `DataContext = vm;`, add:

```csharp
ScanGridEditor.MoveGridRequested += vm.MoveScanLayout;
ScanGridEditor.ScaleGridRequested += vm.ScaleScanLayout;
ScanGridEditor.MoveBoxRequested += vm.MoveScanRectangle;
ScanGridEditor.ResizeBoxRequested += vm.ResizeScanRectangle;
ScanGridEditor.SelectedIndexChanged += index => vm.SelectedScanRectangle = index;
```

Add click handlers:

```csharp
void MoveGridMode_Click(object sender, RoutedEventArgs e) =>
    ((MainViewModel)DataContext).ScanGridEditMode = ScanGridEditMode.MoveGrid;

void ResizeGridMode_Click(object sender, RoutedEventArgs e) =>
    ((MainViewModel)DataContext).ScanGridEditMode = ScanGridEditMode.ResizeGrid;

void MoveBoxesMode_Click(object sender, RoutedEventArgs e) =>
    ((MainViewModel)DataContext).ScanGridEditMode = ScanGridEditMode.MoveBoxes;

void ResizeBoxesMode_Click(object sender, RoutedEventArgs e) =>
    ((MainViewModel)DataContext).ScanGridEditMode = ScanGridEditMode.ResizeBoxes;
```

- [ ] **Step 3: Supply camera source dimensions**

Add observable properties to `MainViewModel`:

```csharp
[ObservableProperty] int cameraFrameWidth;
[ObservableProperty] int cameraFrameHeight;
```

In `TickPreview`, after obtaining the frame and before updating `CameraImage`, assign:

```csharp
CameraFrameWidth = frame.Width;
CameraFrameHeight = frame.Height;
```

In `MainWindow.xaml.cs`, extend the existing property-change handler:

```csharp
if (e.PropertyName is nameof(MainViewModel.CameraFrameWidth)
    or nameof(MainViewModel.CameraFrameHeight))
{
    ScanGridEditor.SourceSize =
        new Size(vm.CameraFrameWidth, vm.CameraFrameHeight);
    ScanGridEditor.InvalidateVisual();
}
```

- [ ] **Step 4: Communicate auto-find behavior**

Under the auto-find checkbox, add:

```xml
<TextBlock Text="Custom dragged boxes are used for manual scans. Auto-find uses its own perspective-warped regular grid."
           TextWrapping="Wrap" FontSize="11"
           Foreground="{StaticResource MutedBrush}" Margin="20,-8,0,12"/>
```

- [ ] **Step 5: Build and run all automated tests**

Run:

```powershell
dotnet test "RubiksCubeSolver.Tests\RubiksCubeSolver.Tests.csproj"
```

Expected: build succeeds and all tests pass.

- [ ] **Step 6: Run manual acceptance checks**

Run:

```powershell
dotnet run --project "RubiksCubeSolver\RubiksCubeSolver.csproj"
```

Verify:

1. Move Grid drags all nine boxes without changing spacing.
2. Resize Grid proportionally changes the full current layout.
3. Move Boxes changes only the selected numbered box.
4. Resize Boxes changes only the selected numbered box.
5. No drag starts in letterboxed space.
6. Window resizing does not shift the boxes relative to the camera image.
7. Sliders, Reset, and Auto calibrate restore a regular 3×3 arrangement.
8. Keep these settings survives an application restart.
9. A scan preview samples the exact areas shown by the boxes.
10. Enabling Auto-find displays the manual-layout limitation.

### Task 7: Final Regression and Hygiene

**Files:**
- Inspect all files listed in this plan.
- Modify only files that fail the checks below.

**Interfaces:**
- No new interfaces.

- [ ] **Step 1: Run the complete test suite without incremental build assumptions**

Run:

```powershell
dotnet clean "RubiksCubeSolver.slnx"
dotnet test "RubiksCubeSolver.slnx"
```

Expected: clean succeeds; every test passes with zero build errors.

- [ ] **Step 2: Check edited files for IDE diagnostics**

Inspect diagnostics for:

- `RubiksCubeSolver/Models/NormalizedScanRect.cs`
- `RubiksCubeSolver/Models/ScanGridEditMode.cs`
- `RubiksCubeSolver/Vision/ScanGridLayout.cs`
- `RubiksCubeSolver/Vision/FaceScanner.cs`
- `RubiksCubeSolver/Controls/ScanGridEditor.cs`
- `RubiksCubeSolver/ViewModels/MainViewModel.cs`
- `RubiksCubeSolver/MainWindow.xaml`
- `RubiksCubeSolver/MainWindow.xaml.cs`
- `RubiksCubeSolver/Models/AppSettings.cs`
- Both new test files

Expected: no new errors or warnings.

- [ ] **Step 3: Inspect repository changes**

Run:

```powershell
git status --short
git diff --check
```

Expected: only intended source, test, and documentation files are changed; `git diff --check` reports no whitespace errors. Do not stage or commit without explicit user authorization.
