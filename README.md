# LookHandles 3.0 - WinUI 3 Window Spy Tool

A WinUI 3 + Windows App SDK (WASDK) implementation of the LookHandles window spy tool, similar to Microsoft Spy++.

## Features

### Window Information
- **Window Handle**: Hexadecimal window handle (HWND)
- **Window Title**: Window caption text (editable)
- **Class Name**: Window class name
- **Window Type**: Decoded window style flags (Overlapped, Popup, Child, Visible, etc.)
- **Style / ExStyle**: Window style and extended style values in hexadecimal
- **Window ID**: Window identifier
- **Parent Info**: Parent window handle, class name, and title
- **Position**: Left, Top, Right, Bottom coordinates and dimensions

### Process Information
- **Thread ID / Process ID**: Associated thread and process identifiers
- **Process Name**: Name of the owning process
- **Process Path**: Full path to the executable
- **Instance Handle**: Module instance handle (HINSTANCE)
- **Module Name / Path**: Main module information

### Window Operations
- **Minimize / Maximize / Normal**: Change window show state
- **Show / Hide**: Control window visibility
- **Flash**: Flash the window to get attention
- **Topmost / No Topmost**: Set or remove topmost z-order
- **Close**: Send WM_CLOSE message to the window
- **Enable / Disable**: Enable or disable the window
- **Kill Process**: Terminate the owning process (with confirmation dialog)

### Advanced Features
- **Auto Mode**: Automatically track the window under the mouse cursor (500ms polling)
- **Spy Mode**: Track the foreground window as it changes
- **Window List**: Open a standalone window that shares selection and refresh state with the main window. Click Window List again to reuse and activate the existing window, restoring it if minimized. Two-line items show the HWND and title, followed by class and process names
- **Show Hidden**: Include invisible windows in the window list
- **Enable Grayed**: Experimental feature for enabling disabled controls
- **Find Window**: Start a one-shot global capture, then click a window or child control to select its raw clicked HWND. Press Escape to cancel
- **Responsive Layout**: Switch from two columns to one column below 760 DIPs, with vertical scrolling for overflow
- **DPI-Aware Sizing**: Scale the initial main window and Window List sizes for each window's DPI
- **Always on Top**: Keep LookHandles on top of other windows

## Technical Stack

- **WinUI 3**: Modern Windows UI framework
- **Windows App SDK 2.1.3**: Provides WinRT API access
- **CsWin32**: Source-generated P/Invoke bindings for Win32 APIs
- **CommunityToolkit.Mvvm**: MVVM support (source-generated observables and commands)
- **.NET 10**: Targets `net10.0-windows10.0.19041.0`
- **C#**: SDK-default language version with nullable reference types enabled

## Key Win32 APIs Used

| Category | APIs |
|----------|------|
| Window Enumeration | `EnumWindows` |
| Window Info | `GetWindowText`, `GetClassName`, `GetWindowRect` |
| Window Styles | `GetWindowLong` |
| Process Info | `GetWindowThreadProcessId`, `OpenProcess`, `QueryFullProcessImageNameW` |
| Window Control | `ShowWindow`, `SetWindowPos`, `EnableWindow`, `SetWindowText` |
| Messages | `PostMessage` |
| Global Hooks | `SetWindowsHookEx`, `UnhookWindowsHookEx`, `CallNextHookEx` |
| Utilities | `WindowFromPoint`, `GetCursorPos`, `GetForegroundWindow`, `FlashWindowEx`, `GetAncestor`, `GetDpiForWindow` |

## Project Structure

```
LookHandles/
|-- LookHandles.slnx              # Visual Studio solution
|-- LookHandles/
|   |-- LookHandles.csproj       # Project file
|   |-- NativeMethods.txt        # CsWin32 API list for source generation
|   |-- Package.appxmanifest     # MSIX package manifest
|   |-- app.manifest            # Application manifest (DPI awareness)
|   |-- App.xaml / App.xaml.cs  # Application entry point
|   |-- Helpers/
|   |   +-- WindowSizing.cs     # DPI-aware initial window sizing
|   |-- Models/
|   |   |-- WindowInfo.cs       # Window information data model
|   |   +-- WindowListItem.cs   # List display item for the window list
|   |-- Services/
|   |   |-- WindowEnumerationService.cs   # EnumWindows / filtering logic
|   |   |-- WindowManipulationService.cs  # ShowWindow / SetWindowPos / EnableWindow / etc.
|   |   +-- MouseCaptureService.cs        # Cursor polling and global hooks for one-shot HWND picking
|   |-- ViewModels/
|   |   +-- MainViewModel.cs    # Main window state, observables, and relay commands
|   |-- Views/
|   |   |-- MainWindow.xaml           # Responsive two-column and single-column main UI
|   |   |-- MainWindow.xaml.cs        # Window setup, layout switching, dialogs, and Window List lifetime
|   |   |-- WindowListWindow.xaml     # Standalone Window List sharing the main view model
|   |   +-- WindowListWindow.xaml.cs  # Window List code-behind
|   +-- Assets/                 # App icons and logos
```

## Prerequisites

1. **.NET 10 SDK**

2. **Visual Studio 18.0 or later** for `net10.0` targeting, with:
   - ".NET desktop development" workload
   - "Windows App SDK C# Templates" component
   - Windows 10 SDK (10.0.19041.0) or later

3. **Windows 10 version 1809 (build 17763)** or later

## Building and Running

### Option 1: Visual Studio
1. Open `LookHandles.slnx` in Visual Studio 18.0 or later
2. Select target platform (x64 recommended)
3. Build solution (Ctrl+Shift+B) - this triggers CsWin32 source generation
4. Press F5 to run

> **Note**: First build may take longer as CsWin32 generates P/Invoke bindings. IntelliSense errors before first build are normal.

### Option 2: Command Line
```bash
cd LookHandles
dotnet build --configuration Debug --runtime win-x64
dotnet run --configuration Debug --runtime win-x64
```

### Deployment
```bash
dotnet publish -c Release -r win-x64 --self-contained true
```

## Known Limitations

1. **UWP/Store apps**: Some modern UWP apps run in protected containers; full info may not be accessible
2. **Protected Processes**: Anti-cheat and DRM-protected processes may block queries
3. **Find Window**: Clicking a child control selects its raw child HWND, which may not have a matching entry or selection in the top-level Window List
4. **Large window counts**: Very large counts (>10,000 windows) may cause brief UI delays during refresh

## License

MIT License