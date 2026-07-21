using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ScreenSplitter.Core.Models;
using ScreenSplitter.Platform.Windows;
using ScreenSplitter.Platform.Windows.Native;
using ScreenSplitter.UI.Services;
using ScreenSplitter.UI.Views;

namespace ScreenSplitter.UI;

public partial class App : Application
{
    private OverlayMenuWindow? _overlayWindow;
    private readonly ZoneManager _zoneManager = new();
    private GlobalHotKeyManager? _hotKeys;
    private DisplayChangeWatcher? _displayWatcher;
    private readonly List<int> _profileHotkeyIds = new();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _overlayWindow = new OverlayMenuWindow(_zoneManager);
            _zoneManager.AttachScreenSource(_overlayWindow);
            _overlayWindow.Show();

            RegisterEmergencyHotKeys();
            RegisterDisplayChangeWatcher();
            RebuildProfileHotkeys();
            ShowOnboardingIfFirstLaunch();
            _ = CheckForUpdatesAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ShowOnboardingIfFirstLaunch()
    {
        try
        {
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScreenSplitter");
            System.IO.Directory.CreateDirectory(dir);
            var marker = System.IO.Path.Combine(dir, "onboarded.flag");

            if (System.IO.File.Exists(marker)) return;

            System.IO.File.WriteAllText(marker, DateTime.UtcNow.ToString("O"));
            new OnboardingWindow().Show();
        }
        catch
        {
            // если не удалось создать маркер — лучше молча пропустить онбординг, чем упасть
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
            var update = await UpdateChecker.CheckForNewerVersionAsync(currentVersion);
            if (update is not null)
            {
                _overlayWindow?.ShowUpdateAvailable(update.TagName, update.HtmlUrl);
            }
        }
        catch
        {
            // проверка обновлений не должна мешать работе приложения
        }
    }

    private void RegisterEmergencyHotKeys()
    {
        var handle = _overlayWindow?.TryGetPlatformHandle();
        if (handle is null || handle.Handle == IntPtr.Zero) return;

        try
        {
            _hotKeys = new GlobalHotKeyManager(handle.Handle);
            _hotKeys.Register(User32.MOD_CONTROL | User32.MOD_ALT, User32.VK_ESCAPE, EmergencyShutdown);
            _hotKeys.Register(User32.MOD_CONTROL | User32.MOD_ALT, User32.VK_R, () => _zoneManager.ApplyPattern(ZonePatternType.Single));
        }
        catch
        {
            // Комбинация уже занята другой программой — не критично, приложение продолжает работать без неё
        }
    }

    private void RegisterDisplayChangeWatcher()
    {
        var handle = _overlayWindow?.TryGetPlatformHandle();
        if (handle is null || handle.Handle == IntPtr.Zero) return;

        _displayWatcher = new DisplayChangeWatcher(handle.Handle);
        _displayWatcher.DisplayChanged += () => _zoneManager.RecomputeLayout();
    }

    /// Перечитывает сохранённые сценарии и перерегистрирует их горячие клавиши (Ctrl+Alt+1..9).
    /// Вызывается при старте и после любых изменений в окне управления сценариями

    public void RebuildProfileHotkeys()
    {
        if (_hotKeys is null) return;

        foreach (var id in _profileHotkeyIds)
        {
            _hotKeys.Unregister(id);
        }
        _profileHotkeyIds.Clear();

        var profiles = ProfileStore.LoadAll();
        foreach (var profile in profiles)
        {
            if (profile.HotkeyDigit is not { } digit || digit is < 1 or > 9) continue;

            try
            {
                var id = _hotKeys.Register(
                    User32.MOD_CONTROL | User32.MOD_ALT,
                    (uint)('0' + digit),
                    () => _ = _zoneManager.ApplyProfileAsync(profile));
                _profileHotkeyIds.Add(id);
            }
            catch
            {
                // комбинация занята — пропускаем именно этот сценарий, остальные продолжают работать
            }
        }
    }

    public void EmergencyShutdown()
    {
        try { _zoneManager.CloseAllZones(); } catch { /* best effort */ }
        try { if (TaskbarController.IsHidden) TaskbarController.Show(); } catch { /* best effort */ }
        Environment.Exit(1);
    }

    private void OnToggleOverlay(object? sender, System.EventArgs e)
    {
        if (_overlayWindow is null) return;
        _overlayWindow.IsVisible = !_overlayWindow.IsVisible;
    }

    private void OnPatternSingleClicked(object? sender, System.EventArgs e) =>
        _zoneManager.ApplyPattern(ZonePatternType.Single);

    private void OnPatternVerticalClicked(object? sender, System.EventArgs e) =>
        _zoneManager.ApplyPattern(ZonePatternType.SplitVertical);

    private void OnPatternHorizontalClicked(object? sender, System.EventArgs e) =>
        _zoneManager.ApplyPattern(ZonePatternType.SplitHorizontal);

    private void OnPatternGrid2x2Clicked(object? sender, System.EventArgs e) =>
        _zoneManager.ApplyPattern(ZonePatternType.Grid2x2);

    private void OnPatternCustomClicked(object? sender, System.EventArgs e)
    {
        new ZonePatternPickerWindow(_zoneManager).Show();
    }

    private void OnScenariosMenuClicked(object? sender, System.EventArgs e)
    {
        new ProfileManagerWindow(_zoneManager, RebuildProfileHotkeys).Show();
    }

    private void OnSettingsMenuClicked(object? sender, System.EventArgs e)
    {
        new SettingsWindow().Show();
    }

    private void OnExit(object? sender, System.EventArgs e)
    {
        _hotKeys?.Dispose();
        _displayWatcher?.Dispose();
        _zoneManager.CloseAllZones();
        if (TaskbarController.IsHidden) TaskbarController.Show();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}