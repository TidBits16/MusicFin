using Jellyfin.Plugin.DeezerTagger;
using Xunit;

namespace Jellyfin.Plugin.DeezerTagger.Tests;

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
    [InlineData("Betty", "Betty")]
    public void StripShortParenthetical_OnlyRemovesShortSuffixes(string input, string expected)
        => Assert.Equal(expected, Titles.StripShortParenthetical(input));

    [Theory]
    [InlineData("Outliars & Hyppocrates", "outliars and hyppocrates")]
    [InlineData("Black Box Warrior", "black box warrior")]
    public void Norm_FoldsAmpersandAndKeepsWords(string input, string expected)
        => Assert.Equal(expected, Titles.Norm(input));

    [Fact]
    public void FoldLeetDigits_MapsStylizedSecondSightSeer()
        => Assert.Equal("second sight seer", Titles.FoldLeetDigits(Titles.Norm("2econd 2ight 2eer")));

    [Theory]
    [InlineData("Black Box Warrior", "BlackBoxWarrior - OKULTRA")]
    [InlineData("Second Sight Seer", "2econd 2ight 2eer (that was fun, goodbye.)")]
    [InlineData("Outliars & Hyppocrates", "Outliars and Hyppocrates: a fun fact about apples")]
    public void TitleMatchScore_HandlesCompoundAndLeetTitles(string local, string catalog)
        => Assert.True(TrackMatcher.TitleMatchScore(local, catalog) >= 0.84);
}
