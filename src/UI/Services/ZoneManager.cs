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
    private class Slot
    {
        public required RelativeZoneRect Relative { get; init; }
        public required PixelRect Bounds { get; init; }
        public required ZoneBorderWindow Border { get; init; }
        public required ZoneChipWindow Chip { get; init; }

        public ZoneSlotStatus Status { get; set; } = ZoneSlotStatus.Empty;
        public string? AppPath { get; set; }
        public string? DisplayName { get; set; }
        public System.Diagnostics.Process? Process { get; set; }
        public IntPtr WindowHandle { get; set; }
        public bool IsDropHighlighted { get; set; }
    }

    private readonly List<Slot> _slots = new();
    private Slot? _pendingSwap;
    private Window? _screenSource;
    private WindowMoveWatcher? _moveWatcher;
    private DispatcherTimer? _dragHoverTimer;
    private IntPtr _draggedWindow;

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

    private void Apply(IReadOnlyList<RelativeZoneRect> pattern)
    {
        ClearAll();

        var area = _screenSource?.Screens.Primary?.WorkingArea;
        if (area is not { } workingArea) return;

        foreach (var rel in pattern)
        {
            var bounds = new PixelRect(
                workingArea.X + (int)(rel.X * workingArea.Width),
                workingArea.Y + (int)(rel.Y * workingArea.Height),
                (int)(rel.Width * workingArea.Width),
                (int)(rel.Height * workingArea.Height));

            CreateSlot(rel, bounds, _slots.Count + 1);
        }

        EnsureMoveWatcherStarted();
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

        WindowStyleHelper.PlaceWindowFlush(hwnd, slot.Bounds.X, slot.Bounds.Y, slot.Bounds.Width, slot.Bounds.Height);

        slot.Chip.Render(ZoneSlotStatus.Assigned, slot.DisplayName);
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

    private void CreateSlot(RelativeZoneRect relative, PixelRect bounds, int index)
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
            Relative = relative,
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
            WindowStyleHelper.PlaceWindowFlush(handle, slot.Bounds.X, slot.Bounds.Y, slot.Bounds.Width, slot.Bounds.Height);
            WindowStyleHelper.ActivateWindow(handle);
        }

        slot.Chip.Render(ZoneSlotStatus.Assigned, title);
    }

    private void OnClearRequested(Slot slot)
    {
        slot.Status = ZoneSlotStatus.Empty;
        slot.AppPath = null;
        slot.DisplayName = null;
        slot.Process = null;
        slot.WindowHandle = IntPtr.Zero;
        slot.Chip.Render(ZoneSlotStatus.Empty, null);
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

        if (a.WindowHandle != IntPtr.Zero)
            WindowStyleHelper.PlaceWindowFlush(a.WindowHandle, a.Bounds.X, a.Bounds.Y, a.Bounds.Width, a.Bounds.Height);
        if (b.WindowHandle != IntPtr.Zero)
            WindowStyleHelper.PlaceWindowFlush(b.WindowHandle, b.Bounds.X, b.Bounds.Y, b.Bounds.Width, b.Bounds.Height);

        var aTitle = a.AppPath is null ? a.DisplayName : (a.DisplayName ?? System.IO.Path.GetFileNameWithoutExtension(a.AppPath));
        var bTitle = b.AppPath is null ? b.DisplayName : (b.DisplayName ?? System.IO.Path.GetFileNameWithoutExtension(b.AppPath));

        a.Chip.Render(a.Status, aTitle);
        b.Chip.Render(b.Status, bTitle);
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

        _dragHoverTimer?.Stop();
        _dragHoverTimer = null;

        _moveWatcher?.Dispose();
        _moveWatcher = null;
    }
}