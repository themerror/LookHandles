using System;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;
using Windows.Win32.UI.WindowsAndMessaging;

namespace LookHandles.Services;

/// <summary>
/// Defines operations for manipulating Win32 windows and requesting process termination.
/// </summary>
public interface IWindowManipulationService
{
	/// <summary>
	/// Determines whether a native handle identifies an existing window.
	/// </summary>
	/// <param name="hwnd">The native window handle to test.</param>
	/// <returns>
	/// <see langword="true"/> if the Win32 <c>IsWindow</c> call returns true;
	/// <see langword="false"/> if it returns false or throws an interop exception.
	/// </returns>
	bool IsWindow(nint hwnd);

	/// <summary>
	/// Attempts to set the title text of a window.
	/// </summary>
	/// <param name="hwnd">The native window handle.</param>
	/// <param name="text">The new window title.</param>
	/// <remarks>
	/// Null or empty text is a no-op, so an empty title cannot be used to clear the existing title.
	/// This best-effort operation does not report success and swallows interop exceptions.
	/// </remarks>
	void SetWindowText(nint hwnd, string text);

	/// <summary>
	/// Attempts to minimize a window using <c>SW_MINIMIZE</c>.
	/// </summary>
	/// <param name="hwnd">The native window handle.</param>
	/// <remarks>This best-effort operation does not report success and swallows interop exceptions.</remarks>
	void Minimize(nint hwnd);

	/// <summary>
	/// Attempts to maximize a window using <c>SW_MAXIMIZE</c>.
	/// </summary>
	/// <param name="hwnd">The native window handle.</param>
	/// <remarks>This best-effort operation does not report success and swallows interop exceptions.</remarks>
	void Maximize(nint hwnd);

	/// <summary>
	/// Attempts to display and activate a window using <c>SW_SHOWNORMAL</c>, restoring its
	/// original size and position when it was minimized or maximized.
	/// </summary>
	/// <param name="hwnd">The native window handle.</param>
	/// <remarks>This best-effort operation does not report success and swallows interop exceptions.</remarks>
	void Restore(nint hwnd);

	/// <summary>
	/// Attempts to display and activate a window in its current size and position using <c>SW_SHOW</c>.
	/// </summary>
	/// <param name="hwnd">The native window handle.</param>
	/// <remarks>This best-effort operation does not report success and swallows interop exceptions.</remarks>
	void Show(nint hwnd);

	/// <summary>
	/// Attempts to hide a window using <c>SW_HIDE</c>.
	/// </summary>
	/// <param name="hwnd">The native window handle.</param>
	/// <remarks>This best-effort operation does not report success and swallows interop exceptions.</remarks>
	void Hide(nint hwnd);

	/// <summary>
	/// Attempts to flash a window's caption and taskbar button to attract attention.
	/// </summary>
	/// <param name="hwnd">The native window handle.</param>
	/// <remarks>
	/// Uses <c>FlashWindowEx</c> with <c>FLASHW_ALL | FLASHW_TIMERNOFG</c>, <c>uCount = 5</c>,
	/// and <c>dwTimeout = 0</c>. This best-effort operation does not report success and swallows interop exceptions.
	/// </remarks>
	void FlashWindow(nint hwnd);

	/// <summary>
	/// Attempts to add or remove a window's topmost z-order state.
	/// </summary>
	/// <param name="hwnd">The native window handle.</param>
	/// <param name="topmost"><see langword="true"/> to make the window topmost; otherwise, <see langword="false"/>.</param>
	/// <remarks>
	/// Uses <c>SetWindowPos</c> with <c>SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW</c>, preserving
	/// the window's position and size while requesting that it be shown. This best-effort operation
	/// does not report success and swallows interop exceptions.
	/// </remarks>
	void SetTopmost(nint hwnd, bool topmost);

	/// <summary>
	/// Attempts to close a window by asynchronously posting a <c>WM_CLOSE</c> message.
	/// </summary>
	/// <param name="hwnd">The native window handle.</param>
	/// <remarks>
	/// Does not wait for the message to be processed, and posting the message does not guarantee closure.
	/// This best-effort operation does not report success and swallows interop exceptions.
	/// </remarks>
	void CloseWindow(nint hwnd);

	/// <summary>
	/// Attempts to enable or disable mouse and keyboard input to a window.
	/// </summary>
	/// <param name="hwnd">The native window handle.</param>
	/// <param name="enabled"><see langword="true"/> to enable input; <see langword="false"/> to disable it.</param>
	/// <remarks>This best-effort operation does not report success and swallows interop exceptions.</remarks>
	void EnableWindow(nint hwnd, bool enabled);

	/// <summary>
	/// Attempts to terminate a process with exit code 1.
	/// </summary>
	/// <param name="processId">The identifier of the process to terminate.</param>
	/// <returns>
	/// <see langword="true"/> if a nonzero process handle was opened and the termination and cleanup
	/// calls completed without throwing; otherwise, <see langword="false"/>. A true result does not
	/// confirm that termination succeeded or that the process has exited.
	/// </returns>
	/// <remarks>
	/// Opens the process with <c>PROCESS_TERMINATE</c> access. On the normal execution path, closes
	/// the process handle after requesting termination. Native return values from <c>TerminateProcess</c>
	/// and <c>CloseHandle</c> are not inspected, and interop exceptions are swallowed.
	/// </remarks>
	bool TryKillProcess(uint processId);
}

/// <summary>
/// Provides the Win32 implementation of <see cref="IWindowManipulationService"/>.
/// </summary>
public class WindowManipulationService : IWindowManipulationService
{
	private static readonly HWND HWND_TOPMOST_VALUE = new HWND(new IntPtr(-1));
	private static readonly HWND HWND_NOTOPMOST_VALUE = new HWND(new IntPtr(-2));
	private const uint WM_CLOSE = 0x0010;

	/// <inheritdoc/>
	public bool IsWindow(nint hwnd)
	{
		try { return PInvoke.IsWindow(new HWND(hwnd)); } catch { return false; }
	}

	/// <inheritdoc/>
	public void SetWindowText(nint hwnd, string text)
	{
		if (string.IsNullOrEmpty(text)) return;
		try { PInvoke.SetWindowText(new HWND(hwnd), text); } catch { }
	}

	/// <inheritdoc/>
	public void Minimize(nint hwnd) => ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_MINIMIZE);

	/// <inheritdoc/>
	public void Maximize(nint hwnd) => ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_MAXIMIZE);

	/// <inheritdoc/>
	public void Restore(nint hwnd) => ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_SHOWNORMAL);

	/// <inheritdoc/>
	public void Show(nint hwnd) => ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_SHOW);

	/// <inheritdoc/>
	public void Hide(nint hwnd) => ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_HIDE);

	private void ShowWindow(nint hwnd, SHOW_WINDOW_CMD cmd)
	{
		try { PInvoke.ShowWindow(new HWND(hwnd), cmd); } catch { }
	}

	/// <inheritdoc/>
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

	/// <inheritdoc/>
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

	/// <inheritdoc/>
	public void CloseWindow(nint hwnd)
	{
		try { PInvoke.PostMessage(new HWND(hwnd), WM_CLOSE, default, default); } catch { }
	}

	/// <inheritdoc/>
	public void EnableWindow(nint hwnd, bool enabled)
	{
		try { PInvoke.EnableWindow(new HWND(hwnd), enabled); } catch { }
	}

	/// <inheritdoc/>
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
