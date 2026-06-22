# mvvm-refactor - Work Plan

## TL;DR (For humans)

**What you'll get:** A clean MVVM structure: Models, ViewModels, and Services; `MainWindow.xaml` bound to a `MainViewModel`; code-behind reduced to view-only concerns. No behavior changes.

**Why this approach:** `CommunityToolkit.Mvvm` provides source-generated commands and observable properties with minimal boilerplate. A thin service layer isolates Win32 interop so the ViewModel owns state and commands while the View stays passive.

**What it will NOT do:** Change functionality, add new features, redesign the UI, or add unit tests.

**Effort:** Large
**Risk:** Medium - Win32 mouse capture and ContentDialog boundary need careful handling during move.
**Decisions to sanity-check:** (1) Use `CommunityToolkit.Mvvm` rather than hand-rolled `INotifyPropertyChanged`; (2) keep ContentDialog and window sizing in code-behind; (3) ViewModel directly uses HWND-based services.

Your next move: approve the plan, then run `$start-work` or tell me to start. Full execution detail follows below.

---

> TL;DR (machine): Large effort, medium risk: add CommunityToolkit.Mvvm, split MainWindow logic into Models/ViewModels/Services, bind XAML to VM, keep code-behind minimal, verify build.

## Scope
### Must have
- Add `CommunityToolkit.Mvvm` package reference.
- Create `Models/`, `ViewModels/`, `Services/`, `Helpers/` folders.
- Move `WindowListItem` from code-behind to `Models/WindowListItem.cs`.
- Create services:
  - `IWindowEnumerationService` / `WindowEnumerationService` — `EnumWindows`, filtering, sorting.
  - `IWindowManipulationService` / `WindowManipulationService` — `ShowWindow`, `SetWindowPos`, `EnableWindow`, `FlashWindowEx`, `SetWindowText`, `PostMessage(WM_CLOSE)`, `KillProcess`.
  - `IMouseCaptureService` / `MouseCaptureService` — `GetCursorPos`, `WindowFromPoint`, `SetCapture`, `ReleaseCapture`.
- Create `MainViewModel` with:
  - Observable properties: `WindowList`, `SelectedWindowItem`, `CurrentWindow`, `IsAutoMode`, `IsFindingWindow`, `IsSpying`, `ShowHiddenWindows`, `EnableGrayed`, button captions.
  - Relay commands for every button action (Refresh, SetTitle, Minimize, Maximize, Normal, Show, Hide, Flash, Topmost, NoTopmost, Close, Enable, Disable, KillProcess, FindWindow, FollowForeground).
  - Tracking-mode mutual-exclusion logic.
- Update `MainWindow.xaml` bindings: `ItemsSource`, `SelectedItem`, `Command`, `IsChecked`, `Content`.
- Reduce `MainWindow.xaml.cs` to: constructor (set DataContext, window sizing), `Always on Top` check handler (needs HWND), ContentDialog helpers, FindWindow pointer event bridge (calls service/VM).
- Build must succeed with zero errors.

### Must NOT have (guardrails, anti-slop, scope boundaries)
- No functional behavior change.
- No new UI elements or layout changes.
- No new tracking modes.
- No unit tests or test projects.
- No changes to app manifest, packaging, or signing.
- No removal of CsWin32 / unsafe code; only reorganize it.

## Verification strategy
> Zero human intervention - all verification is agent-executed.
- Test decision: tests-after / none (no existing test infrastructure). Verification is `dotnet build` exit 0 plus manual run smoke test if possible.
- Evidence: `.omo/evidence/task-<N>-mvvm-refactor.txt` or `.log` from build output.

## Execution strategy
### Parallel execution waves
Wave 1: Add package and create folder skeleton; move Model classes.
Wave 2: Extract Win32 services (enumeration, manipulation, mouse capture).
Wave 3: Create MainViewModel with properties and commands.
Wave 4: Refactor MainWindow XAML + code-behind to bind to VM.
Wave 5: Build, fix errors, clean up unused usings, final verification.

### Dependency matrix
| Todo | Depends on | Blocks | Can parallelize with |
| --- | --- | --- | --- |
| 1 | - | 2,3 | - |
| 2 | 1 | 3 | - |
| 3 | 1,2 | 4 | - |
| 4 | 3 | 5 | - |
| 5 | 4 | - | - |

## Todos
> Implementation + Test = ONE todo. Never separate.
<!-- APPEND TASK BATCHES BELOW THIS LINE WITH edit/apply_patch - never rewrite the headers above. -->

- [ ] 1. Add CommunityToolkit.Mvvm and create folder skeleton
  What to do / Must NOT do: Add `CommunityToolkit.Mvvm` 8.x PackageReference to `LookHandles.csproj`. Create `Models/`, `ViewModels/`, `Services/`, `Helpers/` folders. Move `WindowListItem` class from bottom of `MainWindow.xaml.cs` to `Models/WindowListItem.cs`. Do NOT change any logic yet.
  Parallelization: Wave 1 | Blocked by: - | Blocks: 2, 3
  References (executor has NO interview context - be exhaustive): `LookHandles/LookHandles.csproj:38-45`, `LookHandles/MainWindow.xaml.cs:733-742`
  Acceptance criteria (agent-executable): `dotnet build` succeeds; `WindowListItem` exists in `Models/` and is referenced correctly.
  QA scenarios (name the exact tool + invocation): happy: `dotnet build LookHandles/LookHandles.csproj --configuration Debug --runtime win-x64` exits 0. Evidence `.omo/evidence/task-1-mvvm-refactor.txt`.
  Commit: Y | refactor: add MVVM toolkit and Models folder

- [ ] 2. Extract Win32 interop into services
  What to do / Must NOT do: Create `IWindowEnumerationService`/`WindowEnumerationService` (EnumWindows, filter/sort), `IWindowManipulationService`/`WindowManipulationService` (ShowWindow, SetWindowPos, EnableWindow, FlashWindowEx, SetWindowText, WM_CLOSE, KillProcess), `IMouseCaptureService`/`MouseCaptureService` (GetCursorPos, WindowFromPoint, SetCapture, ReleaseCapture). Move related P/Invoke helpers (POINT struct, WindowFromPoint, GetCursorPos) into services or `Helpers/NativeMethods.cs`. Do NOT call these services from anywhere yet.
  Parallelization: Wave 2 | Blocked by: 1 | Blocks: 3
  References: `LookHandles/MainWindow.xaml.cs:75-180`, `184-227`, `239-413`, `517-580`, `601-675`, `704-728`
  Acceptance criteria (agent-executable): All service interfaces/classes compile; no duplicate logic remains in code-behind for these operations.
  QA scenarios: happy: `dotnet build` exits 0. Evidence `.omo/evidence/task-2-mvvm-refactor.txt`.
  Commit: Y | refactor: extract Win32 interop services

- [ ] 3. Create MainViewModel with state and commands
  What to do / Must NOT do: Create `ViewModels/MainViewModel.cs` using `CommunityToolkit.Mvvm`. Expose observable properties and RelayCommands. Move tracking-mode state/mutual exclusion and command logic from `MainWindow.xaml.cs`. Inject services via constructor. Keep dialog-related actions as `Func<Task<bool>>` callbacks or events so ViewModel does not reference XamlRoot/ContentDialog directly. Do NOT modify XAML in this todo.
  Parallelization: Wave 3 | Blocked by: 1, 2 | Blocks: 4
  References: `LookHandles/MainWindow.xaml.cs:33-45`, `416-501`, `505-643`, `647-675`, `689-701`
  Acceptance criteria (agent-executable): `MainViewModel` compiles; all commands are wired; no button click handlers remain except in VM.
  QA scenarios: happy: `dotnet build` exits 0. Evidence `.omo/evidence/task-3-mvvm-refactor.txt`.
  Commit: Y | refactor: add MainViewModel

- [ ] 4. Bind MainWindow XAML and minimize code-behind
  What to do / Must NOT do: Update `MainWindow.xaml` to bind ListView `ItemsSource`/`SelectedItem`, Button `Command`s, CheckBox `IsChecked`. Reduce `MainWindow.xaml.cs` to: constructor (create VM/services, set DataContext, window setup), `Always on Top` checked/unchecked (HWND), ContentDialog kill confirmation bridge, error dialog bridge, and minimal FindWindow pointer event bridge that calls `MouseCaptureService`/`MainViewModel`. Do NOT leave dead event handlers.
  Parallelization: Wave 4 | Blocked by: 3 | Blocks: 5
  References: `LookHandles/MainWindow.xaml:1-327`, `LookHandles/MainWindow.xaml.cs:1-742`
  Acceptance criteria (agent-executable): All named controls still accessible where needed; bindings compile; code-behind is under ~120 lines.
  QA scenarios: happy: `dotnet build` exits 0. Evidence `.omo/evidence/task-4-mvvm-refactor.txt`.
  Commit: Y | refactor: bind view to MainViewModel

- [ ] 5. Final build and cleanup
  What to do / Must NOT do: Remove unused `using` directives and dead code. Run full build. Verify no regressions. Do NOT change functionality.
  Parallelization: Wave 5 | Blocked by: 4 | Blocks: -
  References: entire `LookHandles/` source
  Acceptance criteria (agent-executable): `dotnet build LookHandles/LookHandles.csproj --configuration Debug --runtime win-x64` exits 0 with only pre-existing warnings.
  QA scenarios: happy: build exits 0. Evidence `.omo/evidence/task-5-mvvm-refactor.txt`.
  Commit: Y | refactor: cleanup after MVVM migration

## Final verification wave
> Runs in parallel after ALL todos. ALL must APPROVE. Surface results and wait for the user's explicit okay before declaring complete.
- [ ] F1. Plan compliance audit
- [ ] F2. Code quality review
- [ ] F3. Real manual QA
- [ ] F4. Scope fidelity

## Commit strategy
One commit per todo. Final verification does not produce a code commit unless issues are found.

## Success criteria
- `dotnet build` succeeds with zero errors.
- `MainWindow.xaml.cs` contains only view-specific code (dialogs, window setup, minimal pointer bridge).
- `MainViewModel` owns all state and commands.
- Services isolate Win32/PInvoke calls.
- No functional behavior change compared to pre-refactor.
