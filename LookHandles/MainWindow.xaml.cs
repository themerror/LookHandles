using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Threading.Tasks;
using Windows.Graphics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using LookHandles.Services;
using LookHandles.ViewModels;

namespace LookHandles;

public sealed partial class MainWindow : Window
{
	private static readonly HWND HWND_TOPMOST_VALUE = new HWND(new IntPtr(-1));
	private static readonly HWND HWND_NOTOPMOST_VALUE = new HWND(new IntPtr(-2));

	public MainViewModel ViewModel { get; }

	public MainWindow()
	{
		this.InitializeComponent();

		SetWindowSize(900, 720);
		Title = "LookHandles 3.0";
		ExtendsContentIntoTitleBar = true;

		ViewModel = new MainViewModel(
			new WindowEnumerationService(),
			new WindowManipulationService(),
			new MouseCaptureService(),
			DispatcherQueue);

		ViewModel.OnShowError += ShowError;
		ViewModel.OnConfirmKillProcessRequested = ShowKillDialogAsync;

		RootGrid.DataContext = ViewModel;
	}

	private void SetWindowSize(int width, int height)
	{
		var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
		var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
		var appWindow = AppWindow.GetFromWindowId(windowId);
		appWindow.Resize(new SizeInt32(width, height));
	}

	private nint GetMyHwnd()
	{
		return (nint)WinRT.Interop.WindowNative.GetWindowHandle(this);
	}

	private void chkTopmost_Checked(object sender, RoutedEventArgs e)
	{
		PInvoke.SetWindowPos(
			new HWND(GetMyHwnd()),
			HWND_TOPMOST_VALUE,
			0, 0, 0, 0,
			SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE);
	}

	private void chkTopmost_Unchecked(object sender, RoutedEventArgs e)
	{
		PInvoke.SetWindowPos(
			new HWND(GetMyHwnd()),
			HWND_NOTOPMOST_VALUE,
			0, 0, 0, 0,
			SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE);
	}

	private void btnFindWindow_Click(object sender, RoutedEventArgs e)
	{
		ViewModel.ToggleFindWindowMode();
		if (ViewModel.IsFindingWindow)
		{
			ViewModel.BeginFindWindowCapture(GetMyHwnd());
			btnFindWindow.PointerPressed += FindWindow_PointerPressed;
		}
	}

	private void FindWindow_PointerPressed(object sender, PointerRoutedEventArgs e)
	{
		var pointer = e.GetCurrentPoint(btnFindWindow);
		if (!pointer.Properties.IsLeftButtonPressed)
			return;

		ViewModel.HandleFindWindowPointerPressed();
		btnFindWindow.PointerPressed -= FindWindow_PointerPressed;
	}

	private void btnSpy_Click(object sender, RoutedEventArgs e)
	{
		ViewModel.IsSpying = !ViewModel.IsSpying;
	}

	private async void ShowError(string message)
	{
		await ShowDialogAsync("Error", message, "OK");
	}

	private Task<bool> ShowKillDialogAsync(string processName, string processId)
	{
		var tcs = new TaskCompletionSource<bool>();
		_ = DispatcherQueue.TryEnqueue(async () =>
		{
			var dialog = new ContentDialog
			{
				Title = "Confirm",
				Content = $"Are you sure you want to terminate process '{processName}' (PID: {processId})?",
				PrimaryButtonText = "Yes",
				CloseButtonText = "No",
				XamlRoot = Content.XamlRoot
			};
			var result = await dialog.ShowAsync();
			tcs.SetResult(result == ContentDialogResult.Primary);
		});
		return tcs.Task;
	}

	private async Task ShowDialogAsync(string title, string content, string closeButtonText)
	{
		var dialog = new ContentDialog
		{
			Title = title,
			Content = content,
			CloseButtonText = closeButtonText,
			XamlRoot = Content.XamlRoot
		};
		await dialog.ShowAsync();
	}
}
