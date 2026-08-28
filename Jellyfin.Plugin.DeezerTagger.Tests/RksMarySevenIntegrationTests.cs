using Jellyfin.Plugin.DeezerTagger;
using Xunit;
using Xunit.Abstractions;

namespace Jellyfin.Plugin.DeezerTagger.Tests;

public class RksMarySevenIntegrationTests
{
    private readonly ITestOutputHelper _out;

    public RksMarySevenIntegrationTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public async Task RainbowKittenSurprise_UserLibraryTracks_ShouldLandOnSevenPlusMary()
    {
        var client = TestMetadata.CreateDeezer();
        var artist = (await client.GetArtistCandidatesAsync("Rainbow Kitten Surprise", CancellationToken.None)).FirstOrDefault();
        Assert.NotNull(artist);

        var discography = await client.GetArtistDiscographyAsync(
            artist!.ArtistId,
            "Rainbow Kitten Surprise",
            2,
            CancellationToken.None);

        _out.WriteLine($"Discography albums: {discography.Count}");
        foreach (var album in discography.Where(a =>
            a.Title.Contains("Mary", StringComparison.OrdinalIgnoreCase) ||
            a.Title.Contains("Seven", StringComparison.OrdinalIgnoreCase)))
        {
            _out.WriteLine($"  {album.AlbumId} \"{album.Title}\" tracks={album.Tracks.Count} type={album.RecordType}");
            foreach (var t in album.Tracks)
            {
                _out.WriteLine($"    {t.TrackPosition,2}. {t.Title}");
            }
        }

        var localTitles = new[]
        {
            "Fail!",
            "Mr. Redundant",
            "American Hero",
            "Shameful Company",
            "All That And More",
            "Hey Pretty Momma",
            "Black and White",
            "Devil Like Me",
            "Seven",
            "First Class",
            "That's My Shit"
        };

        var local = localTitles.Select(t => new LocalTrack { Id = Guid.NewGuid(), Title = t }).ToList();
        var result = AlbumMatcher.Match("Rainbow Kitten Surprise", local, discography, new AlbumMatcherOptions());

        _out.WriteLine($"Summaries: {string.Join(", ", result.AlbumSummaries.Select(s => $"{s.AlbumTitle} ({s.TrackCount})"))}");
        foreach (var a in result.Assignments.OrderBy(x => x.TrackNumber))
        {
            _out.WriteLine($"  #{a.TrackNumber,2} {a.TrackTitle} -> \"{a.AlbumTitle}\"");
        }

        Assert.Equal(0, result.UnmatchedCount);
        Assert.All(result.Assignments, a => Assert.Equal("Seven + Mary", a.AlbumTitle));
        Assert.Equal(Enumerable.Range(1, 11), result.Assignments.Select(a => a.TrackNumber).OrderBy(x => x));
    }
}
