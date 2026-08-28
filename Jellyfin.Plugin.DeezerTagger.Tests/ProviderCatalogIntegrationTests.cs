using Jellyfin.Plugin.DeezerTagger;
using Xunit;
using Xunit.Abstractions;

namespace Jellyfin.Plugin.DeezerTagger.Tests;

public class ProviderCatalogIntegrationTests
{
    private readonly ITestOutputHelper _out;

    public ProviderCatalogIntegrationTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public async Task Deezer_AlexWarren_AlbumsHaveGenresAndTrackMatches()
    {
        var client = TestMetadata.CreateDeezer();
        var artist = (await client.GetArtistCandidatesAsync("Alex Warren", CancellationToken.None)).FirstOrDefault();
        Assert.NotNull(artist);

        var discography = await client.GetArtistDiscographyAsync(artist!.ArtistId, "Alex Warren", 2, CancellationToken.None);
        Assert.NotEmpty(discography);

        var withGenres = discography.Where(a => a.Genres.Count > 0).ToList();
        _out.WriteLine($"Deezer albums: {discography.Count}, with genres: {withGenres.Count}");
        foreach (var album in withGenres.Take(5))
        {
            _out.WriteLine($"  \"{album.Title}\" -> {string.Join(", ", album.Genres)}");
        }

        Assert.NotEmpty(withGenres);

        var local = new[] { "Ordinary", "Burning Down", "Carry You Home" }
            .Select(t => new LocalTrack { Id = Guid.NewGuid(), Title = t })
            .ToList();
        var result = AlbumMatcher.Match("Alex Warren", local, discography, new AlbumMatcherOptions());
        _out.WriteLine($"Unmatched: {result.UnmatchedCount}");
        Assert.True(result.UnmatchedCount < local.Count);
        Assert.Contains(result.Assignments, a => a.Genres.Count > 0);
    }

    [Fact]
    public async Task Itunes_Radiohead_AlbumsHaveGenresAndOkComputerTracks()
    {
        var client = TestMetadata.CreateItunes();
        var artist = (await client.GetArtistCandidatesAsync("Radiohead", CancellationToken.None)).FirstOrDefault();
        Assert.NotNull(artist);

        var discography = await client.GetArtistDiscographyAsync(artist!.ArtistId, "Radiohead", 2, CancellationToken.None);
        Assert.NotEmpty(discography);

        var okComputer = discography.FirstOrDefault(a =>
            a.Title.Contains("OK Computer", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(okComputer);
        _out.WriteLine($"OK Computer genres: {string.Join(", ", okComputer!.Genres)}");
        _out.WriteLine($"OK Computer tracks: {okComputer.Tracks.Count}");

        Assert.NotEmpty(okComputer.Genres);
        Assert.Contains(okComputer.Tracks, t => t.Title.Contains("Paranoid Android", StringComparison.OrdinalIgnoreCase));

        var local = okComputer.Tracks.Take(3)
            .Select(t => new LocalTrack { Id = Guid.NewGuid(), Title = t.Title })
            .ToList();
        var result = AlbumMatcher.Match("Radiohead", local, discography, new AlbumMatcherOptions());
        Assert.Equal(0, result.UnmatchedCount);
        Assert.All(result.Assignments, a => Assert.NotEmpty(a.Genres));
    }

    [Fact]
    public async Task Discogs_Radiohead_SearchWorks_AndGenresParseFromSample()
    {
        var client = TestMetadata.CreateDiscogs();
        var artist = (await client.GetArtistCandidatesAsync("Radiohead", CancellationToken.None)).FirstOrDefault();
        Assert.NotNull(artist);
        _out.WriteLine($"Discogs artist id {artist!.ArtistId} name {artist.Name}");

        using var doc = System.Text.Json.JsonDocument.Parse(
            """
            {
              "genres": ["Electronic", "Rock"],
              "styles": ["Alternative Rock"],
              "tracklist": [
                { "type_": "track", "title": "Airbag", "position": "A1" },
                { "type_": "track", "title": "Paranoid Android", "position": "A2" }
              ]
            }
            """);
        var genres = InvokeDiscogsGenres(doc.RootElement);
        Assert.Contains("Rock", genres);
        Assert.Contains("Alternative Rock", genres);
        Assert.Equal(2, DiscogsContextClient.ParseTrackPosition("A2", 9));
    }

    [Fact]
    public async Task OpenOpus_Bach_WorksHaveClassicalGenres()
    {
        var client = TestMetadata.CreateOpenOpus();
        var artist = (await client.GetArtistCandidatesAsync("Johann Sebastian Bach", CancellationToken.None)).FirstOrDefault();
        Assert.NotNull(artist);
        _out.WriteLine($"Open Opus composer id {artist!.ArtistId} name {artist.Name}");

        var discography = await client.GetArtistDiscographyAsync(artist.ArtistId, artist.Name, 1, CancellationToken.None);
        Assert.NotEmpty(discography);

        var withGenres = discography.Where(a => a.Genres.Count > 0).ToList();
        _out.WriteLine($"Open Opus works: {discography.Count}, with genres: {withGenres.Count}");
        foreach (var work in withGenres.Take(5))
        {
            _out.WriteLine($"  \"{work.Title}\" -> {string.Join(", ", work.Genres)}");
        }

        Assert.NotEmpty(withGenres);
        Assert.Contains(withGenres, w => w.Genres.Any(g => g.Contains("Classical", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(withGenres, w => w.Genres.Any(g => g.Contains("Baroque", StringComparison.OrdinalIgnoreCase)
            || g.Contains("Keyboard", StringComparison.OrdinalIgnoreCase)
            || g.Contains("Orchestral", StringComparison.OrdinalIgnoreCase)));

        var genres = OpenOpusContextClient.GenresForWork("Orchestral", "Baroque");
        Assert.Contains("Orchestral", genres);
        Assert.Contains("Baroque", genres);
        Assert.Contains("Classical", genres);
    }

    private static List<string> InvokeDiscogsGenres(System.Text.Json.JsonElement payload)
    {
        var method = typeof(DiscogsContextClient).GetMethod("GenresFrom", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);
        return (List<string>)method!.Invoke(null, [payload])!;
    }
}
