namespace NikkiDesktop.Core;

public readonly record struct DesktopPoint(int X, int Y);

public readonly record struct DesktopRectangle(int Left, int Top, int Right, int Bottom)
{
    public bool Contains(DesktopPoint point) =>
        point.X >= Left && point.X < Right &&
        point.Y >= Top && point.Y < Bottom;
}

public sealed class DesktopIconMask
{
    private readonly IReadOnlyList<DesktopRectangle> _rectangles;

    public DesktopIconMask(bool isValid, IReadOnlyList<DesktopRectangle> rectangles)
    {
        IsValid = isValid;
        _rectangles = rectangles ?? throw new ArgumentNullException(nameof(rectangles));
    }

    public static DesktopIconMask Invalid { get; } = new(false, []);

    public static DesktopIconMask Empty { get; } = new(true, []);

    public bool IsValid { get; }

    public IReadOnlyList<DesktopRectangle> Rectangles => _rectangles;

    public bool Contains(DesktopPoint point) =>
        IsValid && _rectangles.Any(rectangle => rectangle.Contains(point));
}

public enum DesktopPointerEventKind
{
    Move,
    LeftDown,
    LeftUp,
    RightDown,
    RightUp,
    Wheel
}

public enum DesktopPointerAction
{
    PassThrough,
    Forward,
    ForwardAndConsume
}

public sealed record DesktopPointerRouteContext(
    HostMode Mode,
    bool WebViewReady,
    bool DesktopAttached,
    bool IsDesktopSurface,
    DesktopPoint Point,
    DesktopPointerEventKind EventKind,
    DesktopIconMask IconMask);

public static class DesktopPointerRoutePolicy
{
    public static DesktopPointerAction Decide(DesktopPointerRouteContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.EventKind is DesktopPointerEventKind.RightDown or DesktopPointerEventKind.RightUp)
        {
            return DesktopPointerAction.PassThrough;
        }

        if (context.Mode != HostMode.Wallpaper ||
            !context.WebViewReady ||
            !context.DesktopAttached ||
            !context.IsDesktopSurface ||
            !context.IconMask.IsValid ||
            context.IconMask.Contains(context.Point))
        {
            return DesktopPointerAction.PassThrough;
        }

        return context.EventKind == DesktopPointerEventKind.Move
            ? DesktopPointerAction.Forward
            : DesktopPointerAction.ForwardAndConsume;
    }
}
