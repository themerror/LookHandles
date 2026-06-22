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

public interface IWindowEnumerationService
{
	void RefreshWindows(ObservableCollection<WindowListItem> target, bool includeHidden, nint excludeHwnd, uint excludeProcessId);
}

public class WindowEnumerationService : IWindowEnumerationService
{
	private List<WindowListItem> _enumBuffer = new();
	private bool _includeHidden;
	private uint _excludeProcessId;

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
