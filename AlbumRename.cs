namespace Jellyfin.Plugin.MusicFin;

/// <summary>
/// Guards MusicAlbum entity renames so one matched single cannot rename a full album folder.
/// </summary>
public static class AlbumRename
{
    /// <summary>
    /// True when matched assignments are unanimous and cover enough of the parent album
    /// to safely rename the Jellyfin MusicAlbum entity.
    /// </summary>
    public static bool ShouldRenameAlbumEntity(
        int parentTrackCount,
        IReadOnlyList<TrackAssignment> assignments)
    {
        if (assignments.Count == 0 || parentTrackCount <= 0)
        {
            return false;
        }

        var titles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in assignments)
        {
            var title = a.AlbumTitle.Trim();
            if (title.Length == 0)
            {
                return false;
            }

            titles.Add(title);
        }

        if (titles.Count != 1)
        {
            return false;
        }

        // Need at least half the folder matched (and never rename from a single hit on a big album).
        if (assignments.Count * 2 < parentTrackCount)
        {
            return false;
        }

        // A multi-track folder must not be renamed to a single just because the lead single matched.
        if (assignments.All(a => a.IsSingleRelease) && parentTrackCount > Math.Max(2, assignments.Count))
        {
            return false;
        }

        return true;
    }
}
