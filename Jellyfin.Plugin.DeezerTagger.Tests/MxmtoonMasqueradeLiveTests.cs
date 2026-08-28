using Jellyfin.Plugin.DeezerTagger;
using Xunit;
using Xunit.Abstractions;

namespace Jellyfin.Plugin.DeezerTagger.Tests;

[Trait("Category", "Live")]
public class MxmtoonMasqueradeLiveTests
{
    private readonly ITestOutputHelper _out;
    public MxmtoonMasqueradeLiveTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public async Task AllMasqueradeTracks_LandOnTheMasqueradeWithCorrectDiscs()
    {
        var localTitles = new[]
        {
            "unspoken words", "prom dress", "suffice", "blame game", "high & dry",
            "my ted talk", "seasonal depression", "untitled", "dream of you", "late nights",
            "unspoken words (acoustic)", "prom dress (acoustic)", "suffice (acoustic)",
            "blame game (acoustic)", "high & dry (acoustic)", "my ted talk (acoustic)",
            "seasonal depression (acoustic)", "untitled (acoustic)", "dream of you (acoustic)",
            "late nights (acoustic)",
        };
        var local = localTitles.Select(t => new LocalTrack { Id = Guid.NewGuid(), Title = t }).ToList();
        var client = TestMetadata.CreateDeezer();
        var artist = (await client.GetArtistCandidatesAsync("mxmtoon", CancellationToken.None))
            .First(a => a.Name.Equals("mxmtoon", StringComparison.OrdinalIgnoreCase));
        var discography = await client.GetArtistDiscographyAsync(artist.ArtistId, artist.Name, 2, CancellationToken.None);
        foreach (var a in discography.Where(x => x.Title.Contains("masquerade", StringComparison.OrdinalIgnoreCase)
                                                 || x.IsSingle
                                                 || x.Title.Contains("blame", StringComparison.OrdinalIgnoreCase)
                                                 || x.Title.Contains("seasonal", StringComparison.OrdinalIgnoreCase)
                                                 || x.Title.Contains("prom dress", StringComparison.OrdinalIgnoreCase)
                                                 || x.Title.Contains("dream of you", StringComparison.OrdinalIgnoreCase)
                                                 || x.Title.Contains("high", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(x => x.Title))
        {
            _out.WriteLine($"[{a.RecordType}] {a.Tracks.Count} {a.Title}");
            foreach (var t in a.Tracks.Take(3))
                _out.WriteLine($"    d{t.DiskNumber}#{t.TrackPosition} {t.Title}");
            if (a.Tracks.Count > 3) _out.WriteLine($"    ... +{a.Tracks.Count - 3}");
        }

        var result = AlbumMatcher.Match("mxmtoon", local, discography, new AlbumMatcherOptions());
        foreach (var a in result.Assignments.OrderBy(x => x.AlbumTitle).ThenBy(x => x.DiscNumber).ThenBy(x => x.TrackNumber))
            _out.WriteLine($"d{a.DiscNumber}#{a.TrackNumber,2} [{a.AlbumTitle}] <- {a.TrackTitle}");
        _out.WriteLine($"unmatched={result.UnmatchedCount} singles={result.SingleReleaseCount}");

        Assert.Equal(0, result.UnmatchedCount);
        Assert.All(result.Assignments, a =>
            Assert.Equal("the masquerade", a.AlbumTitle, ignoreCase: true));
        Assert.Equal(0, result.SingleReleaseCount);
        Assert.Contains(result.Assignments, a => a.DiscNumber == 2);
        Assert.Contains(result.Assignments, a => a.DiscNumber == 1 && a.TrackNumber == 4 && a.TrackTitle == "blame game");
    }
}
