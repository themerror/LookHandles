using Microsoft.UI.Xaml;
using System;
using LookHandles.Helpers;
using LookHandles.ViewModels;

namespace LookHandles;

/// <summary>
/// Presents the standalone window list using the main window's shared view model.
/// </summary>
public sealed partial class WindowListWindow : Window
{
	/// <summary>
	/// Gets the main window's view model, which supplies shared selection and refresh state.
	/// </summary>
	public MainViewModel ViewModel { get; }

	/// <summary>
	/// Initializes the window list with the <see cref="MainViewModel"/> instance shared with, and owned by, the main window.
	/// </summary>
	/// <param name="viewModel">The shared view model, not an independently owned copy, so selection and refresh stay synchronized between both windows.</param>
	/// <exception cref="ArgumentNullException">Thrown when <paramref name="viewModel"/> is <see langword="null"/>.</exception>
	public WindowListWindow(MainViewModel viewModel)
	{
		ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

		this.InitializeComponent();
		WindowSizing.ResizeInDips(this, 800, 500);
		RootGrid.DataContext = ViewModel;
	}
}
