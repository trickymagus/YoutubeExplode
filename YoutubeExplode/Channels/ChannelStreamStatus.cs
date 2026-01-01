namespace YoutubeExplode.Channels;

/// <summary>
/// Status of a stream on a channel.
/// </summary>
public enum ChannelStreamStatus
{
    /// <summary>
    /// Stream is currently live.
    /// </summary>
    Live,

    /// <summary>
    /// Stream is scheduled for a future time.
    /// </summary>
    Upcoming,

    /// <summary>
    /// Stream has ended and is available as a recording.
    /// </summary>
    Past,
}
