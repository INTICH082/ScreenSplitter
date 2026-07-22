using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using ScreenSplitter.Platform.Windows;

namespace ScreenSplitter.UI.Views;

public partial class ZoneBorderWindow : Window
{
    private const double BracketLength = 22;
    private const double CompactBracketLength = 9;
    private const double Inset = 10;

    private double _lastWidth;
    private double _lastHeight;
    private bool _isOccupied;
    private bool _isPip;

    public ZoneBorderWindow()
    {
        InitializeComponent();
        Opened += (_, _) => ApplyClickThrough();
    }

    public void PlaceAt(PixelRect bounds)
    {
        Position = new PixelPoint(bounds.X, bounds.Y);
        var scaling = DesktopScaling > 0 ? DesktopScaling : 1.0;
        Width = bounds.Width / scaling;
        Height = bounds.Height / scaling;
        DrawReticle(Width, Height);
    }

    public void SetIndex(int number)
    {
        IndexLabel.Text = number.ToString("00");
    }

    public void SetLabel(string text)
    {
        IndexLabel.Text = text;
    }

    public void SetPictureInPicture(bool isPip)
    {
        _isPip = isPip;
        DrawReticle(_lastWidth, _lastHeight);
    }

    public void SetOccupied(bool occupied)
    {
        _isOccupied = occupied;
        ReticleCanvas.Opacity = occupied ? 0.45 : 1.0;
        DrawReticle(_lastWidth, _lastHeight);
    }

    public void SetHighlighted(bool highlighted)
    {
        var brush = highlighted ? SignalBrush : BaseBrush;
        foreach (var line in new[] { TL1, TL2, TR1, TR2, BL1, BL2, BR1, BR2 })
        {
            line.Stroke = brush;
        }
    }

    public void SetDropTargetActive(bool active)
    {
        var brush = active ? SignalBrush : BaseBrush;
        foreach (var line in new[] { TL1, TL2, TR1, TR2, BL1, BL2, BR1, BR2 })
        {
            line.Stroke = brush;
        }
    }

    private static readonly SolidColorBrush SignalBrush = new(Color.Parse("#4FD1C5"));
    private static readonly SolidColorBrush HairlineBrush = new(Color.Parse("#2A3140"));
    private static readonly SolidColorBrush PipBrush = new(Color.Parse("#F2A65A"));

    private SolidColorBrush BaseBrush => _isPip ? PipBrush : HairlineBrush;

    public void SetDropHighlighted(bool highlighted)
    {
        DropHighlightFill.IsVisible = highlighted;
        DropHighlightBorder.IsVisible = highlighted;
    }

    private void DrawReticle(double width, double height)
    {
        _lastWidth = width;
        _lastHeight = height;

        var w = Math.Max(0, width - Inset * 2);
        var h = Math.Max(0, height - Inset * 2);
        ReticleCanvas.Width = w;
        ReticleCanvas.Height = h;

        var maxLen = _isOccupied ? CompactBracketLength : BracketLength;
        var len = Math.Min(maxLen, Math.Min(w, h) / 3);

        // верх-лево
        SetLine(TL1, 0, 0, len, 0);
        SetLine(TL2, 0, 0, 0, len);
        // верх-право
        SetLine(TR1, w, 0, w - len, 0);
        SetLine(TR2, w, 0, w, len);
        // низ-лево
        SetLine(BL1, 0, h, len, h);
        SetLine(BL2, 0, h, 0, h - len);
        // низ-право
        SetLine(BR1, w, h, w - len, h);
        SetLine(BR2, w, h, w, h - len);

        foreach (var line in new[] { TL1, TL2, TR1, TR2, BL1, BL2, BR1, BR2 })
        {
            line.Stroke = BaseBrush;
        }
    }

    private static void SetLine(Line line, double x1, double y1, double x2, double y2)
    {
        line.StartPoint = new Point(x1, y1);
        line.EndPoint = new Point(x2, y2);
    }

    private void ApplyClickThrough()
    {
        var handle = TryGetPlatformHandle();
        if (handle is not null && handle.Handle != IntPtr.Zero)
        {
            WindowStyleHelper.MakeClickThrough(handle.Handle);
        }
    }
}