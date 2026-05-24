using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;
using Windows.Win32.UI.WindowsAndMessaging;

namespace LookHandles;

public class WindowInfo : INotifyPropertyChanged
{
	private HWND _hwnd;
	private string _windowText = string.Empty;
	private string _className = string.Empty;
	private uint _style;
	private uint _exStyle;
	private int _windowId;
	private HWND _parentHwnd;
	private string _parentClassName = string.Empty;
	private string _parentWindowText = string.Empty;
	private int _left;
	private int _top;
	private int _right;
	private int _bottom;
	private int _width;
	private int _height;
	private uint _threadId;
	private uint _processId;
	private string _processName = string.Empty;
	private string _processPath = string.Empty;
	private nint _instanceHandle;
	private string _moduleName = string.Empty;
	private string _modulePath = string.Empty;

	internal HWND HWnd => _hwnd;

	public unsafe string WindowHandle => $"0x{(nint)_hwnd.Value:X8}";

	public string WindowText
	{
		get => _windowText;
		set { _windowText = value; OnPropertyChanged(nameof(WindowText)); }
	}

	public string ClassName
	{
		get => _className;
		set { _className = value; OnPropertyChanged(nameof(ClassName)); }
	}

	public string WindowType => GetWindowTypeDescription();

	public string Style => $"0x{_style:X8}";

	public string ExStyle => $"0x{_exStyle:X8}";

	public string WindowId => _windowId.ToString();

	public unsafe string ParentWindowHandle => (nint)_parentHwnd.Value != 0 ? $"0x{(nint)_parentHwnd.Value:X8}" : "0x00000000";

	public string ParentClassName
	{
		get => _parentClassName;
		set { _parentClassName = value; OnPropertyChanged(nameof(ParentClassName)); }
	}

	public string ParentWindowText
	{
		get => _parentWindowText;
		set { _parentWindowText = value; OnPropertyChanged(nameof(ParentWindowText)); }
	}

	public int Left => _left;
	public int Top => _top;
	public int Right => _right;
	public int Bottom => _bottom;
	public int Width => _width;
	public int Height => _height;

	public string ThreadId => _threadId.ToString();

	public string ProcessId => _processId.ToString();

	public string ProcessName
	{
		get => _processName;
		set { _processName = value; OnPropertyChanged(nameof(ProcessName)); }
	}

	public string ProcessPath
	{
		get => _processPath;
		set { _processPath = value; OnPropertyChanged(nameof(ProcessPath)); }
	}

	public string InstanceHandle => $"0x{_instanceHandle:X8}";

	public string ModuleName
	{
		get => _moduleName;
		set { _moduleName = value; OnPropertyChanged(nameof(ModuleName)); }
	}

	public string ModulePath
	{
		get => _modulePath;
		set { _modulePath = value; OnPropertyChanged(nameof(ModulePath)); }
	}

	internal unsafe WindowInfo(HWND hwnd)
	{
		_hwnd = hwnd;
		_parentHwnd = default;
		Refresh();
	}

	public unsafe void Refresh()
	{
		if (!PInvoke.IsWindow(_hwnd))
			return;

		// Window text
		try
		{
			int len = (int)PInvoke.GetWindowTextLength(_hwnd);
			if (len > 0)
			{
				Span<char> buffer = stackalloc char[len + 1];
				int actualLen = PInvoke.GetWindowText(_hwnd, buffer);
				WindowText = buffer.Slice(0, actualLen).ToString();
			}
			else
			{
				WindowText = string.Empty;
			}
		}
		catch
		{
			WindowText = "[Access Denied]";
		}

		// Class name
		try
		{
			Span<char> classBuffer = stackalloc char[256];
			int classLen = PInvoke.GetClassName(_hwnd, classBuffer);
			ClassName = classBuffer.Slice(0, classLen).ToString();
		}
		catch
		{
			ClassName = "[Access Denied]";
		}

		// Window styles
		try
		{
			_style = (uint)(int)PInvoke.GetWindowLong(_hwnd, WINDOW_LONG_PTR_INDEX.GWL_STYLE);
			_exStyle = (uint)(int)PInvoke.GetWindowLong(_hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
			_windowId = (int)PInvoke.GetWindowLong(_hwnd, WINDOW_LONG_PTR_INDEX.GWL_ID);
			_instanceHandle = (nint)PInvoke.GetWindowLong(_hwnd, WINDOW_LONG_PTR_INDEX.GWL_HINSTANCE);
			OnPropertyChanged(nameof(Style));
			OnPropertyChanged(nameof(ExStyle));
			OnPropertyChanged(nameof(WindowId));
			OnPropertyChanged(nameof(InstanceHandle));
			OnPropertyChanged(nameof(WindowType));
		}
		catch
		{
			_style = 0;
			_exStyle = 0;
			_windowId = 0;
			_instanceHandle = 0;
		}

		// Parent window
		try
		{
			_parentHwnd = PInvoke.GetAncestor(_hwnd, GET_ANCESTOR_FLAGS.GA_PARENT);
			if ((nint)_parentHwnd.Value != 0 && PInvoke.IsWindow(_parentHwnd))
			{
				Span<char> parentClassBuffer = stackalloc char[256];
				int parentClassLen = PInvoke.GetClassName(_parentHwnd, parentClassBuffer);
				ParentClassName = parentClassBuffer.Slice(0, parentClassLen).ToString();

				int parentTextLen = (int)PInvoke.GetWindowTextLength(_parentHwnd);
				if (parentTextLen > 0)
				{
					Span<char> parentTextBuffer = stackalloc char[parentTextLen + 1];
					int actualParentLen = PInvoke.GetWindowText(_parentHwnd, parentTextBuffer);
					ParentWindowText = parentTextBuffer.Slice(0, actualParentLen).ToString();
				}
				else
				{
					ParentWindowText = string.Empty;
				}
			}
			else
			{
				_parentHwnd = default;
				ParentClassName = string.Empty;
				ParentWindowText = string.Empty;
			}
			OnPropertyChanged(nameof(ParentWindowHandle));
		}
		catch
		{
			_parentHwnd = default;
			ParentClassName = "[Access Denied]";
			ParentWindowText = "[Access Denied]";
		}

		// Window rect
		try
		{
			if (PInvoke.GetWindowRect(_hwnd, out var rect))
			{
				_left = rect.left;
				_top = rect.top;
				_right = rect.right;
				_bottom = rect.bottom;
				_width = rect.right - rect.left;
				_height = rect.bottom - rect.top;
			}
		}
		catch { }

		// Thread and process info
		try
		{
			uint pid = 0;
			_threadId = PInvoke.GetWindowThreadProcessId(_hwnd, &pid);
			_processId = pid;
			OnPropertyChanged(nameof(ThreadId));
			OnPropertyChanged(nameof(ProcessId));

			// Process details
			try
			{
				using var process = Process.GetProcessById((int)_processId);
				ProcessName = process.ProcessName;
				try
				{
					ProcessPath = process.MainModule?.FileName ?? string.Empty;
				}
				catch
				{
					ProcessPath = "[Access Denied]";
				}
			}
			catch
			{
				ProcessName = "[Unknown]";
				ProcessPath = string.Empty;
			}

			// Module info using QueryFullProcessImageNameW
			try
			{
				var hProcess = PInvoke.OpenProcess(
					PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_INFORMATION | PROCESS_ACCESS_RIGHTS.PROCESS_VM_READ,
					false,
					_processId);

				if ((nint)hProcess.Value != 0)
				{
					try
					{
						char* pPath = stackalloc char[260];
						uint size = 260;
						if (QueryFullProcessImageNameW(hProcess, 0, pPath, ref size) && size > 0)
						{
							ModulePath = new string(pPath, 0, (int)size);
							ModuleName = Path.GetFileName(ModulePath);
						}
					}
					finally
					{
						PInvoke.CloseHandle(hProcess);
					}
				}
			}
			catch
			{
				ModuleName = "[Access Denied]";
				ModulePath = "[Access Denied]";
			}
		}
		catch
		{
			_threadId = 0;
			_processId = 0;
		}
	}

	[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	private static extern unsafe bool QueryFullProcessImageNameW(HANDLE hProcess, uint dwFlags, char* lpExeName, ref uint lpdwSize);

	private string GetWindowTypeDescription()
	{
		var type = new StringBuilder();

		if ((_style & (uint)WINDOW_STYLE.WS_OVERLAPPED) == (uint)WINDOW_STYLE.WS_OVERLAPPED)
			type.Append("Overlapped ");
		if ((_style & (uint)WINDOW_STYLE.WS_POPUP) == (uint)WINDOW_STYLE.WS_POPUP)
			type.Append("Popup ");
		if ((_style & (uint)WINDOW_STYLE.WS_CHILD) == (uint)WINDOW_STYLE.WS_CHILD)
			type.Append("Child ");
		if ((_style & (uint)WINDOW_STYLE.WS_MINIMIZE) == (uint)WINDOW_STYLE.WS_MINIMIZE)
			type.Append("Minimized ");
		if ((_style & (uint)WINDOW_STYLE.WS_VISIBLE) == (uint)WINDOW_STYLE.WS_VISIBLE)
			type.Append("Visible ");
		if ((_style & (uint)WINDOW_STYLE.WS_DISABLED) == (uint)WINDOW_STYLE.WS_DISABLED)
			type.Append("Disabled ");
		if ((_style & (uint)WINDOW_STYLE.WS_MAXIMIZE) == (uint)WINDOW_STYLE.WS_MAXIMIZE)
			type.Append("Maximized ");
		if ((_style & (uint)WINDOW_STYLE.WS_CAPTION) == (uint)WINDOW_STYLE.WS_CAPTION)
			type.Append("Caption ");
		if ((_style & (uint)WINDOW_STYLE.WS_THICKFRAME) == (uint)WINDOW_STYLE.WS_THICKFRAME)
			type.Append("Sizable ");
		if ((_style & (uint)WINDOW_STYLE.WS_SYSMENU) == (uint)WINDOW_STYLE.WS_SYSMENU)
			type.Append("SysMenu ");
		if ((_style & (uint)WINDOW_STYLE.WS_MINIMIZEBOX) == (uint)WINDOW_STYLE.WS_MINIMIZEBOX)
			type.Append("MinBox ");
		if ((_style & (uint)WINDOW_STYLE.WS_MAXIMIZEBOX) == (uint)WINDOW_STYLE.WS_MAXIMIZEBOX)
			type.Append("MaxBox ");

		if ((_exStyle & (uint)WINDOW_EX_STYLE.WS_EX_TOPMOST) == (uint)WINDOW_EX_STYLE.WS_EX_TOPMOST)
			type.Append("TopMost ");
		if ((_exStyle & (uint)WINDOW_EX_STYLE.WS_EX_TOOLWINDOW) == (uint)WINDOW_EX_STYLE.WS_EX_TOOLWINDOW)
			type.Append("ToolWindow ");
		if ((_exStyle & (uint)WINDOW_EX_STYLE.WS_EX_LAYERED) == (uint)WINDOW_EX_STYLE.WS_EX_LAYERED)
			type.Append("Layered ");
		if ((_exStyle & (uint)WINDOW_EX_STYLE.WS_EX_TRANSPARENT) == (uint)WINDOW_EX_STYLE.WS_EX_TRANSPARENT)
			type.Append("Transparent ");

		return type.ToString().Trim();
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	protected void OnPropertyChanged(string propertyName)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}

	public override string ToString()
	{
		string display = string.IsNullOrEmpty(WindowText) ? ClassName : WindowText;
		return $"[{WindowHandle}] {display}";
	}
}