using RubiksCubeSolver.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace RubiksCubeSolver;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var vm = new MainViewModel();
        DataContext = vm;
        BuildCubeNet(vm);
        vm.AnimateDigitalMove = async (move, commit, ct) =>
        {
            await Dispatcher.InvokeAsync(() => { });
            await Task.WhenAll(
                CubeView.AnimateMoveAsync(move, () => Dispatcher.Invoke(commit), ct),
                TestCubeView.AnimateMoveAsync(move, null, ct));
        };
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.LogText))
            {
                LogScroll.ScrollToEnd();
            }
        };
        Closed += (_, _) => vm.Closing();
    }

    void BuildCubeNet(MainViewModel vm)
    {
        var grid = CubeNetHost;
        grid.RowDefinitions.Clear();
        grid.ColumnDefinitions.Clear();
        grid.Children.Clear();
        for (int i = 0; i < 9; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition());
        }

        for (int i = 0; i < 12; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition());
        }

        PlaceFace(grid, vm, 0, 3, 0);
        PlaceFace(grid, vm, 36, 0, 3);
        PlaceFace(grid, vm, 18, 3, 3);
        PlaceFace(grid, vm, 9, 6, 3);
        PlaceFace(grid, vm, 45, 9, 3);
        PlaceFace(grid, vm, 27, 3, 6);

        AddLabel(grid, "U", 4, 1);
        AddLabel(grid, "L", 1, 4);
        AddLabel(grid, "F", 4, 4);
        AddLabel(grid, "R", 7, 4);
        AddLabel(grid, "B", 10, 4);
        AddLabel(grid, "D", 4, 7);
    }

    static void PlaceFace(Grid grid, MainViewModel vm, int start, int col, int row)
    {
        for (int r = 0; r < 3; r++)
        {
            for (int c = 0; c < 3; c++)
            {
                var index = start + r * 3 + c;
                var cell = vm.Stickers[index];
                var button = new Button
                {
                    Margin = new Thickness(2),
                    Command = vm.CycleStickerCommand,
                    CommandParameter = cell,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(20, 22, 28)),
                    BorderThickness = new Thickness(1),
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                var bind = new Binding(nameof(StickerCell.Brush))
                {
                    Source = cell
                };
                button.SetBinding(BackgroundProperty, bind);
                Grid.SetColumn(button, col + c);
                Grid.SetRow(button, row + r);
                grid.Children.Add(button);
            }
        }
    }

    static void AddLabel(Grid grid, string text, int col, int row)
    {
        var label = new TextBlock
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.Bold,
            IsHitTestVisible = false,
            Foreground = new SolidColorBrush(Color.FromArgb(180, 20, 20, 20))
        };
        Grid.SetColumn(label, col);
        Grid.SetRow(label, row);
        grid.Children.Add(label);
    }
}
