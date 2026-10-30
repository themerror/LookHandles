using Microsoft.UI.Xaml;

namespace LookHandles;

/// <summary>
/// Provides application-specific behavior and hosts the main LookHandles window.
/// </summary>
public partial class App : Application
{
    private Window? m_window;

    /// <summary>
    /// Initializes a new instance of the <see cref="App"/> class.
    /// </summary>
    public App()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Creates and activates the main window when the application is launched.
    /// </summary>
    /// <param name="args">The launch activation arguments.</param>
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        m_window = new MainWindow();
        m_window.Activate();
    }
}
