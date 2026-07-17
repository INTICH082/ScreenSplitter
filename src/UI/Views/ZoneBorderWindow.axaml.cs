using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ScreenSplitter.Platform.Windows;

namespace ScreenSplitter.UI.Views;

public partial class ZoneBorderWindow : Window
{
    public ZoneBorderWindow()
    {
        InitializeComponent();
        Opened += (_, _) => ApplyClickThrough();
    }

    public void PlaceAt(PixelRect bounds)
    {
        Position = new PixelPoint(bounds.X, bounds.Y);
        Width = bounds.Width;
        Height = bounds.Height;
    }

    public void SetHighlighted(bool highlighted)
    {
        FrameBorder.BorderBrush = new SolidColorBrush(highlighted
            ? Color.Parse("#FFFFA500")
            : Color.Parse("#8000BFFF"));
        FrameBorder.BorderThickness = new Thickness(highlighted ? 3 : 2);
    }

    private void ApplyClickThrough()
    {
        var handle = TryGetPlatformHandle();
        if (handle is not null && handle.Handle != System.IntPtr.Zero)
        {
            WindowStyleHelper.MakeClickThrough(handle.Handle);
        }
    }
}