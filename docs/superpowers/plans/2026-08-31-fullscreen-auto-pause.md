# Fullscreen Auto-Pause Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rename the two mode-switch tray items and pause the player while another foreground window is truly full screen, resuming only audio paused automatically.

**Architecture:** Pure geometry and transition policies live in `NikkiDesktop.Core`. A Windows-only detector gathers foreground-window and monitor facts, while one WinForms timer controller applies the policy and invokes two WebView scripts that preserve exact audio-object identity across pause and resume.

**Tech Stack:** .NET 8, WinForms, Win32 User32, DWM, WebView2, existing console test harness.

**Spec:** `docs/plans/2026-08-31-fullscreen-auto-pause-design.md`

## Global Constraints

- Poll every 500 milliseconds on the WinForms UI thread.
- Treat another window as full screen only when it covers its monitor within 2 physical pixels.
- Exclude this process, Explorer desktop surfaces, taskbars, hidden, minimized, and cloaked windows.
- Resume only the audio objects this feature paused; never start manually paused audio.
- Apply in both window and wallpaper modes.
- Detection and WebView failures must leave playback unchanged and must not terminate the app.
- Change only the first two tray labels; preserve every other tray item and its order.

---

### Task 1: Define full-screen and playback transition policies

**Files:**
- Modify: `tests/NikkiDesktop.Core.Tests/Program.cs`
- Create: `src/NikkiDesktop.Core/FullscreenPlaybackPolicy.cs`

**Interfaces:**
- Produces: `FullscreenWindowContext`, `FullscreenWindowPolicy.IsOtherFullscreen`, `FullscreenPlaybackState`, `FullscreenPlaybackAction`, and `FullscreenPlaybackPolicy.Decide`.

- [ ] **Step 1: Write failing geometry tests**

Add literal tests showing that a visible other window matching `(0,0)-(2560,1440)` is full screen; a window ending at y=1400 is not; negative-coordinate monitor bounds work; own/desktop/hidden/minimized/cloaked windows are excluded; and a 2-pixel edge difference is tolerated.

- [ ] **Step 2: Write failing transition tests**

Assert `Pause` when entering full screen, `None` while remaining full screen, `Resume` when leaving after an automatic pause, and `None` when leaving with no automatically paused audio.

- [ ] **Step 3: Run tests and verify RED**

Run `& '.\tools\Invoke-DotNet.ps1' run --project '.\tests\NikkiDesktop.Core.Tests\NikkiDesktop.Core.Tests.csproj' --no-restore` and confirm compilation fails because the policy types do not exist.

- [ ] **Step 4: Implement the minimal pure policies**

Use immutable record structs and a 2-pixel inclusive edge comparison. The transition policy receives previous/current full-screen state and a boolean indicating whether audio was automatically paused.

- [ ] **Step 5: Run tests and verify GREEN**

Run the same core command and confirm all old and new cases pass.

- [ ] **Step 6: Commit**

```powershell
git add src/NikkiDesktop.Core/FullscreenPlaybackPolicy.cs tests/NikkiDesktop.Core.Tests/Program.cs
git commit -m "feat: define fullscreen playback policies"
```

### Task 2: Detect foreground full-screen windows and control WebView audio

**Files:**
- Create: `src/NikkiDesktop.App/FullscreenWindowDetector.cs`
- Create: `src/NikkiDesktop.App/FullscreenPlaybackController.cs`
- Modify: `src/NikkiDesktop.App/PlayerForm.cs`
- Modify: `src/NikkiDesktop.App/WallpaperEngineShim.cs`

**Interfaces:**
- Consumes: pure policies from Task 1 and `window.__nikkiDesktopTrackedAudio`.
- Produces: `FullscreenWindowDetector.CaptureContext()`, `FullscreenPlaybackController.Start/Stop/Dispose`, `window.__bocchiPauseForFullscreen`, and `window.__bocchiResumeAfterFullscreen`.

- [ ] **Step 1: Add pause/resume functions to the document-created shim**

The pause function stores only currently playing audio objects in a `Set` and pauses them. The resume function copies and clears that set, then plays only connected, paused, non-ended members.

- [ ] **Step 2: Implement Windows foreground detection**

Read the foreground root window, process id, visibility, minimized state, class, DWM cloaking, extended frame bounds, and monitor bounds. Return a fail-safe context that cannot classify as full screen if any required call fails.

- [ ] **Step 3: Implement the UI-thread controller**

Use a 500 ms WinForms timer with a non-reentrant async tick. On `Pause`, invoke the shim pause function and arm automatic resume only after script success. On `Resume`, invoke the shim resume function and clear the armed state. Catch all per-tick exceptions into `LastError`.

- [ ] **Step 4: Integrate lifecycle and tray labels**

Start monitoring after successful navigation, stop it on navigation start, and dispose it with the form. Change only the first two menu strings to `切换-窗口模式` and `切换-桌面模式`.

- [ ] **Step 5: Build and run all automated tests**

Run core tests, import tests, publish tests, and a Release build. Expected: zero failures, warnings, or errors.

- [ ] **Step 6: Commit**

```powershell
git add src/NikkiDesktop.App
git commit -m "feat: pause playback behind fullscreen apps"
```

### Task 3: Verify real behavior and refresh the deliverable

**Files:**
- Modify: `README.md`
- Modify: `packaging/使用说明.txt`
- Regenerate (ignored): `artifacts/publish/孤独摇滚壁纸移植-完整包`
- Regenerate (ignored): `artifacts/release/孤独摇滚壁纸移植-完整包.zip`

**Interfaces:**
- Consumes: the final app and current full content package.
- Produces: refreshed package, ZIP, and real playback timing evidence.

- [ ] **Step 1: Document menu labels, reload behavior, and full-screen pause**

Explain that Reload rebuilds webpage state and may reset track/progress; describe automatic pause and selective resume.

- [ ] **Step 2: Run real desktop acceptance**

Start the final player, record tracked-audio time, foreground a borderless full-screen test window long enough for two polling intervals, verify audio time stops, close it, and verify audio time advances again. Also verify the player does not resume when audio was manually paused first.

- [ ] **Step 3: Rebuild package and ZIP**

Archive the previous generated package/ZIP without deleting them, publish the new complete package, create a new ZIP, and extract-check all relative paths, sizes, and SHA-256 hashes.

- [ ] **Step 4: Run final verification and commit documentation**

Run all tests, Release build, content manifest, packaged self-test, `git diff --check`, and `git status --short`, then commit README and package instructions.

