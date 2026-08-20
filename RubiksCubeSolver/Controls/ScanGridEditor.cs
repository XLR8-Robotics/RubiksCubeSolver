using RubiksCubeSolver.Models;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace RubiksCubeSolver.Controls;

public enum ScanGridEditorCursorKind
{
    Arrow,
    Move,
    Resize
}

public static class ScanGridEditorGeometry
{
    public static Rect ImageBounds(Size control, Size source)
    {
        if (control.Width <= 0 || control.Height <= 0 ||
            source.Width <= 0 || source.Height <= 0)
        {
            return Rect.Empty;
        }

        var scale = Math.Min(control.Width / source.Width, control.Height / source.Height);
        var width = source.Width * scale;
        var height = source.Height * scale;
        return new Rect((control.Width - width) / 2, (control.Height - height) / 2, width, height);
    }

    public static Point? ToNormalized(Point point, Rect imageBounds)
    {
        if (imageBounds.IsEmpty || !imageBounds.Contains(point))
        {
            return null;
        }

        return new Point(
            (point.X - imageBounds.X) / imageBounds.Width,
            (point.Y - imageBounds.Y) / imageBounds.Height);
    }

    public static IReadOnlyList<Rect> DisplayRects(
        Rect imageBounds,
        IReadOnlyList<NormalizedScanRect>? rectangles)
    {
        if (imageBounds.IsEmpty || rectangles is null || rectangles.Count == 0)
        {
            return [];
        }

        var result = new Rect[rectangles.Count];
        for (var index = 0; index < rectangles.Count; index++)
        {
            var rect = rectangles[index];
            result[index] = new Rect(
                imageBounds.X + rect.X * imageBounds.Width,
                imageBounds.Y + rect.Y * imageBounds.Height,
                rect.Width * imageBounds.Width,
                rect.Height * imageBounds.Height);
        }

        return result;
    }

    public static int HitTestRectangleIndex(
        Point point,
        Rect imageBounds,
        IReadOnlyList<NormalizedScanRect>? rectangles)
    {
        var displayRects = DisplayRects(imageBounds, rectangles);
        for (var index = displayRects.Count - 1; index >= 0; index--)
        {
            if (displayRects[index].Contains(point))
            {
                return index;
            }
        }

        return -1;
    }

    public static Rect LayoutBounds(Rect imageBounds, IReadOnlyList<NormalizedScanRect>? rectangles)
    {
        var displayRects = DisplayRects(imageBounds, rectangles);
        if (displayRects.Count == 0)
        {
            return Rect.Empty;
        }

        var union = displayRects[0];
        for (var index = 1; index < displayRects.Count; index++)
        {
            union.Union(displayRects[index]);
        }

        return union;
    }

    public static Rect ResizeHandleBounds(Rect rect)
    {
        if (rect.IsEmpty)
        {
            return Rect.Empty;
        }

        var size = Math.Clamp(Math.Min(rect.Width, rect.Height) * 0.22, 10, 22);
        return new Rect(rect.Right - size, rect.Bottom - size, size, size);
    }

    public static Rect SelectedResizeHandleBounds(
        Rect imageBounds,
        IReadOnlyList<NormalizedScanRect>? rectangles,
        int selectedIndex)
    {
        var displayRects = DisplayRects(imageBounds, rectangles);
        if (selectedIndex < 0 || selectedIndex >= displayRects.Count)
        {
            return Rect.Empty;
        }

        return ResizeHandleBounds(displayRects[selectedIndex]);
    }

    public static int HitTestSelectedResizeHandle(
        Point point,
        Rect imageBounds,
        IReadOnlyList<NormalizedScanRect>? rectangles,
        int selectedIndex)
    {
        var handle = SelectedResizeHandleBounds(imageBounds, rectangles, selectedIndex);
        return handle.Contains(point) ? selectedIndex : -1;
    }

    public static ScanGridEditorCursorKind CursorKind(
        Point point,
        Rect imageBounds,
        IReadOnlyList<NormalizedScanRect>? rectangles,
        ScanGridEditMode editMode,
        int selectedIndex)
    {
        if (imageBounds.IsEmpty || !imageBounds.Contains(point))
        {
            return ScanGridEditorCursorKind.Arrow;
        }

        return editMode switch
        {
            ScanGridEditMode.MoveGrid => HitTestRectangleIndex(point, imageBounds, rectangles) >= 0
                ? ScanGridEditorCursorKind.Move
                : ScanGridEditorCursorKind.Arrow,
            ScanGridEditMode.ResizeGrid => ResizeHandleBounds(LayoutBounds(imageBounds, rectangles)).Contains(point)
                ? ScanGridEditorCursorKind.Resize
                : ScanGridEditorCursorKind.Arrow,
            ScanGridEditMode.MoveBoxes => HitTestRectangleIndex(point, imageBounds, rectangles) >= 0
                ? ScanGridEditorCursorKind.Move
                : ScanGridEditorCursorKind.Arrow,
            ScanGridEditMode.ResizeBoxes => HitTestSelectedResizeHandle(point, imageBounds, rectangles, selectedIndex) >= 0
                ? ScanGridEditorCursorKind.Resize
                : ScanGridEditorCursorKind.Arrow,
            _ => ScanGridEditorCursorKind.Arrow
        };
    }
}

public sealed class ScanGridEditor : FrameworkElement
{
    readonly Pen _outlinePen = CreateFrozenPen(Color.FromArgb(0xE0, 0xFF, 0xD8, 0x35), 2);
    readonly Pen _selectedPen = CreateFrozenPen(Color.FromArgb(0xFF, 0xFF, 0xFF, 0x80), 3);
    readonly Pen _handlePen = CreateFrozenPen(Color.FromArgb(0xFF, 0xFF, 0xF7, 0xA8), 1.5);
    readonly Brush _labelBackground = CreateFrozenBrush(Color.FromArgb(0xCC, 0x12, 0x14, 0x18));
    readonly Brush _labelForeground = Brushes.White;
    readonly Brush _handleBrush = CreateFrozenBrush(Color.FromArgb(0xF0, 0xFF, 0xD8, 0x35));

    ObservableCollection<NormalizedScanRect>? _subscribedRectangles;
    bool _isLoaded;
    Point? _dragPoint;
    DragOperation _dragOperation;
    int _dragIndex = -1;

    public ObservableCollection<NormalizedScanRect>? Rectangles
    {
        get => (ObservableCollection<NormalizedScanRect>?)GetValue(RectanglesProperty);
        set => SetValue(RectanglesProperty, value);
    }

    public static readonly DependencyProperty RectanglesProperty =
        DependencyProperty.Register(
            nameof(Rectangles),
            typeof(ObservableCollection<NormalizedScanRect>),
            typeof(ScanGridEditor),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnRectanglesChanged));

    public ScanGridEditMode EditMode
    {
        get => (ScanGridEditMode)GetValue(EditModeProperty);
        set => SetValue(EditModeProperty, value);
    }

    public static readonly DependencyProperty EditModeProperty =
        DependencyProperty.Register(
            nameof(EditMode),
            typeof(ScanGridEditMode),
            typeof(ScanGridEditor),
            new FrameworkPropertyMetadata(
                ScanGridEditMode.MoveGrid,
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnEditModeChanged));

    public Size SourceSize
    {
        get => _sourceSize;
        set
        {
            if (_sourceSize == value)
            {
                return;
            }

            _sourceSize = value;
            InvalidateVisual();
            RefreshCursorFromCurrentMousePosition();
        }
    }

    public int SelectedIndex { get; private set; }

    public event Action<double, double>? MoveGridRequested;
    public event Action<double>? ScaleGridRequested;
    public event Action<int, double, double>? MoveBoxRequested;
    public event Action<int, double, double>? ResizeBoxRequested;

    Size _sourceSize;

    public ScanGridEditor()
    {
        Loaded += (_, _) => HandleLoaded();
        Unloaded += (_, _) => HandleUnloaded();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var imageBounds = CurrentImageBounds;
        if (imageBounds.IsEmpty)
        {
            return;
        }

        drawingContext.DrawRectangle(Brushes.Transparent, null, imageBounds);

        var displayRects = ScanGridEditorGeometry.DisplayRects(imageBounds, Rectangles);
        if (displayRects.Count == 0)
        {
            return;
        }

        for (var index = 0; index < displayRects.Count; index++)
        {
            var rect = displayRects[index];
            var isSelected = index == SelectedIndex;
            drawingContext.DrawRectangle(null, isSelected ? _selectedPen : _outlinePen, rect);
            DrawLabel(drawingContext, rect, index + 1, isSelected);
        }

        if (EditMode == ScanGridEditMode.ResizeGrid)
        {
            var layoutBounds = ScanGridEditorGeometry.LayoutBounds(imageBounds, Rectangles);
            if (!layoutBounds.IsEmpty)
            {
                DrawHandle(drawingContext, layoutBounds);
            }
        }
        else if (EditMode == ScanGridEditMode.ResizeBoxes &&
                 SelectedIndex >= 0 &&
                 SelectedIndex < displayRects.Count)
        {
            DrawHandle(drawingContext, displayRects[SelectedIndex]);
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        var imageBounds = CurrentImageBounds;
        var pointer = e.GetPosition(this);
        var normalized = ScanGridEditorGeometry.ToNormalized(pointer, imageBounds);
        if (normalized is null)
        {
            return;
        }

        var displayRects = ScanGridEditorGeometry.DisplayRects(imageBounds, Rectangles);
        if (displayRects.Count == 0)
        {
            return;
        }

        switch (EditMode)
        {
            case ScanGridEditMode.MoveGrid:
                var moveGridIndex = ScanGridEditorGeometry.HitTestRectangleIndex(pointer, imageBounds, Rectangles);
                if (moveGridIndex < 0)
                {
                    return;
                }

                SetSelectedIndex(moveGridIndex);
                BeginDrag(DragOperation.MoveGrid, -1, normalized.Value, e);
                break;

            case ScanGridEditMode.ResizeGrid:
                var gridBounds = ScanGridEditorGeometry.LayoutBounds(imageBounds, Rectangles);
                if (gridBounds.IsEmpty || !ScanGridEditorGeometry.ResizeHandleBounds(gridBounds).Contains(pointer))
                {
                    return;
                }

                BeginDrag(DragOperation.ResizeGrid, -1, normalized.Value, e);
                break;

            case ScanGridEditMode.MoveBoxes:
                var moveIndex = ScanGridEditorGeometry.HitTestRectangleIndex(pointer, imageBounds, Rectangles);
                if (moveIndex < 0)
                {
                    return;
                }

                SetSelectedIndex(moveIndex);
                BeginDrag(DragOperation.MoveBox, moveIndex, normalized.Value, e);
                break;

            case ScanGridEditMode.ResizeBoxes:
                var resizeHandleIndex = ScanGridEditorGeometry.HitTestSelectedResizeHandle(
                    pointer,
                    imageBounds,
                    Rectangles,
                    SelectedIndex);
                if (resizeHandleIndex >= 0)
                {
                    BeginDrag(DragOperation.ResizeBox, resizeHandleIndex, normalized.Value, e);
                    break;
                }

                var selectionIndex = ScanGridEditorGeometry.HitTestRectangleIndex(pointer, imageBounds, Rectangles);
                if (selectionIndex < 0)
                {
                    return;
                }

                SetSelectedIndex(selectionIndex);
                Cursor = GetCursor(pointer, imageBounds);
                e.Handled = true;
                break;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var imageBounds = CurrentImageBounds;
        var pointer = e.GetPosition(this);

        if (_dragPoint is null)
        {
            Cursor = GetCursor(pointer, imageBounds);
            return;
        }

        var normalized = ToClampedNormalized(pointer, imageBounds);
        if (normalized is null)
        {
            return;
        }

        var start = _dragPoint.Value;
        var dx = normalized.Value.X - start.X;
        var dy = normalized.Value.Y - start.Y;

        if (Math.Abs(dx) < double.Epsilon && Math.Abs(dy) < double.Epsilon)
        {
            return;
        }

        switch (_dragOperation)
        {
            case DragOperation.MoveGrid:
                MoveGridRequested?.Invoke(dx, dy);
                break;

            case DragOperation.ResizeGrid:
                var factor = Math.Max(0.05, 1 + Math.Max(dx, dy) * 2);
                ScaleGridRequested?.Invoke(factor);
                break;

            case DragOperation.MoveBox:
                if (_dragIndex >= 0)
                {
                    MoveBoxRequested?.Invoke(_dragIndex, dx, dy);
                }

                break;

            case DragOperation.ResizeBox:
                if (_dragIndex >= 0)
                {
                    ResizeBoxRequested?.Invoke(_dragIndex, dx, dy);
                }

                break;
        }

        _dragPoint = normalized.Value;
        Cursor = CurrentDragCursor;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        EndDrag();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        EndDrag();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        RefreshCursorFromCurrentMousePosition();
    }

    static void OnRectanglesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScanGridEditor editor)
        {
            return;
        }

        editor.DetachSubscribedRectangles();
        if (editor._isLoaded)
        {
            editor.AttachCurrentRectangles();
        }

        editor.CoerceSelectedIndex();
        editor.InvalidateVisual();
        editor.RefreshCursorFromCurrentMousePosition();
    }

    static void OnEditModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ScanGridEditor editor)
        {
            editor.RefreshCursorFromCurrentMousePosition();
        }
    }

    void Rectangles_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        InvalidateVisual();
        RefreshCursorFromCurrentMousePosition();
    }

    void HandleLoaded()
    {
        _isLoaded = true;
        AttachCurrentRectangles();
        CoerceSelectedIndex();
        InvalidateVisual();
        RefreshCursorFromCurrentMousePosition();
    }

    void HandleUnloaded()
    {
        _isLoaded = false;
        DetachSubscribedRectangles();
        SetCursorSafely(Cursors.Arrow);
    }

    void AttachCurrentRectangles()
    {
        var rectangles = Rectangles;
        if (rectangles is null)
        {
            return;
        }

        if (ReferenceEquals(_subscribedRectangles, rectangles))
        {
            return;
        }

        DetachSubscribedRectangles();
        _subscribedRectangles = rectangles;
        _subscribedRectangles.CollectionChanged += Rectangles_CollectionChanged;
    }

    void DetachSubscribedRectangles()
    {
        if (_subscribedRectangles is not null)
        {
            _subscribedRectangles.CollectionChanged -= Rectangles_CollectionChanged;
            _subscribedRectangles = null;
        }
    }

    void CoerceSelectedIndex()
    {
        if (Rectangles is { Count: > 0 })
        {
            SelectedIndex = Math.Clamp(SelectedIndex, 0, Rectangles.Count - 1);
            return;
        }

        SelectedIndex = 0;
    }

    void BeginDrag(DragOperation operation, int index, Point normalizedPoint, MouseButtonEventArgs e)
    {
        _dragOperation = operation;
        _dragIndex = index;
        _dragPoint = normalizedPoint;
        CaptureMouse();
        Cursor = CurrentDragCursor;
        e.Handled = true;
    }

    void EndDrag()
    {
        _dragPoint = null;
        _dragOperation = DragOperation.None;
        _dragIndex = -1;

        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        Cursor = Mouse.LeftButton == MouseButtonState.Pressed
            ? CurrentDragCursor
            : GetCursor(Mouse.GetPosition(this), CurrentImageBounds);
    }

    void SetSelectedIndex(int index)
    {
        if (index == SelectedIndex)
        {
            return;
        }

        SelectedIndex = index;
        InvalidateVisual();
        RefreshCursorFromCurrentMousePosition();
    }

    Cursor GetCursor(Point pointer, Rect imageBounds)
    {
        return ToCursor(ScanGridEditorGeometry.CursorKind(
            pointer,
            imageBounds,
            Rectangles,
            EditMode,
            SelectedIndex));
    }

    Point? ToClampedNormalized(Point point, Rect imageBounds)
    {
        if (imageBounds.IsEmpty)
        {
            return null;
        }

        var clamped = new Point(
            Math.Clamp(point.X, imageBounds.Left, imageBounds.Right),
            Math.Clamp(point.Y, imageBounds.Top, imageBounds.Bottom));

        return new Point(
            (clamped.X - imageBounds.X) / imageBounds.Width,
            (clamped.Y - imageBounds.Y) / imageBounds.Height);
    }

    void DrawLabel(DrawingContext drawingContext, Rect rect, int label, bool isSelected)
    {
        var formattedText = new FormattedText(
            label.ToString(CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI Semibold"),
            isSelected ? 14 : 13,
            _labelForeground,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        var padding = 4.0;
        var background = new Rect(
            rect.X + 3,
            rect.Y + 3,
            formattedText.Width + padding * 2,
            formattedText.Height + padding);
        drawingContext.DrawRoundedRectangle(_labelBackground, null, background, 3, 3);
        drawingContext.DrawText(formattedText, new Point(background.X + padding, background.Y + 1));
    }

    void DrawHandle(DrawingContext drawingContext, Rect rect)
    {
        var handle = ScanGridEditorGeometry.ResizeHandleBounds(rect);
        drawingContext.DrawRectangle(_handleBrush, _handlePen, handle);
    }

    Rect CurrentImageBounds => ScanGridEditorGeometry.ImageBounds(
        new Size(ActualWidth, ActualHeight),
        SourceSize);

    Cursor CurrentDragCursor => _dragOperation is DragOperation.ResizeGrid or DragOperation.ResizeBox
        ? Cursors.SizeNWSE
        : Cursors.SizeAll;

    void RefreshCursorFromCurrentMousePosition()
    {
        if (_dragPoint is not null)
        {
            SetCursorSafely(CurrentDragCursor);
            return;
        }

        if (!_isLoaded || !IsVisible || PresentationSource.FromVisual(this) is null)
        {
            SetCursorSafely(Cursors.Arrow);
            return;
        }

        try
        {
            SetCursorSafely(GetCursor(Mouse.GetPosition(this), CurrentImageBounds));
        }
        catch (InvalidOperationException)
        {
            SetCursorSafely(Cursors.Arrow);
        }
    }

    void SetCursorSafely(Cursor cursor)
    {
        if (!ReferenceEquals(Cursor, cursor))
        {
            Cursor = cursor;
        }
    }

    static Cursor ToCursor(ScanGridEditorCursorKind kind) => kind switch
    {
        ScanGridEditorCursorKind.Move => Cursors.SizeAll,
        ScanGridEditorCursorKind.Resize => Cursors.SizeNWSE,
        _ => Cursors.Arrow
    };

    static Pen CreateFrozenPen(Color color, double thickness)
    {
        var pen = new Pen(CreateFrozenBrush(color), thickness);
        pen.Freeze();
        return pen;
    }

    static SolidColorBrush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    enum DragOperation
    {
        None,
        MoveGrid,
        ResizeGrid,
        MoveBox,
        ResizeBox
    }
}
