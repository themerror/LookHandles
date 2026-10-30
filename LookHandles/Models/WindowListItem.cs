using Windows.Win32.Foundation;

namespace LookHandles.Models;

/// <summary>
/// Represents a snapshot of a top-level window for display in the window list.
/// </summary>
public class WindowListItem
{
	/// <summary>
	/// Gets or sets the raw native window handle captured for this snapshot.
	/// </summary>
	internal HWND Handle { get; set; }

	/// <summary>
	/// Gets the window handle formatted as "HWND 0x" followed by uppercase hexadecimal digits.
	/// </summary>
	public unsafe string HandleDisplay => $"HWND 0x{(nint)Handle.Value:X8}";

	/// <summary>
	/// Gets or sets the combined handle, window, class, and process text shown for the item.
	/// </summary>
	public string DisplayText { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the window title captured in this snapshot.
	/// </summary>
	public string WindowText { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the native window class name captured in this snapshot.
	/// </summary>
	public string ClassName { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the name of the process that owns the window, or the enumeration fallback when unavailable.
	/// </summary>
	public string ProcessName { get; set; } = string.Empty;

	/// <summary>
	/// Returns the combined display text for this item.
	/// </summary>
	/// <returns>The value of <see cref="DisplayText"/>.</returns>
	public override string ToString() => DisplayText;
}
