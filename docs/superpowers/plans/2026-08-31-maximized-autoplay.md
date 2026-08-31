# Maximized Pause, Mode Checkmarks, and Autoplay Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Pause for other maximized or full-screen windows, show the active host mode with a tray-menu checkmark, and start the default song automatically after navigation.

**Architecture:** Extend the existing pure foreground-window policy with an `IsMaximized` fact and add a pure host-mode menu mapping. Add one WebView startup-playback service whose observable result is consumed by the existing capture acceptance flow.

**Tech Stack:** .NET 8, WinForms, Win32 User32, WebView2 CDP, existing console and PowerShell acceptance tests.

**Spec:** `docs/plans/2026-08-31-maximized-autoplay-design.md`

## Global Constraints

- Keep the existing true-full-screen detection and manual-pause-safe resume behavior.
- A standard maximized foreground window must trigger automatic pause even when the taskbar remains visible.
- Only the currently active host mode has a tray checkmark.
- Autoplay starts the current default non-keypress audio after navigation; failure remains non-fatal.
- Do not change the remaining tray items or their order.

---

### Task 1: Pure maximized-window and mode-menu policies

**Files:**
- Modify: `tests/NikkiDesktop.Core.Tests/Program.cs`
- Modify: `src/NikkiDesktop.Core/FullscreenPlaybackPolicy.cs`
- Create: `src/NikkiDesktop.Core/HostModeMenuState.cs`

**Interfaces:**
- Consumes: `HostMode`, `DesktopRectangle`.
- Produces: `FullscreenWindowContext.IsMaximized`, `HostModeMenuState.From(HostMode)`.

- [ ] **Step 1: Write a failing core test** asserting a visible other window with `IsMaximized: true` and work-area bounds `(0,0,2560,1400)` triggers the pause classification.
- [ ] **Step 2: Write failing menu-state tests** asserting window mode maps to `(window checked, desktop unchecked)` and wallpaper mode maps to the inverse.
- [ ] **Step 3: Run the core project** and confirm compilation fails because the context field and menu state do not exist.
- [ ] **Step 4: Add the minimal immutable fields and branches**: accept maximized before geometry comparison, while retaining all exclusions; map the two enum values explicitly.
- [ ] **Step 5: Run the core project** and confirm all old and new tests pass.
- [ ] **Step 6: Commit** with `feat: pause for maximized windows and mark active mode`.

### Task 2: Tray integration and startup autoplay

**Files:**
- Modify: `src/NikkiDesktop.App/FullscreenWindowDetector.cs`
- Modify: `src/NikkiDesktop.App/PlayerForm.cs`
- Create: `src/NikkiDesktop.App/StartupPlaybackService.cs`
- Modify: `tests/FullscreenPauseAcceptance.ps1`
- Create: `tests/StartupPlaybackAcceptance.ps1`

**Interfaces:**
- Consumes: `NativeMethods.IsZoomed`, `HostModeMenuState.From`, `window.__nikkiDesktopTrackedAudio`.
- Produces: `StartupPlaybackResult`, capture host fields `startupPlaybackStarted` and `startupPlaybackError`.

- [ ] **Step 1: Change the real window acceptance to use `FormWindowState.Maximized`** and run it against current production code; expect failure because pause count remains zero.
- [ ] **Step 2: Add a startup acceptance** that runs capture mode and requires `startupPlaybackStarted: true`; run it and expect failure because the field is absent.
- [ ] **Step 3: Read `IsZoomed` in the detector** and pass the result into the pure context.
- [ ] **Step 4: Store the first two tray items as fields** and call one update method after creation, successful desktop attachment, and window-mode switching.
- [ ] **Step 5: Add `StartupPlaybackService.TryStartAsync`** using CDP `Runtime.evaluate` with `awaitPromise`, `userGesture`, and `returnByValue`; wait up to three seconds for tracked audio, resume its audio context, and play the first default non-keypress source.
- [ ] **Step 6: Invoke autoplay after successful navigation**; remove capture-only playback initiation, expose the result in host JSON, and require it for capture success.
- [ ] **Step 7: Run both real acceptances** and confirm maximized pause/resume and startup playback pass.
- [ ] **Step 8: Run Release build, core, import, and publish tests** with zero failures.
- [ ] **Step 9: Commit** with `feat: autoplay and show active tray mode`.

### Task 3: Documentation and final package

**Files:**
- Modify: `README.md`
- Modify: `packaging/使用说明.txt`
- Regenerate ignored: `artifacts/publish/孤独摇滚壁纸移植-完整包`
- Regenerate ignored: `artifacts/release/孤独摇滚壁纸移植-完整包.zip`

**Interfaces:**
- Consumes: final executable and existing complete content.
- Produces: clean five-entry complete package and verified ZIP.

- [ ] **Step 1: Document** maximized-window pause, mutually exclusive mode checkmarks, and startup autoplay.
- [ ] **Step 2: Archive the previous generated package and ZIP** under `artifacts/archive/发布包历史` without deleting the rollback copy.
- [ ] **Step 3: Publish the complete package** and run the packaged self-test from an isolated temporary working directory.
- [ ] **Step 4: Create and extract the ZIP**; compare every relative path, size, and SHA-256 with the publish directory.
- [ ] **Step 5: Run final verification**: Release build, all automated tests, both real acceptances, content manifest, package root check, `git diff --check`, and clean Git status.
- [ ] **Step 6: Commit documentation** with `docs: explain maximized pause and autoplay`.
