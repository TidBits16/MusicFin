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
    public void PrettyList_DropsJunkAndLeadingAmpFragments()
    {
        var got = Genres.PrettyList(["& Country", "Explicit", "Screen", "Folk"]);
        Assert.Equal(["Folk"], got);
    }

    [Fact]
    public void PrettyList_NormalizesAiGenerated()
    {
        Assert.Equal(["AI Generated"], Genres.PrettyList(["Ai Generated"]));
        Assert.Equal(["AI Generated"], Genres.PrettyList(["AI"]));
        Assert.Equal(["AI Generated"], Genres.PrettyList(["aigenerated"]));
        Assert.Equal(["AI Generated"], Genres.PrettyList(["AI-Generated"]));
    }

    [Fact]
    public void PrettyList_FixesInsturmentalTypo()
    {
        var got = Genres.PrettyList(["Insturmental"]);
        Assert.Equal(["Instrumental"], got);
    }

    [Fact]
    public void PrettyList_DecodesHtmlEntities()
    {
        var got = Genres.PrettyList(["R&amp;B", "Alternative &amp; Indie"]);
        Assert.Contains("R&B", got);
        Assert.Contains("Alternative", got);
        Assert.Contains("Indie", got);
    }

    [Fact]
    public void PrettyList_SplitsSlashCompound()
    {
        var got = Genres.PrettyList(["Indie Rock/rock Pop"]);
        Assert.Contains("Indie Rock", got);
        Assert.Contains("Rock Pop", got);
    }

    [Fact]
    public void IsGenericOnly_DetectsBroadBuckets()
    {
        Assert.True(Genres.IsGenericOnly(["Pop"]));
        Assert.True(Genres.IsGenericOnly(["Alternative", "Rock"]));
        Assert.False(Genres.IsGenericOnly(["Breakcore", "Electronic"]));
        Assert.False(Genres.IsGenericOnly(["Indie Rock"]));
    }

    [Fact]
    public void StandardizeWant_SplitsAndDropsJunk()
    {
        var got = Genres.StandardizeWant(
        [
            "Classical: World",
            "Alternative & Indie",
            "& Country",
            "Elecronic",
            "Ai Generated",
            "Folk"
        ]);
        Assert.NotNull(got);
        Assert.Contains("Classical", got);
        Assert.Contains("World", got);
        Assert.Contains("Alternative", got);
        Assert.Contains("Indie", got);
        Assert.Contains("Electronic", got);
        Assert.Contains("AI Generated", got);
        Assert.Contains("Folk", got);
        Assert.DoesNotContain("& Country", got);
        Assert.DoesNotContain("Classical: World", got);
    }

    [Fact]
    public void StandardizeWant_NullWhenAlreadyClean()
    {
        Assert.Null(Genres.StandardizeWant(["Indie Rock", "Folk"]));
    }

    [Fact]
    public void PreferSpecific_ReplacesGenericWithDiscogsStyles()
    {
        var got = Genres.PreferSpecific(["Pop"], ["Breakcore", "Electronic", "Hardcore"]);
        Assert.Equal(["Breakcore", "Electronic", "Hardcore"], got);
    }

    [Fact]
    public void PreferSpecific_KeepsSpecificPrimary()
    {
        var got = Genres.PreferSpecific(["Indie Rock", "Dream Pop"], ["Rock"]);
        Assert.Equal(["Indie Rock", "Dream Pop"], got);
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
