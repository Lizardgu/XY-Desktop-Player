# Standalone Desktop Player Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a Git-managed Windows 10 player that first runs the reference web wallpaper in a normal standalone window and then can host the same player behind desktop icons without Wallpaper Engine.

**Architecture:** A small .NET 8 solution separates command-line/content validation from a WinForms WebView2 host. Web content is copied into a Git-ignored content pack and served through a WebView2 virtual HTTPS host. A native desktop-host service attaches the form to Explorer's WorkerW only in wallpaper mode and falls back to window mode on failure.

**Tech Stack:** C# 12, .NET 8, WinForms, Microsoft.Web.WebView2, PowerShell import/build scripts, Git.

**Spec:** `docs/plans/2026-08-30-nikki-desktop-design.md`

## Global Constraints

- Never modify the Steam Workshop source directory.
- Write a failing behavior test before production behavior.
- Keep `content/`, `bin/`, `obj/`, packages, and WebView2 profile data out of normal Git history.
- Do not add startup tasks, registry persistence, Explorer injection, or system file replacement.
- Verify every milestone with a fresh build/test command and commit it separately.

---

## Task 1: Repository and build skeleton

- [x] Add `.gitignore`, `README.md`, `LICENSE`, `THIRD_PARTY_NOTICES.md`, and solution/project files.
- [x] Add repo-local scripts that use the project-local .NET SDK when available.
- [x] Initialize Git and commit the approved design and plan.
- [x] Run `git status --short` and confirm the initial repository contains no media.

## Task 2: Options and content validation (TDD)

- [x] Add a console test that fails because window/wallpaper command-line options are not implemented.
- [x] Implement the smallest parser for `--mode`, `--content`, and `--self-test`; rerun the test green.
- [x] Add tests that fail for a missing content directory and missing `index.html`/asset folders.
- [x] Implement content validation with actionable diagnostics; rerun tests green.
- [x] Commit the parser and validation milestone.

## Task 3: Reproducible content import

- [x] Add a PowerShell import script with source/target safeguards and a generated SHA-256 manifest.
- [x] Run the importer against the Steam Workshop item and verify source and destination file counts and byte totals.
- [x] Verify that the copied player contains `index.html`, `static`, `assets/covers`, `assets/audios`, and `assets/lyrics`.
- [x] Confirm `git status --short` does not list copied media.
- [x] Commit the import tooling and documentation.

## Task 4: Standalone window host (A)

- [x] Add a failing test for virtual-host URI derivation and content-root normalization.
- [x] Implement URI/content-root mapping logic and rerun tests green.
- [x] Add the WinForms WebView2 form, local virtual-host mapping, pre-document compatibility shim, and self-test mode.
- [x] Restore WebView2, build the solution, and run the non-GUI self-test.
- [x] Launch A and verify the visible player, song playback, cover changes, and lyric loading.
- [x] Commit the standalone-window milestone.

## Task 5: Desktop host (B)

- [x] Add a failing test for selecting the WorkerW associated with `SHELLDLL_DefView` from an abstract window tree.
- [x] Implement the selection logic, then the Win32 Explorer adapter; rerun tests green.
- [x] Add wallpaper mode, safe fallback to window mode, tray Exit/Window/Desktop/Reload actions, and Explorer-restart reattachment.
- [x] Build and run self-test, then launch B and verify it sits behind desktop icons and exits cleanly.
- [x] Commit the desktop-host milestone.

## Task 6: Packaging and handoff

- [ ] Add a publish script for a self-contained win-x64 program folder without copyrighted content.
- [ ] Run full tests, Release build, self-test, content-manifest verification, and Git large-file audit.
- [ ] Document A/B launch instructions and the later GitHub/Git LFS/Release choices.
- [ ] Commit the verified handoff milestone; do not push until the user chooses a GitHub destination and visibility.
