using Jellyfin.Plugin.MusicFin;
using Xunit;

namespace Jellyfin.Plugin.MusicFin.Tests;

public class GenresTests
{
    [Fact]
    public void PrettyList_SplitsSemicolonCompound()
    {
        var got = Genres.PrettyList(["Alternative rock; Indie; Folk"]);
        Assert.Equal(["Alternative Rock", "Indie", "Folk"], got);
    }

    [Fact]
    public void PrettyList_KeepsDrumAndBassCompound()
    {
        var got = Genres.PrettyList(["Drum & Bass"]);
        Assert.Equal(["Drum and Bass"], got);
    }

    [Fact]
    public void PrettyList_ReunitesDrumSemicolonBass()
    {
        var got = Genres.PrettyList(["Drum; Bass"]);
        Assert.Equal(["Drum and Bass"], got);
    }

    [Fact]
    public void PrettyList_NormalizesCase()
    {
        var got = Genres.PrettyList(["alternative rock"]);
        Assert.Equal(["Alternative Rock"], got);
    }

    [Fact]
    public void PrettyList_FixesElecronicTypo()
    {
        var got = Genres.PrettyList(["Elecronic"]);
        Assert.Equal(["Electronic"], got);
    }

    [Fact]
    public void PrettyList_SplitsAmpersandPair()
    {
        var got = Genres.PrettyList(["Alternative & Indie"]);
        Assert.Equal(["Alternative", "Indie"], got);
    }

    [Fact]
    public void PrettyList_SplitsColonPair()
    {
        var got = Genres.PrettyList(["Classical: World"]);
        Assert.Equal(["Classical", "World"], got);
    }

    [Fact]
    public void PrettyList_PreservesRandB()
    {
        var got = Genres.PrettyList(["R&B", "Hip-Hop; R&B"]);
        Assert.Contains("R&B", got);
        Assert.Contains("Hip Hop", got);
        Assert.DoesNotContain("R", got);
        Assert.DoesNotContain("B", got);
    }

    [Fact]
    public void NeedsRewrite_TrueForMessyList()
    {
        Assert.True(Genres.NeedsRewrite(["alternative rock; Indie", "Elecronic"]));
    }

    [Fact]
    public void NeedsRewrite_FalseForCleanList()
    {
        Assert.False(Genres.NeedsRewrite(["Alternative Rock", "Electronic"]));
    }

    [Fact]
    public void NeedsRewrite_FalseForNullOrEmpty()
    {
        Assert.False(Genres.NeedsRewrite(null));
        Assert.False(Genres.NeedsRewrite([]));
    }
}
