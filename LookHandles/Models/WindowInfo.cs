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

/// <summary>
/// Represents a best-effort snapshot of a native window and its owning thread and process.
/// </summary>
/// <remarks>
/// The native window handle is copied into this instance but remains owned by its caller. This object
/// does not destroy the window or close handles owned by its caller. Text queries copy native data into
/// temporary, caller-allocated buffers, and process handles opened during a refresh are closed by that scope.
/// Unavailable values can remain empty or zero, or can contain a textual sentinel such as
/// <c>[Access Denied]</c> or <c>[Unknown]</c>, depending on the query that failed.
/// </remarks>
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

	/// <summary>
	/// Gets the native window handle from which this snapshot was populated.
	/// </summary>
	/// <remarks>The handle is a borrowed value. Its validity and lifetime are managed by the caller.</remarks>
	internal HWND HWnd => _hwnd;

	/// <summary>
	/// Gets the native window handle as a display string.
	/// </summary>
	/// <value>
	/// A <c>0x</c>-prefixed uppercase hexadecimal value, zero-padded to at least eight digits. On a
	/// 64-bit process, wider handle values retain all of their digits.
	/// </value>
	public unsafe string WindowHandle => $"0x{(nint)_hwnd.Value:X8}";

	/// <summary>
	/// Gets or sets the window's title text.
	/// </summary>
	/// <value>
	/// The captured title, an empty string when no title is available, or <c>[Access Denied]</c> when
	/// the title query fails inside its refresh error handler.
	/// </value>
	public string WindowText
	{
		get => _windowText;
		set { _windowText = value; OnPropertyChanged(nameof(WindowText)); }
	}

	/// <summary>
	/// Gets or sets the native window class name.
	/// </summary>
	/// <value>
	/// The captured class name, an empty string until one is captured, or <c>[Access Denied]</c> when
	/// the class query fails inside its refresh error handler.
	/// </value>
	public string ClassName
	{
		get => _className;
		set { _className = value; OnPropertyChanged(nameof(ClassName)); }
	}

	/// <summary>
	/// Gets a human-readable decoding of selected standard and extended window style flags.
	/// </summary>
	/// <value>A space-separated list of style descriptions. It is not an exhaustive decoding of every flag.</value>
	public string WindowType => GetWindowTypeDescription();

	/// <summary>
	/// Gets the standard window style bits as a display string.
	/// </summary>
	/// <value>
	/// A <c>0x</c>-prefixed, eight-digit uppercase hexadecimal value. A refresh failure in the style
	/// query resets the underlying value to zero, displayed as <c>0x00000000</c>.
	/// </value>
	public string Style => $"0x{_style:X8}";

	/// <summary>
	/// Gets the extended window style bits as a display string.
	/// </summary>
	/// <value>
	/// A <c>0x</c>-prefixed, eight-digit uppercase hexadecimal value. A refresh failure in the style
	/// query resets the underlying value to zero, displayed as <c>0x00000000</c>.
	/// </value>
	public string ExStyle => $"0x{_exStyle:X8}";

	/// <summary>
	/// Gets the window identifier obtained through <c>GWL_ID</c>.
	/// </summary>
	/// <value>The signed 32-bit identifier in decimal notation, or <c>0</c> when its underlying value is zero.</value>
	public string WindowId => _windowId.ToString();

	/// <summary>
	/// Gets the parent window handle as a display string.
	/// </summary>
	/// <value>
	/// A <c>0x</c>-prefixed uppercase hexadecimal value, zero-padded to at least eight digits. The
	/// sentinel <c>0x00000000</c> indicates that no valid parent handle is stored.
	/// </value>
	public unsafe string ParentWindowHandle => (nint)_parentHwnd.Value != 0 ? $"0x{(nint)_parentHwnd.Value:X8}" : "0x00000000";

	/// <summary>
	/// Gets or sets the parent window's native class name.
	/// </summary>
	/// <value>
	/// An empty string when there is no valid parent, the captured class name, or
	/// <c>[Access Denied]</c> when the parent query fails inside its refresh error handler.
	/// </value>
	public string ParentClassName
	{
		get => _parentClassName;
		set { _parentClassName = value; OnPropertyChanged(nameof(ParentClassName)); }
	}

	/// <summary>
	/// Gets or sets the parent window's title text.
	/// </summary>
	/// <value>
	/// An empty string when there is no valid parent or no parent title, the captured title, or
	/// <c>[Access Denied]</c> when the parent query fails inside its refresh error handler.
	/// </value>
	public string ParentWindowText
	{
		get => _parentWindowText;
		set { _parentWindowText = value; OnPropertyChanged(nameof(ParentWindowText)); }
	}

	/// <summary>Gets the left edge of the window rectangle in screen pixel coordinates.</summary>
	public int Left => _left;

	/// <summary>Gets the top edge of the window rectangle in screen pixel coordinates.</summary>
	public int Top => _top;

	/// <summary>Gets the right edge of the window rectangle in screen pixel coordinates.</summary>
	public int Right => _right;

	/// <summary>Gets the bottom edge of the window rectangle in screen pixel coordinates.</summary>
	public int Bottom => _bottom;

	/// <summary>Gets the window width in pixels, calculated as <see cref="Right"/> minus <see cref="Left"/>.</summary>
	public int Width => _width;

	/// <summary>Gets the window height in pixels, calculated as <see cref="Bottom"/> minus <see cref="Top"/>.</summary>
	public int Height => _height;

	/// <summary>
	/// Gets the identifier of the thread that created the window.
	/// </summary>
	/// <value>The unsigned 32-bit identifier in decimal notation, or <c>0</c> when unavailable.</value>
	public string ThreadId => _threadId.ToString();

	/// <summary>
	/// Gets the identifier of the process that owns the window.
	/// </summary>
	/// <value>The unsigned 32-bit identifier in decimal notation, or <c>0</c> when unavailable.</value>
	public string ProcessId => _processId.ToString();

	/// <summary>
	/// Gets or sets the name of the process that owns the window.
	/// </summary>
	/// <value>
	/// The captured process name, an empty string until one is captured, or <c>[Unknown]</c> when
	/// the process details cannot be obtained.
	/// </value>
	public string ProcessName
	{
		get => _processName;
		set { _processName = value; OnPropertyChanged(nameof(ProcessName)); }
	}

	/// <summary>
	/// Gets or sets the full path to the owning process's main executable.
	/// </summary>
	/// <value>
	/// The captured path, an empty string when no path is available, or <c>[Access Denied]</c> when
	/// the main-module query fails inside its refresh error handler.
	/// </value>
	public string ProcessPath
	{
		get => _processPath;
		set { _processPath = value; OnPropertyChanged(nameof(ProcessPath)); }
	}

	/// <summary>
	/// Gets the window's instance handle obtained through <c>GWL_HINSTANCE</c>.
	/// </summary>
	/// <value>
	/// A <c>0x</c>-prefixed uppercase hexadecimal value, zero-padded to at least eight digits. This is
	/// a borrowed handle value; the snapshot does not own or release the referenced module.
	/// </value>
	public string InstanceHandle => $"0x{_instanceHandle:X8}";

	/// <summary>
	/// Gets or sets the file name portion of the module path returned by the process-image query.
	/// </summary>
	/// <value>
	/// The captured module file name, an empty string until one is captured, or
	/// <c>[Access Denied]</c> when the module query fails inside its refresh error handler.
	/// </value>
	public string ModuleName
	{
		get => _moduleName;
		set { _moduleName = value; OnPropertyChanged(nameof(ModuleName)); }
	}

	/// <summary>
	/// Gets or sets the full process-image path obtained by <c>QueryFullProcessImageNameW</c>.
	/// </summary>
	/// <value>
	/// The captured module path, an empty string until one is captured, or <c>[Access Denied]</c>
	/// when the module query fails inside its refresh error handler.
	/// </value>
	public string ModulePath
	{
		get => _modulePath;
		set { _modulePath = value; OnPropertyChanged(nameof(ModulePath)); }
	}

	/// <summary>
	/// Initializes a snapshot for the specified native window and performs an initial refresh.
	/// </summary>
	/// <param name="hwnd">
	/// The borrowed native window handle to inspect. This instance stores the value but does not take
	/// ownership of the window or its lifetime.
	/// </param>
	internal unsafe WindowInfo(HWND hwnd)
	{
		_hwnd = hwnd;
		_parentHwnd = default;
		Refresh();
	}

	/// <summary>
	/// Requeries the native window and its owning thread and process to update this snapshot.
	/// </summary>
	/// <remarks>
	/// <para>
	/// If the stored handle is not a valid window, this method returns immediately without clearing or
	/// otherwise changing any previously captured values.
	/// </para>
	/// <para>
	/// Refresh is best effort. Each information group is queried independently, so a failure in one group
	/// does not prevent later groups from being attempted. Depending on the query, a failure can set an
	/// empty, zero, or sentinel value, or can leave earlier values unchanged. In particular, an
	/// unsuccessful rectangle query leaves the previous coordinates and dimensions intact.
	/// </para>
	/// <para>
	/// Native text is copied into temporary buffers owned by this method. A process handle opened for the
	/// module query is owned by that query scope and is closed in its <c>finally</c> block; no process
	/// handle is retained by this snapshot.
	/// </para>
	/// </remarks>
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

	/// <summary>
	/// Manually imports the Unicode <c>QueryFullProcessImageNameW</c> function for a caller-supplied buffer.
	/// </summary>
	/// <param name="hProcess">
	/// A valid process handle with the access required by the native function. The caller retains ownership
	/// of this handle and is responsible for closing it.
	/// </param>
	/// <param name="dwFlags">The native process-image name format flags.</param>
	/// <param name="lpExeName">
	/// A pointer to the caller-owned Unicode character buffer that receives the path. The native function
	/// does not take ownership of this buffer.
	/// </param>
	/// <param name="lpdwSize">
	/// On input, the buffer capacity in characters. On success, receives the path length in characters,
	/// excluding the terminating null character.
	/// </param>
	/// <returns><see langword="true"/> when the native query succeeds; otherwise, <see langword="false"/>.</returns>
	[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	private static extern unsafe bool QueryFullProcessImageNameW(HANDLE hProcess, uint dwFlags, char* lpExeName, ref uint lpdwSize);

	/// <summary>
	/// Decodes selected standard and extended style bits into display labels.
	/// </summary>
	/// <returns>A trimmed, space-separated list of the labels selected by the current style values.</returns>
	/// <remarks>
	/// This is a display-oriented, non-exhaustive decoding. Because <c>WS_OVERLAPPED</c> has a value of zero,
	/// its bit test always succeeds and the <c>Overlapped</c> label is always included by the current logic.
	/// </remarks>
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

	/// <inheritdoc/>
	public event PropertyChangedEventHandler? PropertyChanged;

	/// <summary>
	/// Raises <see cref="PropertyChanged"/> for the specified property when subscribers are present.
	/// </summary>
	/// <param name="propertyName">The name of the property whose value may have changed.</param>
	protected void OnPropertyChanged(string propertyName)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}

	/// <inheritdoc/>
	/// <returns>
	/// A string in the form <c>[handle] display</c>, where the handle uses <see cref="WindowHandle"/>
	/// formatting and the display text is <see cref="WindowText"/>, or <see cref="ClassName"/> when the
	/// window text is null or empty.
	/// </returns>
	public override string ToString()
	{
		string display = string.IsNullOrEmpty(WindowText) ? ClassName : WindowText;
		return $"[{WindowHandle}] {display}";
	}
}