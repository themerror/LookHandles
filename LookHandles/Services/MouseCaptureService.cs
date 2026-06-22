using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace LookHandles.Services;

public interface IMouseCaptureService
{
	nint GetWindowHandleFromCursor();
	void SetCapture(nint hwnd);
	void ReleaseCapture();
}

public class MouseCaptureService : IMouseCaptureService
{
	[StructLayout(LayoutKind.Sequential)]
	private struct POINT
	{
		public int x;
		public int y;
	}

	[DllImport("user32.dll", SetLastError = true)]
	private static extern HWND WindowFromPoint(POINT pt);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern BOOL GetCursorPos(out POINT lpPoint);

	public nint GetWindowHandleFromCursor()
	{
		if (GetCursorPos(out var pt))
		{
			unsafe
			{
				return (nint)WindowFromPoint(pt).Value;
			}
		}
		return 0;
	}

	public void SetCapture(nint hwnd)
	{
		try { PInvoke.SetCapture(new HWND(hwnd)); } catch { }
	}

	public void ReleaseCapture()
	{
		try { PInvoke.ReleaseCapture(); } catch { }
	}
}
