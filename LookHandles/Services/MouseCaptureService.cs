using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace LookHandles.Services;

/// <summary>
/// Provides raw window lookup under the cursor and one-shot mouse/keyboard capture for window selection.
/// </summary>
public interface IMouseCaptureService
{
	/// <summary>
	/// Gets the raw window handle at the current cursor position.
	/// </summary>
	/// <returns>
	/// The direct <c>WindowFromPoint</c> result, which may be a child control rather than a top-level
	/// window, or zero if the cursor position cannot be retrieved or no window is found.
	/// </returns>
	nint GetWindowHandleFromCursor();

	/// <summary>
	/// Starts a one-shot capture, replacing any existing capture without invoking its callbacks.
	/// </summary>
	/// <param name="picked">
	/// Called once with the raw <c>WindowFromPoint</c> result for the next left-button press. The handle
	/// may identify a child control and is not normalized to an ancestor window.
	/// </param>
	/// <param name="cancelled">Called once when Escape is pressed.</param>
	/// <remarks>
	/// Callbacks run on the low-level hook thread. Hook cleanup and callback clearing occur before either
	/// callback is invoked, so the capture cannot complete or cancel more than once.
	/// </remarks>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="picked"/> or <paramref name="cancelled"/> is null.
	/// </exception>
	/// <exception cref="Win32Exception">
	/// An existing capture could not be stopped or a low-level hook could not be installed.
	/// </exception>
	/// <exception cref="AggregateException">
	/// Hook installation failed and rollback of the partially installed hooks also failed.
	/// </exception>
	void StartCapture(Action<nint> picked, Action cancelled);

	/// <summary>
	/// Stops the active capture, clears its callbacks, and releases both low-level hooks.
	/// </summary>
	/// <remarks>No callback is invoked. Calling this method with no active capture has no effect.</remarks>
	/// <exception cref="Win32Exception">A low-level hook could not be uninstalled.</exception>
	void StopCapture();
}

/// <summary>
/// Provides cursor-based raw window lookup and one-shot global mouse/keyboard capture.
/// </summary>
/// <remarks>
/// Each instance caches its hook delegates and owns the safe handles for hooks installed by a capture.
/// </remarks>
public sealed class MouseCaptureService : IMouseCaptureService
{
	private readonly HOOKPROC _mouseHookProc;
	private readonly HOOKPROC _keyboardHookProc;
	private UnhookWindowsHookExSafeHandle? _mouseHook;
	private UnhookWindowsHookExSafeHandle? _keyboardHook;
	private Action<nint>? _picked;
	private Action? _cancelled;

	/// <summary>
	/// Initializes the service and caches the delegates passed to the low-level Win32 hooks.
	/// </summary>
	/// <remarks>
	/// Retaining the delegates for the service lifetime prevents garbage collection while the hooks use them.
	/// </remarks>
	public MouseCaptureService()
	{
		_mouseHookProc = MouseHookProc;
		_keyboardHookProc = KeyboardHookProc;
	}

	/// <inheritdoc/>
	public nint GetWindowHandleFromCursor()
	{
		if (!PInvoke.GetCursorPos(out var point))
		{
			return 0;
		}

		return ToIntPtr(PInvoke.WindowFromPoint(point));
	}

	/// <inheritdoc/>
	public void StartCapture(Action<nint> picked, Action cancelled)
	{
		ArgumentNullException.ThrowIfNull(picked);
		ArgumentNullException.ThrowIfNull(cancelled);
		StopCapture();

		_picked = picked;
		_cancelled = cancelled;

		try
		{
			var moduleHandle = PInvoke.GetModuleHandle(null);
			_mouseHook = PInvoke.SetWindowsHookEx(
				WINDOWS_HOOK_ID.WH_MOUSE_LL,
				_mouseHookProc,
				moduleHandle,
				0);
			if (_mouseHook is null || _mouseHook.IsInvalid)
			{
				throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to install WH_MOUSE_LL.");
			}

			_keyboardHook = PInvoke.SetWindowsHookEx(
				WINDOWS_HOOK_ID.WH_KEYBOARD_LL,
				_keyboardHookProc,
				moduleHandle,
				0);
			if (_keyboardHook is null || _keyboardHook.IsInvalid)
			{
				throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to install WH_KEYBOARD_LL.");
			}
		}
		catch (Exception installError)
		{
			var cleanupError = StopCaptureCore();
			if (cleanupError is not null)
			{
				throw new AggregateException("Hook installation and rollback both failed.", installError, cleanupError);
			}

			throw;
		}
	}

	/// <inheritdoc/>
	public void StopCapture()
	{
		var error = StopCaptureCore();
		if (error is not null)
		{
			throw error;
		}
	}

	/// <summary>
	/// Selects the raw window under the next left-button press and completes the active capture.
	/// </summary>
	/// <param name="code">The low-level mouse hook code.</param>
	/// <param name="wParam">The mouse message identifier.</param>
	/// <param name="lParam">A pointer to the low-level mouse event data.</param>
	/// <returns>The result of forwarding the event to the next hook.</returns>
	/// <remarks>
	/// The <c>WindowFromPoint</c> result is passed to the callback unchanged and may identify a child control.
	/// Callback state is cleared and cleanup of both hooks is attempted before the picked callback runs. This keeps
	/// the capture one-shot even if that callback starts another capture. Cleanup failures are only traced on this path.
	/// Every mouse event is forwarded through <c>CallNextHookEx</c>, including picked events and hook failures.
	/// </remarks>
	private LRESULT MouseHookProc(int code, WPARAM wParam, LPARAM lParam)
	{
		try
		{
			if (code >= 0 && wParam.Value == PInvoke.WM_LBUTTONDOWN && _picked is not null)
			{
				var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
				var hwnd = PInvoke.WindowFromPoint(data.pt);
				var picked = _picked;
				// Clean up first to keep the capture one-shot, even if the callback starts another capture.
				TraceCleanupError(StopCaptureCore());
				picked(ToIntPtr(hwnd));
			}
		}
		catch (Exception ex)
		{
			TraceHookException("WH_MOUSE_LL", ex);
		}

		// Always forward mouse events, including picked events and handled hook failures.
		return PInvoke.CallNextHookEx(default, code, wParam, lParam);
	}

	/// <summary>
	/// Cancels the active capture when Escape is pressed.
	/// </summary>
	/// <param name="code">The low-level keyboard hook code.</param>
	/// <param name="wParam">The keyboard message identifier.</param>
	/// <param name="lParam">A pointer to the low-level keyboard event data.</param>
	/// <returns>The result of forwarding the event to the next hook.</returns>
	/// <remarks>
	/// Callback state is cleared and cleanup of both hooks is attempted before the cancelled callback runs, preventing
	/// another callback from the same capture even if that callback starts a new one. Cleanup failures are only traced here.
	/// Escape is not swallowed, and every keyboard event continues through <c>CallNextHookEx</c>.
	/// </remarks>
	private LRESULT KeyboardHookProc(int code, WPARAM wParam, LPARAM lParam)
	{
		try
		{
			var isKeyDown = wParam.Value == PInvoke.WM_KEYDOWN || wParam.Value == PInvoke.WM_SYSKEYDOWN;
			if (code >= 0 && isKeyDown && _cancelled is not null)
			{
				var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
				if (data.vkCode == (uint)VIRTUAL_KEY.VK_ESCAPE)
				{
					var cancelled = _cancelled;
					// Clean up before cancellation so this session cannot invoke another callback.
					TraceCleanupError(StopCaptureCore());
					cancelled();
				}
			}
		}
		catch (Exception ex)
		{
			TraceHookException("WH_KEYBOARD_LL", ex);
		}

		// Escape is not swallowed; every keyboard event continues through the hook chain.
		return PInvoke.CallNextHookEx(default, code, wParam, lParam);
	}

	/// <summary>
	/// Clears capture callbacks and attempts to uninstall both low-level hooks.
	/// </summary>
	/// <returns>The first hook cleanup error, or <see langword="null"/> if both hooks are released or absent.</returns>
	/// <remarks>
	/// Callbacks are cleared before hook teardown so no further session callback can be dispatched. Keyboard cleanup
	/// precedes mouse cleanup, and mouse cleanup is still attempted if keyboard cleanup fails. Hook completion paths
	/// trace the returned error instead of throwing it; <see cref="StopCapture"/> surfaces it to callers.
	/// </remarks>
	private Win32Exception? StopCaptureCore()
	{
		// Clear callbacks before hook teardown so no further session callback can be dispatched.
		_picked = null;
		_cancelled = null;

		var error = UninstallHook(ref _keyboardHook, "WH_KEYBOARD_LL");
		var mouseError = UninstallHook(ref _mouseHook, "WH_MOUSE_LL");
		return error ?? mouseError;
	}

	/// <summary>
	/// Uninstalls one low-level hook and releases its owned safe handle.
	/// </summary>
	/// <param name="hook">
	/// The safe-handle field to release. It is set to null after successful or already-invalid cleanup.
	/// </param>
	/// <param name="hookName">The hook name to include in a cleanup error.</param>
	/// <returns>A cleanup error, or null if no valid hook requires uninstall or uninstall succeeds.</returns>
	/// <remarks>
	/// Success marks the safe handle invalid before disposal so its finalizer cannot uninstall the same native hook
	/// again. Failure leaves the handle and field intact, preserving ownership for another cleanup attempt or eventual
	/// safe-handle release. An invalid handle is disposed without another native uninstall call.
	/// </remarks>
	private static Win32Exception? UninstallHook(ref UnhookWindowsHookExSafeHandle? hook, string hookName)
	{
		if (hook is null)
		{
			return null;
		}

		if (hook.IsInvalid)
		{
			hook.Dispose();
			hook = null;
			return null;
		}

		var rawHook = new HHOOK(hook.DangerousGetHandle());
		if (!PInvoke.UnhookWindowsHookEx(rawHook))
		{
			return new Win32Exception(Marshal.GetLastWin32Error(), $"Failed to uninstall {hookName}.");
		}

		hook.SetHandleAsInvalid();
		hook.Dispose();
		hook = null;
		return null;
	}

	private static unsafe nint ToIntPtr(HWND hwnd) => (nint)hwnd.Value;

	private static void TraceCleanupError(Win32Exception? error)
	{
		if (error is not null)
		{
			TraceHookException("Hook cleanup", error);
		}
	}

	private static void TraceHookException(string source, Exception exception) =>
		Trace.WriteLine($"{source}: {exception}");
}
