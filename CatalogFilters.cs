namespace Jellyfin.Plugin.DeezerTagger;

public static class CatalogFilters
{
    public static bool IsVariousArtists(string name)
    {
        var n = Titles.Norm(name);
        return n is "various artists" or "various" or "va" or "multi artist";
    }

    public static bool IsOwnedByArtist(string artistName, CatalogAlbum album)
    {
        if (album.AlbumArtists.Count == 0)
        {
            return true;
        }

        if (album.AlbumArtists.All(IsVariousArtists))
        {
            return false;
        }

        var want = Titles.Norm(artistName);
        if (want.Length == 0)
        {
            return true;
        }

        return album.AlbumArtists.Any(aa =>
        {
            if (IsVariousArtists(aa))
            {
                return false;
            }

            var got = Titles.Norm(aa);
            return got == want
                || got.Contains(want, StringComparison.Ordinal)
                || want.Contains(got, StringComparison.Ordinal);
        });
    }
}
