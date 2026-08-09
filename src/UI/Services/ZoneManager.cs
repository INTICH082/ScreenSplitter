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
    private const double MinFraction = 0.08;
    private const int SplitterThickness = 5;
    private const int PipMinWidth = 420;
    private const int PipMinHeight = 300;
    private const int MaxPipZones = 4;

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

        /// <summary>Увеличивается при каждом клике "назначить"/"открепить" — позволяет отличить
        /// "устаревший" результат долгого запуска (пользователь мог успеть очистить зону, пока
        /// приложение ещё открывалось) от актуального.</summary>
        public long Generation { get; set; }
    }

    /// <summary>Независимая сетка зон на одном конкретном мониторе. Несколько таких сеток могут
    /// существовать одновременно на разных мониторах — это и даёт возможность переносить окна
    /// между зонами на разных экранах.</summary>
    private class MonitorGrid
    {
        public required int ScreenIndex { get; init; }
        public int Cols;
        public int Rows;
        public double[] ColBounds = Array.Empty<double>();
        public double[] RowBounds = Array.Empty<double>();
        public readonly List<Slot> Slots = new();
        public readonly List<ZoneSplitterWindow> ColSplitters = new();
        public readonly List<ZoneSplitterWindow> RowSplitters = new();
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
        public long Generation;
    }

    private readonly Dictionary<int, MonitorGrid> _monitorGrids = new();
    private readonly List<FloatingZone> _pipZones = new();

    private Slot? _pendingSwap;
    private Window? _screenSource;
    private WindowMoveWatcher? _moveWatcher;
    private DispatcherTimer? _dragHoverTimer;
    private DispatcherTimer? _healthTimer;
    private IntPtr _draggedWindow;
    private int _targetScreenIndex = -1; // -1 = основной монитор (Primary)

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

    private Screen? GetScreenByIndex(int index)
    {
        var screens = _screenSource?.Screens;
        if (screens is null || index < 0 || index >= screens.All.Count) return null;
        return screens.All[index];
    }

    /// <summary>Превращает текущий выбор монитора (в т.ч. -1 = "основной") в конкретный индекс —
    /// нужно, чтобы несколько одновременных сеток на разных мониторах не путались между собой.</summary>
    private int ResolveScreenIndex()
    {
        var screens = _screenSource?.Screens;
        if (screens is null) return 0;

        if (_targetScreenIndex >= 0 && _targetScreenIndex < screens.All.Count) return _targetScreenIndex;

        for (int i = 0; i < screens.All.Count; i++)
        {
            if (screens.All[i].IsPrimary) return i;
        }
        return 0;
    }

    private PixelRect? GetActiveAreaFor(int screenIndex)
    {
        var screen = GetScreenByIndex(screenIndex);
        if (screen is null) return null;
        return TaskbarController.IsHidden ? screen.Bounds : screen.WorkingArea;
    }

    public void ApplyPattern(ZonePatternType type)
    {
        if (type == ZonePatternType.Single)
        {
            ClearGrid(ResolveScreenIndex());
            return;
        }

        Apply(LayoutPresets.GetPattern(type));
    }

    public void ApplyCustomGrid(int cols, int rows)
    {
        Apply(LayoutPresets.BuildGrid(cols, rows));
    }

    /// <summary>Сохраняет разбивку и назначения зон ТЕКУЩЕГО выбранного монитора как сценарий.
    /// Возвращает null, если на этом мониторе сейчас нет активной разбивки.</summary>
    public Profile? CaptureCurrentAsProfile(string name)
    {
        var screenIndex = ResolveScreenIndex();
        if (!_monitorGrids.TryGetValue(screenIndex, out var grid) || grid.Slots.Count == 0) return null;

        var assignments = grid.Slots.Select(s => new ZoneAssignment(
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
            Cols = grid.Cols,
            Rows = grid.Rows,
            Assignments = assignments
        };
    }

    public async Task ApplyProfileAsync(Profile profile)
    {
        Apply(LayoutPresets.BuildGrid(profile.Cols, profile.Rows));

        var screenIndex = ResolveScreenIndex();
        if (!_monitorGrids.TryGetValue(screenIndex, out var grid)) return;

        foreach (var assignment in profile.Assignments)
        {
            var slot = grid.Slots.FirstOrDefault(s => s.Col == assignment.Col && s.Row == assignment.Row);
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
        var screenIndex = ResolveScreenIndex();
        ClearGrid(screenIndex);

        var area = GetActiveAreaFor(screenIndex);
        if (area is not { } workingArea) return;

        var xs = pattern.Select(r => Math.Round(r.X, 6)).Distinct().OrderBy(v => v).ToList();
        var ys = pattern.Select(r => Math.Round(r.Y, 6)).Distinct().OrderBy(v => v).ToList();

        var grid = new MonitorGrid
        {
            ScreenIndex = screenIndex,
            Cols = xs.Count,
            Rows = ys.Count,
            ColBounds = xs.Append(1.0).ToArray(),
            RowBounds = ys.Append(1.0).ToArray()
        };

        var scaling = GetScreenByIndex(screenIndex)?.Scaling ?? 1.0;

        var index = 1;
        foreach (var rel in pattern)
        {
            var col = xs.IndexOf(Math.Round(rel.X, 6));
            var row = ys.IndexOf(Math.Round(rel.Y, 6));
            var bounds = ZoneBounds(grid, col, row, workingArea);
            CreateSlot(grid, col, row, bounds, index++, scaling);
        }

        CreateSplitters(grid, workingArea, scaling);
        _monitorGrids[screenIndex] = grid;

        EnsureWatchersRunning();
    }

    private void EnsureWatchersRunning()
    {
        EnsureMoveWatcherStarted();
        EnsureHealthTimerStarted();
    }

    private void StopWatchersIfIdle()
    {
        if (_monitorGrids.Count > 0 || _pipZones.Count > 0) return;

        _dragHoverTimer?.Stop();
        _dragHoverTimer = null;

        _healthTimer?.Stop();
        _healthTimer = null;

        _moveWatcher?.Dispose();
        _moveWatcher = null;
    }

    /// <summary>
    /// Периодически проверяет "здоровье" всех занятых зон (на всех мониторах и в PiP):
    /// — если окно закрыто, зона автоматически возвращается в пустое состояние;
    /// — если окно "зависло" (не отвечает на сообщения), на чипе показывается предупреждение,
    ///   но зона не сбрасывается — приложение может ещё отойти.
    /// </summary>
    private void EnsureHealthTimerStarted()
    {
        if (_healthTimer is not null) return;

        _healthTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _healthTimer.Tick += (_, _) => CheckWindowHealth();
        _healthTimer.Start();
    }

    private void CheckWindowHealth()
    {
        foreach (var grid in _monitorGrids.Values)
        {
            foreach (var slot in grid.Slots)
            {
                CheckOneWindowHealth(
                    slot.Status, slot.WindowHandle,
                    onClosed: () => ResetSlotToEmpty(slot),
                    onHealthChanged: hung => slot.Chip.SetHungWarning(hung));
            }
        }

        foreach (var pip in _pipZones)
        {
            CheckOneWindowHealth(
                pip.Status, pip.WindowHandle,
                onClosed: () => OnPipClearRequested(pip),
                onHealthChanged: hung => pip.Chip.SetHungWarning(hung));
        }
    }

    private static void CheckOneWindowHealth(ZoneSlotStatus status, IntPtr handle, Action onClosed, Action<bool> onHealthChanged)
    {
        if (status != ZoneSlotStatus.Assigned || handle == IntPtr.Zero) return;

        if (!User32.IsWindow(handle))
        {
            onClosed();
            return;
        }

        var hung = User32.IsHungAppWindow(handle);
        onHealthChanged(hung);
    }

    private void ResetSlotToEmpty(Slot slot)
    {
        slot.Generation++;
        slot.Status = ZoneSlotStatus.Empty;
        slot.AppPath = null;
        slot.DisplayName = null;
        slot.Process?.Dispose();
        slot.Process = null;
        slot.WindowHandle = IntPtr.Zero;
        slot.OriginalWindowBounds = null;
        slot.IconBytes = null;
        slot.Chip.Render(ZoneSlotStatus.Empty, null);
        slot.Border.SetOccupied(false);
    }

    private PixelRect ZoneBounds(MonitorGrid grid, int col, int row, PixelRect area)
    {
        var x0 = grid.ColBounds[col];
        var x1 = grid.ColBounds[col + 1];
        var y0 = grid.RowBounds[row];
        var y1 = grid.RowBounds[row + 1];

        return new PixelRect(
            area.X + (int)(x0 * area.Width),
            area.Y + (int)(y0 * area.Height),
            (int)((x1 - x0) * area.Width),
            (int)((y1 - y0) * area.Height));
    }

    public void RecomputeLayout() => RepositionAll();

    private void RepositionAll()
    {
        foreach (var grid in _monitorGrids.Values)
        {
            var area = GetActiveAreaFor(grid.ScreenIndex);
            if (area is not { } workingArea) continue;

            var scaling = GetScreenByIndex(grid.ScreenIndex)?.Scaling ?? 1.0;

            foreach (var slot in grid.Slots)
            {
                var bounds = ZoneBounds(grid, slot.Col, slot.Row, workingArea);
                slot.Bounds = bounds;

                slot.Border.PlaceAt(bounds, scaling);
                slot.Chip.PlaceAt(new PixelPoint(bounds.X + 12, bounds.Y + 12));

                if (slot.WindowHandle != IntPtr.Zero)
                {
                    WindowStyleHelper.PlaceWindowFlush(slot.WindowHandle, bounds.X, bounds.Y, bounds.Width, bounds.Height);
                }
            }

            for (int i = 0; i < grid.ColSplitters.Count; i++)
            {
                PositionColumnSplitter(grid, grid.ColSplitters[i], i + 1, workingArea, scaling);
            }
            for (int j = 0; j < grid.RowSplitters.Count; j++)
            {
                PositionRowSplitter(grid, grid.RowSplitters[j], j + 1, workingArea, scaling);
            }
        }

        var currentArea = GetActiveAreaFor(ResolveScreenIndex());
        if (currentArea is { } clampArea)
        {
            foreach (var pip in _pipZones)
            {
                var clampedX = Math.Clamp(pip.Bounds.X, clampArea.X, Math.Max(clampArea.X, clampArea.X + clampArea.Width - pip.Bounds.Width));
                var clampedY = Math.Clamp(pip.Bounds.Y, clampArea.Y, Math.Max(clampArea.Y, clampArea.Y + clampArea.Height - pip.Bounds.Height));
                pip.Bounds = new PixelRect(clampedX, clampedY, pip.Bounds.Width, pip.Bounds.Height);
                ApplyPipBounds(pip);
            }
        }
    }

    private void CreateSplitters(MonitorGrid grid, PixelRect area, double scaling)
    {
        for (int i = 1; i < grid.Cols; i++)
        {
            CreateColumnSplitter(grid, i, area, scaling);
        }
        for (int j = 1; j < grid.Rows; j++)
        {
            CreateRowSplitter(grid, j, area, scaling);
        }
    }

    private void CreateColumnSplitter(MonitorGrid grid, int boundaryIndex, PixelRect area, double scaling)
    {
        var splitter = new ZoneSplitterWindow(ZoneSplitterWindow.SplitterOrientation.Vertical);
        splitter.Show();
        PositionColumnSplitter(grid, splitter, boundaryIndex, area, scaling);

        double startFraction = 0;
        splitter.DragStarted += () => startFraction = grid.ColBounds[boundaryIndex];
        splitter.DragDelta += delta =>
        {
            var a = GetActiveAreaFor(grid.ScreenIndex);
            if (a is not { } ar || ar.Width <= 0) return;
            SetColumnBoundary(grid, boundaryIndex, startFraction + delta / ar.Width);
        };

        grid.ColSplitters.Add(splitter);
    }

    private void CreateRowSplitter(MonitorGrid grid, int boundaryIndex, PixelRect area, double scaling)
    {
        var splitter = new ZoneSplitterWindow(ZoneSplitterWindow.SplitterOrientation.Horizontal);
        splitter.Show();
        PositionRowSplitter(grid, splitter, boundaryIndex, area, scaling);

        double startFraction = 0;
        splitter.DragStarted += () => startFraction = grid.RowBounds[boundaryIndex];
        splitter.DragDelta += delta =>
        {
            var a = GetActiveAreaFor(grid.ScreenIndex);
            if (a is not { } ar || ar.Height <= 0) return;
            SetRowBoundary(grid, boundaryIndex, startFraction + delta / ar.Height);
        };

        grid.RowSplitters.Add(splitter);
    }

    private void PositionColumnSplitter(MonitorGrid grid, ZoneSplitterWindow splitter, int boundaryIndex, PixelRect area, double scaling)
    {
        var x = area.X + (int)(grid.ColBounds[boundaryIndex] * area.Width) - SplitterThickness / 2;
        splitter.PlaceAt(new PixelRect(x, area.Y, SplitterThickness, area.Height), scaling);
    }

    private void PositionRowSplitter(MonitorGrid grid, ZoneSplitterWindow splitter, int boundaryIndex, PixelRect area, double scaling)
    {
        var y = area.Y + (int)(grid.RowBounds[boundaryIndex] * area.Height) - SplitterThickness / 2;
        splitter.PlaceAt(new PixelRect(area.X, y, area.Width, SplitterThickness), scaling);
    }

    private void SetColumnBoundary(MonitorGrid grid, int boundaryIndex, double newFraction)
    {
        var min = grid.ColBounds[boundaryIndex - 1] + MinFraction;
        var max = grid.ColBounds[boundaryIndex + 1] - MinFraction;
        if (min > max) return;

        newFraction = Math.Clamp(newFraction, min, max);
        if (Math.Abs(newFraction - grid.ColBounds[boundaryIndex]) < 1e-6) return;

        grid.ColBounds[boundaryIndex] = newFraction;
        RepositionAll();
    }

    private void SetRowBoundary(MonitorGrid grid, int boundaryIndex, double newFraction)
    {
        var min = grid.RowBounds[boundaryIndex - 1] + MinFraction;
        var max = grid.RowBounds[boundaryIndex + 1] - MinFraction;
        if (min > max) return;

        newFraction = Math.Clamp(newFraction, min, max);
        if (Math.Abs(newFraction - grid.RowBounds[boundaryIndex]) < 1e-6) return;

        grid.RowBounds[boundaryIndex] = newFraction;
        RepositionAll();
    }

    // --- Плавающие зоны "картинка в картинке" (PiP) — можно создать несколько одновременно ---

    public bool HasPictureInPicture => _pipZones.Count > 0;

    public int PipZoneCount => _pipZones.Count;

    /// <summary>Добавляет ещё одну плавающую PiP-зону (до MaxPipZones штук одновременно).</summary>
    public void AddPictureInPictureZone()
    {
        if (_pipZones.Count >= MaxPipZones) return;

        var screenIndex = ResolveScreenIndex();
        var area = GetActiveAreaFor(screenIndex);
        if (area is not { } workingArea) return;

        var scaling = GetScreenByIndex(screenIndex)?.Scaling ?? 1.0;

        var width = Math.Max(PipMinWidth, (int)(workingArea.Width * 0.26));
        var height = Math.Max(PipMinHeight, (int)(workingArea.Height * 0.26));

        // Каждая следующая зона смещена по диагонали, чтобы несколько PiP не накладывались друг на друга полностью.
        var offset = _pipZones.Count * 32;
        var x = Math.Clamp(workingArea.X + workingArea.Width - width - 24 - offset, workingArea.X, workingArea.X + workingArea.Width - width);
        var y = Math.Clamp(workingArea.Y + workingArea.Height - height - 24 - offset, workingArea.Y, workingArea.Y + workingArea.Height - height);
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
        _pipZones.Add(zone);

        chip.AssignRequested += (_, _) => FireAndForget(OnPipAssignRequestedAsync(zone, chip));
        chip.ClearRequested += (_, _) => OnPipClearRequested(zone);
        moveHandle.CloseRequested += () => RemovePictureInPictureZone(zone);

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

    private void RemovePictureInPictureZone(FloatingZone zone)
    {
        if (zone.WindowHandle != IntPtr.Zero && zone.OriginalWindowBounds is { } original)
        {
            WindowStyleHelper.PlaceWindowFlush(zone.WindowHandle, original.X, original.Y, original.Width, original.Height);
        }

        zone.Border.Close();
        zone.Chip.Close();
        zone.MoveHandle.Close();
        foreach (var grip in zone.Grips.Values) grip.Close();
        zone.Process?.Dispose();
        _pipZones.Remove(zone);

        StopWatchersIfIdle();
    }

    private void RemoveAllPictureInPictureZones()
    {
        foreach (var zone in _pipZones.ToList())
        {
            RemovePictureInPictureZone(zone);
        }
    }

    private void PositionPipMoveHandle(FloatingZone zone)
    {
        zone.MoveHandle.PlaceAt(zone.Bounds, GetScreenByIndex(ResolveScreenIndex())?.Scaling ?? 1.0);
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
        var scaling = GetScreenByIndex(ResolveScreenIndex())?.Scaling ?? 1.0;
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
                zone.Generation++;
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
        var myGeneration = ++zone.Generation;
        var fallbackTitle = System.IO.Path.GetFileNameWithoutExtension(appPath);
        var title = displayName ?? fallbackTitle;

        zone.Chip.Render(ZoneSlotStatus.Assigned, $"Запуск: {title}...");

        var (process, handle) = await ProcessWindowLocator.LaunchAndWaitForWindowAsync(appPath);

        if (zone.Generation != myGeneration)
        {
            // Пока приложение запускалось, зону уже открепили/переназначили — не воскрешаем старое
            // назначение, просто освобождаем то, что успели захватить (само окно приложения при этом
            // никуда не денется, просто останется там, где само открылось).
            process?.Dispose();
            return;
        }

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

            WindowStyleHelper.PlaceWindowFlushTopmost(handle, zone.Bounds.X, zone.Bounds.Y, zone.Bounds.Width, zone.Bounds.Height);
            ScheduleReconcile(zone);

            var capturedAppPath = appPath;
            var capturedHandle = handle;
            var iconBytes = await Task.Run(() =>
            {
                var iconSourcePath = File.Exists(capturedAppPath) ? capturedAppPath : AppIconExtractor.ResolveExePathFromWindow(capturedHandle);
                return AppIconExtractor.ExtractIconPng(iconSourcePath);
            });

            if (zone.Generation != myGeneration) return; // снова проверяем — вдруг очистили именно во время загрузки иконки
            zone.IconBytes = iconBytes;
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

        zone.Generation++;
        zone.Status = ZoneSlotStatus.Empty;
        zone.AppPath = null;
        zone.DisplayName = null;
        zone.Process?.Dispose();
        zone.Process = null;
        zone.WindowHandle = IntPtr.Zero;
        zone.OriginalWindowBounds = null;
        zone.IconBytes = null;
        zone.Chip.Render(ZoneSlotStatus.Empty, null);
        zone.Chip.SetHungWarning(false);
        zone.Border.SetOccupied(false);
    }

    private void AssignDroppedWindowToPip(FloatingZone zone, IntPtr hwnd)
    {
        zone.Generation++;
        zone.Status = ZoneSlotStatus.Assigned;
        zone.AppPath = null;
        zone.DisplayName = GetWindowTitle(hwnd);
        zone.Process?.Dispose(); // на случай, если в зоне до этого было запущенное нами приложение
        zone.Process = null;
        zone.WindowHandle = hwnd;
        zone.OriginalWindowBounds = User32.GetWindowRect(hwnd, out var rect)
            ? new PixelRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top)
            : null;
        zone.IconBytes = null;

        WindowStyleHelper.PlaceWindowFlushTopmost(hwnd, zone.Bounds.X, zone.Bounds.Y, zone.Bounds.Width, zone.Bounds.Height);
        ScheduleReconcile(zone);

        zone.Chip.Render(ZoneSlotStatus.Assigned, zone.DisplayName, null);
        zone.Border.SetOccupied(true);

        FireAndForget(LoadPipIconInBackgroundAsync(zone, hwnd, zone.Generation));
    }

    /// <summary>Извлекает иконку приложения на фоновом потоке (файловый I/O + GDI+ — не должно
    /// подвешивать UI) и обновляет чип, только если зона за это время не успела измениться.</summary>
    private async Task LoadPipIconInBackgroundAsync(FloatingZone zone, IntPtr hwnd, long generation)
    {
        var iconBytes = await Task.Run(() => AppIconExtractor.ExtractIconPng(AppIconExtractor.ResolveExePathFromWindow(hwnd)));

        if (zone.Generation != generation) return; // зону уже успели открепить/переназначить — не перетираем

        zone.IconBytes = iconBytes;
        zone.Chip.Render(zone.Status, zone.DisplayName, iconBytes);
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
        if (!_pipZones.Contains(zone) || zone.WindowHandle == IntPtr.Zero) return;
        if (!User32.GetWindowRect(zone.WindowHandle, out var actual)) return;

        var actualWidth = actual.Right - actual.Left;
        var actualHeight = actual.Bottom - actual.Top;

        if (Math.Abs(actualWidth - zone.Bounds.Width) <= 6 && Math.Abs(actualHeight - zone.Bounds.Height) <= 6)
        {
            return;
        }

        zone.Bounds = new PixelRect(
            zone.Bounds.X,
            zone.Bounds.Y,
            Math.Max(actualWidth, PipMinWidth),
            Math.Max(actualHeight, PipMinHeight));

        var scaling = GetScreenByIndex(ResolveScreenIndex())?.Scaling ?? 1.0;
        zone.Border.PlaceAt(zone.Bounds, scaling);
        zone.Chip.PlaceAt(new PixelPoint(zone.Bounds.X + 12, zone.Bounds.Y + 28));
        PositionPipMoveHandle(zone);
        PositionPipGrips(zone);
    }

    // --- Перетаскивание окон и подсветка зон-целей (работает через все мониторы сразу) ---

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

        foreach (var grid in _monitorGrids.Values)
        {
            foreach (var slot in grid.Slots) slot.Border.SetDropTargetActive(true);
        }
        foreach (var pip in _pipZones) pip.Border.SetDropTargetActive(true);

        _dragHoverTimer?.Stop();
        _dragHoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _dragHoverTimer.Tick += (_, _) => UpdateHoveredZone();
        _dragHoverTimer.Start();
    }

    private void UpdateHoveredZone()
    {
        if (!User32.GetCursorPos(out var cursor)) return;

        // PiP-зоны визуально лежат поверх сеточных — проверяем их первыми (в порядке от последней
        // созданной к первой, поскольку более новые обычно оказываются выше по z-order).
        FloatingZone? hoveredPip = null;
        for (int i = _pipZones.Count - 1; i >= 0; i--)
        {
            if (Contains(_pipZones[i].Bounds, cursor.X, cursor.Y))
            {
                hoveredPip = _pipZones[i];
                break;
            }
        }

        foreach (var pip in _pipZones)
        {
            var isHovered = ReferenceEquals(pip, hoveredPip);
            if (isHovered != pip.IsDropHighlighted)
            {
                pip.IsDropHighlighted = isHovered;
                pip.Border.SetDropHighlighted(isHovered);
            }
        }

        foreach (var grid in _monitorGrids.Values)
        {
            foreach (var slot in grid.Slots)
            {
                var inside = hoveredPip is null && Contains(slot.Bounds, cursor.X, cursor.Y);
                if (inside != slot.IsDropHighlighted)
                {
                    slot.IsDropHighlighted = inside;
                    slot.Border.SetDropHighlighted(inside);
                }
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
            for (int i = _pipZones.Count - 1; i >= 0; i--)
            {
                if (Contains(_pipZones[i].Bounds, cursor.X, cursor.Y))
                {
                    var pipTarget = _pipZones[i];
                    ResetDropVisuals();
                    AssignDroppedWindowToPip(pipTarget, hwnd);
                    return;
                }
            }

            foreach (var grid in _monitorGrids.Values)
            {
                var target = grid.Slots.FirstOrDefault(s => Contains(s.Bounds, cursor.X, cursor.Y));
                if (target is not null)
                {
                    ResetDropVisuals();
                    AssignDroppedWindow(target, hwnd);
                    return;
                }
            }
        }

        ResetDropVisuals();
    }

    private void ResetDropVisuals()
    {
        foreach (var pip in _pipZones)
        {
            pip.Border.SetDropTargetActive(false);
            pip.Border.SetDropHighlighted(false);
            pip.IsDropHighlighted = false;
        }

        foreach (var grid in _monitorGrids.Values)
        {
            foreach (var slot in grid.Slots)
            {
                slot.Border.SetDropTargetActive(false);
                slot.Border.SetDropHighlighted(false);
                slot.IsDropHighlighted = false;
            }
        }
    }

    private void AssignDroppedWindow(Slot slot, IntPtr hwnd)
    {
        slot.Generation++;
        slot.Status = ZoneSlotStatus.Assigned;
        slot.AppPath = null;
        slot.DisplayName = GetWindowTitle(hwnd);
        slot.Process?.Dispose(); // на случай, если в зоне до этого было запущенное нами приложение
        slot.Process = null;
        slot.WindowHandle = hwnd;
        slot.OriginalWindowBounds = User32.GetWindowRect(hwnd, out var rect)
            ? new PixelRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top)
            : null;
        slot.IconBytes = null;

        WindowStyleHelper.PlaceWindowFlush(hwnd, slot.Bounds.X, slot.Bounds.Y, slot.Bounds.Width, slot.Bounds.Height);

        slot.Chip.Render(ZoneSlotStatus.Assigned, slot.DisplayName, null);
        slot.Border.SetOccupied(true);

        FireAndForget(LoadSlotIconInBackgroundAsync(slot, hwnd, slot.Generation));
    }

    private async Task LoadSlotIconInBackgroundAsync(Slot slot, IntPtr hwnd, long generation)
    {
        var iconBytes = await Task.Run(() => AppIconExtractor.ExtractIconPng(AppIconExtractor.ResolveExePathFromWindow(hwnd)));

        if (slot.Generation != generation) return; // зону уже успели открепить/переназначить — не перетираем

        slot.IconBytes = iconBytes;
        slot.Chip.Render(slot.Status, slot.DisplayName, iconBytes);
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

    /// <summary>
    /// Безопасно запускает асинхронную операцию "в фоне" (без ожидания результата вызывающей стороной).
    /// Без этого любая необработанная ошибка внутри такой операции (например, сбой при запуске
    /// приложения) улетела бы как необработанное исключение и — через общий обработчик крашей
    /// приложения — закрыла бы ScreenSplitter целиком из-за второстепенной фоновой проблемы.
    /// </summary>
    private static async void FireAndForget(Task task)
    {
        try
        {
            await task;
        }
        catch
        {
            // Фоновая операция не должна ронять всё приложение — просто прерываем именно её.
        }
    }

    private void CreateSlot(MonitorGrid grid, int col, int row, PixelRect bounds, int index, double scaling)
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

        chip.AssignRequested += (_, _) => FireAndForget(OnAssignRequestedAsync(slot, chip));
        chip.ClearRequested += (_, _) => OnClearRequested(slot);
        chip.SwapClicked += (_, _) => OnSwapClicked(slot);

        grid.Slots.Add(slot);
    }

    private async Task OnAssignRequestedAsync(Slot slot, ZoneChipWindow chip)
    {
        var choice = await AssignAppPopup.ShowAsync(chip, new PixelPoint(chip.Position.X, chip.Position.Y + 40));

        switch (choice.Kind)
        {
            case AssignChoiceKind.Free:
                slot.Generation++;
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
        var myGeneration = ++slot.Generation;
        var fallbackTitle = System.IO.Path.GetFileNameWithoutExtension(appPath);
        var title = displayName ?? fallbackTitle;

        slot.Chip.Render(ZoneSlotStatus.Assigned, $"Запуск: {title}...");

        var (process, handle) = await ProcessWindowLocator.LaunchAndWaitForWindowAsync(appPath);

        if (slot.Generation != myGeneration)
        {
            process?.Dispose();
            return;
        }

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

            var capturedAppPath = appPath;
            var capturedHandle = handle;
            var iconBytes = await Task.Run(() =>
            {
                var iconSourcePath = File.Exists(capturedAppPath) ? capturedAppPath : AppIconExtractor.ResolveExePathFromWindow(capturedHandle);
                return AppIconExtractor.ExtractIconPng(iconSourcePath);
            });

            if (slot.Generation != myGeneration) return;
            slot.IconBytes = iconBytes;
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

        ResetSlotToEmpty(slot);
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

    /// <summary>Закрывает все окна-оверлеи зон на всех мониторах (включая все PiP). Используется при
    /// аварийном завершении/краше приложения и обычном выходе — чтобы не оставлять "зависшие" рамки.</summary>
    public void CloseAllZones()
    {
        RemoveAllPictureInPictureZones();
        foreach (var screenIndex in _monitorGrids.Keys.ToList())
        {
            ClearGrid(screenIndex);
        }
    }

    private void ClearGrid(int screenIndex)
    {
        if (!_monitorGrids.TryGetValue(screenIndex, out var grid)) return;

        if (_pendingSwap is not null && grid.Slots.Contains(_pendingSwap))
        {
            _pendingSwap = null;
        }

        foreach (var slot in grid.Slots)
        {
            slot.Border.Close();
            slot.Chip.Close();
            slot.Process?.Dispose();
        }
        foreach (var splitter in grid.ColSplitters) splitter.Close();
        foreach (var splitter in grid.RowSplitters) splitter.Close();

        _monitorGrids.Remove(screenIndex);
        StopWatchersIfIdle();
    }
}