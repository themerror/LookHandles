using Microsoft.UI.Xaml;
using System;
using Windows.Graphics;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace LookHandles.Helpers;

/// <summary>
/// Converts device-independent window dimensions to physical pixels using the target window's DPI.
/// </summary>
public static class WindowSizing
{
	/// <summary>
	/// Resizes a window to the specified device-independent dimensions.
	/// </summary>
	/// <param name="window">The window whose current DPI and <c>AppWindow</c> are used.</param>
	/// <param name="widthDips">The desired width in device-independent pixels (DIPs).</param>
	/// <param name="heightDips">The desired height in device-independent pixels (DIPs).</param>
	/// <remarks>
	/// DPI is sampled at call time, and the scale is <c>GetDpiForWindow / 96.0</c>. Each scaled dimension is rounded up with
	/// <see cref="Math.Ceiling(double)"/> before being passed to <c>AppWindow.Resize</c>, which expects physical pixels.
	/// </remarks>
	/// <exception cref="ArgumentNullException"><paramref name="window"/> is <see langword="null"/>.</exception>
	public static void ResizeInDips(Window window, double widthDips, double heightDips)
	{
		ArgumentNullException.ThrowIfNull(window);

		var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
		var dpiScale = PInvoke.GetDpiForWindow(new HWND(hwnd)) / 96.0;
		var physicalWidth = (int)Math.Ceiling(widthDips * dpiScale);
		var physicalHeight = (int)Math.Ceiling(heightDips * dpiScale);

		window.AppWindow.Resize(new SizeInt32(physicalWidth, physicalHeight));
	}
}
