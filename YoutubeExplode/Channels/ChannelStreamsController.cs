using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using YoutubeExplode.Bridge;
using YoutubeExplode.Exceptions;
using YoutubeExplode.Utils;

namespace YoutubeExplode.Channels;

internal class ChannelStreamsController(HttpClient http)
{
    public async ValueTask<ChannelStreamsResponse> GetChannelStreamsResponseAsync(
        ChannelId channelId,
        string? continuationToken,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(continuationToken))
        {
            for (var retriesRemaining = 5; ; retriesRemaining--)
            {
                var page = ChannelStreamsPage.TryParse(
                    await http.GetStringAsync(
                        $"https://www.youtube.com/channel/{channelId}/streams",
                        cancellationToken
                    )
                );

                var initialData = page?.InitialData;
                if (initialData is null)
                {
                    if (retriesRemaining > 0)
                        continue;

                    throw new YoutubeExplodeException(
                        "Channel streams page is broken. Please try again in a few minutes."
                    );
                }

                return ChannelStreamsResponse.Parse(initialData.Value);
            }
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://www.youtube.com/youtubei/v1/browse"
        );

        request.Content = new StringContent(
            // lang=json
            $$"""
            {
              "continuation": {{Json.Serialize(continuationToken)}},
              "context": {
                "client": {
                  "clientName": "WEB",
                  "clientVersion": "2.20210408.08.00",
                  "hl": "ko",
                  "gl": "KR",
                  "utcOffsetMinutes": 0
                }
              }
            }
            """
        );

        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return ChannelStreamsResponse.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken)
        );
    }
}
