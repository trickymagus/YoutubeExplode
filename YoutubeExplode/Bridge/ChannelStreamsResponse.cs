using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Lazy;
using YoutubeExplode.Utils;
using YoutubeExplode.Utils.Extensions;

namespace YoutubeExplode.Bridge;

internal partial class ChannelStreamsResponse(JsonElement content)
{
    [Lazy]
    private JsonElement ContentRoot =>
        content.GetPropertyOrNull("contents")
        ?? content.GetPropertyOrNull("onResponseReceivedActions")
        ?? content.GetPropertyOrNull("onResponseReceivedCommands")
        ?? content;

    [Lazy]
    public IReadOnlyList<ChannelStreamData> Streams =>
        ContentRoot
            .EnumerateDescendantProperties("videoRenderer")
            .Select(j => new ChannelStreamData(j))
            .ToArray();

    [Lazy]
    public string? ContinuationToken => TryGetContinuationToken(ContentRoot);

    private static string? TryGetContinuationToken(JsonElement contentRoot)
    {
        // Prefer continuation tokens that are part of the streams grid itself.
        foreach (var grid in contentRoot.EnumerateDescendantProperties("richGridRenderer"))
        {
            var contents = grid.GetPropertyOrNull("contents")?.EnumerateArrayOrNull();
            if (contents is null)
                continue;

            foreach (var item in contents.Value)
            {
                var token = ExtractContinuationToken(
                    item.GetPropertyOrNull("continuationItemRenderer")
                );

                if (!string.IsNullOrWhiteSpace(token))
                    return token;
            }
        }

        // Continuation responses often use appendContinuationItemsAction.
        foreach (
            var action in contentRoot.EnumerateDescendantProperties("appendContinuationItemsAction")
        )
        {
            var items = action.GetPropertyOrNull("continuationItems")?.EnumerateArrayOrNull();
            if (items is null)
                continue;

            foreach (var item in items.Value)
            {
                var token = ExtractContinuationToken(
                    item.GetPropertyOrNull("continuationItemRenderer")
                );

                if (!string.IsNullOrWhiteSpace(token))
                    return token;

                var nextToken = item.GetPropertyOrNull("nextContinuationData")
                    ?.GetPropertyOrNull("continuation")
                    ?.GetStringOrNull();

                if (!string.IsNullOrWhiteSpace(nextToken))
                    return nextToken;
            }
        }

        // Fallback for formats that only expose nextContinuationData.
        foreach (var next in contentRoot.EnumerateDescendantProperties("nextContinuationData"))
        {
            var token = next.GetPropertyOrNull("continuation")?.GetStringOrNull();
            if (!string.IsNullOrWhiteSpace(token))
                return token;
        }

        return null;
    }

    private static string? ExtractContinuationToken(JsonElement? continuationItemRenderer)
    {
        var token = continuationItemRenderer
            ?.GetPropertyOrNull("continuationEndpoint")
            ?.GetPropertyOrNull("continuationCommand")
            ?.GetPropertyOrNull("token")
            ?.GetStringOrNull();

        if (!string.IsNullOrWhiteSpace(token))
            return token;

        token = continuationItemRenderer
            ?.GetPropertyOrNull("continuationCommand")
            ?.GetPropertyOrNull("token")
            ?.GetStringOrNull();

        return string.IsNullOrWhiteSpace(token) ? null : token;
    }

    [Lazy]
    private JsonElement? PageHeaderRenderer =>
        content.EnumerateDescendantProperties("pageHeaderRenderer").FirstOrNull() ?? content
            .EnumerateDescendantProperties("c4TabbedHeaderRenderer")
            .FirstOrNull()
        ?? content.EnumerateDescendantProperties("channelMetadataRenderer").FirstOrNull();

    [Lazy]
    public string? ChannelTitle =>
        PageHeaderRenderer?.GetPropertyOrNull("pageTitle")?.GetStringOrNull()
        ?? PageHeaderRenderer?.GetPropertyOrNull("title")?.GetStringOrNull()
        ?? PageHeaderRenderer
            ?.GetPropertyOrNull("title")
            ?.GetPropertyOrNull("simpleText")
            ?.GetStringOrNull()
        ?? PageHeaderRenderer
            ?.GetPropertyOrNull("title")
            ?.GetPropertyOrNull("runs")
            ?.EnumerateArrayOrNull()
            ?.Select(j => j.GetPropertyOrNull("text")?.GetStringOrNull())
            .WhereNotNull()
            .Pipe(string.Concat);
}

internal partial class ChannelStreamsResponse
{
    public static ChannelStreamsResponse Parse(string raw) => new(Json.Parse(raw));

    public static ChannelStreamsResponse Parse(JsonElement content) => new(content);
}

internal partial class ChannelStreamsResponse
{
    internal class ChannelStreamData(JsonElement content)
    {
        [Lazy]
        public string? Id => content.GetPropertyOrNull("videoId")?.GetStringOrNull();

        [Lazy]
        public string? Title =>
            content.GetPropertyOrNull("title")?.GetPropertyOrNull("simpleText")?.GetStringOrNull()
            ?? content
                .GetPropertyOrNull("title")
                ?.GetPropertyOrNull("runs")
                ?.EnumerateArrayOrNull()
                ?.Select(j => j.GetPropertyOrNull("text")?.GetStringOrNull())
                .WhereNotNull()
                .Pipe(string.Concat);

        [Lazy]
        private JsonElement? AuthorDetails =>
            content
                .GetPropertyOrNull("longBylineText")
                ?.GetPropertyOrNull("runs")
                ?.EnumerateArrayOrNull()
                ?.ElementAtOrNull(0)
            ?? content
                .GetPropertyOrNull("shortBylineText")
                ?.GetPropertyOrNull("runs")
                ?.EnumerateArrayOrNull()
                ?.ElementAtOrNull(0);

        [Lazy]
        public string? Author => AuthorDetails?.GetPropertyOrNull("text")?.GetStringOrNull();

        [Lazy]
        public string? ChannelId =>
            AuthorDetails
                ?.GetPropertyOrNull("navigationEndpoint")
                ?.GetPropertyOrNull("browseEndpoint")
                ?.GetPropertyOrNull("browseId")
                ?.GetStringOrNull()
            ?? content
                .GetPropertyOrNull("channelThumbnailSupportedRenderers")
                ?.GetPropertyOrNull("channelThumbnailWithLinkRenderer")
                ?.GetPropertyOrNull("navigationEndpoint")
                ?.GetPropertyOrNull("browseEndpoint")
                ?.GetPropertyOrNull("browseId")
                ?.GetStringOrNull();

        [Lazy]
        public TimeSpan? Duration =>
            content
                .GetPropertyOrNull("lengthText")
                ?.GetPropertyOrNull("simpleText")
                ?.GetStringOrNull()
                ?.Pipe(s =>
                    TimeSpan.TryParseExact(
                        s,
                        [@"m\:ss", @"mm\:ss", @"h\:mm\:ss", @"hh\:mm\:ss"],
                        CultureInfo.InvariantCulture,
                        out var result
                    )
                        ? result
                        : (TimeSpan?)null
                )
            ?? content
                .GetPropertyOrNull("lengthText")
                ?.GetPropertyOrNull("runs")
                ?.EnumerateArrayOrNull()
                ?.Select(j => j.GetPropertyOrNull("text")?.GetStringOrNull())
                .WhereNotNull()
                .Pipe(string.Concat)
                ?.Pipe(s =>
                    TimeSpan.TryParseExact(
                        s,
                        [@"m\:ss", @"mm\:ss", @"h\:mm\:ss", @"hh\:mm\:ss"],
                        CultureInfo.InvariantCulture,
                        out var result
                    )
                        ? result
                        : (TimeSpan?)null
                );

        [Lazy]
        public IReadOnlyList<ThumbnailData> Thumbnails =>
            content
                .GetPropertyOrNull("thumbnail")
                ?.GetPropertyOrNull("thumbnails")
                ?.EnumerateArrayOrNull()
                ?.Select(j => new ThumbnailData(j))
                .ToArray() ?? [];

        [Lazy]
        private string? TimeStatusStyle =>
            content
                .GetPropertyOrNull("thumbnailOverlays")
                ?.EnumerateArrayOrNull()
                ?.Select(j => j.GetPropertyOrNull("thumbnailOverlayTimeStatusRenderer"))
                .WhereNotNull()
                .Select(j => j.GetPropertyOrNull("style")?.GetStringOrNull())
                .WhereNotNull()
                .FirstOrDefault();

        [Lazy]
        private bool HasLiveBadge =>
            content
                .GetPropertyOrNull("badges")
                ?.EnumerateArrayOrNull()
                ?.Select(j => j.GetPropertyOrNull("metadataBadgeRenderer"))
                .WhereNotNull()
                .Any(j =>
                {
                    var style = j.GetPropertyOrNull("style")?.GetStringOrNull();
                    return string.Equals(style, "LIVE_NOW", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(style, "LIVE", StringComparison.OrdinalIgnoreCase);
                }) ?? false;

        [Lazy]
        public bool IsLive =>
            string.Equals(TimeStatusStyle, "LIVE", StringComparison.OrdinalIgnoreCase)
            || string.Equals(TimeStatusStyle, "LIVE_NOW", StringComparison.OrdinalIgnoreCase)
            || HasLiveBadge;

        [Lazy]
        public bool IsUpcoming =>
            string.Equals(TimeStatusStyle, "UPCOMING", StringComparison.OrdinalIgnoreCase)
            || content.GetPropertyOrNull("upcomingEventData") is not null;
    }
}
