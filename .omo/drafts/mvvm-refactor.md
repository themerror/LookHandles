---
slug: mvvm-refactor
status: awaiting-approval
intent: clear
pending-action: write finalized .omo/plans/mvvm-refactor.md and await explicit start-work
approach: Add CommunityToolkit.Mvvm, split into Models/ViewModels/Services/Helpers, move all window-spy logic into MainViewModel with services, leave only view-specific dialog/Window setup in code-behind.
---

# Draft: mvvm-refactor

## Components (topology ledger)
| id | outcome | status | evidence path |
|---|---|---|---|
| models | WindowInfo and WindowListItem stay as models; possibly extract pure data DTOs | active | LookHandles/Models/ |
| services | Win32 enumeration, window manipulation, and mouse/cursor helpers become injectable services | active | LookHandles/Services/ |
| viewmodel | MainViewModel owns commands, state, and tracking-mode logic | active | LookHandles/ViewModels/MainViewModel.cs |
| view | MainWindow.xaml binds to VM; code-behind only for ContentDialog and window sizing | active | LookHandles/MainWindow.xaml, MainWindow.xaml.cs |

## Open assumptions (announced defaults)
| assumption | adopted default | rationale | reversible? |
|---|---|---|---|
| MVVM toolkit | Use `CommunityToolkit.Mvvm` 8.x | Standard, source-generators reduce boilerplate, .NET 10 compatible | Reversible (could hand-roll) |
| ViewModel-Win32 coupling | ViewModel may call Win32 services directly | This is a system-interop tool; abstracting HWND away completely is impractical | Reversible with more abstraction |
| Dialogs | Kill confirmation and error dialogs stay in code-behind | ContentDialog needs XamlRoot and is pure view concern | Reversible (could use dialog service) |
| Tests | No unit tests added (no existing test project) | Scope is structural refactor; manual build/run is verification | Reversible |

## Findings (cited - path:lines)
- `LookHandles/MainWindow.xaml.cs:1-742` contains all business logic mixed with UI event handlers.
- `LookHandles/WindowInfo.cs:1-356` is the existing data model with `INotifyPropertyChanged`.
- `LookHandles/MainWindow.xaml.cs:733-742` defines `WindowListItem` inline at the bottom of the code-behind file.
- `LookHandles/LookHandles.csproj:1-58` has no MVVM toolkit reference.
- No test projects exist (`**/*test*` glob returned empty).

## Decisions (with rationale)
1. **CommunityToolkit.Mvvm**: Provides `[ObservableProperty]`, `[RelayCommand]`, and `ObservableObject`, which will replace manual property change notifications and command boilerplate.
2. **Service layer**: `WindowEnumerationService` (EnumWindows/filter/sort), `WindowManipulationService` (ShowWindow, SetWindowPos, EnableWindow, Flash, Kill, SetTitle), `MouseCaptureService` (GetCursorPos/WindowFromPoint/SetCapture/ReleaseCapture). Keeps ViewModel testable-ish and isolates unsafe/PInvoke code.
3. **Code-behind remains minimal**: Only sets DataContext, handles ContentDialog, and performs window-level setup (size, ExtendsContentIntoTitleBar). The FindWindow global mouse capture still needs HWND, so code-behind will call `MouseCaptureService` on behalf of the VM but keep logic in the service/VM.
4. **Tracking modes stay mutually exclusive**: The existing `StopOtherTrackingModes` logic moves into the VM as a private helper.

## Scope IN
- Add `CommunityToolkit.Mvvm` package.
- Create folder structure: `Models/`, `ViewModels/`, `Services/`, `Helpers/`.
- Move `WindowListItem` to `Models/`.
- Extract Win32 enumeration/manipulation/mouse logic into services.
- Create `MainViewModel` with observable properties and relay commands.
- Bind `MainWindow.xaml` to VM commands/properties.
- Reduce `MainWindow.xaml.cs` to view-only concerns.
- Verify build succeeds after refactor.

## Scope OUT (Must NOT have)
- No change to app functionality or behavior.
- No new features (e.g., no new buttons, no new tracking modes).
- No unit test project creation.
- No change to packaging/manifest.
- No redesign of the UI layout.

## Open questions
None. Defaults adopted above.

## Approval gate
status: awaiting-approval
