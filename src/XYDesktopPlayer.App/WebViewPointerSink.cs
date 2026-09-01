using System.Text.Json;
using Microsoft.Web.WebView2.WinForms;
using XYDesktopPlayer.Core;

namespace XYDesktopPlayer.App;

internal sealed class WebViewPointerSink : IDisposable
{
    private readonly WebView2 _webView;
    private readonly object _queueLock = new();
    private readonly List<ObservedPointerEvent> _queue = [];
    private bool _draining;
    private bool _leftButtonDown;
    private int _currentClickCount;
    private uint _lastClickTime;
    private DesktopPoint _lastClickPoint;
    private bool _disposed;

    public WebViewPointerSink(WebView2 webView)
    {
        _webView = webView;
    }

    public string? LastError { get; private set; }

    public void Enqueue(ObservedPointerEvent pointerEvent)
    {
        if (_disposed || _webView.IsDisposed)
        {
            return;
        }

        lock (_queueLock)
        {
            if (pointerEvent.Kind == DesktopPointerEventKind.Move &&
                _queue.Count > 0 &&
                _queue[^1].Kind == DesktopPointerEventKind.Move)
            {
                _queue[^1] = pointerEvent;
            }
            else
            {
                _queue.Add(pointerEvent);
            }

            if (_draining)
            {
                return;
            }

            _draining = true;
        }

        try
        {
            _webView.BeginInvoke(DrainAsync);
        }
        catch (InvalidOperationException)
        {
            lock (_queueLock)
            {
                _queue.Clear();
                _draining = false;
            }
        }
    }

    public void Clear()
    {
        lock (_queueLock)
        {
            _queue.Clear();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        Clear();
    }

    private async void DrainAsync()
    {
        while (!_disposed)
        {
            ObservedPointerEvent pointerEvent;
            lock (_queueLock)
            {
                if (_queue.Count == 0)
                {
                    _draining = false;
                    return;
                }

                pointerEvent = _queue[0];
                _queue.RemoveAt(0);
            }

            try
            {
                await DispatchAsync(pointerEvent);
                LastError = null;
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or ObjectDisposedException or ArgumentException)
            {
                LastError = exception.Message;
            }
        }
    }

    private async Task DispatchAsync(ObservedPointerEvent pointerEvent)
    {
        var core = _webView.CoreWebView2 ??
                   throw new InvalidOperationException("WebView2 尚未准备好接收桌面输入。");
        var screenOrigin = _webView.PointToScreen(Point.Empty);
        var cssPoint = WebPointerCoordinateMapper.ToCssPoint(
            pointerEvent.Point,
            new DesktopPoint(screenOrigin.X, screenOrigin.Y),
            _webView.DeviceDpi / 96d);
        var cssWidth = _webView.ClientSize.Width / (_webView.DeviceDpi / 96d);
        var cssHeight = _webView.ClientSize.Height / (_webView.DeviceDpi / 96d);
        if (cssPoint.X < 0 || cssPoint.Y < 0 || cssPoint.X >= cssWidth || cssPoint.Y >= cssHeight)
        {
            return;
        }

        var payload = CreatePayload(pointerEvent, cssPoint);
        await core.CallDevToolsProtocolMethodAsync(
            "Input.dispatchMouseEvent",
            JsonSerializer.Serialize(payload));
    }

    private Dictionary<string, object> CreatePayload(
        ObservedPointerEvent pointerEvent,
        WebPointerPoint cssPoint)
    {
        var payload = new Dictionary<string, object>
        {
            ["type"] = ToCdpType(pointerEvent.Kind),
            ["x"] = cssPoint.X,
            ["y"] = cssPoint.Y,
            ["modifiers"] = 0
        };

        switch (pointerEvent.Kind)
        {
            case DesktopPointerEventKind.LeftDown:
                _currentClickCount = IsDoubleClick(pointerEvent) ? 2 : 1;
                _lastClickTime = pointerEvent.Timestamp;
                _lastClickPoint = pointerEvent.Point;
                _leftButtonDown = true;
                payload["button"] = "left";
                payload["buttons"] = 1;
                payload["clickCount"] = _currentClickCount;
                break;
            case DesktopPointerEventKind.LeftUp:
                _leftButtonDown = false;
                payload["button"] = "left";
                payload["buttons"] = 0;
                payload["clickCount"] = Math.Max(1, _currentClickCount);
                break;
            case DesktopPointerEventKind.Wheel:
                payload["button"] = "none";
                payload["buttons"] = _leftButtonDown ? 1 : 0;
                payload["deltaX"] = 0;
                payload["deltaY"] = -pointerEvent.WheelDelta;
                break;
            default:
                payload["button"] = "none";
                payload["buttons"] = _leftButtonDown ? 1 : 0;
                break;
        }

        return payload;
    }

    private bool IsDoubleClick(ObservedPointerEvent pointerEvent)
    {
        if (_lastClickTime == 0 || pointerEvent.Timestamp - _lastClickTime > SystemInformation.DoubleClickTime)
        {
            return false;
        }

        var halfWidth = Math.Max(1, SystemInformation.DoubleClickSize.Width / 2);
        var halfHeight = Math.Max(1, SystemInformation.DoubleClickSize.Height / 2);
        return Math.Abs(pointerEvent.Point.X - _lastClickPoint.X) <= halfWidth &&
               Math.Abs(pointerEvent.Point.Y - _lastClickPoint.Y) <= halfHeight;
    }

    private static string ToCdpType(DesktopPointerEventKind kind) => kind switch
    {
        DesktopPointerEventKind.Move => "mouseMoved",
        DesktopPointerEventKind.LeftDown => "mousePressed",
        DesktopPointerEventKind.LeftUp => "mouseReleased",
        DesktopPointerEventKind.Wheel => "mouseWheel",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "不支持的播放器鼠标事件。")
    };
}
