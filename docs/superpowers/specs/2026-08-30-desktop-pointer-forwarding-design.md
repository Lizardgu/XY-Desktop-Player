# Desktop Pointer Forwarding Design

## Goal

Make the existing `wallpaper` mode directly interactive without moving the
player above Windows desktop icons. Desktop files and shortcuts remain a
foreground mask: clicks on an icon continue to belong to Explorer, while
clicks on uncovered desktop space reach the WebView2 player underneath.
Right-click always belongs to the Windows desktop and opens its normal context
menu.

## Approved interaction contract

- The player stays attached to the background `WorkerW` used by the existing B
  mode.
- A visible desktop icon, including its label rectangle, masks the player at
  that location.
- Mouse movement, left-button input, double-click input, and wheel input over
  uncovered desktop space are forwarded to the player.
- Left-button and wheel input forwarded to the player are consumed so Explorer
  does not start a selection rectangle or perform a second action.
- Input over an icon is not forwarded or consumed; Explorer handles it
  normally.
- Right-button input is never forwarded or consumed. The Windows desktop menu
  therefore remains available everywhere on the desktop.
- When “Show desktop icons” is disabled, the icon mask is empty, so all
  uncovered desktop space remains interactive.
- Input is forwarded only while the player is attached in `wallpaper` mode and
  the WebView is ready. Ordinary window mode uses normal WebView2 input.
- Input over another application, the taskbar, Start menu, tray, or an open
  context menu is never forwarded.

## Architecture

### 1. Desktop surface gate

A small native boundary checks `WindowFromPoint` and the root window beneath
the cursor. It accepts only Explorer desktop surfaces or the attached Nikki
Desktop host. This prevents a global mouse observer from affecting foreground
applications and system UI.

### 2. Desktop icon mask

A dedicated background component reads visible `SysListView32` desktop icon
elements through Microsoft Active Accessibility (MSAA) and caches their
physical-screen bounding rectangles. Accessibility work does not run inside
the mouse callback. The cache refreshes
when wallpaper interaction starts, after Explorer/taskbar recreation, after a
display or settings change, and periodically at a low frequency so icon moves
and show/hide changes are picked up.

The cache is an immutable snapshot. The mouse path only performs point-in-
rectangle checks. If icon discovery fails or returns an invalid snapshot, the
component fails safely to Explorer: it does not consume desktop input until a
valid snapshot is available. This avoids trapping the user during Explorer
restart or accessibility-provider failure.

### 3. Low-level mouse observer

`WH_MOUSE_LL` observes desktop mouse events in the Nikki Desktop process. The
callback keeps a rooted delegate, does no UI Automation or WebView work, and is
removed during mode changes and disposal.

For every event, a pure routing policy evaluates:

1. Is the application in attached wallpaper mode?
2. Is WebView2 ready?
3. Is the point on an allowed desktop surface?
4. Is the point outside every cached icon rectangle?
5. Is the event one that the player accepts?

Allowed events are queued onto the WinForms UI thread. Right-click events and
events rejected by any gate immediately continue through `CallNextHookEx`.
Forwarded left-button and wheel events return a nonzero hook result so Explorer
does not also process them. Mouse-move events are observed and forwarded but
not consumed.

### 4. WebView2 input sink

The UI-thread sink maps physical screen coordinates into WebView CSS viewport
coordinates, including the active DPI scale. It serializes mouse move,
left-button down/up, double-click, and wheel input into Chromium DevTools
Protocol `Input.dispatchMouseEvent` calls through
`CoreWebView2.CallDevToolsProtocolMethodAsync`.

Dispatch is ordered so a down/up pair cannot be reversed. Repeated mouse moves
are coalesced to prevent an input backlog. A failed dispatch disables
forwarding for that event and records a concise diagnostic rather than
blocking the hook thread.

### 5. Lifecycle and user recovery

The forwarding service starts only after WebView navigation completes and B
mode is attached. It stops before detaching, when switching to A, on form
close, and when Explorer rebuilds the desktop. It restarts only after a valid
desktop attachment and icon-mask refresh.

The tray menu exposes “桌面交互：已开启/已关闭”. Turning it off removes the
hook immediately and returns all mouse input to Explorer. The existing
“切换到普通窗口” and “退出 Nikki Desktop” actions remain recovery paths.
No mouse positions or click history are persisted.

## Components

- `DesktopPointerRoutePolicy` — pure, platform-independent routing decisions.
- `DesktopIconMask` — immutable icon rectangles and point containment.
- `DesktopIconMaskProvider` — MSAA discovery and refresh lifecycle.
- `DesktopSurfaceClassifier` — native foreground/desktop surface check.
- `LowLevelMouseObserver` — installs, owns, and removes `WH_MOUSE_LL`.
- `WebViewPointerSink` — coordinate conversion, event ordering, and CDP input
  dispatch.
- `DesktopInteractionController` — connects the components to `PlayerForm`
  mode and tray state.

Each component has one responsibility so the timing-sensitive hook callback
does not acquire accessibility, WebView, or form lifecycle duties.

## Failure behavior

- Hook installation failure: B continues as a non-interactive wallpaper and
  the tray reports that desktop interaction is unavailable.
- Icon discovery failure: all mouse input remains with Explorer; the player
  never swallows input based on an unknown mask.
- WebView not ready or dispatch failure: the current event is not considered a
  successful player interaction; diagnostics are written without showing a
  modal dialog behind the desktop.
- Explorer restart: forwarding stops, the existing host reattachment runs,
  the icon mask refreshes, and forwarding resumes only after both succeed.
- App shutdown: the hook is always removed and the rooted callback released.

## Verification

Automated tests cover the routing matrix: wallpaper/window mode, ready/not
ready, desktop/non-desktop surface, icon/blank point, left/right/move/wheel
event, and valid/invalid icon-mask state. Tests also cover icon rectangle
boundaries, DPI coordinate conversion, CDP payloads, event ordering, and
lifecycle enable/disable transitions.

Release verification repeats the existing build, self-test, capture, manifest,
and publish checks. A real desktop acceptance pass must then verify:

1. A left click on an uncovered player control operates that control.
2. A desktop icon overlapping the same background area remains clickable and
   does not operate the player.
3. Hiding desktop icons makes the formerly masked area interactive.
4. Right-click opens the Windows desktop context menu and never the webpage
   menu.
5. Foreground apps, taskbar, Start, and tray receive normal input.
6. Switching to A, disabling interaction in the tray, restarting Explorer, and
   exiting all release input ownership cleanly.

## Security and compatibility

The implementation uses documented Windows input and Active Accessibility APIs in
the Nikki Desktop process. It does not inject a DLL into Explorer, modify
Explorer memory, replace shell files, install a driver, or persist input data.
The target remains Windows 10/11 x64 with the existing WebView2 runtime
requirement.

Microsoft documents `WH_MOUSE_LL` as a global low-level mouse observer whose
callback runs in the installing process, and requires prompt callback handling
and explicit unhooking. Microsoft also documents `AccessibleObjectFromWindow`
for retrieving an `IAccessible` interface from a window, and WebView2's
asynchronous DevTools Protocol call used for input dispatch. These constraints
drive the cached-mask and queued-dispatch design.

Primary references:

- https://learn.microsoft.com/windows/win32/winmsg/lowlevelmouseproc
- https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setwindowshookexw
- https://learn.microsoft.com/windows/win32/api/oleacc/nf-oleacc-accessibleobjectfromwindow
- https://learn.microsoft.com/windows/win32/winauto/retrieving-an-iaccessible-object
- https://learn.microsoft.com/dotnet/api/microsoft.web.webview2.core.corewebview2.calldevtoolsprotocolmethodasync
