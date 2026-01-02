using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using YoutubeExplode.Bridge;
using YoutubeExplode.Common;
using YoutubeExplode.Exceptions;
using YoutubeExplode.Playlists;
using YoutubeExplode.Utils.Extensions;
using YoutubeExplode.Videos;

namespace YoutubeExplode.Channels;

/// <summary>
/// Operations related to YouTube channels.
/// </summary>
public class ChannelClient(HttpClient http)
{
    private readonly ChannelController _controller = new(http);
    private readonly ChannelStreamsController _streamsController = new(http);

    private Channel Get(ChannelPage channelPage)
    {
        var channelId =
            channelPage.Id
            ?? throw new YoutubeExplodeException("Failed to extract the channel ID.");

        var title =
            channelPage.Title
            ?? throw new YoutubeExplodeException("Failed to extract the channel title.");

        var logoUrl =
            channelPage.LogoUrl
            ?? throw new YoutubeExplodeException("Failed to extract the channel logo URL.");

        var logoSize =
            Regex
                .Matches(logoUrl, @"\bs(\d+)\b")
                .ToArray()
                .LastOrDefault()
                ?.Groups[1]
                .Value.NullIfWhiteSpace()
                ?.Pipe(s =>
                    int.TryParse(s, CultureInfo.InvariantCulture, out var result)
                        ? result
                        : (int?)null
                )
            ?? 100;

        var thumbnails = new[] { new Thumbnail(logoUrl, new Resolution(logoSize, logoSize)) };

        return new Channel(channelId, title, thumbnails);
    }

    /// <summary>
    /// Gets the metadata associated with the specified channel.
    /// </summary>
    public async ValueTask<Channel> GetAsync(
        ChannelId channelId,
        CancellationToken cancellationToken = default
    )
    {
        // Special case for the "Movies & TV" channel, which has a custom page
        if (channelId == "UCuVPpxrm2VAgpH3Ktln4HXg")
        {
            return new Channel(
                "UCuVPpxrm2VAgpH3Ktln4HXg",
                "Movies & TV",
                [
                    new Thumbnail(
                        "https://www.gstatic.com/youtube/img/tvfilm/clapperboard_profile.png",
                        new Resolution(1024, 1024)
                    ),
                ]
            );
        }

        return Get(await _controller.GetChannelPageAsync(channelId, cancellationToken));
    }

    /// <summary>
    /// Gets the metadata associated with the channel of the specified user.
    /// </summary>
    public async ValueTask<Channel> GetByUserAsync(
        UserName userName,
        CancellationToken cancellationToken = default
    ) => Get(await _controller.GetChannelPageAsync(userName, cancellationToken));

    /// <summary>
    /// Gets the metadata associated with the channel identified by the specified slug or legacy custom URL.
    /// </summary>
    public async ValueTask<Channel> GetBySlugAsync(
        ChannelSlug channelSlug,
        CancellationToken cancellationToken = default
    ) => Get(await _controller.GetChannelPageAsync(channelSlug, cancellationToken));

    /// <summary>
    /// Gets the metadata associated with the channel identified by the specified handle or custom URL.
    /// </summary>
    public async ValueTask<Channel> GetByHandleAsync(
        ChannelHandle channelHandle,
        CancellationToken cancellationToken = default
    ) => Get(await _controller.GetChannelPageAsync(channelHandle, cancellationToken));

    /// <summary>
    /// Enumerates videos uploaded by the specified channel.
    /// </summary>
    // TODO: should return <IVideo> sequence instead (breaking change)
    public IAsyncEnumerable<PlaylistVideo> GetUploadsAsync(
        ChannelId channelId,
        CancellationToken cancellationToken = default
    )
    {
        // Replace 'UC' in the channel ID with 'UU'
        var playlistId = "UU" + channelId.Value[2..];
        return new PlaylistClient(http).GetVideosAsync(playlistId, cancellationToken);
    }

    /// <summary>
    /// Enumerates streams associated with the specified channel.
    /// </summary>
    public async IAsyncEnumerable<ChannelStreamVideo> GetStreamsAsync(
        ChannelId channelId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        var encounteredIds = new HashSet<VideoId>();
        var encounteredContinuationTokens = new HashSet<string>(StringComparer.Ordinal);
        var continuationToken = default(string?);
        var channelTitle = default(string?);

        do
        {
            var response = await _streamsController.GetChannelStreamsResponseAsync(
                channelId,
                continuationToken,
                cancellationToken
            );

            channelTitle ??= response.ChannelTitle?.NullIfWhiteSpace();

            var newStreamsCount = 0;

            foreach (var streamData in response.Streams)
            {
                var videoIdRaw =
                    streamData.Id
                    ?? throw new YoutubeExplodeException("Failed to extract the video ID.");
                var videoId = (VideoId)videoIdRaw;

                // Don't yield the same video twice
                if (!encounteredIds.Add(videoId))
                    continue;

                // Skip videos that clearly belong to a different channel
                if (
                    !string.IsNullOrWhiteSpace(streamData.ChannelId)
                    && !string.Equals(
                        streamData.ChannelId,
                        channelId.Value,
                        StringComparison.Ordinal
                    )
                )
                {
                    continue;
                }

                var videoTitle =
                    streamData.Title
                    // Videos without title are legal
                    // https://github.com/Tyrrrz/YoutubeExplode/issues/700
                    ?? "";

                channelTitle ??= streamData.Author?.NullIfWhiteSpace();

                var videoChannelTitle =
                    streamData.Author?.NullIfWhiteSpace()
                    ?? channelTitle
                    ?? throw new YoutubeExplodeException("Failed to extract the video author.");

                var videoChannelId = !string.IsNullOrWhiteSpace(streamData.ChannelId)
                    ? (ChannelId)streamData.ChannelId
                    : channelId;

                var videoThumbnails = streamData
                    .Thumbnails.Select(t =>
                    {
                        var thumbnailUrl =
                            t.Url
                            ?? throw new YoutubeExplodeException(
                                "Failed to extract the thumbnail URL."
                            );

                        var thumbnailWidth =
                            t.Width
                            ?? throw new YoutubeExplodeException(
                                "Failed to extract the thumbnail width."
                            );

                        var thumbnailHeight =
                            t.Height
                            ?? throw new YoutubeExplodeException(
                                "Failed to extract the thumbnail height."
                            );

                        var thumbnailResolution = new Resolution(thumbnailWidth, thumbnailHeight);

                        return new Thumbnail(thumbnailUrl, thumbnailResolution);
                    })
                    .Concat(Thumbnail.GetDefaultSet(videoId))
                    .ToArray();

                var status =
                    streamData.IsLive ? ChannelStreamStatus.Live
                    : streamData.IsUpcoming ? ChannelStreamStatus.Upcoming
                    : ChannelStreamStatus.Past;

                newStreamsCount++;

                yield return new ChannelStreamVideo(
                    videoId,
                    videoTitle,
                    new Author(videoChannelId, videoChannelTitle),
                    streamData.Duration,
                    videoThumbnails,
                    status
                );
            }

            if (newStreamsCount == 0)
                break;

            var nextToken = response.ContinuationToken;
            if (string.IsNullOrWhiteSpace(nextToken))
                break;

            if (!encounteredContinuationTokens.Add(nextToken))
                break;

            continuationToken = nextToken;
        } while (true);
    }

    /// <summary>
    /// Enumerates currently live streams associated with the specified channel.
    /// </summary>
    public async IAsyncEnumerable<ChannelStreamVideo> GetLiveStreamsAsync(
        ChannelId channelId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        await foreach (var stream in GetStreamsAsync(channelId, cancellationToken))
        {
            if (stream.Status == ChannelStreamStatus.Live)
                yield return stream;
            else if (stream.Status == ChannelStreamStatus.Past)
                // Assumes streams are grouped as upcoming -> live -> past, so we can stop once past starts.
                yield break;
        }
    }

    /// <summary>
    /// Enumerates past streams associated with the specified channel.
    /// </summary>
    public async IAsyncEnumerable<ChannelStreamVideo> GetPastStreamsAsync(
        ChannelId channelId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        await foreach (var stream in GetStreamsAsync(channelId, cancellationToken))
        {
            if (stream.Status == ChannelStreamStatus.Past)
                yield return stream;
        }
    }
}
