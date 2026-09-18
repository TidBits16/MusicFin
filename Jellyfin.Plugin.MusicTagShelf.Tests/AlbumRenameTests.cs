using Jellyfin.Plugin.MusicTagShelf;
using Xunit;

namespace Jellyfin.Plugin.MusicTagShelf.Tests;

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

    [Fact]
    public void ShouldNotRename_WhenSingleFolderAbsorbedIntoLargerAlbum()
    {
        // Never Be Alone is track 3 on Fiber-Optic Radio; local folder only has 1 file.
        var assignments = new List<TrackAssignment>
        {
            new()
            {
                TrackId = Guid.NewGuid(),
                TrackTitle = "Never Be Alone",
                AlbumTitle = "Fiber-Optic Radio",
                TrackNumber = 3,
                IsSingleRelease = false
            }
        };

        Assert.True(AlbumRename.LooksAbsorbedIntoLargerAlbum(1, assignments));
        Assert.False(AlbumRename.ShouldRenameAlbumEntity(1, assignments));
    }

    [Fact]
    public void ShouldRename_WhenTrackNumbersFitLocalFolder()
    {
        var assignments = Enumerable.Range(1, 7)
            .Select(i => new TrackAssignment
            {
                TrackId = Guid.NewGuid(),
                TrackTitle = "t" + i,
                AlbumTitle = "Seven + Mary",
                TrackNumber = i,
                IsSingleRelease = false
            })
            .ToList();

        Assert.False(AlbumRename.LooksAbsorbedIntoLargerAlbum(7, assignments));
        Assert.True(AlbumRename.ShouldRenameAlbumEntity(7, assignments));
    }
}
