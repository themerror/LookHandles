using Windows.Win32.Foundation;

namespace LookHandles.Models;

public class WindowListItem
{
	internal HWND Handle { get; set; }
	public string DisplayText { get; set; } = string.Empty;
	public string WindowText { get; set; } = string.Empty;
	public string ClassName { get; set; } = string.Empty;
	public string ProcessName { get; set; } = string.Empty;

	public override string ToString() => DisplayText;
}
