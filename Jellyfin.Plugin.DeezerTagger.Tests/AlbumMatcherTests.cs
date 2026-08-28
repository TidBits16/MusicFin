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
            Track("mary-4", "Black and White"),
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
        Assert.Equal(10, result.Assignments.Count);
    }

    [Fact]
    public void StudioAlbumBeatsSprawlingCollectionDespiteFewerLibraryHits()
    {
        var local = new List<LocalTrack>
        {
            Track("e1", "I just need U."),
            Track("e2", "Overflow"),
            Track("e3", "Edge of My Seat"),
            Track("e4", "scars"),
            Track("e5", "Everything"),
            Track("e6", "The Element"),
            Track("e7", "Horizon"),
            Track("e8", "See You Again"),
            Track("e9", "Starts With Me"),
            Track("e10", "It's All About You"),
            Track("e11", "Outro"),
            Track("hits1", "Speak Life"),
            Track("hits2", "Feel It"),
            Track("hits3", "City on Our Knees"),
            Track("hits4", "Made to Love"),
            Track("hits5", "Me Without You"),
            Track("hits6", "Steal My Show"),
            Track("hits7", "Irene"),
            Track("hits8", "Diverse City")
        };

        var albums = new List<CatalogAlbum>
        {
            AlbumWithType("The Elements", 1, "album", "tobyMac",
                "I just need U.", "Overflow", "Edge of My Seat", "scars",
                "Everything", "The Element", "Horizon", "See You Again",
                "Starts With Me", "It's All About You", "Outro"),
            AlbumWithType("TobyMac Collection", 2, "album", "tobyMac",
                "I just need U.", "Overflow", "Edge of My Seat", "scars",
                "Speak Life", "Feel It", "City on Our Knees", "Made to Love",
                "Me Without You", "Steal My Show", "Irene", "Diverse City",
                "Catchafire", "Gone", "Somebody's Watching", "Boomin'",
                "Get Back Up", "Lose Myself", "Tonight", "Hold On",
                "One World", "Extreme Days", "J Train", "Irene (Remix)",
                "Extra 1", "Extra 2", "Extra 3", "Extra 4", "Extra 5",
                "Extra 6", "Extra 7", "Extra 8", "Extra 9", "Extra 10",
                "Extra 11", "Extra 12", "Extra 13", "Extra 14", "Extra 15",
                "Extra 16", "Extra 17", "Extra 18", "Extra 19", "Extra 20",
                "Extra 21", "Extra 22", "Extra 23", "Extra 24", "Extra 25",
                "Extra 26", "Extra 27", "Extra 28", "Extra 29", "Extra 30")
        };

        var result = AlbumMatcher.Match("tobyMac", local, albums, new AlbumMatcherOptions());

        Assert.All(
            result.Assignments.Where(a =>
                a.TrackTitle is "I just need U." or "Overflow" or "Edge of My Seat" or "scars"
                    or "Everything" or "The Element" or "Horizon" or "See You Again"
                    or "Starts With Me" or "It's All About You" or "Outro"),
            a => Assert.Equal("The Elements", a.AlbumTitle));
    }

    [Fact]
    public void StudioAlbumBeatsTourSetAndFullyOwnedEpOnFitness()
    {
        var local = new List<LocalTrack>
        {
            Track("1", "Song A"),
            Track("2", "Song B"),
            Track("3", "Song C"),
            Track("4", "Song D"),
            Track("5", "Song E"),
            Track("6", "Song F"),
            Track("7", "Song G"),
            Track("8", "Song H"),
            Track("9", "Song I"),
            Track("10", "Song J")
        };

        var albums = new List<CatalogAlbum>
        {
            AlbumWithType("Studio Album", 1, "album", "Test Artist",
                "Song A", "Song B", "Song C", "Song D", "Song E",
                "Song F", "Song G", "Song H", "Song I", "Song J"),
            AlbumWithType("Tour Set", 2, "album", "Test Artist",
                "Song A", "Song B", "Song C", "Song D", "Song E",
                "Song F", "Song G", "Song H", "Song I", "Song J",
                "Live Extra 1", "Live Extra 2", "Live Extra 3", "Live Extra 4",
                "Live Extra 5", "Live Extra 6", "Live Extra 7", "Live Extra 8",
                "Live Extra 9", "Live Extra 10"),
            AlbumWithType("Early EP", 3, "ep", "Test Artist",
                "Song A", "Song B", "Song C", "Song D")
        };

        var result = AlbumMatcher.Match("Test Artist", local, albums, new AlbumMatcherOptions());

        Assert.All(result.Assignments, a => Assert.Equal("Studio Album", a.AlbumTitle));
        Assert.Equal(10, result.Assignments.Count);
    }

    [Fact]
    public void FullyOwnedEpBeatsHalfCoveredTourSet()
    {
        var local = new List<LocalTrack>
        {
            Track("1", "Song A"),
            Track("2", "Song B"),
            Track("3", "Song C"),
            Track("4", "Song D")
        };

        var albums = new List<CatalogAlbum>
        {
            AlbumWithType("Early EP", 1, "ep", "Test Artist",
                "Song A", "Song B", "Song C", "Song D"),
            AlbumWithType("Tour Set", 2, "album", "Test Artist",
                "Song A", "Song B", "Song C", "Song D", "Song E",
                "Song F", "Song G", "Song H", "Song I", "Song J",
                "Live Extra 1", "Live Extra 2", "Live Extra 3", "Live Extra 4",
                "Live Extra 5", "Live Extra 6", "Live Extra 7", "Live Extra 8",
                "Live Extra 9", "Live Extra 10")
        };

        var result = AlbumMatcher.Match("Test Artist", local, albums, new AlbumMatcherOptions());

        Assert.All(result.Assignments, a => Assert.Equal("Early EP", a.AlbumTitle));
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
    public void SingleReleaseBeatsLargeAlbumWhenCoverageRatioIsHigher()
    {
        var local = new List<LocalTrack>
        {
            Track("live-1", "FNAFdom (Live)")
        };

        var albums = new List<CatalogAlbum>
        {
            AlbumWithType("zero_one:reloaded", 1, "album", "The Living Tombstone",
                "FNAFdom (Live)", "Drink My Water (Live)", "What I Want (Live)", "Drunk (Live)",
                "In the Land of Gods and Monsters (Live)", "Orphans (Live)", "Fly Home (Live)",
                "My Ordinary Life (Live)", "Sunburn (Live)", "Misplaced (Live)",
                "I Can't Fix You (Live)", "It's Been So Long (Live)", "Die In A Fire (Live)",
                "Five Nights at Freddy's (Live)", "Step On Up (Live)",
                "This Comes From Inside (Live)", "I Got No Time (Live)", "Other Live Cut",
                "Bonus Live"),
            new()
            {
                AlbumId = "2",
                Title = "FNAFdom (Live)",
                RecordType = "single",
                AlbumArtists = ["The Living Tombstone"],
                Tracks =
                [
                    new CatalogTrack { Title = "FNAFdom (Live)", TrackPosition = 1, TrackId = "201" }
                ]
            }
        };

        var result = AlbumMatcher.Match("The Living Tombstone", local, albums, new AlbumMatcherOptions());
        var assignment = Assert.Single(result.Assignments);
        Assert.Equal("FNAFdom (Live)", assignment.AlbumTitle);
        Assert.True(assignment.IsSingleRelease);
        Assert.Equal(1, assignment.TrackNumber);
    }

    [Fact]
    public void FullLiveAlbumBeatsPerTrackSinglesWhenCoverageTiesAt100Percent()
    {
        var local = new List<LocalTrack>
        {
            Track("1", "FNAFdom (Live)"),
            Track("2", "Drink My Water (Live)")
        };

        var albums = new List<CatalogAlbum>
        {
            new()
            {
                AlbumId = "ep",
                Title = "FNAFdom (Live)",
                RecordType = "ep",
                AlbumArtists = ["The Living Tombstone"],
                Tracks =
                [
                    new CatalogTrack { Title = "FNAFdom (Live)", TrackPosition = 1, TrackId = "e1" },
                    new CatalogTrack { Title = "Drink My Water (Live)", TrackPosition = 2, TrackId = "e2" }
                ]
            },
            new()
            {
                AlbumId = "s1",
                Title = "FNAFdom (Live)",
                RecordType = "single",
                AlbumArtists = ["The Living Tombstone"],
                Tracks =
                [
                    new CatalogTrack { Title = "FNAFdom (Live)", TrackPosition = 1, TrackId = "s1t" }
                ]
            },
            new()
            {
                AlbumId = "s2",
                Title = "Drink My Water (Live)",
                RecordType = "single",
                AlbumArtists = ["The Living Tombstone"],
                Tracks =
                [
                    new CatalogTrack { Title = "Drink My Water (Live)", TrackPosition = 1, TrackId = "s2t" }
                ]
            }
        };

        var result = AlbumMatcher.Match("The Living Tombstone", local, albums, new AlbumMatcherOptions());
        Assert.All(result.Assignments, a => Assert.Equal("FNAFdom (Live)", a.AlbumTitle));
        Assert.Equal(0, result.SingleReleaseCount);
    }

    [Fact]
    public void RegularAlbumBeatsDeluxeWhenBonusTrackIsOwnedAsSingle()
    {
        var local = new List<LocalTrack>
        {
            Track("1", "Dreamland"),
            Track("2", "Tangerine"),
            Track("3", "Hot Sugar"),
            Track("4", "Heat Waves"),
            Track("5", "Helium"),
            Track("6", "I Don't Wanna Talk (I Just Wanna Dance)")
        };

        var albums = new List<CatalogAlbum>
        {
            AlbumWithType("Dreamland", 1, "album", "Glass Animals",
                "Dreamland", "Tangerine", "Hot Sugar", "Heat Waves", "Helium"),
            AlbumWithType("Dreamland (+ Bonus Levels 2.0)", 2, "album", "Glass Animals",
                "Dreamland", "Tangerine", "Hot Sugar", "Heat Waves", "Helium",
                "I Don't Wanna Talk (I Just Wanna Dance)"),
            new()
            {
                AlbumId = "3",
                Title = "I Don't Wanna Talk (I Just Wanna Dance)",
                RecordType = "single",
                AlbumArtists = ["Glass Animals"],
                Tracks =
                [
                    new CatalogTrack
                    {
                        Title = "I Don't Wanna Talk (I Just Wanna Dance)",
                        TrackPosition = 1,
                        TrackId = "301"
                    }
                ]
            }
        };

        var result = AlbumMatcher.Match("Glass Animals", local, albums, new AlbumMatcherOptions());

        Assert.Equal(0, result.UnmatchedCount);
        Assert.All(
            result.Assignments.Where(a => a.TrackTitle != "I Don't Wanna Talk (I Just Wanna Dance)"),
            a => Assert.Equal("Dreamland", a.AlbumTitle));
        var single = Assert.Single(result.Assignments, a => a.TrackTitle == "I Don't Wanna Talk (I Just Wanna Dance)");
        Assert.Equal("I Don't Wanna Talk (I Just Wanna Dance)", single.AlbumTitle);
        Assert.True(single.IsSingleRelease);
    }

    [Fact]
    public void RegularAlbumBeatsDeluxeWhenOnlyBaseTracksOwned()
    {
        var local = new List<LocalTrack>
        {
            Track("1", "Dreamland"),
            Track("2", "Tangerine"),
            Track("3", "Hot Sugar"),
            Track("4", "Heat Waves"),
            Track("5", "Helium")
        };

        var albums = new List<CatalogAlbum>
        {
            AlbumWithType("Dreamland", 1, "album", "Glass Animals",
                "Dreamland", "Tangerine", "Hot Sugar", "Heat Waves", "Helium"),
            AlbumWithType("Dreamland (+ Bonus Levels 2.0)", 2, "album", "Glass Animals",
                "Dreamland", "Tangerine", "Hot Sugar", "Heat Waves", "Helium",
                "I Don't Wanna Talk (I Just Wanna Dance)")
        };

        var result = AlbumMatcher.Match("Glass Animals", local, albums, new AlbumMatcherOptions());

        Assert.All(result.Assignments, a => Assert.Equal("Dreamland", a.AlbumTitle));
        Assert.Equal(0, result.SingleReleaseCount);
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
