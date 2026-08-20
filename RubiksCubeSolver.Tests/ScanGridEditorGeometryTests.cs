using RubiksCubeSolver.Controls;
using RubiksCubeSolver.Models;
using System.Windows;

namespace RubiksCubeSolver.Tests;

public class ScanGridEditorGeometryTests
{
    static readonly IReadOnlyList<NormalizedScanRect> TwoBoxes =
    [
        new(0.10, 0.10, 0.10, 0.10),
        new(0.25, 0.10, 0.10, 0.10)
    ];

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

    [Fact]
    public void HitTestRectangleIndex_PointInGapBetweenBoxes_ReturnsMinusOne()
    {
        var imageBounds = new Rect(0, 0, 300, 300);

        var result = ScanGridEditorGeometry.HitTestRectangleIndex(
            new Point(68, 45), imageBounds, TwoBoxes);

        Assert.Equal(-1, result);
    }

    [Fact]
    public void HitTestSelectedResizeHandle_IgnoresUnselectedInvisibleHandles()
    {
        var imageBounds = new Rect(0, 0, 300, 300);
        var selectedHandle = ScanGridEditorGeometry.SelectedResizeHandleBounds(imageBounds, TwoBoxes, selectedIndex: 0);
        var unselectedHandle = ScanGridEditorGeometry.SelectedResizeHandleBounds(imageBounds, TwoBoxes, selectedIndex: 1);

        Assert.Equal(
            0,
            ScanGridEditorGeometry.HitTestSelectedResizeHandle(
                selectedHandle.BottomRight, imageBounds, TwoBoxes, selectedIndex: 0));
        Assert.Equal(
            -1,
            ScanGridEditorGeometry.HitTestSelectedResizeHandle(
                unselectedHandle.BottomRight, imageBounds, TwoBoxes, selectedIndex: 0));
    }

    [Fact]
    public void CursorKind_TracksModeAndSelectedHandleAtSamePointerLocation()
    {
        var imageBounds = new Rect(0, 0, 300, 300);
        var bodyPoint = new Point(45, 45);
        var handlePoint = ScanGridEditorGeometry.SelectedResizeHandleBounds(imageBounds, TwoBoxes, selectedIndex: 0).BottomRight;

        Assert.Equal(
            ScanGridEditorCursorKind.Move,
            ScanGridEditorGeometry.CursorKind(
                bodyPoint,
                imageBounds,
                TwoBoxes,
                ScanGridEditMode.MoveGrid,
                selectedIndex: 0));
        Assert.Equal(
            ScanGridEditorCursorKind.Arrow,
            ScanGridEditorGeometry.CursorKind(
                bodyPoint,
                imageBounds,
                TwoBoxes,
                ScanGridEditMode.ResizeBoxes,
                selectedIndex: 0));
        Assert.Equal(
            ScanGridEditorCursorKind.Resize,
            ScanGridEditorGeometry.CursorKind(
                handlePoint,
                imageBounds,
                TwoBoxes,
                ScanGridEditMode.ResizeBoxes,
                selectedIndex: 0));
    }
}
