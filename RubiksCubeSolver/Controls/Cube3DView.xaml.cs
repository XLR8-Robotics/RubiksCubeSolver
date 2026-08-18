using RubiksCubeSolver.Models;
using RubiksCubeSolver.ViewModels;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace RubiksCubeSolver.Controls;

public partial class Cube3DView : UserControl
{
    public static readonly DependencyProperty StickersProperty =
        DependencyProperty.Register(nameof(Stickers), typeof(ObservableCollection<StickerCell>), typeof(Cube3DView),
            new PropertyMetadata(null, OnStickersChanged));

    readonly MaterialGroup[] _materials = new MaterialGroup[54];
    readonly List<CubieVisual> _cubies = [];
    readonly AxisAngleRotation3D _sliceRotation = new(new Vector3D(0, 1, 0), 0);
    readonly RotateTransform3D _sliceTransform;
    Model3DGroup? _staticGroup;
    Model3DGroup? _sliceGroup;
    Point _lastMouse;
    bool _dragging;
    double _yaw = 38;
    double _pitch = 24;
    double _distance = 6.4;

    public Cube3DView()
    {
        InitializeComponent();
        _sliceTransform = new RotateTransform3D(_sliceRotation);
        Loaded += (_, _) =>
        {
            BuildCube();
            UpdateCamera();
        };
        MouseLeftButtonDown += OnMouseDown;
        MouseLeftButtonUp += OnMouseUp;
        MouseMove += OnMouseMove;
        MouseWheel += OnWheel;
        MouseLeave += (_, _) => _dragging = false;
    }

    public ObservableCollection<StickerCell>? Stickers
    {
        get => (ObservableCollection<StickerCell>?)GetValue(StickersProperty);
        set => SetValue(StickersProperty, value);
    }

    static void OnStickersChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (Cube3DView)d;
        if (e.OldValue is ObservableCollection<StickerCell> oldItems)
        {
            oldItems.CollectionChanged -= view.OnStickerCollectionChanged;
            foreach (var cell in oldItems)
            {
                cell.PropertyChanged -= view.OnStickerChanged;
            }
        }

        if (e.NewValue is ObservableCollection<StickerCell> items)
        {
            items.CollectionChanged += view.OnStickerCollectionChanged;
            foreach (var cell in items)
            {
                cell.PropertyChanged += view.OnStickerChanged;
            }

            view.RefreshColors();
        }
    }

    void OnStickerCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshColors();

    void OnStickerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(StickerCell.Color) or nameof(StickerCell.Brush) or null)
        {
            RefreshColors();
        }
    }

    public async Task AnimateMoveAsync(CubeMove move, Action? commitColors = null, CancellationToken cancellationToken = default)
    {
        if (!Dispatcher.CheckAccess())
        {
            Task? inner = null;
            await Dispatcher.InvokeAsync(() =>
            {
                inner = AnimateMoveAsync(move, commitColors, cancellationToken);
            });
            await inner!;
            return;
        }

        if (_sliceGroup is null || _staticGroup is null)
        {
            commitColors?.Invoke();
            return;
        }

        MoveLabel.Text = move.ToString();
        try
        {
            ArrangeSlice(move.Face);
            _sliceRotation.Axis = move.Face switch
            {
                CubeFace.U or CubeFace.D => new Vector3D(0, 1, 0),
                CubeFace.R or CubeFace.L => new Vector3D(1, 0, 0),
                _ => new Vector3D(0, 0, 1)
            };
            var sign = move.Face switch
            {
                CubeFace.U or CubeFace.R or CubeFace.F => -1,
                _ => 1
            };
            var target = sign * 90.0 * move.QuarterTurns;
            var steps = 16 * Math.Max(1, move.QuarterTurns);
            for (int i = 1; i <= steps; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _sliceRotation.Angle = target * i / steps;
                await Task.Delay(14, cancellationToken);
            }

            commitColors?.Invoke();
            _sliceRotation.Angle = 0;
            RestackCubies();
        }
        finally
        {
            MoveLabel.Text = "";
        }
    }

    void BuildCube()
    {
        foreach (var leftover in Root.Children.OfType<Model3DGroup>().ToList())
        {
            Root.Children.Remove(leftover);
        }

        _cubies.Clear();
        _staticGroup = new Model3DGroup();
        _sliceGroup = new Model3DGroup { Transform = _sliceTransform };
        Root.Children.Add(_staticGroup);
        Root.Children.Add(_sliceGroup);

        const double cubie = 0.96;
        const double sticker = 0.78;
        for (int x = -1; x <= 1; x++)
        {
            for (int y = -1; y <= 1; y++)
            {
                for (int z = -1; z <= 1; z++)
                {
                    if (x == 0 && y == 0 && z == 0)
                    {
                        continue;
                    }

                    var visual = CreateCubie(x, y, z, cubie, sticker);
                    _cubies.Add(visual);
                    _staticGroup.Children.Add(visual.Model);
                }
            }
        }

        RefreshColors();
    }

    CubieVisual CreateCubie(int x, int y, int z, double cubieSize, double stickerSize)
    {
        var group = new Model3DGroup();
        var cx = x * 1.04;
        var cy = y * 1.04;
        var cz = z * 1.04;
        var center = new Point3D(cx, cy, cz);
        AddPlastic(group, center, cubieSize / 2);

        if (y == 1)
        {
            AddSticker(group, FaceletIndex(CubeFace.U, x, y, z), center, new Vector3D(0, 1, 0), new Vector3D(1, 0, 0), stickerSize);
        }

        if (y == -1)
        {
            AddSticker(group, FaceletIndex(CubeFace.D, x, y, z), center, new Vector3D(0, -1, 0), new Vector3D(1, 0, 0), stickerSize);
        }

        if (x == 1)
        {
            AddSticker(group, FaceletIndex(CubeFace.R, x, y, z), center, new Vector3D(1, 0, 0), new Vector3D(0, 1, 0), stickerSize);
        }

        if (x == -1)
        {
            AddSticker(group, FaceletIndex(CubeFace.L, x, y, z), center, new Vector3D(-1, 0, 0), new Vector3D(0, 1, 0), stickerSize);
        }

        if (z == 1)
        {
            AddSticker(group, FaceletIndex(CubeFace.F, x, y, z), center, new Vector3D(0, 0, 1), new Vector3D(0, 1, 0), stickerSize);
        }

        if (z == -1)
        {
            AddSticker(group, FaceletIndex(CubeFace.B, x, y, z), center, new Vector3D(0, 0, -1), new Vector3D(0, 1, 0), stickerSize);
        }

        return new CubieVisual(x, y, z, group);
    }

    void AddPlastic(Model3DGroup group, Point3D center, double h)
    {
        var mesh = CubeMesh(center, h);
        var plastic = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(12, 12, 14)));
        group.Children.Add(new GeometryModel3D(mesh, plastic) { BackMaterial = plastic });
    }

    void AddSticker(Model3DGroup group, int facelet, Point3D cubieCenter, Vector3D outward, Vector3D upHint, double size)
    {
        outward.Normalize();
        var right = Vector3D.CrossProduct(upHint, outward);
        if (right.LengthSquared < 0.001)
        {
            right = Vector3D.CrossProduct(new Vector3D(1, 0, 0), outward);
        }

        right.Normalize();
        var up = Vector3D.CrossProduct(outward, right);
        up.Normalize();

        var origin = cubieCenter + outward * (0.49);
        var h = size / 2;
        var a = origin - right * h - up * h;
        var b = origin + right * h - up * h;
        var c = origin + right * h + up * h;
        var d = origin - right * h + up * h;
        var mesh = Quad(a, b, c, d, outward);
        var material = MaterialFor(facelet);
        group.Children.Add(new GeometryModel3D(mesh, material) { BackMaterial = material });
    }

    MaterialGroup MaterialFor(int facelet)
    {
        if (_materials[facelet] is not null)
        {
            return _materials[facelet];
        }

        var group = new MaterialGroup();
        group.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(80, 80, 80))));
        group.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(50, 50, 50))));
        _materials[facelet] = group;
        return group;
    }

    static int FaceletIndex(CubeFace face, int x, int y, int z)
    {
        int row, col;
        switch (face)
        {
            case CubeFace.U:
                row = z + 1;
                col = x + 1;
                return row * 3 + col;
            case CubeFace.D:
                row = 1 - z;
                col = x + 1;
                return 27 + row * 3 + col;
            case CubeFace.F:
                row = 1 - y;
                col = x + 1;
                return 18 + row * 3 + col;
            case CubeFace.B:
                row = 1 - y;
                col = 1 - x;
                return 45 + row * 3 + col;
            case CubeFace.R:
                row = 1 - y;
                col = 1 - z;
                return 9 + row * 3 + col;
            default:
                row = 1 - y;
                col = z + 1;
                return 36 + row * 3 + col;
        }
    }

    public void RefreshColors()
    {
        if (Stickers is null)
        {
            return;
        }

        for (int i = 0; i < 54 && i < Stickers.Count; i++)
        {
            var material = MaterialFor(i);
            var color = StickerPalette.BrushFor(Stickers[i].Color) is SolidColorBrush brush
                ? brush.Color
                : Color.FromRgb(70, 74, 82);
            ((DiffuseMaterial)material.Children[0]).Brush = new SolidColorBrush(color);
            ((EmissiveMaterial)material.Children[1]).Brush = new SolidColorBrush(Color.FromRgb(
                (byte)Math.Min(255, color.R * 0.55 + 20),
                (byte)Math.Min(255, color.G * 0.55 + 20),
                (byte)Math.Min(255, color.B * 0.55 + 20)));
        }
    }

    void ArrangeSlice(CubeFace face)
    {
        RestackCubies();
        foreach (var cubie in _cubies)
        {
            var onSlice = face switch
            {
                CubeFace.U => cubie.Y == 1,
                CubeFace.D => cubie.Y == -1,
                CubeFace.R => cubie.X == 1,
                CubeFace.L => cubie.X == -1,
                CubeFace.F => cubie.Z == 1,
                _ => cubie.Z == -1
            };
            if (onSlice)
            {
                _staticGroup!.Children.Remove(cubie.Model);
                _sliceGroup!.Children.Add(cubie.Model);
            }
        }
    }

    void RestackCubies()
    {
        _sliceRotation.Angle = 0;
        foreach (var cubie in _cubies)
        {
            if (_sliceGroup!.Children.Contains(cubie.Model))
            {
                _sliceGroup.Children.Remove(cubie.Model);
            }

            if (!_staticGroup!.Children.Contains(cubie.Model))
            {
                _staticGroup.Children.Add(cubie.Model);
            }
        }
    }

    void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        _lastMouse = e.GetPosition(this);
        CaptureMouse();
    }

    void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        ReleaseMouseCapture();
    }

    void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        var pos = e.GetPosition(this);
        var dx = pos.X - _lastMouse.X;
        var dy = pos.Y - _lastMouse.Y;
        _lastMouse = pos;
        _yaw += dx * 0.4;
        _pitch = Math.Clamp(_pitch + dy * 0.4, -80, 80);
        UpdateCamera();
    }

    void OnWheel(object sender, MouseWheelEventArgs e)
    {
        _distance = Math.Clamp(_distance - e.Delta * 0.004, 4.2, 14);
        UpdateCamera();
    }

    void UpdateCamera()
    {
        var yaw = _yaw * Math.PI / 180;
        var pitch = _pitch * Math.PI / 180;
        var x = _distance * Math.Cos(pitch) * Math.Sin(yaw);
        var y = _distance * Math.Sin(pitch);
        var z = _distance * Math.Cos(pitch) * Math.Cos(yaw);
        Cam.Position = new Point3D(x, y, z);
        Cam.LookDirection = new Vector3D(-x, -y, -z);
    }

    static MeshGeometry3D Quad(Point3D a, Point3D b, Point3D c, Point3D d, Vector3D normal)
    {
        normal.Normalize();
        var mesh = new MeshGeometry3D();
        mesh.Positions.Add(a);
        mesh.Positions.Add(b);
        mesh.Positions.Add(c);
        mesh.Positions.Add(d);
        mesh.TriangleIndices.Add(0);
        mesh.TriangleIndices.Add(1);
        mesh.TriangleIndices.Add(2);
        mesh.TriangleIndices.Add(0);
        mesh.TriangleIndices.Add(2);
        mesh.TriangleIndices.Add(3);
        mesh.Normals.Add(normal);
        mesh.Normals.Add(normal);
        mesh.Normals.Add(normal);
        mesh.Normals.Add(normal);
        return mesh;
    }

    static MeshGeometry3D CubeMesh(Point3D center, double h)
    {
        var p = new[]
        {
            new Point3D(center.X - h, center.Y - h, center.Z - h),
            new Point3D(center.X + h, center.Y - h, center.Z - h),
            new Point3D(center.X - h, center.Y + h, center.Z - h),
            new Point3D(center.X + h, center.Y + h, center.Z - h),
            new Point3D(center.X - h, center.Y - h, center.Z + h),
            new Point3D(center.X + h, center.Y - h, center.Z + h),
            new Point3D(center.X - h, center.Y + h, center.Z + h),
            new Point3D(center.X + h, center.Y + h, center.Z + h)
        };

        var mesh = new MeshGeometry3D();
        void Face(int a, int b, int c, int d)
        {
            int i = mesh.Positions.Count;
            mesh.Positions.Add(p[a]);
            mesh.Positions.Add(p[b]);
            mesh.Positions.Add(p[c]);
            mesh.Positions.Add(p[d]);
            mesh.TriangleIndices.Add(i);
            mesh.TriangleIndices.Add(i + 1);
            mesh.TriangleIndices.Add(i + 2);
            mesh.TriangleIndices.Add(i);
            mesh.TriangleIndices.Add(i + 2);
            mesh.TriangleIndices.Add(i + 3);
        }

        Face(1, 5, 7, 3);
        Face(4, 0, 2, 6);
        Face(2, 3, 7, 6);
        Face(0, 1, 5, 4);
        Face(5, 4, 6, 7);
        Face(0, 1, 3, 2);
        return mesh;
    }

    sealed record CubieVisual(int X, int Y, int Z, Model3DGroup Model);
}
