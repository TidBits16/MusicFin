using Jellyfin.Plugin.DeezerTagger;
using Xunit;
using Xunit.Abstractions;

namespace Jellyfin.Plugin.DeezerTagger.Tests;

public class AjrWhatNoOnesThinkingTests
{
    private readonly ITestOutputHelper _out;

    public AjrWhatNoOnesThinkingTests(ITestOutputHelper output) => _out = output;

    [Fact]
    [Trait("Category", "Live")]
    public async Task DeezerEpBeatsLiveAlbum_ForWhatNoOnesThinkingTracks()
    {
        var client = TestDeezer.CreateClient();
        var candidates = await client.GetArtistCandidatesAsync("AJR", CancellationToken.None);
        Assert.NotEmpty(candidates);
        var artist = candidates[0];
        Assert.Equal("3288461", artist.ArtistId);

        var discography = await client.GetArtistDiscographyAsync(
            artist.ArtistId,
            "AJR",
            2,
            CancellationToken.None);

        var ep = discography.FirstOrDefault(a =>
            a.Title.Contains("What No One", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(ep);
        _out.WriteLine($"EP: {ep!.Title} ({ep.Tracks.Count} tracks, type={ep.RecordType})");
        Assert.Equal("ep", ep.RecordType, ignoreCase: true);

        var localTitles = new[]
        {
            "The Plane That Never Lands",
            "A Dog Song",
            "Betty",
            "I'm Sorry You Went Crazy",
            "The Big Goodbye"
        };

        var local = localTitles.Select(t => new LocalTrack { Id = Guid.NewGuid(), Title = t }).ToList();
        var result = AlbumMatcher.Match("AJR", local, discography, new AlbumMatcherOptions());

        _out.WriteLine(string.Join(", ", result.AlbumSummaries.Select(s => $"{s.AlbumTitle} ({s.TrackCount})")));
        foreach (var a in result.Assignments.OrderBy(x => x.TrackNumber))
        {
            _out.WriteLine($"  #{a.TrackNumber,2} {a.TrackTitle} -> {a.AlbumTitle}");
        }

        Assert.Equal(0, result.UnmatchedCount);
        Assert.All(result.Assignments, a =>
            Assert.Contains("What No One", a.AlbumTitle, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(
            new[] { 1, 2, 3, 4, 5 },
            result.Assignments.Select(a => a.TrackNumber).OrderBy(x => x));
    }

    [Fact]
    public void StudioEpWinsOverLiveAlbum_WhenBothMatchSameTracks()
    {
        var local = new List<LocalTrack>
        {
            new() { Id = Guid.NewGuid(), Title = "Betty" },
            new() { Id = Guid.NewGuid(), Title = "The Big Goodbye" },
            new() { Id = Guid.NewGuid(), Title = "I'm Sorry You Went Crazy" },
            new() { Id = Guid.NewGuid(), Title = "A Dog Song" },
            new() { Id = Guid.NewGuid(), Title = "The Plane That Never Lands" }
        };

        var discography = new List<CatalogAlbum>
        {
            StudioEp(),
            LiveAlbum()
        };

        var result = AlbumMatcher.Match("AJR", local, discography, new AlbumMatcherOptions());

        Assert.All(result.Assignments, a => Assert.Equal("What No One's Thinking", a.AlbumTitle));
    }

    private static CatalogAlbum StudioEp()
        => new()
        {
            AlbumId = "ep",
            Title = "What No One's Thinking",
            RecordType = "ep",
            AlbumArtists = ["AJR"],
            Tracks =
            [
                Track("1", "The Plane That Never Lands", 1),
                Track("2", "A Dog Song", 2),
                Track("3", "Betty", 3),
                Track("4", "I'm Sorry You Went Crazy", 4),
                Track("5", "The Big Goodbye", 5)
            ]
        };

    private static CatalogAlbum LiveAlbum()
        => new()
        {
            AlbumId = "live",
            Title = "Live from the Hollywood Bowl",
            RecordType = "album",
            AlbumArtists = ["AJR"],
            Tracks =
            [
                Track("l1", "Way Less Sad (Live from the Hollywood Bowl)", 1),
                Track("l2", "Karma (Live from the Hollywood Bowl)", 2),
                Track("l3", "Yes I'm A Mess (Live from the Hollywood Bowl)", 3),
                Track("l4", "The Good Part (Live from the Hollywood Bowl)", 4),
                Track("l5", "The Big Goodbye (Live from the Hollywood Bowl)", 5),
                Track("l6", "100 Bad Days (Live from the Hollywood Bowl)", 6),
                Track("l7", "Burn The House Down (Live from the Hollywood Bowl)", 7),
                Track("l8", "Bang! (Live from the Hollywood Bowl)", 8),
                Track("l9", "Inertia (Live from the Hollywood Bowl)", 9),
                Track("l10", "World's Smallest Violin (Live from the Hollywood Bowl)", 10),
                Track("l11", "Wow I'm Not Crazy (Live from the Hollywood Bowl)", 11),
                Track("l12", "Betty (Live from the Hollywood Bowl)", 12),
                Track("l13", "A Bunch of Songs We Haven't Played In a Long Time (Live from the Hollywood Bowl)", 13),
                Track("l14", "Steve's Going To London (Live from the Hollywood Bowl)", 14),
                Track("l15", "Sober Up (Live from the Hollywood Bowl)", 15),
                Track("l16", "Weak (Live from the Hollywood Bowl)", 16),
                Track("l17", "Marching Band Finale (Live from the Hollywood Bowl)", 17)
            ]
        };

    private static CatalogTrack Track(string id, string title, int pos)
        => new() { TrackId = id, Title = title, TrackPosition = pos };
}
