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
