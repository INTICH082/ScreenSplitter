using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
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
    private const int PipMinWidth = 420;
    private const int PipMinHeight = 300;

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
        public byte[]? IconBytes { get; set; }
        public bool IsDropHighlighted { get; set; }
    }

    private enum PipCorner { TopLeft, TopRight, BottomLeft, BottomRight }

    private class FloatingZone
    {
        public PixelRect Bounds;
        public required ZoneBorderWindow Border { get; init; }
        public required ZoneChipWindow Chip { get; init; }
        public required ZonePipMoveHandleWindow MoveHandle { get; init; }
        public required Dictionary<PipCorner, ZoneResizeGripWindow> Grips { get; init; }

        public ZoneSlotStatus Status = ZoneSlotStatus.Empty;
        public string? AppPath;
        public string? DisplayName;
        public System.Diagnostics.Process? Process;
        public IntPtr WindowHandle;
        public PixelRect? OriginalWindowBounds;
        public byte[]? IconBytes;
        public bool IsDropHighlighted;
    }

    private readonly List<Slot> _slots = new();
    private readonly List<ZoneSplitterWindow> _colSplitters = new();
    private readonly List<ZoneSplitterWindow> _rowSplitters = new();
    private FloatingZone? _pip;

    private Slot? _pendingSwap;
    private Window? _screenSource;
    private WindowMoveWatcher? _moveWatcher;
    private DispatcherTimer? _dragHoverTimer;
    private DispatcherTimer? _livenessTimer;
    private IntPtr _draggedWindow;
    private int _targetScreenIndex = -1;

    private int _cols;
    private int _rows;
    private double[] _colBounds = Array.Empty<double>();
    private double[] _rowBounds = Array.Empty<double>();

    public void AttachScreenSource(Window window)
    {
        _screenSource = window;
    }

    public IReadOnlyList<string> GetAvailableScreenDescriptions()
    {
        var screens = _screenSource?.Screens.All;
        if (screens is null) return Array.Empty<string>();

        return screens.Select((s, i) =>
        {
            var primaryMark = s.IsPrimary ? " — основной" : "";
            return $"Монитор {i + 1} ({s.Bounds.Width}x{s.Bounds.Height}){primaryMark}";
        }).ToList();
    }

    public int GetTargetScreenIndex() => _targetScreenIndex;

    public void SetTargetScreenIndex(int index)
    {
        _targetScreenIndex = index;
        RecomputeLayout();
    }

    private Screen? GetTargetScreen()
    {
        var screens = _screenSource?.Screens;
        if (screens is null) return null;

        if (_targetScreenIndex < 0 || _targetScreenIndex >= screens.All.Count)
        {
            return screens.Primary;
        }

        return screens.All[_targetScreenIndex];
    }

    private double GetScreenScaling() => GetTargetScreen()?.Scaling ?? 1.0;

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

        var scaling = GetScreenScaling();
        var index = 1;
        foreach (var rel in pattern)
        {
            var col = xs.IndexOf(Math.Round(rel.X, 6));
            var row = ys.IndexOf(Math.Round(rel.Y, 6));
            var bounds = ZoneBounds(col, row, workingArea);
            CreateSlot(col, row, bounds, index++, scaling);
        }

        CreateSplitters(workingArea);
        EnsureWatchersRunning();
    }

    private void EnsureWatchersRunning()
    {
        EnsureMoveWatcherStarted();
        EnsureLivenessTimerStarted();
    }

    private void StopWatchersIfIdle()
    {
        if (_slots.Count > 0 || _pip is not null) return;

        _dragHoverTimer?.Stop();
        _dragHoverTimer = null;

        _livenessTimer?.Stop();
        _livenessTimer = null;

        _moveWatcher?.Dispose();
        _moveWatcher = null;
    }

    private void EnsureLivenessTimerStarted()
    {
        if (_livenessTimer is not null) return;

        _livenessTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _livenessTimer.Tick += (_, _) => CheckForClosedWindows();
        _livenessTimer.Start();
    }

    private void CheckForClosedWindows()
    {
        foreach (var slot in _slots.ToList())
        {
            if (slot.Status == ZoneSlotStatus.Assigned
                && slot.WindowHandle != IntPtr.Zero
                && !User32.IsWindow(slot.WindowHandle))
            {
                slot.Status = ZoneSlotStatus.Empty;
                slot.AppPath = null;
                slot.DisplayName = null;
                slot.Process = null;
                slot.WindowHandle = IntPtr.Zero;
                slot.OriginalWindowBounds = null;
                slot.IconBytes = null;
                slot.Chip.Render(ZoneSlotStatus.Empty, null);
                slot.Border.SetOccupied(false);
            }
        }

        if (_pip is { WindowHandle: var pipHandle } pip && pipHandle != IntPtr.Zero && !User32.IsWindow(pipHandle))
        {
            OnPipClearRequested(pip);
        }
    }

    private PixelRect? GetActiveArea()
    {
        var screen = GetTargetScreen();
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
        var area = GetActiveArea();
        if (area is not { } workingArea) return;

        var scaling = GetScreenScaling();

        foreach (var slot in _slots)
        {
            var bounds = ZoneBounds(slot.Col, slot.Row, workingArea);
            slot.Bounds = bounds;

            slot.Border.PlaceAt(bounds, scaling);
            slot.Chip.PlaceAt(new PixelPoint(bounds.X + 12, bounds.Y + 12));

            if (slot.WindowHandle != IntPtr.Zero)
            {
                WindowStyleHelper.PlaceWindowFlush(slot.WindowHandle, bounds.X, bounds.Y, bounds.Width, bounds.Height);
            }
        }

        for (int i = 0; i < _colSplitters.Count; i++)
        {
            PositionColumnSplitter(_colSplitters[i], i + 1, workingArea, scaling);
        }
        for (int j = 0; j < _rowSplitters.Count; j++)
        {
            PositionRowSplitter(_rowSplitters[j], j + 1, workingArea, scaling);
        }

        if (_pip is { } pip)
        {
            var clampedX = Math.Clamp(pip.Bounds.X, workingArea.X, workingArea.X + workingArea.Width - pip.Bounds.Width);
            var clampedY = Math.Clamp(pip.Bounds.Y, workingArea.Y, workingArea.Y + workingArea.Height - pip.Bounds.Height);
            pip.Bounds = new PixelRect(clampedX, clampedY, pip.Bounds.Width, pip.Bounds.Height);
            pip.Border.PlaceAt(pip.Bounds, scaling);
            pip.Chip.PlaceAt(new PixelPoint(pip.Bounds.X + 12, pip.Bounds.Y + 12));
            PositionPipGrips(pip);

            if (pip.WindowHandle != IntPtr.Zero)
            {
                WindowStyleHelper.PlaceWindowFlushTopmost(pip.WindowHandle, pip.Bounds.X, pip.Bounds.Y, pip.Bounds.Width, pip.Bounds.Height);
            }
        }
    }

    private void CreateSplitters(PixelRect area)
    {
        var scaling = GetScreenScaling();
        for (int i = 1; i < _cols; i++)
        {
            CreateColumnSplitter(i, area, scaling);
        }
        for (int j = 1; j < _rows; j++)
        {
            CreateRowSplitter(j, area, scaling);
        }
    }

    private void CreateColumnSplitter(int boundaryIndex, PixelRect area, double scaling)
    {
        var splitter = new ZoneSplitterWindow(ZoneSplitterWindow.SplitterOrientation.Vertical);
        splitter.Show();
        PositionColumnSplitter(splitter, boundaryIndex, area, scaling);

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

    private void CreateRowSplitter(int boundaryIndex, PixelRect area, double scaling)
    {
        var splitter = new ZoneSplitterWindow(ZoneSplitterWindow.SplitterOrientation.Horizontal);
        splitter.Show();
        PositionRowSplitter(splitter, boundaryIndex, area, scaling);

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

    private void PositionColumnSplitter(ZoneSplitterWindow splitter, int boundaryIndex, PixelRect area, double scaling)
    {
        var x = area.X + (int)(_colBounds[boundaryIndex] * area.Width) - SplitterThickness / 2;
        splitter.PlaceAt(new PixelRect(x, area.Y, SplitterThickness, area.Height), scaling);
    }

    private void PositionRowSplitter(ZoneSplitterWindow splitter, int boundaryIndex, PixelRect area, double scaling)
    {
        var y = area.Y + (int)(_rowBounds[boundaryIndex] * area.Height) - SplitterThickness / 2;
        splitter.PlaceAt(new PixelRect(area.X, y, area.Width, SplitterThickness), scaling);
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

    public bool HasPictureInPicture => _pip is not null;

    public void TogglePictureInPicture()
    {
        if (_pip is not null) RemovePictureInPicture();
        else AddPictureInPicture();
    }

    private void AddPictureInPicture()
    {
        var area = GetActiveArea();
        if (area is not { } workingArea) return;

        var scaling = GetScreenScaling();

        var width = Math.Max(PipMinWidth, (int)(workingArea.Width * 0.26));
        var height = Math.Max(PipMinHeight, (int)(workingArea.Height * 0.26));
        var x = workingArea.X + workingArea.Width - width - 24;
        var y = workingArea.Y + workingArea.Height - height - 24;
        var bounds = new PixelRect(x, y, width, height);

        var border = new ZoneBorderWindow();
        border.Show();
        border.PlaceAt(bounds, scaling);
        border.SetLabel("PIP");
        border.SetPictureInPicture(true);

        var chip = new ZoneChipWindow();
        chip.Show();
        chip.PlaceAt(new PixelPoint(bounds.X + 12, bounds.Y + 28));
        chip.Render(ZoneSlotStatus.Empty, null);

        var moveHandle = new ZonePipMoveHandleWindow();
        moveHandle.Show();

        var grips = new Dictionary<PipCorner, ZoneResizeGripWindow>();
        foreach (var corner in Enum.GetValues<PipCorner>())
        {
            var grip = new ZoneResizeGripWindow();
            grip.Show();
            grips[corner] = grip;
        }

        var zone = new FloatingZone
        {
            Bounds = bounds,
            Border = border,
            Chip = chip,
            MoveHandle = moveHandle,
            Grips = grips
        };

        PositionPipMoveHandle(zone);
        PositionPipGrips(zone);
        _pip = zone;

        chip.AssignRequested += async (_, _) => await OnPipAssignRequestedAsync(zone, chip);
        chip.ClearRequested += (_, _) => OnPipClearRequested(zone);

        PixelRect moveStartBounds = default;
        moveHandle.DragStarted += () => moveStartBounds = zone.Bounds;
        moveHandle.DragDelta += (dx, dy) => OnPipMoved(zone, moveStartBounds, dx, dy);

        foreach (var (corner, grip) in grips)
        {
            PixelRect resizeStartBounds = default;
            grip.DragStarted += () => resizeStartBounds = zone.Bounds;
            grip.DragDelta += (dx, dy) => OnPipResize(zone, resizeStartBounds, corner, dx, dy);
        }

        EnsureWatchersRunning();
    }

    private void RemovePictureInPicture()
    {
        if (_pip is not { } zone) return;

        if (zone.WindowHandle != IntPtr.Zero && zone.OriginalWindowBounds is { } original)
        {
            WindowStyleHelper.PlaceWindowFlush(zone.WindowHandle, original.X, original.Y, original.Width, original.Height);
        }

        zone.Border.Close();
        zone.Chip.Close();
        zone.MoveHandle.Close();
        foreach (var grip in zone.Grips.Values) grip.Close();
        _pip = null;

        StopWatchersIfIdle();
    }

    private void PositionPipMoveHandle(FloatingZone zone)
    {
        zone.MoveHandle.PlaceAt(zone.Bounds, GetScreenScaling());
    }

    private static void PositionPipGrips(FloatingZone zone)
    {
        const int gripSize = 14;
        var b = zone.Bounds;

        zone.Grips[PipCorner.TopLeft].PlaceAt(new PixelPoint(b.X - gripSize / 2, b.Y - gripSize / 2));
        zone.Grips[PipCorner.TopRight].PlaceAt(new PixelPoint(b.X + b.Width - gripSize / 2, b.Y - gripSize / 2));
        zone.Grips[PipCorner.BottomLeft].PlaceAt(new PixelPoint(b.X - gripSize / 2, b.Y + b.Height - gripSize / 2));
        zone.Grips[PipCorner.BottomRight].PlaceAt(new PixelPoint(b.X + b.Width - gripSize / 2, b.Y + b.Height - gripSize / 2));
    }

    private void OnPipMoved(FloatingZone zone, PixelRect startBounds, double dx, double dy)
    {
        zone.Bounds = new PixelRect((int)(startBounds.X + dx), (int)(startBounds.Y + dy), startBounds.Width, startBounds.Height);
        ApplyPipBounds(zone);
    }

    private void OnPipResize(FloatingZone zone, PixelRect startBounds, PipCorner corner, double dx, double dy)
    {
        var movesX = corner is PipCorner.TopLeft or PipCorner.BottomLeft;
        var movesY = corner is PipCorner.TopLeft or PipCorner.TopRight;

        var rawWidth = movesX ? startBounds.Width - dx : startBounds.Width + dx;
        var rawHeight = movesY ? startBounds.Height - dy : startBounds.Height + dy;

        var newWidth = (int)Math.Max(PipMinWidth, rawWidth);
        var newHeight = (int)Math.Max(PipMinHeight, rawHeight);

        var newX = movesX ? startBounds.X + startBounds.Width - newWidth : startBounds.X;
        var newY = movesY ? startBounds.Y + startBounds.Height - newHeight : startBounds.Y;

        zone.Bounds = new PixelRect(newX, newY, newWidth, newHeight);
        ApplyPipBounds(zone);
    }

    private void ApplyPipBounds(FloatingZone zone)
    {
        var scaling = GetScreenScaling();
        zone.Border.PlaceAt(zone.Bounds, scaling);
        zone.Chip.PlaceAt(new PixelPoint(zone.Bounds.X + 12, zone.Bounds.Y + 28));
        PositionPipMoveHandle(zone);
        PositionPipGrips(zone);

        if (zone.WindowHandle != IntPtr.Zero)
        {
            WindowStyleHelper.PlaceWindowFlushTopmost(zone.WindowHandle, zone.Bounds.X, zone.Bounds.Y, zone.Bounds.Width, zone.Bounds.Height);
        }
    }

    private async Task OnPipAssignRequestedAsync(FloatingZone zone, ZoneChipWindow chip)
    {
        var choice = await AssignAppPopup.ShowAsync(chip, new PixelPoint(chip.Position.X, chip.Position.Y + 40));

        switch (choice.Kind)
        {
            case AssignChoiceKind.Free:
                zone.Status = ZoneSlotStatus.Free;
                chip.Render(ZoneSlotStatus.Free, null);
                break;

            case AssignChoiceKind.App when choice.AppPath is not null:
                await LaunchIntoPipAsync(zone, choice.AppPath, choice.DisplayName);
                break;
        }
    }

    private async Task LaunchIntoPipAsync(FloatingZone zone, string appPath, string? displayName)
    {
        var fallbackTitle = System.IO.Path.GetFileNameWithoutExtension(appPath);
        var title = displayName ?? fallbackTitle;

        zone.Chip.Render(ZoneSlotStatus.Assigned, $"Запуск: {title}...");

        var (process, handle) = await ProcessWindowLocator.LaunchAndWaitForWindowAsync(appPath);

        zone.AppPath = appPath;
        zone.DisplayName = displayName;
        zone.Process = process;
        zone.WindowHandle = handle;
        zone.Status = ZoneSlotStatus.Assigned;

        if (handle != IntPtr.Zero)
        {
            zone.OriginalWindowBounds = User32.GetWindowRect(handle, out var rect)
                ? new PixelRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top)
                : null;
            zone.IconBytes = AppIconExtractor.ExtractIconPng(
                File.Exists(appPath) ? appPath : AppIconExtractor.ResolveExePathFromWindow(handle));

            WindowStyleHelper.PlaceWindowFlushTopmost(handle, zone.Bounds.X, zone.Bounds.Y, zone.Bounds.Width, zone.Bounds.Height);
            ScheduleReconcile(zone);
        }

        zone.Chip.Render(ZoneSlotStatus.Assigned, title, zone.IconBytes);
        zone.Border.SetOccupied(true);
    }

    private void OnPipClearRequested(FloatingZone zone)
    {
        if (zone.WindowHandle != IntPtr.Zero && zone.OriginalWindowBounds is { } original)
        {
            WindowStyleHelper.PlaceWindowFlush(zone.WindowHandle, original.X, original.Y, original.Width, original.Height);
        }

        zone.Status = ZoneSlotStatus.Empty;
        zone.AppPath = null;
        zone.DisplayName = null;
        zone.Process = null;
        zone.WindowHandle = IntPtr.Zero;
        zone.OriginalWindowBounds = null;
        zone.IconBytes = null;
        zone.Chip.Render(ZoneSlotStatus.Empty, null);
        zone.Border.SetOccupied(false);
    }

    private void AssignDroppedWindowToPip(IntPtr hwnd)
    {
        if (_pip is not { } zone) return;

        zone.Status = ZoneSlotStatus.Assigned;
        zone.AppPath = null;
        zone.DisplayName = GetWindowTitle(hwnd);
        zone.Process = null;
        zone.WindowHandle = hwnd;
        zone.OriginalWindowBounds = User32.GetWindowRect(hwnd, out var rect)
            ? new PixelRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top)
            : null;
        zone.IconBytes = AppIconExtractor.ExtractIconPng(AppIconExtractor.ResolveExePathFromWindow(hwnd));

        WindowStyleHelper.PlaceWindowFlushTopmost(hwnd, zone.Bounds.X, zone.Bounds.Y, zone.Bounds.Width, zone.Bounds.Height);
        ScheduleReconcile(zone);

        zone.Chip.Render(ZoneSlotStatus.Assigned, zone.DisplayName, zone.IconBytes);
        zone.Border.SetOccupied(true);
    }

    private void ScheduleReconcile(FloatingZone zone)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            ReconcilePipBounds(zone);
        };
        timer.Start();
    }

    private void ReconcilePipBounds(FloatingZone zone)
    {
        if (_pip != zone || zone.WindowHandle == IntPtr.Zero) return;
        if (!User32.GetWindowRect(zone.WindowHandle, out var actual)) return;

        var actualWidth = actual.Right - actual.Left;
        var actualHeight = actual.Bottom - actual.Top;

        if (Math.Abs(actualWidth - zone.Bounds.Width) <= 6 && Math.Abs(actualHeight - zone.Bounds.Height) <= 6)
        {
            return; // всё совпало — приложение согласилось с запрошенным размером
        }

        zone.Bounds = new PixelRect(
            zone.Bounds.X,
            zone.Bounds.Y,
            Math.Max(actualWidth, PipMinWidth),
            Math.Max(actualHeight, PipMinHeight));

        var scaling = GetScreenScaling();
        zone.Border.PlaceAt(zone.Bounds, scaling);
        zone.Chip.PlaceAt(new PixelPoint(zone.Bounds.X + 12, zone.Bounds.Y + 28));
        PositionPipMoveHandle(zone);
        PositionPipGrips(zone);
    }

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
        _pip?.Border.SetDropTargetActive(true);

        _dragHoverTimer?.Stop();
        _dragHoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _dragHoverTimer.Tick += (_, _) => UpdateHoveredZone();
        _dragHoverTimer.Start();
    }

    private void UpdateHoveredZone()
    {
        if (!User32.GetCursorPos(out var cursor)) return;

        var overPip = _pip is not null && Contains(_pip.Bounds, cursor.X, cursor.Y);

        if (_pip is { } pip && overPip != pip.IsDropHighlighted)
        {
            pip.IsDropHighlighted = overPip;
            pip.Border.SetDropHighlighted(overPip);
        }

        foreach (var slot in _slots)
        {
            var inside = !overPip && Contains(slot.Bounds, cursor.X, cursor.Y);
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

        if (User32.GetCursorPos(out var cursor))
        {
            if (_pip is not null && Contains(_pip.Bounds, cursor.X, cursor.Y))
            {
                ResetDropVisuals();
                AssignDroppedWindowToPip(hwnd);
                return;
            }

            var target = _slots.FirstOrDefault(s => Contains(s.Bounds, cursor.X, cursor.Y));
            ResetDropVisuals();
            if (target is not null) AssignDroppedWindow(target, hwnd);
            return;
        }

        ResetDropVisuals();
    }

    private void ResetDropVisuals()
    {
        if (_pip is { } pip)
        {
            pip.Border.SetDropTargetActive(false);
            pip.Border.SetDropHighlighted(false);
            pip.IsDropHighlighted = false;
        }

        foreach (var slot in _slots)
        {
            slot.Border.SetDropTargetActive(false);
            slot.Border.SetDropHighlighted(false);
            slot.IsDropHighlighted = false;
        }
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
        slot.IconBytes = AppIconExtractor.ExtractIconPng(AppIconExtractor.ResolveExePathFromWindow(hwnd));

        WindowStyleHelper.PlaceWindowFlush(hwnd, slot.Bounds.X, slot.Bounds.Y, slot.Bounds.Width, slot.Bounds.Height);

        slot.Chip.Render(ZoneSlotStatus.Assigned, slot.DisplayName, slot.IconBytes);
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

    private void CreateSlot(int col, int row, PixelRect bounds, int index, double scaling)
    {
        var border = new ZoneBorderWindow();
        border.Show();
        border.PlaceAt(bounds, scaling);
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
            slot.IconBytes = AppIconExtractor.ExtractIconPng(
                File.Exists(appPath) ? appPath : AppIconExtractor.ResolveExePathFromWindow(handle));

            WindowStyleHelper.PlaceWindowFlush(handle, slot.Bounds.X, slot.Bounds.Y, slot.Bounds.Width, slot.Bounds.Height);
            WindowStyleHelper.ActivateWindow(handle);
        }

        slot.Chip.Render(ZoneSlotStatus.Assigned, title, slot.IconBytes);
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
        slot.IconBytes = null;
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
        (a.IconBytes, b.IconBytes) = (b.IconBytes, a.IconBytes);

        if (a.WindowHandle != IntPtr.Zero)
            WindowStyleHelper.PlaceWindowFlush(a.WindowHandle, a.Bounds.X, a.Bounds.Y, a.Bounds.Width, a.Bounds.Height);
        if (b.WindowHandle != IntPtr.Zero)
            WindowStyleHelper.PlaceWindowFlush(b.WindowHandle, b.Bounds.X, b.Bounds.Y, b.Bounds.Width, b.Bounds.Height);

        var aTitle = a.AppPath is null ? a.DisplayName : (a.DisplayName ?? System.IO.Path.GetFileNameWithoutExtension(a.AppPath));
        var bTitle = b.AppPath is null ? b.DisplayName : (b.DisplayName ?? System.IO.Path.GetFileNameWithoutExtension(b.AppPath));

        a.Chip.Render(a.Status, aTitle, a.IconBytes);
        b.Chip.Render(b.Status, bTitle, b.IconBytes);
        a.Border.SetOccupied(a.Status == ZoneSlotStatus.Assigned);
        b.Border.SetOccupied(b.Status == ZoneSlotStatus.Assigned);
    }

    public void CloseAllZones()
    {
        RemovePictureInPicture();
        ClearAll();
    }

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

        StopWatchersIfIdle();
    }
}