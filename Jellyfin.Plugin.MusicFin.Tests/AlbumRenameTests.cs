using Jellyfin.Plugin.MusicFin;
using Xunit;

namespace Jellyfin.Plugin.MusicFin.Tests;

public class AlbumRenameTests
{
    [Fact]
    public void ShouldRename_WhenMostTracksAgreeOnStudioAlbum()
    {
        var assignments = Enumerable.Range(1, 10)
            .Select(i => new TrackAssignment
            {
                TrackId = Guid.NewGuid(),
                TrackTitle = "t" + i,
                AlbumTitle = "SOUR",
                IsSingleRelease = false
            })
            .ToList();

        Assert.True(AlbumRename.ShouldRenameAlbumEntity(11, assignments));
    }

    [Fact]
    public void ShouldNotRename_WhenOnlyLeadSingleMatchedOnFullAlbum()
    {
        var assignments = new List<TrackAssignment>
        {
            new()
            {
                TrackId = Guid.NewGuid(),
                TrackTitle = "the cure",
                AlbumTitle = "the cure",
                IsSingleRelease = true
            }
        };

        Assert.False(AlbumRename.ShouldRenameAlbumEntity(13, assignments));
    }

    [Fact]
    public void ShouldNotRename_WhenMatchedTitlesDisagree()
    {
        var assignments = new List<TrackAssignment>
        {
            new() { TrackId = Guid.NewGuid(), TrackTitle = "a", AlbumTitle = "SOUR", IsSingleRelease = false },
            new() { TrackId = Guid.NewGuid(), TrackTitle = "b", AlbumTitle = "drivers license", IsSingleRelease = true }
        };

        Assert.False(AlbumRename.ShouldRenameAlbumEntity(11, assignments));
    }
}
