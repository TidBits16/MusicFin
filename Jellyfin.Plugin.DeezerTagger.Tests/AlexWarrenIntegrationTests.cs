using Jellyfin.Plugin.DeezerTagger;
using Xunit;
using Xunit.Abstractions;

namespace Jellyfin.Plugin.DeezerTagger.Tests;

public class AlexWarrenIntegrationTests
{
    private readonly ITestOutputHelper _out;

    public AlexWarrenIntegrationTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public async Task AlexWarren_AlbumTracksMatchLocalTitles()
    {
        var client = TestMetadata.CreateDeezer();
        var artist = (await client.GetArtistCandidatesAsync("Alex Warren", CancellationToken.None)).FirstOrDefault();
        Assert.NotNull(artist);
        _out.WriteLine($"Artist id {artist!.ArtistId} name {artist.Name}");

        var discography = await client.GetArtistDiscographyAsync(
            artist.ArtistId,
            "Alex Warren",
            2,
            CancellationToken.None);

        _out.WriteLine($"Discography albums: {discography.Count}");
        foreach (var album in discography)
        {
            _out.WriteLine($"  {album.AlbumId} \"{album.Title}\" tracks={album.Tracks.Count}");
        }

        Assert.NotEmpty(discography);

        var localTitles = new[]
        {
            "Getaway Car",
            "Who I Am",
            "You Can't Stop This",
            "On My Mind",
            "Burning Down",
            "You'll Be Alright, Kid"
        };

        var local = localTitles.Select(t => new LocalTrack { Id = Guid.NewGuid(), Title = t }).ToList();
        var result = AlbumMatcher.Match("Alex Warren", local, discography, new AlbumMatcherOptions());

        _out.WriteLine($"Single releases: {result.SingleReleaseCount}, unmatched: {result.UnmatchedCount}");
        foreach (var a in result.Assignments)
        {
            _out.WriteLine($"  {a.TrackTitle} -> {a.AlbumTitle} #{a.TrackNumber} single={a.IsSingleRelease}");
        }

        Assert.Equal(0, result.UnmatchedCount);
        Assert.Contains(result.Assignments, a => a.AlbumTitle.Contains("You'll Be Alright", StringComparison.OrdinalIgnoreCase));
    }
}
