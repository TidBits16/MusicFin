using Jellyfin.Plugin.DeezerTagger;
using Xunit;

namespace Jellyfin.Plugin.DeezerTagger.Tests;

public class AlbumMatcherTests
{
    [Fact]
    public void CombinedAlbumWinsWhenLocalTracksSpanBothAlbums()
    {
        var local = new List<LocalTrack>
        {
            Track("mary-1", "All That And More"),
            Track("mary-2", "Mary"),
            Track("mary-3", "Hey Pretty Momma"),
            Track("seven-1", "Devil Like Me"),
            Track("seven-2", "Seven"),
            Track("seven-3", "Mr. Redundant"),
            Track("seven-4", "Folk Machine"),
            Track("seven-5", "Goodnight Chicago"),
            Track("seven-6", "Wasted")
        };

        var albums = new List<CatalogAlbum>
        {
            Album("Mary", 1,
                "All That And More", "Mary", "Hey Pretty Momma", "Black and White"),
            Album("Seven", 2,
                "Devil Like Me", "Seven", "Mr. Redundant", "Folk Machine", "Goodnight Chicago", "Wasted"),
            Album("Seven + Mary", 3,
                "Devil Like Me", "Seven", "Mr. Redundant", "Folk Machine", "Goodnight Chicago", "Wasted",
                "All That And More", "Mary", "Hey Pretty Momma", "Black and White")
        };

        var result = AlbumMatcher.Match("Rainbow Kitten Surprise", local, albums, new AlbumMatcherOptions());

        Assert.All(result.Assignments, a => Assert.Equal("Seven + Mary", a.AlbumTitle));
        Assert.Equal(0, result.SingleReleaseCount);
        Assert.Equal(0, result.UnmatchedCount);
        Assert.Equal(9, result.Assignments.Count);
    }

    [Fact]
    public void SpecificAlbumWinsTieWhenOnlyOneDiscOwned()
    {
        var local = new List<LocalTrack>
        {
            Track("s1", "Devil Like Me"),
            Track("s2", "Seven"),
            Track("s3", "Mr. Redundant"),
            Track("s4", "Folk Machine")
        };

        var albums = new List<CatalogAlbum>
        {
            Album("Seven", 1, "Devil Like Me", "Seven", "Mr. Redundant", "Folk Machine"),
            Album("Seven + Mary", 2,
                "Devil Like Me", "Seven", "Mr. Redundant", "Folk Machine",
                "All That And More", "Mary", "Hey Pretty Momma", "Black and White")
        };

        var result = AlbumMatcher.Match("Rainbow Kitten Surprise", local, albums, new AlbumMatcherOptions());

        Assert.All(result.Assignments, a => Assert.Equal("Seven", a.AlbumTitle));
        Assert.Equal(0, result.SingleReleaseCount);
        Assert.Equal(0, result.UnmatchedCount);
    }

    [Fact]
    public void UnmatchedTracksAreLeftUnassigned()
    {
        var local = new List<LocalTrack>
        {
            Track("a1", "Devil Like Me"),
            Track("a2", "Seven"),
            Track("u1", "Mystery Bootleg Live 2099")
        };

        var albums = new List<CatalogAlbum>
        {
            Album("Seven", 1, "Devil Like Me", "Seven", "Mr. Redundant")
        };

        var result = AlbumMatcher.Match("Rainbow Kitten Surprise", local, albums, new AlbumMatcherOptions());

        Assert.Equal(2, result.Assignments.Count);
        Assert.Equal(1, result.UnmatchedCount);
        Assert.DoesNotContain(result.Assignments, a => a.TrackTitle == "Mystery Bootleg Live 2099");
    }

    [Fact]
    public void LeftoverTrackUsesDeezerSingleReleaseTitle()
    {
        var local = new List<LocalTrack>
        {
            Track("album-1", "Devil Like Me"),
            Track("single-1", "Sober")
        };

        var albums = new List<CatalogAlbum>
        {
            Album("Seven", 1, "Devil Like Me", "Seven", "Mr. Redundant"),
            Single("Sober", 2, "Sober")
        };

        var result = AlbumMatcher.Match("Rainbow Kitten Surprise", local, albums, new AlbumMatcherOptions());

        var single = Assert.Single(result.Assignments, a => a.IsSingleRelease);
        Assert.Equal("Sober", single.AlbumTitle);
        Assert.Equal(1, single.TrackNumber);
        Assert.Equal(1, result.SingleReleaseCount);
        Assert.Equal(0, result.UnmatchedCount);
    }

    [Fact]
    public void OneOffSongPrefersStudioAlbumOverVariousArtistsCompilation()
    {
        var local = new List<LocalTrack>
        {
            Track("one", "That One Cool Song")
        };

        var albums = new List<CatalogAlbum>
        {
            new()
            {
                AlbumId = "1",
                Title = "Best Pop Songs of 2005!",
                RecordType = "compilation",
                AlbumArtists = ["Various Artists"],
                Tracks = Enumerable.Range(1, 20).Select(i => new CatalogTrack
                {
                    Title = i == 7 ? "That One Cool Song" : "Filler Track " + i,
                    TrackPosition = i,
                    TrackId = (100 + i).ToString()
                }).ToList()
            },
            new()
            {
                AlbumId = "2",
                Title = "Cool Album",
                RecordType = "album",
                AlbumArtists = ["Cool Band"],
                Tracks =
                [
                    new CatalogTrack { Title = "Intro", TrackPosition = 1, TrackId = "201" },
                    new CatalogTrack { Title = "Another Song", TrackPosition = 2, TrackId = "202" },
                    new CatalogTrack { Title = "Yet Another", TrackPosition = 3, TrackId = "203" },
                    new CatalogTrack { Title = "Interlude", TrackPosition = 4, TrackId = "204" },
                    new CatalogTrack { Title = "That One Cool Song", TrackPosition = 5, TrackId = "205" }
                ]
            }
        };

        var owned = albums.Where(a => CatalogFilters.IsOwnedByArtist("Cool Band", a)).ToList();
        Assert.Single(owned);
        Assert.Equal("Cool Album", owned[0].Title);

        var result = AlbumMatcher.Match("Cool Band", local, owned, new AlbumMatcherOptions());
        var assignment = Assert.Single(result.Assignments);
        Assert.Equal("Cool Album", assignment.AlbumTitle);
        Assert.Equal(5, assignment.TrackNumber);
        Assert.Equal(0, result.SingleReleaseCount);
        Assert.Equal(0, result.UnmatchedCount);
    }

    [Fact]
    public void StudioAlbumBeatsArtistCompilationForSingleOwnedTrack()
    {
        var local = new List<LocalTrack>
        {
            Track("one", "Hit Single")
        };

        var albums = new List<CatalogAlbum>
        {
            AlbumWithType("Greatest Hits", 1, "compilation", "Pop Star",
                "Hit Single", "Other Hit", "Third Hit", "Fourth Hit", "Fifth Hit"),
            AlbumWithType("Debut", 2, "album", "Pop Star",
                "Opener", "Hit Single", "Closer")
        };

        var result = AlbumMatcher.Match("Pop Star", local, albums, new AlbumMatcherOptions());
        var assignment = Assert.Single(result.Assignments);
        Assert.Equal("Debut", assignment.AlbumTitle);
        Assert.Equal(2, assignment.TrackNumber);
    }

    [Fact]
    public void AssignsCatalogTrackPosition()
    {
        var local = new List<LocalTrack>
        {
            Track("t1", "Devil Like Me"),
            Track("t2", "Seven")
        };

        var albums = new List<CatalogAlbum>
        {
            new()
            {
                AlbumId = "1",
                Title = "Seven",
                Tracks =
                [
                    new CatalogTrack { Title = "Devil Like Me", TrackPosition = 1, TrackId = "101" },
                    new CatalogTrack { Title = "Seven", TrackPosition = 2, TrackId = "102" }
                ]
            }
        };

        var result = AlbumMatcher.Match("Rainbow Kitten Surprise", local, albums, new AlbumMatcherOptions());

        var devil = result.Assignments.Single(a => a.TrackTitle == "Devil Like Me");
        var seven = result.Assignments.Single(a => a.TrackTitle == "Seven");
        Assert.Equal(1, devil.TrackNumber);
        Assert.Equal(2, seven.TrackNumber);
        Assert.Equal("101", devil.ProviderTrackId);
    }

    [Fact]
    public void SingleReleaseCarriesAlbumYear()
    {
        var local = new List<LocalTrack>
        {
            Track("s1", "Sober")
        };

        var albums = new List<CatalogAlbum>
        {
            new()
            {
                AlbumId = "2",
                Title = "Sober",
                RecordType = "single",
                AlbumArtists = ["Rainbow Kitten Surprise"],
                ReleaseDate = new DateTime(2019, 3, 15),
                Tracks =
                [
                    new CatalogTrack { Title = "Sober", TrackPosition = 1, TrackId = "201" }
                ]
            }
        };

        var result = AlbumMatcher.Match("Rainbow Kitten Surprise", local, albums, new AlbumMatcherOptions());
        var assignment = Assert.Single(result.Assignments);

        Assert.Equal(2019, assignment.Year);
        Assert.True(assignment.IsSingleRelease);
    }

    private static LocalTrack Track(string suffix, string title)
    {
        _ = suffix;
        return new LocalTrack { Id = Guid.NewGuid(), Title = title };
    }

    [Fact]
    public void Match_CopiesTrackAndAlbumArtistsFromCatalog()
    {
        var local = new List<LocalTrack> { Track("t1", "Airbag") };
        var albums = new List<CatalogAlbum>
        {
            new()
            {
                AlbumId = "1",
                Title = "OK Computer",
                RecordType = "album",
                AlbumArtists = ["Radiohead"],
                Tracks =
                [
                    new CatalogTrack
                    {
                        Title = "Airbag",
                        TrackPosition = 1,
                        TrackId = "101",
                        Artists = ["Radiohead", "Guest"]
                    }
                ]
            }
        };

        var result = AlbumMatcher.Match("Radiohead", local, albums, new AlbumMatcherOptions());
        var assignment = Assert.Single(result.Assignments);
        Assert.Equal(["Radiohead", "Guest"], assignment.TrackArtists);
        Assert.Equal(["Radiohead"], assignment.AlbumArtists);
    }

    private static CatalogAlbum Single(string title, int id, params string[] trackTitles)
        => new()
        {
            AlbumId = id.ToString(),
            Title = title,
            RecordType = "single",
            AlbumArtists = ["Rainbow Kitten Surprise"],
            Genres = ["Indie Rock"],
            Tracks = trackTitles.Select((t, i) => new CatalogTrack
            {
                Title = t,
                TrackPosition = i + 1,
                TrackId = (id * 100 + i + 1).ToString()
            }).ToList()
        };

    private static CatalogAlbum Album(string title, int id, params string[] trackTitles)
        => new()
        {
            AlbumId = id.ToString(),
            Title = title,
            RecordType = "album",
            AlbumArtists = ["Rainbow Kitten Surprise"],
            Genres = ["Indie Rock"],
            Tracks = trackTitles.Select((t, i) => new CatalogTrack
            {
                Title = t,
                TrackPosition = i + 1,
                TrackId = (id * 100 + i + 1).ToString()
            }).ToList()
        };

    private static CatalogAlbum AlbumWithType(
        string title,
        int id,
        string recordType,
        string artist,
        params string[] trackTitles)
        => new()
        {
            AlbumId = id.ToString(),
            Title = title,
            RecordType = recordType,
            AlbumArtists = [artist],
            Genres = ["Indie Rock"],
            Tracks = trackTitles.Select((t, i) => new CatalogTrack
            {
                Title = t,
                TrackPosition = i + 1,
                TrackId = (id * 100 + i + 1).ToString()
            }).ToList()
        };
}
