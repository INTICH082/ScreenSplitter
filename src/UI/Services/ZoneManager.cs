using Avalonia;
using Avalonia.Controls;
using ScreenSplitter.Core;
using ScreenSplitter.Core.Models;
using ScreenSplitter.Platform.Windows;
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
        public System.Diagnostics.Process? Process { get; set; }
        public IntPtr WindowHandle { get; set; }
    }

    private readonly List<Slot> _slots = new();
    private Slot? _pendingSwap;
    private Window? _screenSource;

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

            CreateSlot(rel, bounds);
        }
    }

    private void CreateSlot(RelativeZoneRect relative, PixelRect bounds)
    {
        var border = new ZoneBorderWindow();
        border.Show();
        border.PlaceAt(bounds);

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
                await LaunchIntoSlotAsync(slot, choice.AppPath);
                break;
        }
    }

    private async Task LaunchIntoSlotAsync(Slot slot, string appPath)
    {
        chipBusy(slot.Chip, appPath);

        var (process, handle) = await ProcessWindowLocator.LaunchAndWaitForWindowAsync(appPath);

        slot.AppPath = appPath;
        slot.Process = process;
        slot.WindowHandle = handle;
        slot.Status = ZoneSlotStatus.Assigned;

        var title = System.IO.Path.GetFileNameWithoutExtension(appPath);

        if (handle != IntPtr.Zero)
        {
            PlaceAppWindow(handle, slot.Bounds);
            WindowStyleHelper.ActivateWindow(handle);
        }

        slot.Chip.Render(ZoneSlotStatus.Assigned, title);

        static void chipBusy(ZoneChipWindow chip, string path) =>
            chip.Render(ZoneSlotStatus.Assigned, $"Запуск: {System.IO.Path.GetFileNameWithoutExtension(path)}...");
    }

    private void OnClearRequested(Slot slot)
    {
        slot.Status = ZoneSlotStatus.Empty;
        slot.AppPath = null;
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
            return;
        }

        if (ReferenceEquals(_pendingSwap, slot))
        {
            _pendingSwap.Chip.SetSelectedForSwap(false);
            _pendingSwap = null;
            return;
        }

        SwapSlots(_pendingSwap, slot);
        _pendingSwap.Chip.SetSelectedForSwap(false);
        _pendingSwap = null;
    }

    private void SwapSlots(Slot a, Slot b)
    {
        (a.Status, b.Status) = (b.Status, a.Status);
        (a.AppPath, b.AppPath) = (b.AppPath, a.AppPath);
        (a.Process, b.Process) = (b.Process, a.Process);
        (a.WindowHandle, b.WindowHandle) = (b.WindowHandle, a.WindowHandle);

        if (a.WindowHandle != IntPtr.Zero) PlaceAppWindow(a.WindowHandle, a.Bounds);
        if (b.WindowHandle != IntPtr.Zero) PlaceAppWindow(b.WindowHandle, b.Bounds);

        var aTitle = a.AppPath is null ? null : System.IO.Path.GetFileNameWithoutExtension(a.AppPath);
        var bTitle = b.AppPath is null ? null : System.IO.Path.GetFileNameWithoutExtension(b.AppPath);

        a.Chip.Render(a.Status, aTitle);
        b.Chip.Render(b.Status, bTitle);
    }

    private static void PlaceAppWindow(IntPtr handle, PixelRect bounds)
    {
        const int margin = 6;
        WindowStyleHelper.MoveWindow(
            handle,
            bounds.X + margin,
            bounds.Y + margin,
            bounds.Width - margin * 2,
            bounds.Height - margin * 2);
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
    }
}