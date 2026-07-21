using Avalonia.Controls;
using Avalonia.Interactivity;
using ScreenSplitter.Core.Models;
using ScreenSplitter.UI.Services;

namespace ScreenSplitter.UI.Views;

public partial class ZonePatternPickerWindow : Window
{
    private readonly ZoneManager _zoneManager;
    private bool _initializingScreenList = true;

    public ZonePatternPickerWindow() : this(new ZoneManager())
    {
    }

    public ZonePatternPickerWindow(ZoneManager zoneManager)
    {
        InitializeComponent();
        _zoneManager = zoneManager;
        PopulateScreenList();
    }

    private void PopulateScreenList()
    {
        _initializingScreenList = true;
        ScreenComboBox.ItemsSource = _zoneManager.GetAvailableScreenDescriptions();
        var current = _zoneManager.GetTargetScreenIndex();
        ScreenComboBox.SelectedIndex = current >= 0 && current < ScreenComboBox.ItemCount ? current : 0;
        _initializingScreenList = false;
    }

    private void OnScreenSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_initializingScreenList) return;
        _zoneManager.SetTargetScreenIndex(ScreenComboBox.SelectedIndex);
    }

    private void OnSingleClicked(object? sender, RoutedEventArgs e)
    {
        _zoneManager.ApplyPattern(ZonePatternType.Single);
        Close();
    }

    private void OnVerticalClicked(object? sender, RoutedEventArgs e)
    {
        _zoneManager.ApplyPattern(ZonePatternType.SplitVertical);
        Close();
    }

    private void OnHorizontalClicked(object? sender, RoutedEventArgs e)
    {
        _zoneManager.ApplyPattern(ZonePatternType.SplitHorizontal);
        Close();
    }

    private void OnGrid2x2Clicked(object? sender, RoutedEventArgs e)
    {
        _zoneManager.ApplyPattern(ZonePatternType.Grid2x2);
        Close();
    }

    private void OnCustomGridClicked(object? sender, RoutedEventArgs e)
    {
        var cols = (int)(ColsUpDown.Value ?? 2);
        var rows = (int)(RowsUpDown.Value ?? 2);
        _zoneManager.ApplyCustomGrid(cols, rows);
        Close();
    }
}