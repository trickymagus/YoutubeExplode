using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using YoutubeExplode.Common;
using YoutubeExplode.Videos;

namespace YoutubeExplode.Channels;

/// <summary>
/// Metadata associated with a stream on a YouTube channel.
/// </summary>
public class ChannelStreamVideo(
    VideoId id,
    string title,
    Author author,
    TimeSpan? duration,
    IReadOnlyList<Thumbnail> thumbnails,
    ChannelStreamStatus status
) : IVideo, IBatchItem
{
    /// <inheritdoc />
    public VideoId Id { get; } = id;

    /// <inheritdoc cref="IVideo.Url" />
    public string Url => $"https://www.youtube.com/watch?v={Id}";

    /// <inheritdoc cref="IVideo.Title" />
    public string Title { get; } = title;

    /// <inheritdoc />
    public Author Author { get; } = author;

    /// <inheritdoc />
    public TimeSpan? Duration { get; } = duration;

    /// <inheritdoc />
    public IReadOnlyList<Thumbnail> Thumbnails { get; } = thumbnails;

    /// <summary>
    /// Stream status.
    /// </summary>
    public ChannelStreamStatus Status { get; } = status;

    /// <inheritdoc />
    [ExcludeFromCodeCoverage]
    public override string ToString() => $"Stream ({Title})";
}
