using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using LookHandles.Models;

namespace LookHandles.Services;

/// <summary>
/// Defines refresh operations for an observable collection of top-level window snapshots.
/// </summary>
public interface IWindowEnumerationService
{
	/// <summary>
	/// Clears and repopulates a collection with filtered and sorted window snapshots.
	/// </summary>
	/// <param name="target">The collection to clear and repopulate with the sorted snapshots.</param>
	/// <param name="includeHidden"><see langword="true"/> to include invisible windows; <see langword="false"/> to omit them.</param>
	/// <param name="excludeHwnd">The native window handle to exclude, or 0 to disable HWND-based exclusion.</param>
	/// <param name="excludeProcessId">The ID of the process whose windows should be excluded.</param>
	/// <remarks>
	/// The window identified by <paramref name="excludeHwnd"/> and windows owned by <paramref name="excludeProcessId"/> are omitted.
	/// Results are ordered with visible windows first, then by process name, and finally by window title.
	/// When the owning process name cannot be retrieved, <see cref="WindowListItem.ProcessName"/> is set to <c>Unknown</c>.
	/// </remarks>
	void RefreshWindows(ObservableCollection<WindowListItem> target, bool includeHidden, nint excludeHwnd, uint excludeProcessId);
}

/// <summary>
/// Enumerates top-level windows and creates snapshots for display in a window list.
/// </summary>
/// <remarks>
/// Enumeration settings and collected items are stored in instance fields during a refresh.
/// Overlapping or nested refreshes on the same instance are not supported.
/// </remarks>
public class WindowEnumerationService : IWindowEnumerationService
{
	private List<WindowListItem> _enumBuffer = new();
	private bool _includeHidden;
	private uint _excludeProcessId;

	/// <inheritdoc />
	public unsafe void RefreshWindows(ObservableCollection<WindowListItem> target, bool includeHidden, nint excludeHwnd, uint excludeProcessId)
	{
		_includeHidden = includeHidden;
		_excludeProcessId = excludeProcessId;
		_enumBuffer = new List<WindowListItem>();

		PInvoke.EnumWindows(EnumWindowsCallback, new LPARAM(excludeHwnd));

		var sorted = _enumBuffer
			.OrderByDescending(w => IsWindowVisibleSafe(w.Handle))
			.ThenBy(w => w.ProcessName)
			.ThenBy(w => w.WindowText)
			.ToList();

		target.Clear();
		foreach (var w in sorted)
			target.Add(w);
	}

	/// <summary>
	/// Collects a snapshot for an eligible window while allowing native enumeration to continue.
	/// </summary>
	/// <param name="hwnd">The top-level window supplied by the native enumerator.</param>
	/// <param name="lParam">The native window handle to exclude, passed through from the refresh operation.</param>
	/// <returns>Always <see langword="true"/>, including when the current window is filtered out or cannot be processed.</returns>
	/// <remarks>
	/// Hidden-window, HWND, and process exclusions skip only the current window.
	/// A failure while processing one window is ignored so enumeration can proceed with subsequent windows.
	/// The callback uses the current refresh's mutable instance state and is not reentrant on the same service instance.
	/// </remarks>
	private unsafe BOOL EnumWindowsCallback(HWND hwnd, LPARAM lParam)
	{
		try
		{
			if (!_includeHidden && !PInvoke.IsWindowVisible(hwnd))
				return new BOOL(1);

			if ((nint)hwnd.Value == lParam.Value || IsWindowOwnedByProcess(hwnd, _excludeProcessId))
				return new BOOL(1);

			int textLen = (int)PInvoke.GetWindowTextLength(hwnd);
			string windowText = string.Empty;
			if (textLen > 0)
			{
				Span<char> textBuffer = stackalloc char[textLen + 1];
				int actualLen = PInvoke.GetWindowText(hwnd, textBuffer);
				windowText = textBuffer.Slice(0, actualLen).ToString();
			}

			Span<char> classBuffer = stackalloc char[256];
			int classLen = PInvoke.GetClassName(hwnd, classBuffer);
			string className = classBuffer.Slice(0, classLen).ToString();

			uint pid = 0;
			PInvoke.GetWindowThreadProcessId(hwnd, &pid);

			string processName = "Unknown";
			try
			{
				using var proc = Process.GetProcessById((int)pid);
				processName = proc.ProcessName;
			}
			catch { }

			string display = string.IsNullOrEmpty(windowText)
				? $"[{className}]"
				: $"{windowText} [{className}]";

			_enumBuffer.Add(new WindowListItem
			{
				Handle = hwnd,
				DisplayText = $"0x{(nint)hwnd.Value:X8} - {display} ({processName})",
				WindowText = windowText,
				ClassName = className,
				ProcessName = processName
			});
		}
		catch { }
		return new BOOL(1);
	}

	private static unsafe bool IsWindowOwnedByProcess(HWND hwnd, uint processId)
	{
		if ((nint)hwnd.Value == 0)
			return false;
		try
		{
			uint pid = 0;
			PInvoke.GetWindowThreadProcessId(hwnd, &pid);
			return pid == processId;
		}
		catch
		{
			return false;
		}
	}

	private static bool IsWindowVisibleSafe(HWND hwnd)
	{
		try { return PInvoke.IsWindowVisible(hwnd); } catch { return false; }
	}
}
