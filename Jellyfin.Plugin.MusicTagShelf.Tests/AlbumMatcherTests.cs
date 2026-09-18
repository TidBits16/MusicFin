using Jellyfin.Plugin.MusicTagShelf;
using Xunit;

namespace Jellyfin.Plugin.MusicTagShelf.Tests;

public class AlbumMatcherTests
{
    [Fact]
    public void CombinedAlbumWinsWhenBothHalvesShareOneFolder()
    {
        var parent = Guid.NewGuid();
        var local = new List<LocalTrack>
        {
            Track("mary-1", "All That And More", parentAlbumId: parent),
            Track("mary-2", "Mary", parentAlbumId: parent),
            Track("mary-3", "Hey Pretty Momma", parentAlbumId: parent),
            Track("mary-4", "Black and White", parentAlbumId: parent),
            Track("seven-1", "Devil Like Me", parentAlbumId: parent),
            Track("seven-2", "Seven", parentAlbumId: parent),
            Track("seven-3", "Mr. Redundant", parentAlbumId: parent),
            Track("seven-4", "Folk Machine", parentAlbumId: parent),
            Track("seven-5", "Goodnight Chicago", parentAlbumId: parent),
            Track("seven-6", "Wasted", parentAlbumId: parent)
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
    public void SeparateFolders_MaryAndSevenStayAsTheirOwnEps()
    {
        var maryParent = Guid.NewGuid();
        var sevenParent = Guid.NewGuid();
        var local = new List<LocalTrack>
        {
            Track("mary-1", "All That And More", parentAlbumId: maryParent),
            Track("mary-2", "Mary", parentAlbumId: maryParent),
            Track("mary-3", "Hey Pretty Momma", parentAlbumId: maryParent),
            Track("mary-4", "Black and White", parentAlbumId: maryParent),
            Track("seven-1", "Devil Like Me", parentAlbumId: sevenParent),
            Track("seven-2", "Seven", parentAlbumId: sevenParent),
            Track("seven-3", "Mr. Redundant", parentAlbumId: sevenParent),
            Track("seven-4", "Folk Machine", parentAlbumId: sevenParent),
            Track("seven-5", "Goodnight Chicago", parentAlbumId: sevenParent),
            Track("seven-6", "Wasted", parentAlbumId: sevenParent)
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

        Assert.Equal(10, result.Assignments.Count);
        Assert.All(
            result.Assignments.Where(a => a.TrackTitle is "All That And More" or "Mary" or "Hey Pretty Momma" or "Black and White"),
            a => Assert.Equal("Mary", a.AlbumTitle));
        Assert.All(
            result.Assignments.Where(a => a.TrackTitle is "Devil Like Me" or "Seven" or "Mr. Redundant" or "Folk Machine" or "Goodnight Chicago" or "Wasted"),
            a => Assert.Equal("Seven", a.AlbumTitle));
        Assert.DoesNotContain(result.Assignments, a => a.AlbumTitle == "Seven + Mary");
    }

    [Fact]
    public void StudioAlbumBeatsSprawlingCollectionDespiteFewerLibraryHits()
    {
        var parent = Guid.NewGuid();
        var local = new List<LocalTrack>
        {
            Track("e1", "I just need U.", parentAlbumId: parent),
            Track("e2", "Overflow", parentAlbumId: parent),
            Track("e3", "Edge of My Seat", parentAlbumId: parent),
            Track("e4", "scars", parentAlbumId: parent),
            Track("e5", "Everything", parentAlbumId: parent),
            Track("e6", "The Element", parentAlbumId: parent),
            Track("e7", "Horizon", parentAlbumId: parent),
            Track("e8", "See You Again", parentAlbumId: parent),
            Track("e9", "Starts With Me", parentAlbumId: parent),
            Track("e10", "It's All About You", parentAlbumId: parent),
            Track("e11", "Outro", parentAlbumId: parent),
            Track("hits1", "Speak Life", parentAlbumId: parent),
            Track("hits2", "Feel It", parentAlbumId: parent),
            Track("hits3", "City on Our Knees", parentAlbumId: parent),
            Track("hits4", "Made to Love", parentAlbumId: parent),
            Track("hits5", "Me Without You", parentAlbumId: parent),
            Track("hits6", "Steal My Show", parentAlbumId: parent),
            Track("hits7", "Irene", parentAlbumId: parent),
            Track("hits8", "Diverse City", parentAlbumId: parent)
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
        var parent = Guid.NewGuid();
        var local = new List<LocalTrack>
        {
            Track("1", "Song A", parentAlbumId: parent),
            Track("2", "Song B", parentAlbumId: parent),
            Track("3", "Song C", parentAlbumId: parent),
            Track("4", "Song D", parentAlbumId: parent),
            Track("5", "Song E", parentAlbumId: parent),
            Track("6", "Song F", parentAlbumId: parent),
            Track("7", "Song G", parentAlbumId: parent),
            Track("8", "Song H", parentAlbumId: parent),
            Track("9", "Song I", parentAlbumId: parent),
            Track("10", "Song J", parentAlbumId: parent)
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
                    new CatalogTrack { Title = "Devil Like Me", TrackPosition = 1, DiskNumber = 1, TrackId = "101" },
                    new CatalogTrack { Title = "Seven", TrackPosition = 2, DiskNumber = 1, TrackId = "102" }
                ]
            }
        };

        var result = AlbumMatcher.Match("Rainbow Kitten Surprise", local, albums, new AlbumMatcherOptions());

        var devil = result.Assignments.Single(a => a.TrackTitle == "Devil Like Me");
        var seven = result.Assignments.Single(a => a.TrackTitle == "Seven");
        Assert.Equal(1, devil.TrackNumber);
        Assert.Equal(1, devil.DiscNumber);
        Assert.Equal(2, seven.TrackNumber);
        Assert.Equal(1, seven.DiscNumber);
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

    [Fact]
    public void StudioSinglesBeatLiveTourAlbum_WhenCatalogHasLiveSuffixTracks()
    {
        var local = new List<LocalTrack>
        {
            Track("1", "Step On Up", "Step On Up"),
            Track("2", "I Got No Time", "I Got No Time"),
            Track("3", "Die In A Fire", "Die In A Fire")
        };

        var albums = new List<CatalogAlbum>
        {
            AlbumWithType("Step On Up", 1, "single", "The Living Tombstone", "Step On Up"),
            AlbumWithType("I Got No Time", 2, "single", "The Living Tombstone", "I Got No Time"),
            AlbumWithType("Die In A Fire", 3, "single", "The Living Tombstone", "Die In A Fire"),
            AlbumWithType(
                "Live in '25",
                4,
                "album",
                "The Living Tombstone",
                "Step On Up (Live)",
                "I Got No Time (Live)",
                "Die In A Fire (Live)",
                "Five Nights at Freddy's (Live)",
                "It's Been So Long (Live)",
                "My Ordinary Life (Live)",
                "This Comes From Inside (Live)",
                "I Can't Fix You (Live)")
        };

        var result = AlbumMatcher.Match("The Living Tombstone", local, albums, new AlbumMatcherOptions());

        Assert.Equal(3, result.Assignments.Count);
        Assert.DoesNotContain(result.Assignments, a => a.AlbumTitle == "Live in '25");
        Assert.Equal("Step On Up", result.Assignments.Single(a => a.TrackTitle == "Step On Up").AlbumTitle);
        Assert.Equal("I Got No Time", result.Assignments.Single(a => a.TrackTitle == "I Got No Time").AlbumTitle);
        Assert.Equal("Die In A Fire", result.Assignments.Single(a => a.TrackTitle == "Die In A Fire").AlbumTitle);
    }

    [Fact]
    public void StudioSinglesBeatLiveTourAlbum_EvenWhenLocalAlbumAlreadyRenamedToLive()
    {
        // Recovery path: album names already wrong, track titles still studio.
        var local = new List<LocalTrack>
        {
            Track("1", "Step On Up", "Live in '25"),
            Track("2", "I Got No Time", "Live in '25")
        };

        var albums = new List<CatalogAlbum>
        {
            AlbumWithType("Step On Up", 1, "single", "The Living Tombstone", "Step On Up"),
            AlbumWithType("I Got No Time", 2, "single", "The Living Tombstone", "I Got No Time"),
            AlbumWithType(
                "Live in '25",
                3,
                "album",
                "The Living Tombstone",
                "Step On Up (Live)",
                "I Got No Time (Live)",
                "Die In A Fire (Live)",
                "Five Nights at Freddy's (Live)")
        };

        var result = AlbumMatcher.Match("The Living Tombstone", local, albums, new AlbumMatcherOptions());

        Assert.DoesNotContain(result.Assignments, a => a.AlbumTitle == "Live in '25");
        Assert.Equal("Step On Up", result.Assignments.Single(a => a.TrackTitle == "Step On Up").AlbumTitle);
        Assert.Equal("I Got No Time", result.Assignments.Single(a => a.TrackTitle == "I Got No Time").AlbumTitle);
    }

    [Fact]
    public void DistinctNearDuplicateAlbumTitles_StayDistinct_WhenLocalAlbumTagsDiffer()
    {
        var local = new List<LocalTrack>
        {
            Track("t1", "THE ANTIHUMAN", "THE ANTIHUMAN"),
            Track("t2", "THE ANTIHUMAN - Instrumental", "THE ANTIHUMAN"),
            Track("a1", "ANTIHUMAN (feat. Stephanafro)", "ANTIHUMAN"),
            Track("a2", "ANTIHUMAN (Instrumental)", "ANTIHUMAN")
        };

        var albums = new List<CatalogAlbum>
        {
            AlbumWithType(
                "THE ANTIHUMAN",
                1,
                "album",
                "ivycomb",
                "THE ANTIHUMAN",
                "THE ANTIHUMAN - Instrumental"),
            AlbumWithType(
                "ANTIHUMAN",
                2,
                "album",
                "ivycomb",
                "ANTIHUMAN (feat. Stephanafro)",
                "ANTIHUMAN (Instrumental)")
        };

        var result = AlbumMatcher.Match("ivycomb", local, albums, new AlbumMatcherOptions());

        Assert.All(
            result.Assignments.Where(a => a.TrackTitle.StartsWith("THE ANTIHUMAN", StringComparison.Ordinal)),
            a => Assert.Equal("THE ANTIHUMAN", a.AlbumTitle));
        Assert.All(
            result.Assignments.Where(a => a.TrackTitle.StartsWith("ANTIHUMAN", StringComparison.Ordinal)),
            a => Assert.Equal("ANTIHUMAN", a.AlbumTitle));
    }

    [Fact]
    public void StudioAlbumBeatsSameTitledSingle_WhenAlbumIsWellCovered()
    {
        var local = new List<LocalTrack>
        {
            Track("1", "brutal", "SOUR"),
            Track("2", "traitor", "SOUR"),
            Track("3", "drivers license", "SOUR"),
            Track("4", "deja vu", "SOUR"),
            Track("5", "good 4 u", "SOUR")
        };

        var albums = new List<CatalogAlbum>
        {
            AlbumWithType(
                "SOUR",
                1,
                "album",
                "Olivia Rodrigo",
                "brutal", "traitor", "drivers license", "1 step forward, 3 steps back",
                "deja vu", "good 4 u", "enough for you", "happier",
                "jealousy, jealousy", "favorite crime", "hope ur ok"),
            AlbumWithType("drivers license", 2, "single", "Olivia Rodrigo", "drivers license"),
            AlbumWithType("good 4 u", 3, "single", "Olivia Rodrigo", "good 4 u"),
            AlbumWithType("traitor", 4, "single", "Olivia Rodrigo", "traitor")
        };

        var result = AlbumMatcher.Match("Olivia Rodrigo", local, albums, new AlbumMatcherOptions());

        Assert.Equal(5, result.Assignments.Count);
        Assert.All(result.Assignments, a => Assert.Equal("SOUR", a.AlbumTitle));
        Assert.Equal(0, result.SingleReleaseCount);
    }

    [Fact]
    public void StandardAlbumBeatsDeluxeSpilledEdition()
    {
        var local = new List<LocalTrack>
        {
            Track("1", "vampire", "GUTS"),
            Track("2", "bad idea right?", "GUTS"),
            Track("3", "get him back!", "GUTS"),
            Track("4", "teenage dream", "GUTS")
        };

        var albums = new List<CatalogAlbum>
        {
            AlbumWithType(
                "GUTS",
                1,
                "album",
                "Olivia Rodrigo",
                "all-american bitch", "bad idea right?", "vampire", "lacy",
                "ballad of a homeschooled girl", "making the bed", "logical", "get him back!",
                "love is embarrassing", "the grudge", "pretty isn't pretty", "teenage dream"),
            AlbumWithType(
                "GUTS (spilled)",
                2,
                "album",
                "Olivia Rodrigo",
                "all-american bitch", "bad idea right?", "vampire", "lacy",
                "ballad of a homeschooled girl", "making the bed", "logical", "get him back!",
                "love is embarrassing", "the grudge", "pretty isn't pretty", "teenage dream",
                "obsessed", "girl i've always been", "scared of my guitar", "stranger", "so american")
        };

        var result = AlbumMatcher.Match("Olivia Rodrigo", local, albums, new AlbumMatcherOptions());

        Assert.All(result.Assignments, a => Assert.Equal("GUTS", a.AlbumTitle));
    }

    [Fact]
    public void OriginalSinglesBeatClassicsCollectionCompilation()
    {
        var local = new List<LocalTrack>
        {
            Track("1", "INFAMOUS", "INFAMOUS"),
            Track("2", "LDR", "LDR"),
            Track("3", "Blue Bird", "Blue Bird"),
            Track("4", "ANTIHUMAN", "ANTIHUMAN")
        };

        var albums = new List<CatalogAlbum>
        {
            AlbumWithType("INFAMOUS", 1, "single", "ivycomb", "INFAMOUS"),
            AlbumWithType("LDR", 2, "single", "ivycomb", "LDR"),
            AlbumWithType("Blue Bird", 3, "single", "ivycomb", "Blue Bird"),
            AlbumWithType("ANTIHUMAN", 4, "single", "ivycomb", "ANTIHUMAN"),
            AlbumWithType(
                "Classics Collection",
                5,
                "album",
                "ivycomb",
                "INFAMOUS", "FALSE IDOL", "LDR", "DATA_REJECT", "LUMINESCENCE",
                "NEVERLAND", "SUN SPOTS", "ANTIHUMAN", "TOKYO", "Soul Astray",
                "ANTIVILLAIN", "MAKE BELIEVE", "Fratricide", "Blue Bird")
        };

        var result = AlbumMatcher.Match("ivycomb", local, albums, new AlbumMatcherOptions());

        Assert.DoesNotContain(result.Assignments, a => a.AlbumTitle.Contains("Classics", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("INFAMOUS", result.Assignments.Single(a => a.TrackTitle == "INFAMOUS").AlbumTitle);
        Assert.Equal("LDR", result.Assignments.Single(a => a.TrackTitle == "LDR").AlbumTitle);
        Assert.Equal("Blue Bird", result.Assignments.Single(a => a.TrackTitle == "Blue Bird").AlbumTitle);
        Assert.Equal("ANTIHUMAN", result.Assignments.Single(a => a.TrackTitle == "ANTIHUMAN").AlbumTitle);
    }

    [Fact]
    public void ClassicsCollectionRename_RecoversToOriginalSingles()
    {
        // Local tags already wrong from a prior bad run.
        var local = new List<LocalTrack>
        {
            Track("1", "INFAMOUS", "Classics Collection"),
            Track("2", "LDR", "Classics Collection"),
            Track("3", "Blue Bird", "Classics Collection")
        };

        var albums = new List<CatalogAlbum>
        {
            AlbumWithType("INFAMOUS", 1, "single", "ivycomb", "INFAMOUS"),
            AlbumWithType("LDR", 2, "single", "ivycomb", "LDR"),
            AlbumWithType("Blue Bird", 3, "single", "ivycomb", "Blue Bird"),
            AlbumWithType(
                "Classics Collection",
                4,
                "album",
                "ivycomb",
                "INFAMOUS", "LDR", "Blue Bird", "ANTIHUMAN", "TOKYO", "NEVERLAND")
        };

        var result = AlbumMatcher.Match("ivycomb", local, albums, new AlbumMatcherOptions());

        Assert.DoesNotContain(result.Assignments, a => a.AlbumTitle == "Classics Collection");
        Assert.Equal("INFAMOUS", result.Assignments.Single(a => a.TrackTitle == "INFAMOUS").AlbumTitle);
        Assert.Equal("LDR", result.Assignments.Single(a => a.TrackTitle == "LDR").AlbumTitle);
        Assert.Equal("Blue Bird", result.Assignments.Single(a => a.TrackTitle == "Blue Bird").AlbumTitle);
    }

    [Fact]
    public void BeTheCowboyBeatsTheLandLiveAlbum_EvenWhenLiveHasSharedHits()
    {
        var local = new List<LocalTrack>
        {
            Track("1", "Geyser", "Be the Cowboy"),
            Track("2", "Nobody", "Be the Cowboy"),
            Track("3", "Washing Machine Heart", "Be the Cowboy"),
            Track("4", "Pink in the Night", "Be the Cowboy"),
            Track("5", "A Pearl", "Be the Cowboy"),
            Track("6", "Me and My Husband", "Be the Cowboy")
        };

        var albums = new List<CatalogAlbum>
        {
            AlbumWithType(
                "Be the Cowboy",
                1,
                "album",
                "Mitski",
                "Geyser", "Why Didn't You Stop Me?", "Old Friend", "A Pearl",
                "Lonesome Love", "Remember My Name", "Me and My Husband", "Come into the Water",
                "Nobody", "Pink in the Night", "A Horse Named Cold Air", "Washing Machine Heart",
                "Blue Light", "Two Slow Dancers"),
            AlbumWithType(
                "The Land: The Live Album",
                2,
                "album",
                "Mitski",
                "Everyone", "Buffalo Replaced", "Working for the Knife", "The Deal",
                "Valentine, TX", "I Bet on Losing Dogs", "Thursday Girl / Geyser",
                "First Love / Late Spring", "Star", "Heaven", "I Don't Like My Mind",
                "I Love Me After You", "Happy", "My Love Mine All Mine",
                "Last Words of a Shooting Star", "Pink in the Night", "I Don't Smoke",
                "I'm Your Man", "Fireworks", "Nobody", "Washing Machine Heart"),
            AlbumWithType(
                "The Land Is Inhospitable and So Are We",
                3,
                "album",
                "Mitski",
                "Bug Like an Angel", "Buffalo Replaced", "Heaven", "I Don't Like My Mind",
                "The Deal", "When Memories Snow", "My Love Mine All Mine", "The Frost",
                "Star", "I'm Your Man", "I Love Me After You")
        };

        var result = AlbumMatcher.Match("Mitski", local, albums, new AlbumMatcherOptions());

        Assert.Equal(6, result.Assignments.Count);
        Assert.All(result.Assignments, a => Assert.Equal("Be the Cowboy", a.AlbumTitle));
    }

    [Fact]
    public void BeTheCowboyRecoversFromTheLandLiveAlbumRename()
    {
        var local = new List<LocalTrack>
        {
            Track("1", "Nobody", "The Land: The Live Album"),
            Track("2", "Washing Machine Heart", "The Land: The Live Album"),
            Track("3", "Pink in the Night", "The Land: The Live Album"),
            Track("4", "A Pearl", "The Land: The Live Album")
        };

        var albums = new List<CatalogAlbum>
        {
            AlbumWithType(
                "Be the Cowboy",
                1,
                "album",
                "Mitski",
                "Geyser", "A Pearl", "Nobody", "Pink in the Night", "Washing Machine Heart",
                "Me and My Husband", "Two Slow Dancers"),
            AlbumWithType(
                "The Land: The Live Album",
                2,
                "album",
                "Mitski",
                "Everyone", "Thursday Girl / Geyser", "Pink in the Night", "Nobody",
                "Washing Machine Heart", "My Love Mine All Mine", "Heaven")
        };

        var result = AlbumMatcher.Match("Mitski", local, albums, new AlbumMatcherOptions());

        Assert.All(result.Assignments, a => Assert.Equal("Be the Cowboy", a.AlbumTitle));
    }

    [Fact]
    public void FullStudioAlbumBeatsLeadSingle_OliviaYouSeemPrettySad()
    {
        var local = new List<LocalTrack>
        {
            Track("1", "drop dead", "you seem pretty sad for a girl so in love"),
            Track("2", "stupid song", "you seem pretty sad for a girl so in love"),
            Track("3", "honeybee", "you seem pretty sad for a girl so in love"),
            Track("4", "maggots for brains", "you seem pretty sad for a girl so in love"),
            Track("5", "u + me = <3", "you seem pretty sad for a girl so in love"),
            Track("6", "my way", "you seem pretty sad for a girl so in love"),
            Track("7", "purple", "you seem pretty sad for a girl so in love"),
            Track("8", "the cure", "you seem pretty sad for a girl so in love"),
            Track("9", "begged", "you seem pretty sad for a girl so in love"),
            Track("10", "what's wrong with me", "you seem pretty sad for a girl so in love"),
            Track("11", "less", "you seem pretty sad for a girl so in love"),
            Track("12", "expectations", "you seem pretty sad for a girl so in love"),
            Track("13", "cigarette smoke", "you seem pretty sad for a girl so in love")
        };

        var albums = new List<CatalogAlbum>
        {
            AlbumWithType(
                "you seem pretty sad for a girl so in love",
                1,
                "album",
                "Olivia Rodrigo",
                "drop dead", "stupid song", "honeybee", "maggots for brains",
                "u + me = <3", "my way", "purple", "the cure", "begged",
                "what's wrong with me", "less", "expectations", "cigarette smoke"),
            new()
            {
                AlbumId = "2",
                Title = "the cure",
                RecordType = "single",
                AlbumArtists = ["Olivia Rodrigo"],
                Tracks =
                [
                    new CatalogTrack { Title = "the cure", TrackPosition = 1, TrackId = "c1" },
                    new CatalogTrack { Title = "Never Do (Demo)", TrackPosition = 2, TrackId = "c2" }
                ]
            }
        };

        var result = AlbumMatcher.Match("Olivia Rodrigo", local, albums, new AlbumMatcherOptions());

        Assert.Equal(13, result.Assignments.Count);
        Assert.All(
            result.Assignments,
            a => Assert.Equal("you seem pretty sad for a girl so in love", a.AlbumTitle));
        Assert.Equal(0, result.SingleReleaseCount);
        Assert.Equal(
            8,
            result.Assignments.Single(a => a.TrackTitle == "the cure").TrackNumber);
    }

    [Fact]
    public void ParentConsensus_HealsPoisonedSingleTagInsideStudioAlbumFolder()
    {
        var parentId = Guid.NewGuid();
        var local = new List<LocalTrack>
        {
            Track("1", "brutal", "SOUR", parentId),
            Track("2", "traitor", "SOUR", parentId),
            Track("3", "drivers license", "drivers license", parentId),
            Track("4", "1 step forward, 3 steps back", "SOUR", parentId),
            Track("5", "deja vu", "SOUR", parentId),
            Track("6", "good 4 u", "SOUR", parentId),
            Track("7", "enough for you", "SOUR", parentId),
            Track("8", "happier", "SOUR", parentId),
            Track("9", "jealousy, jealousy", "SOUR", parentId),
            Track("10", "favorite crime", "SOUR", parentId),
            Track("11", "hope ur ok", "SOUR", parentId)
        };

        var albums = new List<CatalogAlbum>
        {
            AlbumWithType(
                "SOUR",
                1,
                "album",
                "Olivia Rodrigo",
                "brutal", "traitor", "drivers license", "1 step forward, 3 steps back",
                "deja vu", "good 4 u", "enough for you", "happier",
                "jealousy, jealousy", "favorite crime", "hope ur ok"),
            AlbumWithType("drivers license", 2, "single", "Olivia Rodrigo", "drivers license"),
            AlbumWithType("good 4 u", 3, "single", "Olivia Rodrigo", "good 4 u")
        };

        var result = AlbumMatcher.Match("Olivia Rodrigo", local, albums, new AlbumMatcherOptions());

        Assert.Equal(11, result.Assignments.Count);
        Assert.All(result.Assignments, a => Assert.Equal("SOUR", a.AlbumTitle));
        Assert.Equal(0, result.SingleReleaseCount);
        Assert.Equal(3, result.Assignments.Single(a => a.TrackTitle == "drivers license").TrackNumber);
    }

    [Fact]
    public void AlbumFirst_IgnoresForeignCoverSingleCreditsInsideStudioFolder()
    {
        var parentId = Guid.NewGuid();
        var local = new List<LocalTrack>
        {
            Track("1", "brutal", "SOUR", parentId),
            Track("2", "traitor", "SOUR", parentId),
            Track("3", "drivers license", "drivers license", parentId),
            Track("4", "1 step forward, 3 steps back", "SOUR", parentId),
            Track("5", "deja vu", "SOUR", parentId),
            Track("6", "good 4 u", "SOUR", parentId),
            Track("7", "enough for you", "SOUR", parentId),
            Track("8", "happier", "SOUR", parentId),
            Track("9", "jealousy, jealousy", "SOUR", parentId),
            Track("10", "favorite crime", "SOUR", parentId),
            Track("11", "hope ur ok", "SOUR", parentId)
        };

        var byrneSingle = new CatalogAlbum
        {
            AlbumId = "888",
            Title = "drivers license",
            RecordType = "single",
            AlbumArtists = ["David Byrne"],
            Genres = ["Pop"],
            Tracks =
            [
                new CatalogTrack
                {
                    Title = "drivers license",
                    TrackPosition = 1,
                    TrackId = "88801",
                    Artists = ["David Byrne"]
                },
                new CatalogTrack
                {
                    Title = "drivers license",
                    TrackPosition = 2,
                    TrackId = "88802",
                    Artists = ["Olivia Rodrigo"]
                }
            ]
        };

        var albums = new List<CatalogAlbum>
        {
            AlbumWithType(
                "SOUR",
                1,
                "album",
                "Olivia Rodrigo",
                "brutal", "traitor", "drivers license", "1 step forward, 3 steps back",
                "deja vu", "good 4 u", "enough for you", "happier",
                "jealousy, jealousy", "favorite crime", "hope ur ok"),
            AlbumWithType("drivers license", 2, "single", "Olivia Rodrigo", "drivers license"),
            byrneSingle
        };

        var result = AlbumMatcher.Match("Olivia Rodrigo", local, albums, new AlbumMatcherOptions());

        Assert.Equal(11, result.Assignments.Count);
        Assert.All(result.Assignments, a =>
        {
            Assert.Equal("SOUR", a.AlbumTitle);
            Assert.Equal(["Olivia Rodrigo"], a.AlbumArtists);
            Assert.DoesNotContain("David Byrne", a.TrackArtists);
            Assert.DoesNotContain("David Byrne", a.AlbumArtists);
        });

        var violin = result.Assignments.Single(a => a.TrackTitle == "drivers license");
        Assert.Equal(3, violin.TrackNumber);
        Assert.False(violin.IsSingleRelease);
    }

    [Fact]
    public void ContextTrackArtists_ReplacesForeignAlbumCredits()
    {
        var want = AlbumMatcher.ContextTrackArtists(
            ["David Byrne"],
            ["David Byrne"],
            "Olivia Rodrigo");
        Assert.Equal(["Olivia Rodrigo"], want);

        var featured = AlbumMatcher.ContextTrackArtists(
            ["Olivia Rodrigo", "Guest"],
            ["Olivia Rodrigo"],
            "Olivia Rodrigo");
        Assert.Equal(["Olivia Rodrigo", "Guest"], featured);
    }

    [Fact]
    public void StandaloneSingleFolder_KeepsSingleWhenParentHasFewTracks()
    {
        var singleParent = Guid.NewGuid();
        var sourParent = Guid.NewGuid();
        var local = new List<LocalTrack>
        {
            Track("s1", "drivers license", "drivers license", singleParent),
            Track("1", "brutal", "SOUR", sourParent),
            Track("2", "traitor", "SOUR", sourParent),
            Track("3", "drivers license", "SOUR", sourParent),
            Track("4", "deja vu", "SOUR", sourParent),
            Track("5", "good 4 u", "SOUR", sourParent)
        };

        var albums = new List<CatalogAlbum>
        {
            AlbumWithType(
                "SOUR",
                1,
                "album",
                "Olivia Rodrigo",
                "brutal", "traitor", "drivers license", "1 step forward, 3 steps back",
                "deja vu", "good 4 u", "enough for you", "happier",
                "jealousy, jealousy", "favorite crime", "hope ur ok"),
            AlbumWithType("drivers license", 2, "single", "Olivia Rodrigo", "drivers license")
        };

        var result = AlbumMatcher.Match("Olivia Rodrigo", local, albums, new AlbumMatcherOptions());

        var standalone = Assert.Single(
            result.Assignments,
            a => a.TrackTitle == "drivers license" && a.IsSingleRelease);
        Assert.Equal("drivers license", standalone.AlbumTitle);
        Assert.Equal(1, standalone.TrackNumber);

        Assert.All(
            result.Assignments.Where(a => !a.IsSingleRelease),
            a => Assert.Equal("SOUR", a.AlbumTitle));
    }

    private static LocalTrack Track(string suffix, string title, string? album = null, Guid? parentAlbumId = null)
    {
        _ = suffix;
        return new LocalTrack
        {
            Id = Guid.NewGuid(),
            Title = title,
            Album = album,
            ParentAlbumId = parentAlbumId
        };
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
