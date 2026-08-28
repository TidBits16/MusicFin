using System.Text.Json;
using Jellyfin.Plugin.DeezerTagger;
using Xunit;
using Xunit.Abstractions;

namespace Jellyfin.Plugin.DeezerTagger.Tests;

public class AjrArtistSearchTests
{
    private readonly ITestOutputHelper _out;

    public AjrArtistSearchTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void RankArtistSearchResults_PrefersPopularAjrOverHomonym()
    {
        using var doc = JsonDocument.Parse(
            """
            {
              "data": [
                { "id": 176420427, "name": "AJR", "nb_fan": 682, "nb_album": 1 },
                { "id": 3288461, "name": "AJR", "nb_fan": 182928, "nb_album": 25 }
              ]
            }
            """);

        var ranked = DeezerContextClient.RankArtistSearchResults(
            JsonUtil.Arr(doc.RootElement, "data"),
            Titles.Norm("AJR"));

        Assert.Equal(2, ranked.Count);
        Assert.Equal("3288461", ranked[0].ArtistId);
        Assert.Equal("176420427", ranked[1].ArtistId);
    }

    [Fact]
    public async Task Ajr_LiveSearchPicksPopularArtistWithStudioAlbums()
    {
        var client = TestDeezer.CreateClient();
        var candidates = await client.GetArtistCandidatesAsync("AJR", CancellationToken.None);
        Assert.NotEmpty(candidates);

        _out.WriteLine(string.Join(", ", candidates.Select(c => $"{c.ArtistId} {c.Name}")));

        Assert.Equal("3288461", candidates[0].ArtistId);

        var discography = await client.GetArtistDiscographyAsync(
            candidates[0].ArtistId,
            "AJR",
            1,
            CancellationToken.None);

        _out.WriteLine($"Albums: {discography.Count}");
        foreach (var album in discography)
        {
            _out.WriteLine($"  {album.Title} ({album.Tracks.Count} tracks)");
        }

        Assert.NotEmpty(discography);
        Assert.Contains(discography, a => a.Title.Contains("Neotheater", StringComparison.OrdinalIgnoreCase)
            || a.Title.Contains("OK Orchestra", StringComparison.OrdinalIgnoreCase)
            || a.Title.Contains("The Click", StringComparison.OrdinalIgnoreCase));
    }
}
