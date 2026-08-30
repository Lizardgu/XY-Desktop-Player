# Desktop Pointer Forwarding Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the WorkerW-hosted player receive left-button, hover, and wheel input only through desktop space not covered by Explorer icons, while preserving desktop right-click everywhere.

**Architecture:** Pure routing and geometry rules live in `NikkiDesktop.Core`; Windows-only services in `NikkiDesktop.App` cache desktop icon bounds through Microsoft Active Accessibility (MSAA), observe low-level mouse events, classify the visible surface, and queue ordered CDP mouse input to WebView2. `PlayerForm` owns one controller and starts or stops it with wallpaper attachment, navigation, tray state, Explorer rebuild, and disposal.

**Tech Stack:** .NET 8, WinForms, Microsoft Active Accessibility, Win32 `WH_MOUSE_LL`, Microsoft Edge WebView2, Chromium DevTools Protocol.

**Spec:** `docs/superpowers/specs/2026-08-30-desktop-pointer-forwarding-design.md`

## Global Constraints

- Target Windows 10/11 x64 and keep the existing self-contained publish model.
- Keep the player below desktop icons in the existing WorkerW host.
- Preserve Explorer handling on desktop icon rectangles.
- Preserve Windows desktop right-click everywhere.
- Do not forward input over foreground apps, taskbar, Start, tray, or menus.
- Do not inject Explorer, allocate Explorer memory, install drivers, log pointer history, or modify system files.
- Fail safely to Explorer when icon-mask discovery, hook installation, or WebView dispatch is unavailable.

---

### Task 1: Pure pointer-routing model

**Files:**
- Create: `src/NikkiDesktop.Core/DesktopPointerRouting.cs`
- Modify: `tests/NikkiDesktop.Core.Tests/Program.cs`

**Interfaces:**
- Produces: `DesktopPoint`, `DesktopRectangle`, `DesktopIconMask`, `DesktopPointerEventKind`, `DesktopPointerAction`, `DesktopPointerRouteContext`, and `DesktopPointerRoutePolicy.Decide(DesktopPointerRouteContext)`.
- Consumes: existing `HostMode`.

- [ ] **Step 1: Write failing routing and geometry tests**

Add test cases showing that icon rectangles include their edges, invalid masks pass through, right-click always passes through, icon hits pass through, blank wallpaper left/wheel events forward and consume, blank mouse move forwards without consuming, and non-desktop/window/not-ready contexts pass through.

```csharp
var context = new DesktopPointerRouteContext(
    HostMode.Wallpaper,
    WebViewReady: true,
    DesktopAttached: true,
    IsDesktopSurface: true,
    new DesktopPoint(500, 500),
    DesktopPointerEventKind.LeftDown,
    new DesktopIconMask(true, [new DesktopRectangle(0, 0, 100, 100)]));

Expect.Equal(DesktopPointerAction.ForwardAndConsume,
    DesktopPointerRoutePolicy.Decide(context));
```

- [ ] **Step 2: Run the core tests and verify RED**

Run:

```powershell
& '.\tools\Invoke-DotNet.ps1' run --project '.\tests\NikkiDesktop.Core.Tests\NikkiDesktop.Core.Tests.csproj'
```

Expected: compilation fails because the routing types do not exist.

- [ ] **Step 3: Implement the minimal routing model**

```csharp
public static class DesktopPointerRoutePolicy
{
    public static DesktopPointerAction Decide(DesktopPointerRouteContext context)
    {
        if (context.EventKind is DesktopPointerEventKind.RightDown or DesktopPointerEventKind.RightUp)
            return DesktopPointerAction.PassThrough;
        if (context.Mode != HostMode.Wallpaper || !context.WebViewReady ||
            !context.DesktopAttached || !context.IsDesktopSurface ||
            !context.IconMask.IsValid || context.IconMask.Contains(context.Point))
            return DesktopPointerAction.PassThrough;
        return context.EventKind == DesktopPointerEventKind.Move
            ? DesktopPointerAction.Forward
            : DesktopPointerAction.ForwardAndConsume;
    }
}
```

- [ ] **Step 4: Run all core tests and verify GREEN**

Expected: every old and new core test passes with no warnings.

- [ ] **Step 5: Commit the pure model**

```powershell
git add src/NikkiDesktop.Core/DesktopPointerRouting.cs tests/NikkiDesktop.Core.Tests/Program.cs
git commit -m "feat: define masked desktop pointer routing"
```

---

### Task 2: Windows desktop input services

**Files:**
- Modify: `src/NikkiDesktop.App/NikkiDesktop.App.csproj`
- Create: `src/NikkiDesktop.App/DesktopIconMaskProvider.cs`
- Create: `src/NikkiDesktop.App/DesktopSurfaceClassifier.cs`
- Create: `src/NikkiDesktop.App/LowLevelMouseObserver.cs`
- Create: `src/NikkiDesktop.App/WebViewPointerSink.cs`
- Create: `src/NikkiDesktop.App/DesktopInteractionController.cs`

**Interfaces:**
- Consumes: Task 1 routing types, `DesktopHostService.IsAttached`, form/WebView handles, WinForms synchronization context, and `CoreWebView2.CallDevToolsProtocolMethodAsync`.
- Produces: `DesktopInteractionController.Start()`, `Stop()`, `RefreshIconMask()`, `IsRunning`, `LastError`, and `Dispose()`.

- [ ] **Step 1: Add an app-side compile contract before implementation**

Use the `Accessibility` assembly already provided by WinForms; do not add WPF or UI Automation framework references. Reference the wished-for controller API from `PlayerForm` only after Task 3 tests require it. Do not add production behavior in this step.

- [ ] **Step 2: Implement cached Explorer icon-mask discovery**

Locate `SHELLDLL_DefView` and its `SysListView32` desktop view. If the view is hidden, publish a valid empty mask. Otherwise call `AccessibleObjectFromWindow` on a dedicated background worker, enumerate `IAccessible` children, and publish their physical-screen `accLocation` values as one immutable `DesktopIconMask` snapshot. Cross-check the native ListView item count so visible icons with no accessibility rectangles produce a fail-safe invalid mask. Refresh at startup and every 750 ms; catch transient COM failures and retain fail-safe invalid state.

```csharp
public DesktopIconMask Snapshot => Volatile.Read(ref _snapshot);
```

- [ ] **Step 3: Implement desktop-surface classification**

Use `WindowFromPoint`, `GetAncestor`, `GetClassName`, `IsChild`, and the attached player handle. Accept only `Progman`, `WorkerW`, `SHELLDLL_DefView`, `SysListView32`, or the player/WebView subtree. Reject visible `#32768` menu surfaces and all unrelated roots.

- [ ] **Step 4: Implement the low-level mouse observer**

Install `WH_MOUSE_LL` from the WinForms message-loop thread, keep the callback delegate rooted, map `WM_MOUSEMOVE`, left down/up, right down/up, and `WM_MOUSEWHEEL` into core event kinds, and always unhook on `Stop`/`Dispose`. The callback performs only native point classification, immutable mask lookup, pure routing, and queue submission.

```csharp
return action == DesktopPointerAction.ForwardAndConsume
    ? (nint)1
    : NativeMethods.CallNextHookEx(_hook, code, message, data);
```

- [ ] **Step 5: Implement ordered WebView2 CDP dispatch**

Convert physical screen points through `WebView2.PointToClient` and `DeviceDpi / 96d`. Queue `Input.dispatchMouseEvent` calls in order; coalesce redundant moves and include left-button state, click count, and wheel delta. Never block the hook callback on an async CDP call.

```csharp
await core.CallDevToolsProtocolMethodAsync(
    "Input.dispatchMouseEvent",
    JsonSerializer.Serialize(payload));
```

- [ ] **Step 6: Compose the interaction controller**

Start icon refresh, then the hook. Route accepted events to the sink and fail safely on service errors. Stop the hook before the icon worker and sink. Expose concise status for the tray without modal dialogs.

- [ ] **Step 7: Build and verify the Windows services compile**

Run:

```powershell
& '.\tools\Invoke-DotNet.ps1' build '.\NikkiDesktop.sln' --no-restore
```

Expected: success with zero warnings and zero errors.

- [ ] **Step 8: Commit the Windows services**

```powershell
git add src/NikkiDesktop.App src/NikkiDesktop.App/NikkiDesktop.App.csproj
git commit -m "feat: forward blank-desktop input to WebView2"
```

---

### Task 3: Player lifecycle, tray control, and user guidance

**Files:**
- Modify: `src/NikkiDesktop.App/PlayerForm.cs`
- Modify: `README.md`
- Modify: `packaging/运行说明.txt`

**Interfaces:**
- Consumes: `DesktopInteractionController` from Task 2 and existing attach/detach/navigation/tray methods.
- Produces: B mode with interaction enabled by default and a tray toggle labeled `桌面交互：已开启` or `桌面交互：已关闭`.

- [ ] **Step 1: Write the lifecycle expectation as an executable state test**

Add pure controller-state tests or extract a small lifecycle policy into Core if form state cannot be tested without a window. The test must fail before production wiring and show: start only for attached+ready wallpaper, stop before window mode/detach, and user-disabled state prevents restart.

- [ ] **Step 2: Run the focused tests and verify RED**

Expected: the new lifecycle type or transition is missing.

- [ ] **Step 3: Wire navigation and wallpaper lifecycle**

Create the controller once after WebView2 is initialized. Start it only after successful navigation and desktop attachment. Stop before `SwitchToWindowMode`, form close, and Explorer reattachment; refresh/restart after successful reattachment.

- [ ] **Step 4: Add the tray toggle**

Insert the desktop-interaction item above reload. Keep it checked and labeled from actual controller state. Disabling stops immediately; enabling attempts a mask refresh and hook start only in attached B mode. Show one concise tray balloon on failure.

- [ ] **Step 5: Update usage and packaging documentation**

Document that left-click and wheel on blank desktop space control the player, icon rectangles stay with Explorer, right-click stays with Windows, hiding icons leaves the full desktop interactive, and the tray switch is the recovery control.

- [ ] **Step 6: Run tests and Release build**

```powershell
& '.\tools\Invoke-DotNet.ps1' run --project '.\tests\NikkiDesktop.Core.Tests\NikkiDesktop.Core.Tests.csproj'
& '.\tools\Invoke-DotNet.ps1' build '.\NikkiDesktop.sln' --configuration Release --no-restore
```

Expected: all tests pass; build has zero warnings and zero errors.

- [ ] **Step 7: Commit lifecycle and documentation**

```powershell
git add src/NikkiDesktop.App/PlayerForm.cs README.md packaging/运行说明.txt src/NikkiDesktop.Core tests/NikkiDesktop.Core.Tests
git commit -m "feat: enable masked interaction in wallpaper mode"
```

---

### Task 4: Release verification and portable package

**Files:**
- Modify if required: `tests/Publish.Tests.ps1`
- Regenerate (Git-ignored): `artifacts/smoke/*`, `artifacts/publish/NikkiDesktop-win-x64`

**Interfaces:**
- Consumes: completed feature and existing verification scripts.
- Produces: a verified self-contained win-x64 portable folder and explicit manual acceptance status.

- [ ] **Step 1: Run the full non-visual verification suite**

```powershell
& '.\tools\Invoke-DotNet.ps1' run --project '.\tests\NikkiDesktop.Core.Tests\NikkiDesktop.Core.Tests.csproj'
& '.\tests\ImportReferencePlayer.Tests.ps1'
& '.\tests\Publish.Tests.ps1'
& '.\tools\Run-SelfTest.ps1'
& '.\tools\Verify-ContentManifest.ps1'
```

Expected: every command exits zero.

- [ ] **Step 2: Run A and B capture smoke tests**

```powershell
& '.\tools\Run-CaptureTest.ps1' -Mode window -Output '.\artifacts\smoke\interaction-window.png'
& '.\tools\Run-CaptureTest.ps1' -Mode wallpaper -Output '.\artifacts\smoke\interaction-wallpaper.png'
```

Expected: both capture reports show a complete page, loaded images, advancing audio, installed shim, and B attached to `WorkerW`.

- [ ] **Step 3: Perform real desktop input acceptance**

Launch B and verify an uncovered play control, an overlapping desktop icon, hidden-icon behavior, desktop right-click, foreground app/taskbar isolation, tray disable, A-mode switch, and clean exit. Record any item that cannot be automated as pending user confirmation rather than claiming it passed.

- [ ] **Step 4: Rebuild the portable package without silent overwrite**

Move the previous verified publish directory to a timestamped backup under `artifacts/publish`, run `tools/Publish.ps1`, and run the published EXE `--self-test` against the imported content. Confirm no copyrighted media entered the source-only package.

- [ ] **Step 5: Run final Git and package audits**

Confirm the working tree contains only intended source/doc changes, no tracked file exceeds 10 MiB, the published launchers and EXE exist, and the release package contains no `.mp3`, `.flac`, `.jpg`, or `.lrc` files.

- [ ] **Step 6: Commit any verification-script changes**

```powershell
git add tests/Publish.Tests.ps1
git commit -m "test: verify interactive wallpaper release"
```

Skip the commit when no tracked verification file changed.
