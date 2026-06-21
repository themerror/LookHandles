using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;
using Windows.Win32.UI.WindowsAndMessaging;

namespace LookHandles;

public sealed partial class MainWindow : Window
{
	// HWND constants for SetWindowPos (CsWin32 doesn't generate these as constants)
	private static readonly HWND HWND_TOPMOST_VALUE = new HWND(new IntPtr(-1));

	private static readonly HWND HWND_NOTOPMOST_VALUE = new HWND(new IntPtr(-2));

	private const uint WM_CLOSE = 0x0010;
	private const uint IDC_SIZEALL = 32646;

	private ObservableCollection<WindowListItem> _windowList = new();
	private WindowInfo? _currentWindow;
	private HWND _currentHwnd;
	private bool _isAutoMode = false;
	private bool _showHiddenWindows = false;
	private bool _enableDisabledButtons = false;
	private Timer? _autoModeTimer;
	private bool _isFindingWindow = false;
	private bool _findWindowJustCompleted = false;
	private bool _isSpying = false;
	private uint _myProcessId;
	private HCURSOR? _dragCursor;
	private HCURSOR? _previousCursor;
	private DispatcherTimer? _findWindowTimer;

	// Accumulate EnumWindows results
	private List<WindowListItem> _enumBuffer = new();

	public MainWindow()
	{
		this.InitializeComponent();

		SetWindowSize(900, 720);

		_currentHwnd = default;
		_myProcessId = (uint)Environment.ProcessId;
		lvWindows.ItemsSource = _windowList;
		Title = "LookHandles 3.0";
		ExtendsContentIntoTitleBar = true;

		RefreshWindowList();
	}

	private void SetWindowSize(int width, int height)
	{
		var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
		var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
		var appWindow = AppWindow.GetFromWindowId(windowId);
		appWindow.Resize(new SizeInt32(width, height));
	}

	private HWND GetMyHwnd()
	{
		return new HWND(WinRT.Interop.WindowNative.GetWindowHandle(this));
	}

	// ========== Window List Enumeration ==========

	private void RefreshWindowList()
	{
		_windowList.Clear();
		_enumBuffer = new List<WindowListItem>();
		var myHwnd = GetMyHwnd();

		// Method group conversion to WNDENUMPROC delegate
		// (instance method - 'this' is captured by the delegate)
		unsafe
		{
			PInvoke.EnumWindows(EnumWindowsCallback, new LPARAM((nint)myHwnd.Value));
		}

		// Sort: visible first, then by process name
		var sorted = _enumBuffer.OrderByDescending(w => IsWindowVisibleSafe(w.Handle))
								.ThenBy(w => w.ProcessName)
								.ThenBy(w => w.WindowText)
								.ToList();

		foreach (var w in sorted)
			_windowList.Add(w);
	}

	/// <summary>
	/// EnumWindows callback - instance method converted to WNDENUMPROC delegate.
	/// CsWin32 generates WNDENUMPROC as a delegate type, so method group conversion works.
	/// </summary>
	private unsafe BOOL EnumWindowsCallback(HWND hwnd, LPARAM lParam)
	{
		try
		{
			// Skip invisible windows unless show hidden is checked
			if (!_showHiddenWindows && !PInvoke.IsWindowVisible(hwnd))
				return new BOOL(1);

			// Skip our own process windows
			if ((nint)hwnd.Value == lParam.Value || IsWindowOwnedByProcess(hwnd, _myProcessId))
				return new BOOL(1);

			// Get window text using stackalloc Span<char> (no heap allocation)
			int textLen = (int)PInvoke.GetWindowTextLength(hwnd);
			string windowText = string.Empty;
			if (textLen > 0)
			{
				Span<char> textBuffer = stackalloc char[textLen + 1];
				int actualLen = PInvoke.GetWindowText(hwnd, textBuffer);
				windowText = textBuffer.Slice(0, actualLen).ToString();
			}

			// Get class name
			Span<char> classBuffer = stackalloc char[256];
			int classLen = PInvoke.GetClassName(hwnd, classBuffer);
			string className = classBuffer.Slice(0, classLen).ToString();

			// Get process info
			uint pid = 0;
			uint* pPid = &pid;  // In unsafe context, direct pointer to local
			PInvoke.GetWindowThreadProcessId(hwnd, pPid);

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

	// ========== Window Selection & UI Update ==========

	private void SelectWindow(HWND hwnd)
	{
		if (!PInvoke.IsWindow(hwnd))
			return;

		if (_enableDisabledButtons && !PInvoke.IsWindowEnabled(hwnd))
		{
			PInvoke.EnableWindow(hwnd, true);
		}

		_currentHwnd = hwnd;
		_currentWindow = new WindowInfo(hwnd);
		UpdateUI();
	}

	private void UpdateUI()
	{
		if (_currentWindow == null)
			return;

		txtWindowHandle.Text = _currentWindow.WindowHandle;
		txtWindowTitle.Text = _currentWindow.WindowText;
		txtClassName.Text = _currentWindow.ClassName;
		txtWindowType.Text = _currentWindow.WindowType;
		txtStyle.Text = _currentWindow.Style;
		txtExStyle.Text = _currentWindow.ExStyle;
		txtWindowId.Text = _currentWindow.WindowId;
		txtParentHandle.Text = _currentWindow.ParentWindowHandle;
		txtParentClass.Text = _currentWindow.ParentClassName;
		txtParentTitle.Text = _currentWindow.ParentWindowText;
		txtLeft.Text = _currentWindow.Left.ToString();
		txtTop.Text = _currentWindow.Top.ToString();
		txtRight.Text = _currentWindow.Right.ToString();
		txtBottom.Text = _currentWindow.Bottom.ToString();
		txtWidth.Text = _currentWindow.Width.ToString();
		txtHeight.Text = _currentWindow.Height.ToString();
		txtThreadId.Text = _currentWindow.ThreadId;
		txtProcessId.Text = _currentWindow.ProcessId;
		txtProcessName.Text = _currentWindow.ProcessName;
		txtProcessPath.Text = _currentWindow.ProcessPath;
		txtInstanceHandle.Text = _currentWindow.InstanceHandle;
		txtModuleName.Text = _currentWindow.ModuleName;
		txtModulePath.Text = _currentWindow.ModulePath;
	}

	// ========== Event Handlers ==========

	private void lvWindows_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (lvWindows.SelectedItem is WindowListItem item)
		{
			SelectWindow(item.Handle);
		}
	}

	private void btnRefresh_Click(object sender, RoutedEventArgs e)
	{
		RefreshWindowList();
	}

	private unsafe void btnSetTitle_Click(object sender, RoutedEventArgs e)
	{
		if (!ValidateWindow()) return;

		string newTitle = txtWindowTitle.Text;
		if (string.IsNullOrEmpty(newTitle))
			return;

		// 使用 SetWindowText 避免依赖 WM_SETTEXT 常量和不安全代码
		PInvoke.SetWindowText(_currentHwnd, newTitle);

		_currentWindow?.Refresh();
		UpdateUI();
	}

	private void btnMinimize_Click(object sender, RoutedEventArgs e)
	{
		if (!ValidateWindow()) return;
		PInvoke.ShowWindow(_currentHwnd, SHOW_WINDOW_CMD.SW_MINIMIZE);
		_currentWindow?.Refresh();
		UpdateUI();
	}

	private void btnMaximize_Click(object sender, RoutedEventArgs e)
	{
		if (!ValidateWindow()) return;
		PInvoke.ShowWindow(_currentHwnd, SHOW_WINDOW_CMD.SW_MAXIMIZE);
		_currentWindow?.Refresh();
		UpdateUI();
	}

	private void btnNormal_Click(object sender, RoutedEventArgs e)
	{
		if (!ValidateWindow()) return;
		PInvoke.ShowWindow(_currentHwnd, SHOW_WINDOW_CMD.SW_SHOWNORMAL);
		_currentWindow?.Refresh();
		UpdateUI();
	}

	private void btnShow_Click(object sender, RoutedEventArgs e)
	{
		if (!ValidateWindow()) return;
		PInvoke.ShowWindow(_currentHwnd, SHOW_WINDOW_CMD.SW_SHOW);
		_currentWindow?.Refresh();
		UpdateUI();
	}

	private void btnHide_Click(object sender, RoutedEventArgs e)
	{
		if (!ValidateWindow()) return;
		PInvoke.ShowWindow(_currentHwnd, SHOW_WINDOW_CMD.SW_HIDE);
		_currentWindow?.Refresh();
		UpdateUI();
	}

	private unsafe void btnFlash_Click(object sender, RoutedEventArgs e)
	{
		if (!ValidateWindow()) return;

		var flashInfo = new FLASHWINFO
		{
			cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
			hwnd = _currentHwnd,
			dwFlags = FLASHWINFO_FLAGS.FLASHW_ALL | FLASHWINFO_FLAGS.FLASHW_TIMERNOFG,
			uCount = 5,
			dwTimeout = 0
		};

		PInvoke.FlashWindowEx(&flashInfo);
	}

	private void btnTopmost_Click(object sender, RoutedEventArgs e)
	{
		if (!ValidateWindow()) return;
		PInvoke.SetWindowPos(
			_currentHwnd,
			HWND_TOPMOST_VALUE,
			0, 0, 0, 0,
			SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW);
		_currentWindow?.Refresh();
		UpdateUI();
	}

	private void btnNoTopmost_Click(object sender, RoutedEventArgs e)
	{
		if (!ValidateWindow()) return;
		PInvoke.SetWindowPos(
			_currentHwnd,
			HWND_NOTOPMOST_VALUE,
			0, 0, 0, 0,
			SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW);
		_currentWindow?.Refresh();
		UpdateUI();
	}

	private void btnClose_Click(object sender, RoutedEventArgs e)
	{
		if (!ValidateWindow()) return;
		PInvoke.PostMessage(_currentHwnd, WM_CLOSE, default, default);
		Task.Delay(500).ContinueWith(_ =>
		{
			DispatcherQueue.TryEnqueue(() => RefreshWindowList());
		});
	}

	private void btnEnable_Click(object sender, RoutedEventArgs e)
	{
		if (!ValidateWindow()) return;
		PInvoke.EnableWindow(_currentHwnd, true);
		_currentWindow?.Refresh();
		UpdateUI();
	}

	private void btnDisable_Click(object sender, RoutedEventArgs e)
	{
		if (!ValidateWindow()) return;
		PInvoke.EnableWindow(_currentHwnd, false);
		_currentWindow?.Refresh();
		UpdateUI();
	}

	private void btnKillProcess_Click(object sender, RoutedEventArgs e)
	{
		if (_currentWindow == null || _currentWindow.ProcessId == "0")
		{
			ShowError("No valid process selected.");
			return;
		}

		_ = ShowKillDialogAsync();
	}

	private async Task ShowKillDialogAsync()
	{
		var dialog = new ContentDialog
		{
			Title = "Confirm",
			Content = $"Are you sure you want to terminate process '{_currentWindow?.ProcessName}' (PID: {_currentWindow?.ProcessId})?",
			PrimaryButtonText = "Yes",
			CloseButtonText = "No",
			XamlRoot = Content.XamlRoot
		};

		var result = await dialog.ShowAsync();
		if (result == ContentDialogResult.Primary)
		{
			try
			{
				if (_currentWindow != null)
				{
					uint pid = uint.Parse(_currentWindow.ProcessId);
					var hProcess = PInvoke.OpenProcess(PROCESS_ACCESS_RIGHTS.PROCESS_TERMINATE, false, pid);
					unsafe
					{
						if ((nint)hProcess.Value != 0)
						{
							PInvoke.TerminateProcess(hProcess, 1);
							PInvoke.CloseHandle(hProcess);
							RefreshWindowList();
						}
					}
				}
			}
			catch (Exception ex)
			{
				ShowError($"Failed to terminate process: {ex.Message}");
			}
		}
	}

	// ========== Checkbox handlers ==========

	private void chkTopmost_Checked(object sender, RoutedEventArgs e)
	{
		var myHwnd = GetMyHwnd();
		PInvoke.SetWindowPos(
			myHwnd,
			HWND_TOPMOST_VALUE,
			0, 0, 0, 0,
			SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE);
	}

	private void chkTopmost_Unchecked(object sender, RoutedEventArgs e)
	{
		var myHwnd = GetMyHwnd();
		PInvoke.SetWindowPos(
			myHwnd,
			HWND_NOTOPMOST_VALUE,
			0, 0, 0, 0,
			SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE);
	}

	private void chkAutoMode_Checked(object sender, RoutedEventArgs e)
	{
		if (!_isAutoMode)
		{
			StopOtherTrackingModes(TrackingMode.Auto);
			_isAutoMode = true;
			StartAutoMode();
		}
	}

	private void chkAutoMode_Unchecked(object sender, RoutedEventArgs e)
	{
		_isAutoMode = false;
		StopAutoMode();
	}

	private void chkShowHidden_Checked(object sender, RoutedEventArgs e)
	{
		_showHiddenWindows = true;
		RefreshWindowList();
	}

	private void chkShowHidden_Unchecked(object sender, RoutedEventArgs e)
	{
		_showHiddenWindows = false;
		RefreshWindowList();
	}

	private void chkEnableDisabled_Checked(object sender, RoutedEventArgs e)
	{
		_enableDisabledButtons = true;
	}

	private void chkEnableDisabled_Unchecked(object sender, RoutedEventArgs e)
	{
		_enableDisabledButtons = false;
	}

	private enum TrackingMode
	{
		Auto,
		FindWindow,
		FollowForeground
	}

	private void StopOtherTrackingModes(TrackingMode activeMode)
	{
		if (activeMode != TrackingMode.Auto && _isAutoMode)
		{
			_isAutoMode = false;
			StopAutoMode();
			chkAutoMode.IsChecked = false;
		}

		if (activeMode != TrackingMode.FollowForeground && _isSpying)
		{
			StopSpy();
			_isSpying = false;
			btnSpy.Content = "Follow Foreground";
		}

		if (activeMode != TrackingMode.FindWindow && _isFindingWindow)
		{
			StopFindWindow();
		}
	}

	// ========== Find Window (Drag crosshair) ==========

	private void btnFindWindow_Click(object sender, RoutedEventArgs e)
	{
		if (_findWindowJustCompleted)
		{
			_findWindowJustCompleted = false;
			return;
		}

		if (_isFindingWindow)
		{
			StopFindWindow();
			return;
		}

		StopOtherTrackingModes(TrackingMode.FindWindow);
		StartFindWindow();
	}

	private unsafe void StartFindWindow()
	{
		_isFindingWindow = true;
		btnFindWindow.Content = "Release to select";

		// Capture mouse globally for this window so pointer events keep firing
		// even when the cursor leaves the app.
		PInvoke.SetCapture(GetMyHwnd());

		// Load a size-all cursor for the drag operation.
		if (_dragCursor == null || (nint)_dragCursor.Value.Value == 0)
		{
			_dragCursor = PInvoke.LoadCursor(default(HINSTANCE), new PCWSTR((char*)IDC_SIZEALL));
		}
		_previousCursor = PInvoke.SetCursor(_dragCursor ?? default);

		// Poll cursor position so we can preview the target under the cursor.
		_findWindowTimer = new DispatcherTimer();
		_findWindowTimer.Interval = TimeSpan.FromMilliseconds(50);
		_findWindowTimer.Tick += FindWindowTimer_Tick;
		_findWindowTimer.Start();

		btnFindWindow.PointerReleased += FindWindow_PointerReleased;
	}

	private unsafe void StopFindWindow()
	{
		_isFindingWindow = false;
		btnFindWindow.Content = "Find Window";

		if (_findWindowTimer != null)
		{
			_findWindowTimer.Tick -= FindWindowTimer_Tick;
			_findWindowTimer.Stop();
			_findWindowTimer = null;
		}

		PInvoke.ReleaseCapture();
		if (_previousCursor != null)
		{
			PInvoke.SetCursor(_previousCursor ?? default);
			_previousCursor = null;
		}

		btnFindWindow.PointerReleased -= FindWindow_PointerReleased;
		ToolTipService.SetToolTip(btnFindWindow, "Drag to select any window on screen");
	}

	private unsafe void FindWindowTimer_Tick(object? sender, object? e)
	{
		if (!_isFindingWindow) return;

		try
		{
			// Keep the drag cursor active because WinUI may reset it.
			if (_dragCursor != null)
			{
				PInvoke.SetCursor(_dragCursor ?? default);
			}

			var hwnd = GetWindowFromCursor();
			if ((nint)hwnd.Value != 0)
			{
				var rootHwnd = PInvoke.GetAncestor(hwnd, GET_ANCESTOR_FLAGS.GA_ROOT);
				if ((nint)rootHwnd.Value != 0)
					hwnd = rootHwnd;
			}

			if ((nint)hwnd.Value != 0 && PInvoke.IsWindow(hwnd) && !IsWindowOwnedByProcess(hwnd, _myProcessId))
			{
				Span<char> textBuffer = stackalloc char[256];
				int len = PInvoke.GetWindowText(hwnd, textBuffer);
				string title = len > 0 ? textBuffer.Slice(0, len).ToString() : "(no title)";
				ToolTipService.SetToolTip(btnFindWindow, $"Target: {title} ({(nint)hwnd.Value:X8})");
			}
			else
			{
				ToolTipService.SetToolTip(btnFindWindow, "Release to select window");
			}
		}
		catch { }
	}

	private void FindWindow_PointerReleased(object sender, PointerRoutedEventArgs e)
	{
		if (!_isFindingWindow) return;

		try
		{
			var hwnd = GetWindowFromCursor();
			unsafe
			{
				if ((nint)hwnd.Value != 0)
				{
					var rootHwnd = PInvoke.GetAncestor(hwnd, GET_ANCESTOR_FLAGS.GA_ROOT);
					if ((nint)rootHwnd.Value != 0)
						hwnd = rootHwnd;

					if (!IsWindowOwnedByProcess(hwnd, _myProcessId))
					{
						SelectWindow(hwnd);
						foreach (var item in _windowList)
						{
							if ((nint)item.Handle.Value == (nint)hwnd.Value)
							{
								lvWindows.SelectedItem = item;
								lvWindows.ScrollIntoView(item);
								break;
							}
						}
					}
				}
			}
		}
		catch { }
		finally
		{
			_findWindowJustCompleted = true;
			StopFindWindow();
			ToolTipService.SetToolTip(btnFindWindow, "Drag to select any window on screen");
		}
	}

	// ========== Spy mode ==========

	private void btnSpy_Click(object sender, RoutedEventArgs e)
	{
		if (_isSpying)
		{
			StopSpy();
			btnSpy.Content = "Follow Foreground";
			_isSpying = false;
		}
		else
		{
			StopOtherTrackingModes(TrackingMode.FollowForeground);
			_isSpying = true;
			btnSpy.Content = "Stop Following";
			StartSpy();
		}
	}

	private void StartSpy()
	{
		Task.Run(async () =>
		{
			HWND lastHwnd = default;
			while (_isSpying)
			{
				try
				{
					var hwnd = PInvoke.GetForegroundWindow();
					unsafe
					{
						if ((nint)hwnd.Value != 0 && (nint)hwnd.Value != (nint)lastHwnd.Value && !IsWindowOwnedByProcess(hwnd, _myProcessId))
						{
							lastHwnd = hwnd;
							DispatcherQueue.TryEnqueue(() =>
							{
								if (_isSpying)
								{
									SelectWindow(hwnd);
									foreach (var item in _windowList)
									{
										if ((nint)item.Handle.Value == (nint)hwnd.Value)
										{
											lvWindows.SelectedItem = item;
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
		_isSpying = false;
	}

	// ========== Auto Mode ==========

	private void StartAutoMode()
	{
		_autoModeTimer = new Timer(_ =>
		{
			DispatcherQueue.TryEnqueue(() =>
			{
				if (!_isAutoMode) return;

				try
				{
					var hwnd = GetWindowFromCursor();
					unsafe
					{
						if ((nint)hwnd.Value != 0 && !IsWindowOwnedByProcess(hwnd, _myProcessId))
						{
							SelectWindow(hwnd);
						}
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

	// ========== Helper methods ==========

	private bool ValidateWindow()
	{
		if (!PInvoke.IsWindow(_currentHwnd))
		{
			ShowError("The selected window is no longer valid.");
			return false;
		}
		return true;
	}

	private void ShowError(string message)
	{
		_ = DispatcherQueue.TryEnqueue(async () =>
		{
			var dialog = new ContentDialog
			{
				Title = "Error",
				Content = message,
				CloseButtonText = "OK",
				XamlRoot = Content.XamlRoot
			};
			await dialog.ShowAsync();
		});
	}

	// ========== Native P/Invoke Helpers (not generated by CsWin32) ==========

	[StructLayout(LayoutKind.Sequential)]
	private struct POINT
	{ public int x; public int y; }

	[DllImport("user32.dll", SetLastError = true)]
	private static extern HWND WindowFromPoint(POINT pt);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern BOOL GetCursorPos(out POINT lpPoint);

	private static HWND WindowFromPoint(int x, int y)
	{
		return WindowFromPoint(new POINT { x = x, y = y });
	}

	private HWND GetWindowFromCursor()
	{
		if (GetCursorPos(out var pt))
		{
			return WindowFromPoint(pt);
		}
		return default;
	}
}

// ========== Data model ==========

public class WindowListItem
{
	internal HWND Handle { get; set; }
	public string DisplayText { get; set; } = string.Empty;
	public string WindowText { get; set; } = string.Empty;
	public string ClassName { get; set; } = string.Empty;
	public string ProcessName { get; set; } = string.Empty;

	public override string ToString() => DisplayText;
}