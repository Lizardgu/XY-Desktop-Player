# Theme Host Isolation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make hot theme switching load each theme's own audio and image assets without restarting the player.

**Architecture:** Derive one valid WebView2 virtual host name from each unique theme id. Pass that exact host from the folder mapping into the runtime payload so mapped files and generated URLs cannot diverge.

**Tech Stack:** .NET 8, WinForms, Microsoft WebView2, existing executable core test harness

**Spec:** `docs/plans/2026-09-02-theme-host-isolation-design.md`

## Global Constraints

- Preserve one shared player with separate theme folders.
- Do not change theme-pack files, media formats, playback controls, or foreground pause behavior.
- Built-in `孤独摇滚` remains non-removable.

---

### Task 1: Isolate theme resource hosts

**Files:**
- Modify: `tests/XYDesktopPlayer.Core.Tests/Program.cs`
- Modify: `src/XYDesktopPlayer.Core/ThemeWebContentMapping.cs`
- Modify: `src/XYDesktopPlayer.App/PlayerForm.cs`

**Interfaces:**
- Consumes: `ThemeWebContentMapping.Create(ThemePack)` and `ThemeRuntimePayloadFactory.Create(ThemePack, string)`
- Produces: a stable per-theme `VirtualHostName` used by both WebView2 mapping and runtime media URLs

- [ ] **Step 1: Write the failing test**

Add a test that creates two real theme fixtures, asserts their mapping hosts differ, and asserts a payload generated with each mapping points to that mapping's host.

- [ ] **Step 2: Run test to verify it fails**

Run the core test executable and confirm the new assertion fails because both mappings initially returned `theme.xydesktop.local`.

Also add a frontend regression test proving that generated per-theme hosts are accepted while unrelated hosts remain rejected. The root cause found during real WebView2 verification was a second, hard-coded host check in `web/player-src/src/theme-model.mjs`.

- [ ] **Step 3: Write minimal implementation**

Derive the mapping host from `theme.Id`, and have `PlayerForm.SendCurrentThemeAsync` pass `themeMapping.VirtualHostName` to `ThemeRuntimePayloadFactory.Create` after installing that mapping.

- [ ] **Step 4: Run verification**

Run the complete core test executable, build the solution, publish a fresh candidate, and perform a real switch from external theme to built-in theme and back.

- [ ] **Step 5: Review Git state**

Inspect the diff and leave changes on `feature/theme-pack-skeleton-recovery` for user acceptance; do not push or merge.
