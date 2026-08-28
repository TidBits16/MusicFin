using Jellyfin.Plugin.DeezerTagger;
using Xunit;
using Xunit.Abstractions;

namespace Jellyfin.Plugin.DeezerTagger.Tests;

[Trait("Category", "Live")]
public class WillWoodNormalAlbumLiveTests
{
    private readonly ITestOutputHelper _out;
    public WillWoodNormalAlbumLiveTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public async Task JellyfinShortTitles_AllLandOnTheNormalAlbum()
    {
        // Exact titles currently on jelly.dangerdrive.com for Will Wood
        var localTitles = new[]
        {
            "Suburbia Overture",
            "Second Sight Seer",
            "Laplace's Angel",
            "I/Me/Myself",
            "Well, Better Than the Alternative",
            "Outliars & Hyppocrates",
            "Black Box Warrior",
            "Marsha, Thank You for the Dialectics, But I Need You to Leave",
            "Love Me Normally (2018 Mix)",
            "Memento Mori",
        };

        var local = localTitles.Select(t => new LocalTrack { Id = Guid.NewGuid(), Title = t }).ToList();
        var client = TestMetadata.CreateDeezer();
        var artist = (await client.GetArtistCandidatesAsync("Will Wood", CancellationToken.None))
            .FirstOrDefault(a => a.Name.Equals("Will Wood", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(artist);
        _out.WriteLine($"artist {artist!.ArtistId} {artist.Name}");

        var discography = await client.GetArtistDiscographyAsync(artist.ArtistId, artist.Name, 2, CancellationToken.None);
        foreach (var a in discography.OrderBy(x => x.Title))
            _out.WriteLine($"  [{a.RecordType}] {a.Tracks.Count} {a.Title}");

        var result = AlbumMatcher.Match("Will Wood", local, discography, new AlbumMatcherOptions());
        foreach (var a in result.Assignments.OrderBy(x => x.AlbumTitle).ThenBy(x => x.TrackNumber))
            _out.WriteLine($"#{a.TrackNumber,2} [{a.AlbumTitle}] <- {a.TrackTitle}");
        _out.WriteLine($"unmatched={result.UnmatchedCount}");

        Assert.Equal(0, result.UnmatchedCount);
        Assert.All(result.Assignments, a =>
            Assert.Contains("Normal Album", a.AlbumTitle, StringComparison.OrdinalIgnoreCase));
        var albums = result.Assignments.Select(a => a.AlbumTitle).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Assert.True(albums.Count == 1, "expected one album, got: " + string.Join(" | ", albums));
    }
}
