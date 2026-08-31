using Microsoft.Web.WebView2.WinForms;
using NikkiDesktop.Core;

namespace NikkiDesktop.App;

internal sealed class DesktopInteractionController : IDisposable
{
    private readonly DesktopHostService _desktopHost;
    private readonly DesktopIconMaskProvider _iconMaskProvider;
    private readonly DesktopSurfaceClassifier _surfaceClassifier;
    private readonly DesktopContextMenuController _contextMenuController;
    private readonly WebViewPointerSink _pointerSink;
    private readonly LowLevelMouseObserver _mouseObserver;
    private bool _disposed;

    public DesktopInteractionController(
        nint playerWindow,
        WebView2 webView,
        DesktopHostService desktopHost)
    {
        _desktopHost = desktopHost;
        _iconMaskProvider = new DesktopIconMaskProvider();
        _surfaceClassifier = new DesktopSurfaceClassifier(playerWindow);
        _contextMenuController = new DesktopContextMenuController();
        _pointerSink = new WebViewPointerSink(webView);
        _mouseObserver = new LowLevelMouseObserver(Route);
    }

    public bool IsRunning => _mouseObserver.IsRunning;

    public bool IconMaskValid => _iconMaskProvider.Snapshot.IsValid;

    public int IconRectangleCount => _iconMaskProvider.Snapshot.Rectangles.Count;

    public string? LastError =>
        _mouseObserver.LastError ?? _iconMaskProvider.LastError ?? _pointerSink.LastError;

    public bool Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _iconMaskProvider.Start();
        _iconMaskProvider.RefreshNow();
        if (_mouseObserver.Start())
        {
            return true;
        }

        _iconMaskProvider.Stop();
        return false;
    }

    public void RefreshIconMask() => _iconMaskProvider.RefreshNow();

    public void Stop()
    {
        _mouseObserver.Stop();
        _pointerSink.Clear();
        _iconMaskProvider.Stop();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _mouseObserver.Dispose();
        _pointerSink.Dispose();
        _iconMaskProvider.Dispose();
    }

    private DesktopPointerAction Route(ObservedPointerEvent pointerEvent)
    {
        var action = DesktopPointerRoutePolicy.Decide(new DesktopPointerRouteContext(
            HostMode.Wallpaper,
            WebViewReady: true,
            DesktopAttached: _desktopHost.IsAttached,
            IsDesktopSurface: _surfaceClassifier.IsDesktopSurface(pointerEvent.Point),
            pointerEvent.Point,
            pointerEvent.Kind,
            _iconMaskProvider.Snapshot,
            IsDesktopMenuOpen: pointerEvent.Kind == DesktopPointerEventKind.LeftDown &&
                _contextMenuController.IsDesktopMenuOpen()));

        if (action == DesktopPointerAction.DismissMenuForwardAndConsume)
        {
            _contextMenuController.TryDismissDesktopMenu();
        }

        if (action is DesktopPointerAction.Forward or
            DesktopPointerAction.ForwardAndConsume or
            DesktopPointerAction.DismissMenuForwardAndConsume)
        {
            _pointerSink.Enqueue(pointerEvent);
        }

        return action;
    }
}
