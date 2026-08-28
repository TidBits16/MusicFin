using Jellyfin.Plugin.DeezerTagger;
using Xunit;

namespace Jellyfin.Plugin.DeezerTagger.Tests;

public class RksMarySevenMatcherTests
{
    [Fact]
    public void MusicBrainzCatalog_SevenPlusMaryWinsOverEps()
    {
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
        var discography = new List<CatalogAlbum>
        {
            MbAlbum("Mary", "8ee338b5-380a-4541-8728-450d3fa63a19", "ep",
                "All That And More", "Hey Pretty Momma", "Black and White", "That's My Shit"),
            MbAlbum("Seven", "afdca811-5150-4e1a-b5f8-a439ed4c8f75", "ep",
                "Fail!", "Mr. Redundant", "First Class", "Shameful Company", "Seven", "Devil Like Me", "American Hero"),
            MbAlbum("Seven + Mary", "e29b63e5-f93e-46e0-8d29-3e7c86d390d8", "album",
                "Fail!", "Mr. Redundant", "First Class", "Shameful Company", "Seven", "Devil Like Me", "American Hero",
                "All That and More (Sailboat)", "Hey Pretty Momma", "Black and White", "That's My Shit")
        };

        var result = AlbumMatcher.Match("Rainbow Kitten Surprise", local, discography, new AlbumMatcherOptions());

        Assert.Equal(0, result.UnmatchedCount);
        Assert.All(result.Assignments, a => Assert.Equal("Seven + Mary", a.AlbumTitle));
        Assert.Equal(Enumerable.Range(1, 11), result.Assignments.Select(a => a.TrackNumber).OrderBy(x => x));
    }

    [Fact]
    public void MusicBrainzCatalog_WhenCombinedMissing_SplitsAcrossEps()
    {
        var localTitles = new[]
        {
            "Fail!",
            "Mr. Redundant",
            "All That And More",
            "Hey Pretty Momma",
            "Seven",
            "Devil Like Me"
        };

        var local = localTitles.Select(t => new LocalTrack { Id = Guid.NewGuid(), Title = t }).ToList();
        var discography = new List<CatalogAlbum>
        {
            MbAlbum("Mary", "mary", "ep", "All That And More", "Hey Pretty Momma", "Black and White", "That's My Shit"),
            MbAlbum("Seven", "seven", "ep", "Fail!", "Mr. Redundant", "First Class", "Shameful Company", "Seven", "Devil Like Me", "American Hero")
        };

        var result = AlbumMatcher.Match("Rainbow Kitten Surprise", local, discography, new AlbumMatcherOptions());

        Assert.Equal(0, result.UnmatchedCount);
        Assert.Contains(result.Assignments, a => a.AlbumTitle == "Mary" && a.TrackTitle == "All That And More");
        Assert.Contains(result.Assignments, a => a.AlbumTitle == "Seven" && a.TrackTitle == "Fail!");
    }

    [Fact]
    public void ExactStudioTitle_BeatsLiveAlbumEvenWhenLiveHasMoreLibraryHits()
    {
        var local = new List<LocalTrack>
        {
            new() { Id = Guid.NewGuid(), Title = "That's My Shit - Rainbow Kitten Suprise" },
            new() { Id = Guid.NewGuid(), Title = "Mission to Mars" },
            new() { Id = Guid.NewGuid(), Title = "Cocaine Jesus" },
            new() { Id = Guid.NewGuid(), Title = "Hide" },
            new() { Id = Guid.NewGuid(), Title = "Run" },
            new() { Id = Guid.NewGuid(), Title = "Goodnight Chicago" }
        };

        var discography = new List<CatalogAlbum>
        {
            MbAlbum("Seven + Mary", "combo", "album",
                "Fail!", "Mr. Redundant", "First Class", "Shameful Company", "Seven", "Devil Like Me", "American Hero",
                "All That and More (Sailboat)", "Hey Pretty Momma", "Black and White", "That's My Shit"),
            MbAlbum("RKS! Live From Athens Georgia", "live", "album",
                "Mission to Mars (Live from Athens Georgia)",
                "Cocaine Jesus (Live from Athens Georgia)",
                "Hide (Live from Athens Georgia)",
                "Seven (Live from Athens Georgia)",
                "Devil Like Me (Live from Athens Georgia)",
                "That's My Shit (Live from Athens Georgia)",
                "Goodnight Chicago (Live from Athens Georgia)",
                "Run (Live from Athens Georgia)",
                "First Class (Live from Athens Georgia)",
                "Shameful Company (Live from Athens Georgia)")
        };

        var result = AlbumMatcher.Match("Rainbow Kitten Surprise", local, discography, new AlbumMatcherOptions());

        var thatsMyShit = Assert.Single(result.Assignments, a =>
            a.TrackTitle.Contains("That's My Shit", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Seven + Mary", thatsMyShit.AlbumTitle);
        Assert.Equal(11, thatsMyShit.TrackNumber);
    }


    private static CatalogAlbum MbAlbum(string title, string id, string recordType, params string[] tracks)
        => new()
        {
            AlbumId = id,
            Title = title,
            RecordType = recordType,
            AlbumArtists = ["Rainbow Kitten Surprise"],
            Tracks = tracks.Select((t, i) => new CatalogTrack
            {
                Title = t,
                TrackPosition = i + 1,
                TrackId = id + "-" + (i + 1)
            }).ToList()
        };
}
