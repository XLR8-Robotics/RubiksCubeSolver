using RubiksCubeSolver.Models;
using RubiksCubeSolver.ViewModels;
using RubiksCubeSolver.Vision;

namespace RubiksCubeSolver.Tests;

public class MainViewModelScanGridTests
{
    [Fact]
    public void Constructor_UsesSavedValidScanRectangles_AndDefaultsToMoveGridMode()
    {
        var saved = ScanGridLayout.CreateRegular(0.2, 0.05, -0.04, 0.1, 1280, 720).ToList();
        var settings = new AppSettings
        {
            ScanRectangles = saved
        };

        var viewModel = new MainViewModel(settings, runStartupTasks: false);

        Assert.Equal(saved, viewModel.ScanRectangles);
        Assert.Equal(ScanGridEditMode.MoveGrid, viewModel.ScanGridEditMode);
    }

    [Fact]
    public void FaceMargin_WithoutFrameDimensions_DoesNotRegenerateLayout()
    {
        var viewModel = new MainViewModel(new AppSettings(), runStartupTasks: false);

        viewModel.FaceMargin = 0.25;

        Assert.Empty(viewModel.ScanRectangles);
    }

    [Fact]
    public void FaceMargin_WithFrameDimensions_RegeneratesRegularLayout()
    {
        var settings = new AppSettings
        {
            FaceMargin = 0.22,
            FaceSampleInset = 0.18
        };
        var viewModel = new MainViewModel(settings, runStartupTasks: false)
        {
            CameraFrameWidth = 1280,
            CameraFrameHeight = 720
        };

        viewModel.FaceMargin = 0.20;

        Assert.Equal(
            ScanGridLayout.CreateRegular(
                0.20,
                settings.FaceOffsetX,
                settings.FaceOffsetY,
                settings.FaceSampleInset,
                1280,
                720),
            viewModel.ScanRectangles);
    }

    [Fact]
    public void MoveAndResizeHelpers_ReplaceCurrentEditableLayout()
    {
        var viewModel = new MainViewModel(new AppSettings(), runStartupTasks: false);
        var layout = ScanGridLayout.CreateRegular(0.2, 0, 0, 0.1, 1280, 720);

        viewModel.ReplaceScanRectangles(layout);
        viewModel.MoveScanLayout(0.02, -0.01);
        var moved = ScanGridLayout.MoveAll(layout, 0.02, -0.01);
        Assert.Equal(moved, viewModel.ScanRectangles);

        viewModel.ResizeScanRectangle(4, 0.03, 0.02);
        var resized = ScanGridLayout.ResizeOne(moved, 4, 0.03, 0.02);
        Assert.Equal(resized, viewModel.ScanRectangles);
    }

    [Fact]
    public void SyncScanRectanglesToSettings_CopiesCurrentEditableLayout()
    {
        var settings = new AppSettings();
        var viewModel = new MainViewModel(settings, runStartupTasks: false);
        var layout = ScanGridLayout.CreateRegular(0.2, 0, 0, 0.1, 1280, 720);

        viewModel.ReplaceScanRectangles(layout);
        viewModel.SyncScanRectanglesToSettings();

        Assert.Equal(layout, settings.ScanRectangles);
    }

    [Fact]
    public void PrepareManualLayoutForFrame_RegeneratesAndSyncsLayoutFromActualFrameDimensions()
    {
        var settings = new AppSettings
        {
            FaceMargin = 0.22,
            FaceOffsetX = 0.05,
            FaceOffsetY = -0.03,
            FaceSampleInset = 0.18
        };
        var viewModel = new MainViewModel(settings, runStartupTasks: false);

        viewModel.PrepareManualLayoutForFrame(1280, 720);

        var expected = ScanGridLayout.CreateRegular(
            settings.FaceMargin,
            settings.FaceOffsetX,
            settings.FaceOffsetY,
            settings.FaceSampleInset,
            1280,
            720);
        Assert.Equal(1280, viewModel.CameraFrameWidth);
        Assert.Equal(720, viewModel.CameraFrameHeight);
        Assert.Equal(expected, viewModel.ScanRectangles);
        Assert.Equal(expected, settings.ScanRectangles);
    }

    [Fact]
    public void PrepareManualLayoutForFrame_PreservesExistingUnsavedEditedLayout_AndSyncsIt()
    {
        var original = ScanGridLayout.CreateRegular(0.22, 0, 0, 0.18, 1280, 720);
        var edited = ScanGridLayout.ResizeOne(
            ScanGridLayout.MoveOne(original, 4, 0.01, -0.015),
            4,
            0.02,
            0.01);
        var settings = new AppSettings
        {
            ScanRectangles = original.ToList()
        };
        var viewModel = new MainViewModel(settings, runStartupTasks: false);

        viewModel.ReplaceScanRectangles(edited);
        viewModel.PrepareManualLayoutForFrame(1280, 720);

        Assert.Equal(edited, viewModel.ScanRectangles);
        Assert.Equal(edited, settings.ScanRectangles);
        Assert.NotEqual(original, settings.ScanRectangles);
    }

    [Fact]
    public void ResetScanGrid_WithoutFrameDimensions_ClearsCustomLayoutAndSettings()
    {
        var settings = new AppSettings
        {
            FaceMargin = 0.31,
            FaceOffsetX = 0.11,
            FaceOffsetY = -0.07,
            FaceSampleInset = 0.09,
            FaceAutoDetect = true,
            ScanRectangles = ScanGridLayout.CreateRegular(0.2, 0.04, -0.03, 0.1, 1280, 720).ToList()
        };
        var viewModel = new MainViewModel(settings, runStartupTasks: false);

        viewModel.ResetScanGrid();

        Assert.Empty(viewModel.ScanRectangles);
        Assert.NotNull(settings.ScanRectangles);
        Assert.Empty(settings.ScanRectangles!);
        Assert.Equal(0.22, settings.FaceMargin, 8);
        Assert.Equal(0, settings.FaceOffsetX, 8);
        Assert.Equal(0, settings.FaceOffsetY, 8);
        Assert.Equal(0.18, settings.FaceSampleInset, 8);
        Assert.False(settings.FaceAutoDetect);
    }

    [Fact]
    public void FaceAutoDetect_RaisesPropertyChangedImmediately()
    {
        var viewModel = new MainViewModel(new AppSettings(), runStartupTasks: false);
        var notifications = new List<string>();
        viewModel.PropertyChanged += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.PropertyName))
            {
                notifications.Add(e.PropertyName);
            }
        };

        viewModel.FaceAutoDetect = true;

        Assert.True(viewModel.FaceAutoDetect);
        Assert.Contains(nameof(MainViewModel.FaceAutoDetect), notifications);
    }

    [Fact]
    public void RedOrangeHueSplit_ClampsOutOfRangeValuesToNeighborBand()
    {
        var viewModel = new MainViewModel(new AppSettings { RedOrangeHueSplit = 0 }, runStartupTasks: false);

        Assert.Equal(8, viewModel.RedOrangeHueSplit);
        Assert.Equal(18, viewModel.OrangeYellowHueSplit);
        Assert.Equal(38, viewModel.YellowGreenHueSplit);
        Assert.Equal(85, viewModel.GreenBlueHueSplit);
        Assert.Equal(170, viewModel.BlueRedHueSplit);
        Assert.Equal(50, viewModel.WhiteSaturation);

        viewModel.RedOrangeHueSplit = 12;
        Assert.Equal(12, viewModel.RedOrangeHueSplit);

        viewModel.RedOrangeHueSplit = 99;
        Assert.Equal(viewModel.OrangeYellowHueSplit - 1, viewModel.RedOrangeHueSplit);
    }

    [Fact]
    public void OrangeYellowHueSplit_StaysBetweenNeighbors()
    {
        var viewModel = new MainViewModel(new AppSettings(), runStartupTasks: false);

        viewModel.OrangeYellowHueSplit = 22;
        Assert.Equal(22, viewModel.OrangeYellowHueSplit);

        viewModel.YellowGreenHueSplit = 45;
        Assert.Equal(45, viewModel.YellowGreenHueSplit);
        Assert.True(viewModel.OrangeYellowHueSplit < viewModel.YellowGreenHueSplit);
        Assert.True(viewModel.YellowGreenHueSplit < viewModel.GreenBlueHueSplit);
    }
}
