using System;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;
using Windows.Win32.UI.WindowsAndMessaging;

namespace LookHandles.Services;

public interface IWindowManipulationService
{
	bool IsWindow(nint hwnd);
	void SetWindowText(nint hwnd, string text);
	void Minimize(nint hwnd);
	void Maximize(nint hwnd);
	void Restore(nint hwnd);
	void Show(nint hwnd);
	void Hide(nint hwnd);
	void FlashWindow(nint hwnd);
	void SetTopmost(nint hwnd, bool topmost);
	void CloseWindow(nint hwnd);
	void EnableWindow(nint hwnd, bool enabled);
	bool TryKillProcess(uint processId);
}

public class WindowManipulationService : IWindowManipulationService
{
	private static readonly HWND HWND_TOPMOST_VALUE = new HWND(new IntPtr(-1));
	private static readonly HWND HWND_NOTOPMOST_VALUE = new HWND(new IntPtr(-2));
	private const uint WM_CLOSE = 0x0010;

	public bool IsWindow(nint hwnd)
	{
		try { return PInvoke.IsWindow(new HWND(hwnd)); } catch { return false; }
	}

	public void SetWindowText(nint hwnd, string text)
	{
		if (string.IsNullOrEmpty(text)) return;
		try { PInvoke.SetWindowText(new HWND(hwnd), text); } catch { }
	}

	public void Minimize(nint hwnd) => ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_MINIMIZE);
	public void Maximize(nint hwnd) => ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_MAXIMIZE);
	public void Restore(nint hwnd) => ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_SHOWNORMAL);
	public void Show(nint hwnd) => ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_SHOW);
	public void Hide(nint hwnd) => ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_HIDE);

	private void ShowWindow(nint hwnd, SHOW_WINDOW_CMD cmd)
	{
		try { PInvoke.ShowWindow(new HWND(hwnd), cmd); } catch { }
	}

	public unsafe void FlashWindow(nint hwnd)
	{
		try
		{
			var flashInfo = new FLASHWINFO
			{
				cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
				hwnd = new HWND(hwnd),
				dwFlags = FLASHWINFO_FLAGS.FLASHW_ALL | FLASHWINFO_FLAGS.FLASHW_TIMERNOFG,
				uCount = 5,
				dwTimeout = 0
			};
			PInvoke.FlashWindowEx(&flashInfo);
		}
		catch { }
	}

	public void SetTopmost(nint hwnd, bool topmost)
	{
		try
		{
			PInvoke.SetWindowPos(
				new HWND(hwnd),
				topmost ? HWND_TOPMOST_VALUE : HWND_NOTOPMOST_VALUE,
				0, 0, 0, 0,
				SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW);
		}
		catch { }
	}

	public void CloseWindow(nint hwnd)
	{
		try { PInvoke.PostMessage(new HWND(hwnd), WM_CLOSE, default, default); } catch { }
	}

	public void EnableWindow(nint hwnd, bool enabled)
	{
		try { PInvoke.EnableWindow(new HWND(hwnd), enabled); } catch { }
	}

	public bool TryKillProcess(uint processId)
	{
		try
		{
			var hProcess = PInvoke.OpenProcess(PROCESS_ACCESS_RIGHTS.PROCESS_TERMINATE, false, processId);
			unsafe
			{
				if ((nint)hProcess.Value != 0)
				{
					PInvoke.TerminateProcess(hProcess, 1);
					PInvoke.CloseHandle(hProcess);
					return true;
				}
			}
		}
		catch { }
		return false;
	}
}
