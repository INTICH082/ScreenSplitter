using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using ScreenSplitter.Platform.Windows.Native;

namespace ScreenSplitter.Platform.Windows;

[SupportedOSPlatform("windows")]
public static class AppIconExtractor
{
    /// Пытается определить путь к .exe процесса, которому принадлежит окно.
    /// Может вернуть null, если процесс запущен с повышенными правами (доступ к MainModule запрещён) —
    /// это не ошибка, просто в этом случае иконка не покажется
    public static string? ResolveExePathFromWindow(IntPtr hwnd)
    {
        try
        {
            User32.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return null;

            using var process = Process.GetProcessById((int)pid);
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    /// Извлекает системную иконку .exe-файла и возвращает её как PNG-байты (32x32).
    /// Возвращает null, если файл не найден или иконку извлечь не удалось
    public static byte[]? ExtractIconPng(string? exePath)
    {
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return null;

        try
        {
            using var icon = Icon.ExtractAssociatedIcon(exePath);
            if (icon is null) return null;

            using var bitmap = icon.ToBitmap();
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
        catch
        {
            return null;
        }
    }
}