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
}
