namespace NikkiDesktop.Core;

public sealed record WebContentMapping(
    string ResolvedContentRoot,
    string VirtualHostName,
    Uri StartUri)
{
    public static WebContentMapping Create(string contentRoot)
    {
        var validation = ContentRootValidator.Validate(contentRoot);
        if (!validation.IsValid || validation.ResolvedContentRoot is null)
        {
            throw new InvalidDataException(validation.Error ?? "播放器内容目录无效。");
        }

        const string virtualHostName = "nikkidesktop.local";
        return new WebContentMapping(
            validation.ResolvedContentRoot,
            virtualHostName,
            new Uri($"https://{virtualHostName}/index.html", UriKind.Absolute));
    }
}
