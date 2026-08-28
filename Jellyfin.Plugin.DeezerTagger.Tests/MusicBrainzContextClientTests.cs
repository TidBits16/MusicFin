using System.Text.Json;
using Jellyfin.Plugin.DeezerTagger;
using Xunit;

namespace Jellyfin.Plugin.DeezerTagger.Tests;

public class MusicBrainzContextClientTests
{
    [Fact]
    public void ShouldFetchReleaseGroup_IncludesEp()
    {
        using var doc = JsonDocument.Parse(
            """
            {
              "id": "8ee338b5-380a-4541-8728-450d3fa63a19",
              "title": "Mary",
              "primary-type": "EP",
              "artist-credit": [
                { "artist": { "name": "Rainbow Kitten Surprise" } }
              ]
            }
            """);

        Assert.True(MusicBrainzContextClient.ShouldFetchReleaseGroup(doc.RootElement, "Rainbow Kitten Surprise"));
    }
}
