namespace ScreenSplitter.Core.Models;

public class Profile
{
    public string Name { get; set; } = "";
    public int Cols { get; set; }
    public int Rows { get; set; }
    public List<ZoneAssignment> Assignments { get; set; } = new();

    /// <summary>Цифра 1-9 для горячей клавиши Ctrl+Alt+&lt;цифра&gt;, или null — без горячей клавиши.</summary>
    public int? HotkeyDigit { get; set; }
}