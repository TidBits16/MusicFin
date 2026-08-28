namespace Jellyfin.Plugin.DeezerTagger;

public sealed class CatalogTrack
{
    public string Title { get; init; } = string.Empty;

    public bool? Explicit { get; init; }

    public List<string> Artists { get; init; } = [];

    public string TrackId { get; init; } = string.Empty;

    public int TrackPosition { get; init; }

    public int DiskNumber { get; init; }

    public DateTime? ReleaseDate { get; init; }
}

public sealed class CatalogArtistInfo
{
    public string Name { get; init; } = string.Empty;

    public string ArtistId { get; init; } = string.Empty;

    public string Picture { get; init; } = string.Empty;
}

public sealed class CatalogAlbum
{
    public List<string> Genres { get; init; } = [];

    public string Source { get; init; } = "no-match";

    public string AlbumId { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public List<string> AlbumArtists { get; init; } = [];

    public List<CatalogTrack> Tracks { get; init; } = [];

    public DateTime? ReleaseDate { get; init; }

    public string RecordType { get; init; } = string.Empty;

    public bool IsCompilation => RecordType.Equals("compilation", StringComparison.OrdinalIgnoreCase);

    public bool IsSingle => RecordType.Equals("single", StringComparison.OrdinalIgnoreCase);

    public int? Year => ReleaseDate is { Year: >= 1000 } d ? d.Year : null;
}
