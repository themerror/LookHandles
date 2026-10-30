using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using LookHandles.Models;
using LookHandles.Services;

namespace LookHandles.ViewModels;

/// <summary>
/// Identifies the tracking mode that currently owns automatic window selection.
/// </summary>
public enum TrackingMode
{
	/// <summary>
	/// Tracks the window under the mouse cursor through periodic polling.
	/// </summary>
	Auto,

	/// <summary>
	/// Captures the raw HWND of a single clicked window or child control.
	/// </summary>
	FindWindow,

	/// <summary>
	/// Tracks changes to the foreground window.
	/// </summary>
	FollowForeground
}

/// <summary>
/// Coordinates window selection, tracking modes, and Find Window capture state shared across both
/// application windows.
/// </summary>
public partial class MainViewModel : ObservableObject
{
	private readonly IWindowEnumerationService _enumerationService;
	private readonly IWindowManipulationService _manipulationService;
	private readonly IMouseCaptureService _mouseCaptureService;
	private readonly DispatcherQueue _dispatcherQueue;

	private uint _myProcessId;
	private nint _currentHwnd;
	private int _findWindowCaptureSession;
	private Timer? _autoModeTimer;

	/// <summary>
	/// Gets or sets the shared collection of enumerated top-level window entries displayed by both views.
	/// </summary>
	/// <remarks>
	/// The MVVM Toolkit source generator exposes this contract as the public <c>WindowList</c> property.
	/// </remarks>
	[ObservableProperty]
	private ObservableCollection<WindowListItem> _windowList = new();

	/// <summary>
	/// Gets or sets the window-list entry selected in either view.
	/// </summary>
	/// <remarks>
	/// The generated public <c>SelectedWindowItem</c> property also selects a non-null entry's HWND.
	/// Clearing the list selection does not clear <c>CurrentWindow</c> or the current native handle.
	/// </remarks>
	[ObservableProperty]
	private WindowListItem? _selectedWindowItem;

	/// <summary>
	/// Gets or sets the information snapshot for the currently selected native window.
	/// </summary>
	/// <remarks>
	/// The MVVM Toolkit source generator exposes this contract as the public <c>CurrentWindow</c> property.
	/// Normal selection updates this snapshot together with the native handle used by window commands.
	/// </remarks>
	[ObservableProperty]
	private WindowInfo? _currentWindow;

	/// <summary>
	/// Gets or sets whether automatic cursor tracking is active.
	/// </summary>
	/// <remarks>
	/// The generated public <c>IsAutoMode</c> property starts or stops the auto-mode timer when changed.
	/// </remarks>
	[ObservableProperty]
	private bool _isAutoMode;

	/// <summary>
	/// Gets or sets whether a one-shot Find Window capture session is active.
	/// </summary>
	/// <remarks>
	/// The generated public <c>IsFindingWindow</c> property keeps the capture button text synchronized.
	/// Capture startup and cancellation are managed separately so stale hook callbacks can be rejected.
	/// </remarks>
	[ObservableProperty]
	private bool _isFindingWindow;

	/// <summary>
	/// Gets or sets whether foreground-window tracking is active.
	/// </summary>
	/// <remarks>
	/// The generated public <c>IsSpying</c> property starts or stops foreground polling and updates its
	/// button text when changed.
	/// </remarks>
	[ObservableProperty]
	private bool _isSpying;

	/// <summary>
	/// Gets or sets whether hidden windows are included in the window list.
	/// </summary>
	/// <remarks>
	/// Changing the generated public <c>ShowHiddenWindows</c> property refreshes the shared list.
	/// </remarks>
	[ObservableProperty]
	private bool _showHiddenWindows;

	/// <summary>
	/// Gets or sets whether selecting a disabled window attempts to enable it.
	/// </summary>
	/// <remarks>
	/// The generated public <c>EnableGrayed</c> property stores the preference. The native state change
	/// occurs during window selection, not when this property changes.
	/// </remarks>
	[ObservableProperty]
	private bool _enableGrayed;

	/// <summary>
	/// Gets or sets the text displayed on the Find Window capture button.
	/// </summary>
	/// <remarks>
	/// The MVVM Toolkit source generator exposes this contract as the public
	/// <c>FindWindowButtonText</c> property.
	/// </remarks>
	[ObservableProperty]
	private string _findWindowButtonText = "Find Window";

	/// <summary>
	/// Gets or sets the text displayed on the foreground-tracking button.
	/// </summary>
	/// <remarks>
	/// The MVVM Toolkit source generator exposes this contract as the public
	/// <c>FollowForegroundButtonText</c> property.
	/// </remarks>
	[ObservableProperty]
	private string _followForegroundButtonText = "Follow Foreground";

	/// <summary>
	/// Initializes the shared view model, records the application process ID, and populates the initial
	/// window list.
	/// </summary>
	/// <param name="enumerationService">The service used to enumerate and refresh top-level windows.</param>
	/// <param name="manipulationService">The service used to inspect and manipulate selected windows.</param>
	/// <param name="mouseCaptureService">The service used for cursor queries and Find Window hooks.</param>
	/// <param name="dispatcherQueue">
	/// The queue used to hand off hook callbacks, polling results, and delayed refreshes to the UI thread.
	/// </param>
	public MainViewModel(
		IWindowEnumerationService enumerationService,
		IWindowManipulationService manipulationService,
		IMouseCaptureService mouseCaptureService,
		DispatcherQueue dispatcherQueue)
	{
		_enumerationService = enumerationService;
		_manipulationService = manipulationService;
		_mouseCaptureService = mouseCaptureService;
		_dispatcherQueue = dispatcherQueue;

		_myProcessId = (uint)Environment.ProcessId;
		RefreshWindows();
	}

	/// <summary>
	/// Refreshes the shared top-level window list using the current hidden-window filter.
	/// </summary>
	/// <remarks>
	/// Windows owned by this application process are excluded. The generated
	/// <c>RefreshWindowsCommand</c> exposes the operation to both views.
	/// </remarks>
	[RelayCommand]
	private void RefreshWindows()
	{
		_enumerationService.RefreshWindows(WindowList, ShowHiddenWindows, 0, _myProcessId);
	}

	/// <summary>
	/// Selects the native window associated with a newly selected list entry.
	/// </summary>
	/// <param name="value">The new entry, or null when the list selection is cleared.</param>
	/// <remarks>
	/// This generated-property change hook ignores null, so clearing list selection leaves the current
	/// native window and its information snapshot intact.
	/// </remarks>
	partial void OnSelectedWindowItemChanged(WindowListItem? value)
	{
		if (value != null)
		{
			SelectWindow(value.Handle);
		}
	}

	/// <summary>
	/// Refreshes the window list when the hidden-window filter changes.
	/// </summary>
	/// <param name="value">The new filter value.</param>
	partial void OnShowHiddenWindowsChanged(bool value)
	{
		RefreshWindows();
	}

	/// <summary>
	/// Makes auto mode the exclusive tracking mode and starts or stops its polling timer.
	/// </summary>
	/// <param name="value">True to start cursor tracking; false to stop it.</param>
	/// <remarks>
	/// Enabling auto mode stops Find Window and foreground tracking before starting the timer.
	/// </remarks>
	partial void OnIsAutoModeChanged(bool value)
	{
		if (value)
		{
			StopOtherTrackingModes(TrackingMode.Auto);
			StartAutoMode();
		}
		else
		{
			StopAutoMode();
		}
	}

	/// <summary>
	/// Makes foreground tracking the exclusive tracking mode and starts or stops its polling loop.
	/// </summary>
	/// <param name="value">True to start foreground tracking; false to stop it.</param>
	/// <remarks>
	/// This generated-property change hook also keeps the button text synchronized. Stopping sets the
	/// state observed by the background loop, which exits asynchronously after its current iteration.
	/// </remarks>
	partial void OnIsSpyingChanged(bool value)
	{
		if (value)
		{
			StopOtherTrackingModes(TrackingMode.FollowForeground);
			FollowForegroundButtonText = "Stop Following";
			StartSpy();
		}
		else
		{
			StopSpy();
			FollowForegroundButtonText = "Follow Foreground";
		}
	}

	/// <summary>
	/// Synchronizes the Find Window button text with the capture-state property.
	/// </summary>
	/// <param name="value">True while capture is active; otherwise false.</param>
	/// <remarks>
	/// Capture startup, cancellation, and session invalidation are handled outside this change hook.
	/// </remarks>
	partial void OnIsFindingWindowChanged(bool value)
	{
		FindWindowButtonText = value ? "Click a window" : "Find Window";
	}

	/// <summary>
	/// Starts a one-shot Find Window capture, or cancels the capture already in progress.
	/// </summary>
	/// <remarks>
	/// Starting a session stops the other tracking modes and assigns a token used to reject stale
	/// callbacks. Mouse and keyboard hook callbacks are handed off through the dispatcher queue before
	/// they change shared view-model state.
	/// </remarks>
	[RelayCommand]
	public void ToggleFindWindowMode()
	{
		if (IsFindingWindow)
		{
			CancelFindWindow();
			return;
		}

		StopOtherTrackingModes(TrackingMode.FindWindow);
		var session = ++_findWindowCaptureSession;
		IsFindingWindow = true;

		try
		{
			_mouseCaptureService.StartCapture(
				hwnd => _dispatcherQueue.TryEnqueue(() => CompleteFindWindowCapture(session, hwnd)),
				() => _dispatcherQueue.TryEnqueue(() => CancelFindWindowCapture(session)));
		}
		catch (Exception ex)
		{
			CancelFindWindow();
			OnShowError?.Invoke($"Failed to start window capture: {ex.Message}");
		}
	}

	/// <summary>
	/// Cancels Find Window mode and invalidates any completion or cancellation callbacks already queued.
	/// </summary>
	/// <remarks>
	/// The capture service is stopped without invoking a callback. Find Window state is cleared even if
	/// hook cleanup reports an error.
	/// </remarks>
	public void CancelFindWindow()
	{
		_findWindowCaptureSession++;
		try
		{
			_mouseCaptureService.StopCapture();
		}
		catch (Exception ex)
		{
			OnShowError?.Invoke($"Failed to stop window capture: {ex.Message}");
		}
		finally
		{
			IsFindingWindow = false;
		}
	}

	/// <summary>
	/// Handles the capture service's Escape callback when it belongs to the active session.
	/// </summary>
	/// <param name="session">The session token captured when the hook session started.</param>
	private void CancelFindWindowCapture(int session)
	{
		if (session != _findWindowCaptureSession || !IsFindingWindow) return;
		CancelFindWindow();
	}

	/// <summary>
	/// Completes an active capture using the raw HWND returned for the clicked point.
	/// </summary>
	/// <param name="session">The session token captured when the hook session started.</param>
	/// <param name="hwndValue">The raw clicked HWND, or zero when no window was found.</param>
	/// <remarks>
	/// The clicked HWND is used directly without ancestor normalization. It may identify a child control
	/// and therefore may not match any top-level Window List entry. Stale or inactive sessions are
	/// ignored, and an accepted session is cancelled after completion, including on failure.
	/// </remarks>
	private unsafe void CompleteFindWindowCapture(int session, nint hwndValue)
	{
		if (session != _findWindowCaptureSession || !IsFindingWindow) return;

		try
		{
			if (hwndValue != 0)
			{
				var hwnd = new HWND(hwndValue);

				if (!IsWindowOwnedByProcess(hwnd, _myProcessId))
				{
					SelectWindow(hwnd);
					WindowListItem? selectedItem = null;
					foreach (var item in WindowList)
					{
						if ((nint)item.Handle.Value == (nint)hwnd.Value)
						{
							selectedItem = item;
							break;
						}
					}

					SelectedWindowItem = selectedItem;
				}
			}
		}
		catch (Exception ex)
		{
			OnShowError?.Invoke($"Failed to select the captured window: {ex.Message}");
		}
		finally
		{
			CancelFindWindow();
		}
	}

	/// <summary>
	/// Sets the title of the currently selected window.
	/// </summary>
	/// <param name="title">The new title. Null or empty values are ignored.</param>
	/// <remarks>
	/// The generated <c>SetTitleCommand</c> validates the native handle before applying the title and
	/// refreshes the current window snapshot afterward.
	/// </remarks>
	[RelayCommand]
	private void SetTitle(string title)
	{
		if (!ValidateWindow() || string.IsNullOrEmpty(title)) return;
		_manipulationService.SetWindowText(_currentHwnd, title);
		CurrentWindow?.Refresh();
	}

	/// <summary>
	/// Minimizes the currently selected window after validating its native handle.
	/// </summary>
	/// <remarks>The generated <c>MinimizeCommand</c> refreshes the current snapshot afterward.</remarks>
	[RelayCommand]
	private void Minimize()
	{
		if (!ValidateWindow()) return;
		_manipulationService.Minimize(_currentHwnd);
		CurrentWindow?.Refresh();
	}

	/// <summary>
	/// Maximizes the currently selected window after validating its native handle.
	/// </summary>
	/// <remarks>The generated <c>MaximizeCommand</c> refreshes the current snapshot afterward.</remarks>
	[RelayCommand]
	private void Maximize()
	{
		if (!ValidateWindow()) return;
		_manipulationService.Maximize(_currentHwnd);
		CurrentWindow?.Refresh();
	}

	/// <summary>
	/// Restores the currently selected window after validating its native handle.
	/// </summary>
	/// <remarks>The generated <c>RestoreCommand</c> refreshes the current snapshot afterward.</remarks>
	[RelayCommand]
	private void Restore()
	{
		if (!ValidateWindow()) return;
		_manipulationService.Restore(_currentHwnd);
		CurrentWindow?.Refresh();
	}

	/// <summary>
	/// Shows the currently selected window after validating its native handle.
	/// </summary>
	/// <remarks>The generated <c>ShowCommand</c> refreshes the current snapshot afterward.</remarks>
	[RelayCommand]
	private void Show()
	{
		if (!ValidateWindow()) return;
		_manipulationService.Show(_currentHwnd);
		CurrentWindow?.Refresh();
	}

	/// <summary>
	/// Hides the currently selected window after validating its native handle.
	/// </summary>
	/// <remarks>The generated <c>HideCommand</c> refreshes the current snapshot afterward.</remarks>
	[RelayCommand]
	private void Hide()
	{
		if (!ValidateWindow()) return;
		_manipulationService.Hide(_currentHwnd);
		CurrentWindow?.Refresh();
	}

	/// <summary>
	/// Flashes the currently selected window after validating its native handle.
	/// </summary>
	/// <remarks>The generated <c>FlashCommand</c> exposes this operation to the views.</remarks>
	[RelayCommand]
	private void Flash()
	{
		if (!ValidateWindow()) return;
		_manipulationService.FlashWindow(_currentHwnd);
	}

	/// <summary>
	/// Places the currently selected window above non-topmost windows.
	/// </summary>
	/// <remarks>
	/// The generated <c>SetTopmostCommand</c> validates the native handle and refreshes the current
	/// snapshot after the request.
	/// </remarks>
	[RelayCommand]
	private void SetTopmost()
	{
		if (!ValidateWindow()) return;
		_manipulationService.SetTopmost(_currentHwnd, true);
		CurrentWindow?.Refresh();
	}

	/// <summary>
	/// Removes the topmost state from the currently selected window.
	/// </summary>
	/// <remarks>
	/// The generated <c>ClearTopmostCommand</c> validates the native handle and refreshes the current
	/// snapshot after the request.
	/// </remarks>
	[RelayCommand]
	private void ClearTopmost()
	{
		if (!ValidateWindow()) return;
		_manipulationService.SetTopmost(_currentHwnd, false);
		CurrentWindow?.Refresh();
	}

	/// <summary>
	/// Requests closure of the currently selected window after validating its native handle.
	/// </summary>
	/// <remarks>
	/// The generated <c>CloseCommand</c> does not wait for destruction. It schedules a window-list
	/// refresh 500 ms later and hands that refresh off to the dispatcher queue.
	/// </remarks>
	[RelayCommand]
	private void Close()
	{
		if (!ValidateWindow()) return;
		_manipulationService.CloseWindow(_currentHwnd);
		Task.Delay(500).ContinueWith(_ => _dispatcherQueue.TryEnqueue(RefreshWindows));
	}

	/// <summary>
	/// Enables input for the currently selected window after validating its native handle.
	/// </summary>
	/// <remarks>The generated <c>EnableCommand</c> refreshes the current snapshot afterward.</remarks>
	[RelayCommand]
	private void Enable()
	{
		if (!ValidateWindow()) return;
		_manipulationService.EnableWindow(_currentHwnd, true);
		CurrentWindow?.Refresh();
	}

	/// <summary>
	/// Disables input for the currently selected window after validating its native handle.
	/// </summary>
	/// <remarks>The generated <c>DisableCommand</c> refreshes the current snapshot afterward.</remarks>
	[RelayCommand]
	private void Disable()
	{
		if (!ValidateWindow()) return;
		_manipulationService.EnableWindow(_currentHwnd, false);
		CurrentWindow?.Refresh();
	}

	/// <summary>
	/// Asynchronously confirms and attempts to terminate the process that owns the current window.
	/// </summary>
	/// <remarks>
	/// The generated <c>KillProcessCommand</c> reports an error when no valid process is selected. It
	/// awaits <c>OnConfirmKillProcessRequested</c> and does nothing if confirmation is declined. After
	/// confirmation, termination is best effort: a successful attempt refreshes the window list, while
	/// an invalid PID or failed attempt reports an error.
	/// </remarks>
	[RelayCommand]
	private async Task KillProcess()
	{
		if (CurrentWindow == null || CurrentWindow.ProcessId == "0")
		{
			OnShowError?.Invoke("No valid process selected.");
			return;
		}

		var confirmed = await OnConfirmKillProcessRequested(CurrentWindow.ProcessName, CurrentWindow.ProcessId);
		if (!confirmed) return;

		if (uint.TryParse(CurrentWindow.ProcessId, out var pid) && _manipulationService.TryKillProcess(pid))
		{
			RefreshWindows();
		}
		else
		{
			OnShowError?.Invoke("Failed to terminate process.");
		}
	}

	/// <summary>
	/// Occurs when an operation needs to present an error message to the user.
	/// </summary>
	/// <remarks>
	/// The string argument is the user-facing message. When no handler is attached, no message is
	/// presented and the operation's existing control flow is unchanged.
	/// </remarks>
	public event Action<string>? OnShowError;

	/// <summary>
	/// Requests asynchronous confirmation before terminating a process.
	/// </summary>
	/// <remarks>
	/// The callback receives the process name and process ID string, and its task returns true to allow
	/// the termination attempt. The default callback returns false, so termination is denied until a view
	/// supplies a confirmation handler. <c>KillProcess</c> awaits this callback before touching the process.
	/// </remarks>
	public Func<string, string, Task<bool>> OnConfirmKillProcessRequested = (_, _) => Task.FromResult(false);

	/// <summary>
	/// Checks that the current native handle still identifies a valid window.
	/// </summary>
	/// <returns>True when the window is valid; otherwise false.</returns>
	/// <remarks>
	/// A failed check raises <c>OnShowError</c> and leaves selection unchanged. Window-manipulation
	/// commands use this guard before acting on the cached handle.
	/// </remarks>
	private bool ValidateWindow()
	{
		if (!_manipulationService.IsWindow(_currentHwnd))
		{
			OnShowError?.Invoke("The selected window is no longer valid.");
			return false;
		}
		return true;
	}

	/// <summary>
	/// Selects a valid native window and replaces the current information snapshot.
	/// </summary>
	/// <param name="hwnd">The raw top-level or child window handle to select.</param>
	/// <remarks>
	/// Invalid handles are ignored. When <c>EnableGrayed</c> is set and the target is disabled, selection
	/// first attempts to enable the target through Win32, which changes the target window as a side effect.
	/// The enable result is not checked, and selection proceeds on a best-effort basis. This method updates
	/// the cached handle and <c>CurrentWindow</c>, but does not change the window-list selection.
	/// </remarks>
	private unsafe void SelectWindow(HWND hwnd)
	{
		if (!_manipulationService.IsWindow((nint)hwnd.Value))
			return;

		if (EnableGrayed && !PInvoke.IsWindowEnabled(hwnd))
		{
			PInvoke.EnableWindow(hwnd, true);
		}

		_currentHwnd = (nint)hwnd.Value;
		CurrentWindow = new WindowInfo(hwnd);
	}

	/// <summary>
	/// Stops every tracking mode except <paramref name="activeMode"/> so only one mode owns selection.
	/// </summary>
	/// <param name="activeMode">The mode being started or kept active.</param>
	private void StopOtherTrackingModes(TrackingMode activeMode)
	{
		if (activeMode != TrackingMode.Auto && IsAutoMode)
		{
			IsAutoMode = false;
		}

		if (activeMode != TrackingMode.FollowForeground && IsSpying)
		{
			IsSpying = false;
		}

		if (activeMode != TrackingMode.FindWindow && IsFindingWindow)
		{
			CancelFindWindow();
		}
	}

	/// <summary>
	/// Starts best-effort cursor polling immediately and then at 500 ms intervals.
	/// </summary>
	/// <remarks>
	/// The <see cref="Timer"/> callback runs on a thread-pool thread and only enqueues work on the
	/// dispatcher queue. The dispatcher callback rechecks <c>IsAutoMode</c> so work queued before a stop
	/// cannot change selection. Zero handles and windows owned by this application are skipped, and query
	/// failures are ignored so polling can continue without interrupting the UI.
	/// </remarks>
	private void StartAutoMode()
	{
		_autoModeTimer = new Timer(_ =>
		{
			_dispatcherQueue.TryEnqueue(() =>
			{
				if (!IsAutoMode) return;

				try
				{
					var hwndValue = _mouseCaptureService.GetWindowHandleFromCursor();
					if (hwndValue != 0 && !IsWindowOwnedByProcess(new HWND(hwndValue), _myProcessId))
					{
						SelectWindow(new HWND(hwndValue));
					}
				}
				catch { }
			});
		}, null, 0, 500);
	}

	/// <summary>
	/// Disposes the auto-mode timer and clears its reference.
	/// </summary>
	/// <remarks>Already queued dispatcher callbacks are rejected by their <c>IsAutoMode</c> check.</remarks>
	private void StopAutoMode()
	{
		_autoModeTimer?.Dispose();
		_autoModeTimer = null;
	}

	/// <summary>
	/// Starts best-effort foreground-window polling on a background task.
	/// </summary>
	/// <remarks>
	/// The loop waits 200 ms between polls and uses the last accepted HWND to avoid repeated selection.
	/// It skips zero handles and windows owned by this application. Selection and matching window-list
	/// synchronization are handed off to the dispatcher queue, where <c>IsSpying</c> is checked again to
	/// reject stale work. Polling errors are ignored so the loop can continue while tracking remains active.
	/// </remarks>
	private void StartSpy()
	{
		Task.Run(async () =>
		{
			nint lastHwnd = 0;
			while (IsSpying)
			{
				try
				{
					var hwnd = PInvoke.GetForegroundWindow();
					unsafe
					{
						if ((nint)hwnd.Value != 0 && (nint)hwnd.Value != lastHwnd && !IsWindowOwnedByProcess(hwnd, _myProcessId))
						{
							lastHwnd = (nint)hwnd.Value;
							_dispatcherQueue.TryEnqueue(() =>
							{
								if (IsSpying)
								{
									SelectWindow(hwnd);
									foreach (var item in WindowList)
									{
										if ((nint)item.Handle.Value == (nint)hwnd.Value)
										{
											SelectedWindowItem = item;
											break;
										}
									}
								}
							});
						}
					}
				}
				catch { }
				await Task.Delay(200);
			}
		});
	}

	/// <summary>
	/// Stops foreground tracking without explicitly cancelling its background task.
	/// </summary>
	/// <remarks>The loop observes <c>IsSpying</c> and exits after its current iteration or delay.</remarks>
	private void StopSpy()
	{
		// The background loop checks IsSpying and exits on its own.
	}

	/// <summary>
	/// Determines whether a native window belongs to the specified process.
	/// </summary>
	/// <param name="hwnd">The window handle to query.</param>
	/// <param name="processId">The process ID to compare with the window owner.</param>
	/// <returns>True when the queried owner matches; otherwise false.</returns>
	/// <remarks>
	/// This ownership check is best effort. A zero handle or query exception returns false. Callers use
	/// the application's nonzero process ID, so a failed query also leaves the window eligible for
	/// selection or tracking rather than treating it as application-owned.
	/// </remarks>
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
}
