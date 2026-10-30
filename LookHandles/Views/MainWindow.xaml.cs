using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using LookHandles.Helpers;
using LookHandles.Services;
using LookHandles.ViewModels;

namespace LookHandles;

/// <summary>
/// Hosts the main LookHandles interface and coordinates its layout, dialogs, and standalone window list.
/// </summary>
public sealed partial class MainWindow : Window
{
	private static readonly HWND HWND_TOPMOST_VALUE = new HWND(new IntPtr(-1));
	private static readonly HWND HWND_NOTOPMOST_VALUE = new HWND(new IntPtr(-2));
	private WindowListWindow? _windowListWindow;

	/// <summary>
	/// Gets the view model shared with the standalone window list.
	/// </summary>
	public MainViewModel ViewModel { get; }

	/// <summary>
	/// Initializes the main window, its shared view model, and its event handlers.
	/// </summary>
	public MainWindow()
	{
		this.InitializeComponent();
		RootGrid.SizeChanged += RootGrid_SizeChanged;
		RootGrid_SizeChanged(RootGrid, null!);

		WindowSizing.ResizeInDips(this, 900, 720);
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
		Closed += MainWindow_Closed;
	}

	/// <summary>
	/// Uses a two-column layout at 760 DIPs or wider, and stacks the right panel below the left panel in a single column below that width.
	/// </summary>
	private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e)
	{
		if (RootGrid.ActualWidth < 760)
		{
			PanelSpacingColumn.Width = new GridLength(0);
			RightPanelColumn.Width = new GridLength(0);
			Grid.SetRow(RightPanel, 1);
			Grid.SetColumn(RightPanel, 0);
			RightPanel.Margin = new Thickness(0, 16, 0, 0);
			return;
		}

		PanelSpacingColumn.Width = new GridLength(12);
		RightPanelColumn.Width = new GridLength(1, GridUnitType.Star);
		Grid.SetRow(RightPanel, 0);
		Grid.SetColumn(RightPanel, 2);
		RightPanel.Margin = new Thickness(0);
	}

	private nint GetMyHwnd()
	{
		return (nint)WinRT.Interop.WindowNative.GetWindowHandle(this);
	}

	/// <summary>
	/// Makes the main window topmost while preserving its current size and position.
	/// </summary>
	private void chkTopmost_Checked(object sender, RoutedEventArgs e)
	{
		PInvoke.SetWindowPos(
			new HWND(GetMyHwnd()),
			HWND_TOPMOST_VALUE,
			0, 0, 0, 0,
			SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE);
	}

	/// <summary>
	/// Removes the main window's topmost status while preserving its current size and position.
	/// </summary>
	private void chkTopmost_Unchecked(object sender, RoutedEventArgs e)
	{
		PInvoke.SetWindowPos(
			new HWND(GetMyHwnd()),
			HWND_NOTOPMOST_VALUE,
			0, 0, 0, 0,
			SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE);
	}

	/// <summary>
	/// Creates the standalone window list on first use, then reuses that single instance by restoring it if minimized or otherwise activating it.
	/// </summary>
	private void btnWindowList_Click(object sender, RoutedEventArgs e)
	{
		if (_windowListWindow is null)
		{
			_windowListWindow = new WindowListWindow(ViewModel);
			_windowListWindow.Closed += WindowListWindow_Closed;
			_windowListWindow.Activate();
			return;
		}

		var presenter = _windowListWindow.AppWindow.Presenter as OverlappedPresenter;
		if (presenter?.State == OverlappedPresenterState.Minimized)
			presenter.Restore(true);
		else
			_windowListWindow.Activate();
	}

	/// <summary>
	/// Clears the cached window-list reference only when the closed window is the tracked instance.
	/// </summary>
	private void WindowListWindow_Closed(object sender, WindowEventArgs args)
	{
		if (ReferenceEquals(_windowListWindow, sender))
			_windowListWindow = null;
	}

	private void btnFindWindow_Click(object sender, RoutedEventArgs e)
	{
		ViewModel.ToggleFindWindowMode();
	}

	private void btnSpy_Click(object sender, RoutedEventArgs e)
	{
		ViewModel.IsSpying = !ViewModel.IsSpying;
	}

	/// <summary>
	/// Cancels Find Window capture, stops automatic and spy tracking, and closes the standalone window list during main-window cleanup.
	/// </summary>
	private void MainWindow_Closed(object sender, WindowEventArgs args)
	{
		ViewModel.CancelFindWindow();
		ViewModel.IsAutoMode = false;
		ViewModel.IsSpying = false;
		_windowListWindow?.Close();
	}

	/// <summary>
	/// Displays an error through the main window's dialog helper.
	/// </summary>
	/// <param name="message">The error message to display.</param>
	private async void ShowError(string message)
	{
		await ShowDialogAsync("Error", message, "OK");
	}

	/// <summary>
	/// Marshals the kill-process confirmation dialog to the main window's dispatcher queue.
	/// </summary>
	/// <param name="processName">The name of the process awaiting confirmation.</param>
	/// <param name="processId">The identifier of the process awaiting confirmation.</param>
	/// <returns>A task whose result indicates whether the primary confirmation button was selected.</returns>
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

	/// <summary>
	/// Shows a content dialog using the main window's XAML root.
	/// </summary>
	/// <param name="title">The dialog title.</param>
	/// <param name="content">The dialog message.</param>
	/// <param name="closeButtonText">The text for the dialog's close button.</param>
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
