namespace Jellyfin.Plugin.MusicFin;

/// <summary>
/// Guards MusicAlbum entity renames so one matched single cannot rename a full album folder.
/// </summary>
public static class AlbumRename
{
    /// <summary>True when <paramref name="hitCount"/> covers at least half of <paramref name="parentTrackCount"/>.</summary>
    public static bool HasMajorityCoverage(int parentTrackCount, int hitCount)
        => parentTrackCount > 0 && hitCount * 2 >= parentTrackCount;

    /// <summary>
    /// True when matched assignments are unanimous and cover enough of the parent album
    /// to safely rename the Jellyfin MusicAlbum entity.
    /// </summary>
    public static bool ShouldRenameAlbumEntity(
        int parentTrackCount,
        IReadOnlyList<TrackAssignment> assignments)
    {
        if (assignments.Count == 0 || !HasMajorityCoverage(parentTrackCount, assignments.Count))
        {
            return false;
        }

        var title = assignments[0].AlbumTitle.Trim();
        if (title.Length == 0)
        {
            return false;
        }

        for (var i = 1; i < assignments.Count; i++)
        {
            if (!title.Equals(assignments[i].AlbumTitle.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // A multi-track folder must not be renamed to a single just because the lead single matched.
        return !assignments.All(a => a.IsSingleRelease)
            || parentTrackCount <= Math.Max(2, assignments.Count);
    }
}
