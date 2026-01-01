using System;
using System.Linq;
using System.Text.Json;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Lazy;
using YoutubeExplode.Utils;
using YoutubeExplode.Utils.Extensions;

namespace YoutubeExplode.Bridge;

internal partial class ChannelStreamsPage(IHtmlDocument content)
{
    private static string? TryExtractInitialData(string script)
    {
        var index = script.IndexOf("ytInitialData", StringComparison.Ordinal);
        if (index < 0)
            return null;

        var jsonStartIndex = script.IndexOf('{', index);
        if (jsonStartIndex < 0)
            return null;

        return Json.Extract(script[jsonStartIndex..]);
    }

    [Lazy]
    public JsonElement? InitialData =>
        content
            .GetElementsByTagName("script")
            .Select(e => e.Text())
            .Select(TryExtractInitialData)
            .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s))
            ?.Pipe(Json.TryParse);
}

internal partial class ChannelStreamsPage
{
    public static ChannelStreamsPage? TryParse(string raw)
    {
        if (!raw.Contains("ytInitialData", StringComparison.Ordinal))
            return null;

        var content = Html.Parse(raw);
        return new ChannelStreamsPage(content);
    }
}
