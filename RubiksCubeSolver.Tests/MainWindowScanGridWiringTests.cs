using System.IO;

namespace RubiksCubeSolver.Tests;

public class MainWindowScanGridWiringTests
{
    [Fact]
    public void MainWindowXaml_WiresScanGridOverlay_AndKeepsLiveCameraImage()
    {
        var xaml = File.ReadAllText(Path.Combine(RepositoryRoot, "RubiksCubeSolver", "MainWindow.xaml"));

        Assert.Contains("Content=\"Move Grid\"", xaml);
        Assert.Contains("Content=\"Resize Grid\"", xaml);
        Assert.Contains("Content=\"Move Boxes\"", xaml);
        Assert.Contains("Content=\"Resize Boxes\"", xaml);
        Assert.Contains("x:Name=\"ScanGridCameraImage\"", xaml);
        Assert.Contains("Source=\"{Binding CameraImage}\"", xaml);
        Assert.Contains("controls:ScanGridEditor x:Name=\"ScanGridEditor\"", xaml);
        Assert.Contains("Rectangles=\"{Binding ScanRectangles}\"", xaml);
        Assert.Contains("EditMode=\"{Binding ScanGridEditMode}\"", xaml);
        Assert.Contains(
            "Custom dragged boxes are used for manual scans. Auto-find uses its own perspective-warped regular grid.",
            xaml);
    }

    [Fact]
    public void MainWindowXaml_HidesManualEditor_WhenAutoFindIsEnabled()
    {
        var xaml = File.ReadAllText(Path.Combine(RepositoryRoot, "RubiksCubeSolver", "MainWindow.xaml"));

        Assert.Contains("Binding=\"{Binding FaceAutoDetect}\" Value=\"True\"", xaml);
        Assert.Contains("Setter Property=\"Visibility\" Value=\"Collapsed\"", xaml);
        Assert.Contains("Setter Property=\"IsHitTestVisible\" Value=\"False\"", xaml);
        Assert.Contains("Manual editor hidden while Auto-find face during scans is enabled.", xaml);
    }

    [Fact]
    public void MainWindowCodeBehind_WiresEditorEvents_ModeButtons_AndSourceSizeRefresh()
    {
        var codeBehind = File.ReadAllText(Path.Combine(RepositoryRoot, "RubiksCubeSolver", "MainWindow.xaml.cs"));

        Assert.Contains("ScanGridEditor.MoveGridRequested += vm.MoveScanLayout;", codeBehind);
        Assert.Contains("ScanGridEditor.ScaleGridRequested += vm.ScaleScanLayout;", codeBehind);
        Assert.Contains("ScanGridEditor.MoveBoxRequested += vm.MoveScanRectangle;", codeBehind);
        Assert.Contains("ScanGridEditor.ResizeBoxRequested += vm.ResizeScanRectangle;", codeBehind);
        Assert.DoesNotContain("SelectedScanRectangle", codeBehind);
        Assert.Contains("nameof(MainViewModel.CameraFrameWidth)", codeBehind);
        Assert.Contains("nameof(MainViewModel.CameraFrameHeight)", codeBehind);
        Assert.Contains("ScanGridEditor.SourceSize =", codeBehind);
        Assert.Contains("new Size(vm.CameraFrameWidth, vm.CameraFrameHeight)", codeBehind);
        Assert.Contains("MoveGridMode_Click", codeBehind);
        Assert.Contains("ResizeGridMode_Click", codeBehind);
        Assert.Contains("MoveBoxesMode_Click", codeBehind);
        Assert.Contains("ResizeBoxesMode_Click", codeBehind);
        Assert.Contains("ScanGridEditMode.MoveGrid", codeBehind);
        Assert.Contains("ScanGridEditMode.ResizeGrid", codeBehind);
        Assert.Contains("ScanGridEditMode.MoveBoxes", codeBehind);
        Assert.Contains("ScanGridEditMode.ResizeBoxes", codeBehind);
    }

    static string RepositoryRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
}
