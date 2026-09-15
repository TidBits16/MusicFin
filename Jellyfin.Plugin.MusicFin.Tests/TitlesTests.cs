using Jellyfin.Plugin.MusicFin;
using Xunit;

namespace Jellyfin.Plugin.MusicFin.Tests;

public class TitlesTests
{
    private static readonly string[] DefaultMarkers = ["🅴", "[Explicit]"];

    [Theory]
    [InlineData("You\u2019ll Be Alright, Kid", "you ll be alright kid")]
    [InlineData("\u201cGetaway Car\u201d", "getaway car")]
    [InlineData("You Can\u2019t Stop This", "you can t stop this")]
    public void Norm_FoldsUnicodePunctuation(string input, string expected)
        => Assert.Equal(expected, Titles.Norm(input, DefaultMarkers));

    [Theory]
    [InlineData("Ordinary 🅴", "Ordinary")]
    [InlineData("Ordinary [Explicit]", "Ordinary")]
    [InlineData("🅴 Ordinary", "Ordinary")]
    [InlineData("[Explicit] Ordinary", "Ordinary")]
    public void StripMark_RemovesConfiguredExplicitMarkers(string input, string expected)
        => Assert.Equal(expected, Titles.StripMark(input, DefaultMarkers));

    [Theory]
    [InlineData("CHASER 🅴", "CHASER", true)]
    [InlineData("🅴 CHASER", "CHASER", true)]
    [InlineData("CHASER", "CHASER", true)]
    [InlineData("REACTOR 🅴", "CHASER", false)]
    [InlineData("chaser 🅴", "CHASER", true)]
    [InlineData("how to: friend, love, freefall 🅴", "How To: Friend, Love, Freefall", true)]
    [InlineData("Love Hate Music Box 🅴", "Love Hate Music Box", true)]
    [InlineData("love hate music box 🅴", "Love Hate Music Box", true)]
    [InlineData("CHASER [Explicit]", "CHASER", true)]
    public void SameTitleIgnoringMarks_IgnoresConfiguredMarkers(
        string current,
        string catalog,
        bool expected)
        => Assert.Equal(expected, Titles.SameTitleIgnoringMarks(current, catalog, DefaultMarkers));

    [Theory]
    [InlineData("how to: friend, love, freefall 🅴", "How To: Friend, Love, Freefall", false)]
    [InlineData("THE ANTIHUMAN", "ANTIHUMAN", false)]
    [InlineData("Step On Up", "Live in '25", false)]
    [InlineData("Wrong Name", "Right Name", true)]
    [InlineData("Seven", "Seven + Mary", false)]
    [InlineData("Mary", "Seven + Mary", false)]
    public void ShouldReplaceAlbumTitle_Guards(
        string current,
        string catalog,
        bool expected)
        => Assert.Equal(expected, Titles.ShouldReplaceAlbumTitle(current, catalog, DefaultMarkers));

    [Theory]
    [InlineData("Seven", "Seven + Mary", "Seven")]
    [InlineData("Mary", "Seven + Mary", "Mary")]
    [InlineData("Seven + Mary", "Seven + Mary", "Seven + Mary")]
    [InlineData("SOUR", "SOUR", "SOUR")]
    [InlineData("", "Seven + Mary", "Seven + Mary")]
    public void PreferredAlbumWriteTitle_KeepsEpFolderOverCombo(
        string local,
        string catalog,
        string expected)
        => Assert.Equal(expected, Titles.PreferredAlbumWriteTitle(local, catalog, DefaultMarkers));
    [Fact]
    public void PreferExactArtistMatches_DropsFentanylWhenFemtanylExactExists()
    {
        var ranked = new List<CatalogArtistInfo>
        {
            new() { Name = "femtanyl", ArtistId = "220484855" },
            new() { Name = "Fentanyl", ArtistId = "1166093" },
            new() { Name = "FXNTANYL", ArtistId = "284960531" },
        };

        var kept = Titles.PreferExactArtistMatches(ranked, Titles.Norm("Femtanyl"));

        Assert.Single(kept);
        Assert.Equal("220484855", kept[0].ArtistId);
    }

    [Fact]
    public void PreferExactArtistMatches_KeepsNearMissesWhenNoExact()
    {
        var ranked = new List<CatalogArtistInfo>
        {
            new() { Name = "Fentanyl", ArtistId = "1166093" },
            new() { Name = "FXNTANYL", ArtistId = "284960531" },
        };

        var kept = Titles.PreferExactArtistMatches(ranked, Titles.Norm("Femtanyl"));

        Assert.Equal(2, kept.Count);
    }

    [Fact]
    public void MatchTrack_IgnoresExplicitMarkerOnLocalTitle()
    {
        var tracks = new List<CatalogTrack>
        {
            new() { Title = "Ordinary", TrackPosition = 1, TrackId = "1" }
        };

        var match = TrackMatcher.MatchTrack("Ordinary 🅴", tracks, 0.72, DefaultMarkers);

        Assert.NotNull(match);
        Assert.Equal("Ordinary", match!.Title);
    }

    [Fact]
    public void MatchTrack_PrefersExactShorterTitleOnTie()
    {
        var tracks = new List<CatalogTrack>
        {
            new() { Title = "Betty (Live from the Hollywood Bowl)", TrackPosition = 12, TrackId = "long" },
            new() { Title = "Betty", TrackPosition = 3, TrackId = "short" }
        };

        var match = TrackMatcher.MatchTrack("Betty", tracks, 0.72, DefaultMarkers);

        Assert.NotNull(match);
        Assert.Equal("short", match!.TrackId);
        Assert.Equal(3, match.TrackPosition);
    }

    [Theory]
    [InlineData("That's My Shit - Rainbow Kitten Surprise", "Rainbow Kitten Surprise", "That's My Shit")]
    [InlineData("That's My Shit - Rainbow Kitten Suprise", "Rainbow Kitten Surprise", "That's My Shit")]
    [InlineData("That's My Shit", "Rainbow Kitten Surprise", "That's My Shit")]
    public void StripTrailingArtist_RemovesArtistSuffix(string input, string artist, string expected)
        => Assert.Equal(expected, Titles.StripTrailingArtist(input, artist));

    [Theory]
    [InlineData("All That and More (Sailboat)", "All That and More")]
    [InlineData("That's My Shit (Live from Athens Georgia)", "That's My Shit (Live from Athens Georgia)")]
    [InlineData("Step On Up (Live)", "Step On Up (Live)")]
    [InlineData("Song (Remix)", "Song (Remix)")]
    [InlineData("Song (Radio Edit)", "Song (Radio Edit)")]
    [InlineData("Betty", "Betty")]
    public void StripShortParenthetical_OnlyRemovesShortSuffixes(string input, string expected)
        => Assert.Equal(expected, Titles.StripShortParenthetical(input));

    [Theory]
    [InlineData("THE ANTIHUMAN", "ANTIHUMAN", true)]
    [InlineData("ANTIHUMAN", "ANTIHUMAN", false)]
    [InlineData("ANTIHUMAN", "THE ANTIHUMAN", false)]
    [InlineData("Live in '25", "Step On Up", false)]
    public void IsMoreSpecificAlbumTitle_DetectsLeadingArticleVariants(
        string local,
        string catalog,
        bool expected)
        => Assert.Equal(expected, Titles.IsMoreSpecificAlbumTitle(local, catalog));

    [Theory]
    [InlineData("Classics Collection", true)]
    [InlineData("IVYCOMB: Classics Collection", true)]
    [InlineData("Greatest Hits", true)]
    [InlineData("Live in '25", true)]
    [InlineData("Live from Athens Georgia", true)]
    [InlineData("The Land: The Live Album", true)]
    [InlineData("FNAFdom (Live)", false)]
    [InlineData("SOUR", false)]
    [InlineData("GUTS", false)]
    [InlineData("Be the Cowboy", false)]
    [InlineData("INFAMOUS", false)]
    public void IsSecondaryAlbumTitle_DetectsCompilationsAndLiveTours(string title, bool expected)
        => Assert.Equal(expected, Titles.IsSecondaryAlbumTitle(title));

    [Theory]
    [InlineData("GUTS (spilled)", true)]
    [InlineData("GUTS (Deluxe)", true)]
    [InlineData("GUTS", false)]
    public void LooksLikeDeluxeTitle_DetectsExpandedEditions(string title, bool expected)
        => Assert.Equal(expected, Titles.LooksLikeDeluxeTitle(title));

    [Fact]
    public void TitleMatchScore_DoesNotTreatLiveSuffixAsExactStudioMatch()
        => Assert.True(TrackMatcher.TitleMatchScore("Step On Up", "Step On Up (Live)") < 0.999);

    [Theory]
    [InlineData("Outliars & Hyppocrates", "outliars and hyppocrates")]
    [InlineData("Black Box Warrior", "black box warrior")]
    public void Norm_FoldsAmpersandAndKeepsWords(string input, string expected)
        => Assert.Equal(expected, Titles.Norm(input));

    [Theory]
    [InlineData("01-07 Stressed Out", "Stressed Out")]
    [InlineData("01.07 Stressed Out", "Stressed Out")]
    [InlineData("07 - Ride", "Ride")]
    [InlineData("07. Fairly Local", "Fairly Local")]
    [InlineData("07 Fairly Local", "Fairly Local")]
    [InlineData("Heavydirtysoul", "Heavydirtysoul")]
    [InlineData("99 Problems", "99 Problems")]
    [InlineData("7 Years", "7 Years")]
    public void TitleFromFileName_StripsTrackPrefixes(string input, string expected)
        => Assert.Equal(expected, Titles.TitleFromFileName(input));

    [Theory]
    [InlineData("Blurry Face - Twenty One Pilots", "Twenty One Pilots", "Blurry Face")]
    [InlineData("Twenty One Pilots - Blurryface", "Twenty One Pilots", "Blurryface")]
    [InlineData("Live in '25", "The Living Tombstone", "Live in '25")]
    [InlineData("SOUR", "Olivia Rodrigo", "SOUR")]
    public void AlbumFromDirectoryName_StripsArtist(string dir, string artist, string expected)
        => Assert.Equal(expected, Titles.AlbumFromDirectoryName(dir, artist));

    [Fact]
    public void TitleFromStoragePath_UsesFileStem()
        => Assert.Equal(
            "Stressed Out",
            Titles.TitleFromStoragePath("/media/music/admin/Blurry Face - Twenty One Pilots/01-07 Stressed Out.flac"));

    [Fact]
    public void AlbumFromStoragePath_UsesParentFolder()
        => Assert.Equal(
            "Blurry Face",
            Titles.AlbumFromStoragePath(
                "/media/music/admin/Blurry Face - Twenty One Pilots/01-07 Stressed Out.flac",
                "Twenty One Pilots"));

    [Fact]
    public void FoldLeetDigits_MapsStylizedSecondSightSeer()
        => Assert.Equal("second sight seer", Titles.FoldLeetDigits(Titles.Norm("2econd 2ight 2eer")));

    [Theory]
    [InlineData("Black Box Warrior", "BlackBoxWarrior - OKULTRA")]
    [InlineData("Second Sight Seer", "2econd 2ight 2eer (that was fun, goodbye.)")]
    [InlineData("Outliars & Hyppocrates", "Outliars and Hyppocrates: a fun fact about apples")]
    public void TitleMatchScore_HandlesCompoundAndLeetTitles(string local, string catalog)
        => Assert.True(TrackMatcher.TitleMatchScore(local, catalog) >= 0.84);

    [Fact]
    public void SameNames_IgnoresProviderCasing()
    {
        Assert.True(Titles.SameNames(["NF"], ["nf"]));
        Assert.True(Titles.SameNames(["femtanyl"], ["Femtanyl"]));
        Assert.False(Titles.SameNames(["NF"], ["Nate Feuerstein"]));
        Assert.False(Titles.SameNames(["NF"], ["NF", "Hopsin"]));
    }
}
