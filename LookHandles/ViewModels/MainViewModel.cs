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

public enum TrackingMode
{
	Auto,
	FindWindow,
	FollowForeground
}

public partial class MainViewModel : ObservableObject
{
	private readonly IWindowEnumerationService _enumerationService;
	private readonly IWindowManipulationService _manipulationService;
	private readonly IMouseCaptureService _mouseCaptureService;
	private readonly DispatcherQueue _dispatcherQueue;

	private uint _myProcessId;
	private nint _currentHwnd;
	private Timer? _autoModeTimer;

	[ObservableProperty]
	private ObservableCollection<WindowListItem> _windowList = new();

	[ObservableProperty]
	private WindowListItem? _selectedWindowItem;

	[ObservableProperty]
	private WindowInfo? _currentWindow;

	[ObservableProperty]
	private bool _isAutoMode;

	[ObservableProperty]
	private bool _isFindingWindow;

	[ObservableProperty]
	private bool _isSpying;

	[ObservableProperty]
	private bool _showHiddenWindows;

	[ObservableProperty]
	private bool _enableGrayed;

	[ObservableProperty]
	private string _findWindowButtonText = "Find Window";

	[ObservableProperty]
	private string _followForegroundButtonText = "Follow Foreground";

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

	[RelayCommand]
	private void RefreshWindows()
	{
		_enumerationService.RefreshWindows(WindowList, ShowHiddenWindows, 0, _myProcessId);
	}

	partial void OnSelectedWindowItemChanged(WindowListItem? value)
	{
		if (value != null)
		{
			SelectWindow(value.Handle);
		}
	}

	partial void OnShowHiddenWindowsChanged(bool value)
	{
		RefreshWindows();
	}

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

	partial void OnIsFindingWindowChanged(bool value)
	{
		FindWindowButtonText = value ? "Click a window" : "Find Window";
	}

	[RelayCommand]
	public void ToggleFindWindowMode()
	{
		if (IsFindingWindow)
		{
			IsFindingWindow = false;
			_mouseCaptureService.ReleaseCapture();
		}
		else
		{
			StopOtherTrackingModes(TrackingMode.FindWindow);
			IsFindingWindow = true;
		}
	}

	public void BeginFindWindowCapture(nint ownerHwnd)
	{
		_mouseCaptureService.SetCapture(ownerHwnd);
	}

	public void HandleFindWindowPointerPressed()
	{
		if (!IsFindingWindow) return;

		try
		{
			var hwndValue = _mouseCaptureService.GetWindowHandleFromCursor();
			if (hwndValue == 0) return;

			unsafe
			{
				var hwnd = new HWND(hwndValue);
				var rootHwnd = PInvoke.GetAncestor(hwnd, GET_ANCESTOR_FLAGS.GA_ROOT);
				if ((nint)rootHwnd.Value != 0)
					hwnd = rootHwnd;

				if (!IsWindowOwnedByProcess(hwnd, _myProcessId))
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
			}
		}
		catch { }
		finally
		{
			IsFindingWindow = false;
			_mouseCaptureService.ReleaseCapture();
		}
	}

	[RelayCommand]
	private void SetTitle(string title)
	{
		if (!ValidateWindow() || string.IsNullOrEmpty(title)) return;
		_manipulationService.SetWindowText(_currentHwnd, title);
		CurrentWindow?.Refresh();
	}

	[RelayCommand]
	private void Minimize()
	{
		if (!ValidateWindow()) return;
		_manipulationService.Minimize(_currentHwnd);
		CurrentWindow?.Refresh();
	}

	[RelayCommand]
	private void Maximize()
	{
		if (!ValidateWindow()) return;
		_manipulationService.Maximize(_currentHwnd);
		CurrentWindow?.Refresh();
	}

	[RelayCommand]
	private void Restore()
	{
		if (!ValidateWindow()) return;
		_manipulationService.Restore(_currentHwnd);
		CurrentWindow?.Refresh();
	}

	[RelayCommand]
	private void Show()
	{
		if (!ValidateWindow()) return;
		_manipulationService.Show(_currentHwnd);
		CurrentWindow?.Refresh();
	}

	[RelayCommand]
	private void Hide()
	{
		if (!ValidateWindow()) return;
		_manipulationService.Hide(_currentHwnd);
		CurrentWindow?.Refresh();
	}

	[RelayCommand]
	private void Flash()
	{
		if (!ValidateWindow()) return;
		_manipulationService.FlashWindow(_currentHwnd);
	}

	[RelayCommand]
	private void SetTopmost()
	{
		if (!ValidateWindow()) return;
		_manipulationService.SetTopmost(_currentHwnd, true);
		CurrentWindow?.Refresh();
	}

	[RelayCommand]
	private void ClearTopmost()
	{
		if (!ValidateWindow()) return;
		_manipulationService.SetTopmost(_currentHwnd, false);
		CurrentWindow?.Refresh();
	}

	[RelayCommand]
	private void Close()
	{
		if (!ValidateWindow()) return;
		_manipulationService.CloseWindow(_currentHwnd);
		Task.Delay(500).ContinueWith(_ => _dispatcherQueue.TryEnqueue(RefreshWindows));
	}

	[RelayCommand]
	private void Enable()
	{
		if (!ValidateWindow()) return;
		_manipulationService.EnableWindow(_currentHwnd, true);
		CurrentWindow?.Refresh();
	}

	[RelayCommand]
	private void Disable()
	{
		if (!ValidateWindow()) return;
		_manipulationService.EnableWindow(_currentHwnd, false);
		CurrentWindow?.Refresh();
	}

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

	public event Action<string>? OnShowError;
	public Func<string, string, Task<bool>> OnConfirmKillProcessRequested = (_, _) => Task.FromResult(false);

	private bool ValidateWindow()
	{
		if (!_manipulationService.IsWindow(_currentHwnd))
		{
			OnShowError?.Invoke("The selected window is no longer valid.");
			return false;
		}
		return true;
	}

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
			IsFindingWindow = false;
			_mouseCaptureService.ReleaseCapture();
		}
	}

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

	private void StopAutoMode()
	{
		_autoModeTimer?.Dispose();
		_autoModeTimer = null;
	}

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

	private void StopSpy()
	{
		// The background loop checks IsSpying and exits on its own.
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
}
