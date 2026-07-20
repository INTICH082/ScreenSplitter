using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ScreenSplitter.Core;
using ScreenSplitter.Core.Models;
using ScreenSplitter.Platform.Windows;
using ScreenSplitter.Platform.Windows.Native;
using ScreenSplitter.UI.Views;

namespace ScreenSplitter.UI.Services;

public class ZoneManager
{
    private const double MinFraction = 0.08; // минимальный размер зоны — 8% ширины/высоты области
    private const int SplitterThickness = 5;

    private class Slot
    {
        public required int Col { get; init; }
        public required int Row { get; init; }
        public required PixelRect Bounds { get; set; }
        public required ZoneBorderWindow Border { get; init; }
        public required ZoneChipWindow Chip { get; init; }

        public ZoneSlotStatus Status { get; set; } = ZoneSlotStatus.Empty;
        public string? AppPath { get; set; }
        public string? DisplayName { get; set; }
        public System.Diagnostics.Process? Process { get; set; }
        public IntPtr WindowHandle { get; set; }
        public PixelRect? OriginalWindowBounds { get; set; }
        public bool IsDropHighlighted { get; set; }
    }

    private readonly List<Slot> _slots = new();
    private readonly List<ZoneSplitterWindow> _colSplitters = new();
    private readonly List<ZoneSplitterWindow> _rowSplitters = new();

    private Slot? _pendingSwap;
    private Window? _screenSource;
    private WindowMoveWatcher? _moveWatcher;
    private DispatcherTimer? _dragHoverTimer;
    private IntPtr _draggedWindow;

    private int _cols;
    private int _rows;
    private double[] _colBounds = Array.Empty<double>(); // длины cols+1, значения 0..1
    private double[] _rowBounds = Array.Empty<double>();

    public void AttachScreenSource(Window window)
    {
        _screenSource = window;
    }

    public void ApplyPattern(ZonePatternType type)
    {
        if (type == ZonePatternType.Single)
        {
            ClearAll();
            return;
        }

        Apply(LayoutPresets.GetPattern(type));
    }

    public void ApplyCustomGrid(int cols, int rows)
    {
        Apply(LayoutPresets.BuildGrid(cols, rows));
    }

    public Profile? CaptureCurrentAsProfile(string name)
    {
        if (_slots.Count == 0 || _cols == 0 || _rows == 0) return null;

        var assignments = _slots.Select(s => new ZoneAssignment(
            s.Col,
            s.Row,
            s.Status switch
            {
                ZoneSlotStatus.Free => ZoneAssignmentKind.Free,
                ZoneSlotStatus.Assigned => ZoneAssignmentKind.App,
                _ => ZoneAssignmentKind.Empty
            },
            s.AppPath,
            s.DisplayName)).ToList();

        return new Profile
        {
            Name = name,
            Cols = _cols,
            Rows = _rows,
            Assignments = assignments
        };
    }

    public async Task ApplyProfileAsync(Profile profile)
    {
        Apply(LayoutPresets.BuildGrid(profile.Cols, profile.Rows));

        foreach (var assignment in profile.Assignments)
        {
            var slot = _slots.FirstOrDefault(s => s.Col == assignment.Col && s.Row == assignment.Row);
            if (slot is null) continue;

            switch (assignment.Kind)
            {
                case ZoneAssignmentKind.Free:
                    slot.Status = ZoneSlotStatus.Free;
                    slot.Chip.Render(ZoneSlotStatus.Free, null);
                    break;

                case ZoneAssignmentKind.App when assignment.Target is not null:
                    await LaunchIntoSlotAsync(slot, assignment.Target, assignment.DisplayName);
                    break;
            }
        }
    }

    private void Apply(IReadOnlyList<RelativeZoneRect> pattern)
    {
        ClearAll();

        var area = GetActiveArea();
        if (area is not { } workingArea) return;

        var xs = pattern.Select(r => Math.Round(r.X, 6)).Distinct().OrderBy(v => v).ToList();
        var ys = pattern.Select(r => Math.Round(r.Y, 6)).Distinct().OrderBy(v => v).ToList();

        _cols = xs.Count;
        _rows = ys.Count;
        _colBounds = xs.Append(1.0).ToArray();
        _rowBounds = ys.Append(1.0).ToArray();

        var index = 1;
        foreach (var rel in pattern)
        {
            var col = xs.IndexOf(Math.Round(rel.X, 6));
            var row = ys.IndexOf(Math.Round(rel.Y, 6));
            var bounds = ZoneBounds(col, row, workingArea);
            CreateSlot(col, row, bounds, index++);
        }

        CreateSplitters(workingArea);
        EnsureMoveWatcherStarted();
    }

    private PixelRect? GetActiveArea()
    {
        var screen = _screenSource?.Screens.Primary;
        if (screen is null) return null;

        return TaskbarController.IsHidden ? screen.Bounds : screen.WorkingArea;
    }

    private PixelRect ZoneBounds(int col, int row, PixelRect area)
    {
        var x0 = _colBounds[col];
        var x1 = _colBounds[col + 1];
        var y0 = _rowBounds[row];
        var y1 = _rowBounds[row + 1];

        return new PixelRect(
            area.X + (int)(x0 * area.Width),
            area.Y + (int)(y0 * area.Height),
            (int)((x1 - x0) * area.Width),
            (int)((y1 - y0) * area.Height));
    }

    public void RecomputeLayout() => RepositionAll();

    private void RepositionAll()
    {
        if (_slots.Count == 0) return;

        var area = GetActiveArea();
        if (area is not { } workingArea) return;

        foreach (var slot in _slots)
        {
            var bounds = ZoneBounds(slot.Col, slot.Row, workingArea);
            slot.Bounds = bounds;

            slot.Border.PlaceAt(bounds);
            slot.Chip.PlaceAt(new PixelPoint(bounds.X + 12, bounds.Y + 12));

            if (slot.WindowHandle != IntPtr.Zero)
            {
                WindowStyleHelper.PlaceWindowFlush(slot.WindowHandle, bounds.X, bounds.Y, bounds.Width, bounds.Height);
            }
        }

        for (int i = 0; i < _colSplitters.Count; i++)
        {
            PositionColumnSplitter(_colSplitters[i], i + 1, workingArea);
        }
        for (int j = 0; j < _rowSplitters.Count; j++)
        {
            PositionRowSplitter(_rowSplitters[j], j + 1, workingArea);
        }
    }

    // --- Разделители между зонами (изменение размера "выдавливанием" соседей) ---

    private void CreateSplitters(PixelRect area)
    {
        for (int i = 1; i < _cols; i++)
        {
            CreateColumnSplitter(i, area);
        }
        for (int j = 1; j < _rows; j++)
        {
            CreateRowSplitter(j, area);
        }
    }

    private void CreateColumnSplitter(int boundaryIndex, PixelRect area)
    {
        var splitter = new ZoneSplitterWindow(ZoneSplitterWindow.SplitterOrientation.Vertical);
        splitter.Show();
        PositionColumnSplitter(splitter, boundaryIndex, area);

        double startFraction = 0;
        splitter.DragStarted += () => startFraction = _colBounds[boundaryIndex];
        splitter.DragDelta += delta =>
        {
            var a = GetActiveArea();
            if (a is not { } ar || ar.Width <= 0) return;
            SetColumnBoundary(boundaryIndex, startFraction + delta / ar.Width);
        };

        _colSplitters.Add(splitter);
    }

    private void CreateRowSplitter(int boundaryIndex, PixelRect area)
    {
        var splitter = new ZoneSplitterWindow(ZoneSplitterWindow.SplitterOrientation.Horizontal);
        splitter.Show();
        PositionRowSplitter(splitter, boundaryIndex, area);

        double startFraction = 0;
        splitter.DragStarted += () => startFraction = _rowBounds[boundaryIndex];
        splitter.DragDelta += delta =>
        {
            var a = GetActiveArea();
            if (a is not { } ar || ar.Height <= 0) return;
            SetRowBoundary(boundaryIndex, startFraction + delta / ar.Height);
        };

        _rowSplitters.Add(splitter);
    }

    private void PositionColumnSplitter(ZoneSplitterWindow splitter, int boundaryIndex, PixelRect area)
    {
        var x = area.X + (int)(_colBounds[boundaryIndex] * area.Width) - SplitterThickness / 2;
        splitter.PlaceAt(new PixelRect(x, area.Y, SplitterThickness, area.Height));
    }

    private void PositionRowSplitter(ZoneSplitterWindow splitter, int boundaryIndex, PixelRect area)
    {
        var y = area.Y + (int)(_rowBounds[boundaryIndex] * area.Height) - SplitterThickness / 2;
        splitter.PlaceAt(new PixelRect(area.X, y, area.Width, SplitterThickness));
    }

    private void SetColumnBoundary(int boundaryIndex, double newFraction)
    {
        var min = _colBounds[boundaryIndex - 1] + MinFraction;
        var max = _colBounds[boundaryIndex + 1] - MinFraction;
        if (min > max) return;

        newFraction = Math.Clamp(newFraction, min, max);
        if (Math.Abs(newFraction - _colBounds[boundaryIndex]) < 1e-6) return;

        _colBounds[boundaryIndex] = newFraction;
        RepositionAll();
    }

    private void SetRowBoundary(int boundaryIndex, double newFraction)
    {
        var min = _rowBounds[boundaryIndex - 1] + MinFraction;
        var max = _rowBounds[boundaryIndex + 1] - MinFraction;
        if (min > max) return;

        newFraction = Math.Clamp(newFraction, min, max);
        if (Math.Abs(newFraction - _rowBounds[boundaryIndex]) < 1e-6) return;

        _rowBounds[boundaryIndex] = newFraction;
        RepositionAll();
    }

    // --- Перетаскивание окон и подсветка зон-целей ---

    private void EnsureMoveWatcherStarted()
    {
        if (_moveWatcher is not null) return;

        _moveWatcher = new WindowMoveWatcher();
        _moveWatcher.MoveStarted += OnDragStarted;
        _moveWatcher.MoveEnded += OnDragEnded;
    }

    private void OnDragStarted(IntPtr hwnd)
    {
        _draggedWindow = hwnd;

        foreach (var slot in _slots)
        {
            slot.Border.SetDropTargetActive(true);
        }

        _dragHoverTimer?.Stop();
        _dragHoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _dragHoverTimer.Tick += (_, _) => UpdateHoveredZone();
        _dragHoverTimer.Start();
    }

    private void UpdateHoveredZone()
    {
        if (!User32.GetCursorPos(out var cursor)) return;

        foreach (var slot in _slots)
        {
            var inside = Contains(slot.Bounds, cursor.X, cursor.Y);
            if (inside != slot.IsDropHighlighted)
            {
                slot.IsDropHighlighted = inside;
                slot.Border.SetDropHighlighted(inside);
            }
        }
    }

    private void OnDragEnded(IntPtr hwnd)
    {
        _dragHoverTimer?.Stop();
        _dragHoverTimer = null;
        _draggedWindow = IntPtr.Zero;

        Slot? target = null;

        if (User32.GetCursorPos(out var cursor))
        {
            target = _slots.FirstOrDefault(s => Contains(s.Bounds, cursor.X, cursor.Y));
        }

        foreach (var slot in _slots)
        {
            slot.Border.SetDropTargetActive(false);
            slot.Border.SetDropHighlighted(false);
            slot.IsDropHighlighted = false;
        }

        if (target is null) return;

        AssignDroppedWindow(target, hwnd);
    }

    private void AssignDroppedWindow(Slot slot, IntPtr hwnd)
    {
        slot.Status = ZoneSlotStatus.Assigned;
        slot.AppPath = null;
        slot.DisplayName = GetWindowTitle(hwnd);
        slot.Process = null;
        slot.WindowHandle = hwnd;
        slot.OriginalWindowBounds = User32.GetWindowRect(hwnd, out var rect)
            ? new PixelRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top)
            : null;

        WindowStyleHelper.PlaceWindowFlush(hwnd, slot.Bounds.X, slot.Bounds.Y, slot.Bounds.Width, slot.Bounds.Height);

        slot.Chip.Render(ZoneSlotStatus.Assigned, slot.DisplayName);
        slot.Border.SetOccupied(true);
    }

    private static string GetWindowTitle(IntPtr hwnd)
    {
        var length = User32.GetWindowTextLength(hwnd);
        if (length <= 0) return "Окно";

        var sb = new StringBuilder(length + 1);
        User32.GetWindowText(hwnd, sb, sb.Capacity);
        var text = sb.ToString();
        return string.IsNullOrWhiteSpace(text) ? "Окно" : text;
    }

    private static bool Contains(PixelRect b, int x, int y) =>
        x >= b.X && x < b.X + b.Width && y >= b.Y && y < b.Y + b.Height;

    // --- Создание зон ---

    private void CreateSlot(int col, int row, PixelRect bounds, int index)
    {
        var border = new ZoneBorderWindow();
        border.Show();
        border.PlaceAt(bounds);
        border.SetIndex(index);

        var chip = new ZoneChipWindow();
        chip.Show();
        chip.PlaceAt(new PixelPoint(bounds.X + 12, bounds.Y + 12));
        chip.Render(ZoneSlotStatus.Empty, null);

        var slot = new Slot
        {
            Col = col,
            Row = row,
            Bounds = bounds,
            Border = border,
            Chip = chip
        };

        chip.AssignRequested += async (_, _) => await OnAssignRequestedAsync(slot, chip);
        chip.ClearRequested += (_, _) => OnClearRequested(slot);
        chip.SwapClicked += (_, _) => OnSwapClicked(slot);

        _slots.Add(slot);
    }

    private async Task OnAssignRequestedAsync(Slot slot, ZoneChipWindow chip)
    {
        var choice = await AssignAppPopup.ShowAsync(chip, new PixelPoint(chip.Position.X, chip.Position.Y + 40));

        switch (choice.Kind)
        {
            case AssignChoiceKind.Free:
                slot.Status = ZoneSlotStatus.Free;
                chip.Render(ZoneSlotStatus.Free, null);
                break;

            case AssignChoiceKind.App when choice.AppPath is not null:
                await LaunchIntoSlotAsync(slot, choice.AppPath, choice.DisplayName);
                break;
        }
    }

    private async Task LaunchIntoSlotAsync(Slot slot, string appPath, string? displayName)
    {
        var fallbackTitle = System.IO.Path.GetFileNameWithoutExtension(appPath);
        var title = displayName ?? fallbackTitle;

        slot.Chip.Render(ZoneSlotStatus.Assigned, $"Запуск: {title}...");

        var (process, handle) = await ProcessWindowLocator.LaunchAndWaitForWindowAsync(appPath);

        slot.AppPath = appPath;
        slot.DisplayName = displayName;
        slot.Process = process;
        slot.WindowHandle = handle;
        slot.Status = ZoneSlotStatus.Assigned;

        if (handle != IntPtr.Zero)
        {
            slot.OriginalWindowBounds = User32.GetWindowRect(handle, out var rect)
                ? new PixelRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top)
                : null;

            WindowStyleHelper.PlaceWindowFlush(handle, slot.Bounds.X, slot.Bounds.Y, slot.Bounds.Width, slot.Bounds.Height);
            WindowStyleHelper.ActivateWindow(handle);
        }

        slot.Chip.Render(ZoneSlotStatus.Assigned, title);
        slot.Border.SetOccupied(true);
    }

    private void OnClearRequested(Slot slot)
    {
        if (slot.WindowHandle != IntPtr.Zero && slot.OriginalWindowBounds is { } original)
        {
            WindowStyleHelper.PlaceWindowFlush(slot.WindowHandle, original.X, original.Y, original.Width, original.Height);
        }

        slot.Status = ZoneSlotStatus.Empty;
        slot.AppPath = null;
        slot.DisplayName = null;
        slot.Process = null;
        slot.WindowHandle = IntPtr.Zero;
        slot.OriginalWindowBounds = null;
        slot.Chip.Render(ZoneSlotStatus.Empty, null);
        slot.Border.SetOccupied(false);
    }

    private void OnSwapClicked(Slot slot)
    {
        if (_pendingSwap is null)
        {
            _pendingSwap = slot;
            slot.Chip.SetSelectedForSwap(true);
            slot.Border.SetHighlighted(true);
            return;
        }

        if (ReferenceEquals(_pendingSwap, slot))
        {
            _pendingSwap.Chip.SetSelectedForSwap(false);
            _pendingSwap.Border.SetHighlighted(false);
            _pendingSwap = null;
            return;
        }

        SwapSlots(_pendingSwap, slot);
        _pendingSwap.Chip.SetSelectedForSwap(false);
        _pendingSwap.Border.SetHighlighted(false);
        _pendingSwap = null;
    }

    private void SwapSlots(Slot a, Slot b)
    {
        (a.Status, b.Status) = (b.Status, a.Status);
        (a.AppPath, b.AppPath) = (b.AppPath, a.AppPath);
        (a.DisplayName, b.DisplayName) = (b.DisplayName, a.DisplayName);
        (a.Process, b.Process) = (b.Process, a.Process);
        (a.WindowHandle, b.WindowHandle) = (b.WindowHandle, a.WindowHandle);
        (a.OriginalWindowBounds, b.OriginalWindowBounds) = (b.OriginalWindowBounds, a.OriginalWindowBounds);

        if (a.WindowHandle != IntPtr.Zero)
            WindowStyleHelper.PlaceWindowFlush(a.WindowHandle, a.Bounds.X, a.Bounds.Y, a.Bounds.Width, a.Bounds.Height);
        if (b.WindowHandle != IntPtr.Zero)
            WindowStyleHelper.PlaceWindowFlush(b.WindowHandle, b.Bounds.X, b.Bounds.Y, b.Bounds.Width, b.Bounds.Height);

        var aTitle = a.AppPath is null ? a.DisplayName : (a.DisplayName ?? System.IO.Path.GetFileNameWithoutExtension(a.AppPath));
        var bTitle = b.AppPath is null ? b.DisplayName : (b.DisplayName ?? System.IO.Path.GetFileNameWithoutExtension(b.AppPath));

        a.Chip.Render(a.Status, aTitle);
        b.Chip.Render(b.Status, bTitle);
        a.Border.SetOccupied(a.Status == ZoneSlotStatus.Assigned);
        b.Border.SetOccupied(b.Status == ZoneSlotStatus.Assigned);
    }

    public void CloseAllZones() => ClearAll();

    private void ClearAll()
    {
        _pendingSwap = null;
        foreach (var slot in _slots)
        {
            slot.Border.Close();
            slot.Chip.Close();
        }
        _slots.Clear();

        foreach (var splitter in _colSplitters) splitter.Close();
        foreach (var splitter in _rowSplitters) splitter.Close();
        _colSplitters.Clear();
        _rowSplitters.Clear();

        _cols = 0;
        _rows = 0;
        _colBounds = Array.Empty<double>();
        _rowBounds = Array.Empty<double>();

        _dragHoverTimer?.Stop();
        _dragHoverTimer = null;

        _moveWatcher?.Dispose();
        _moveWatcher = null;
    }
}