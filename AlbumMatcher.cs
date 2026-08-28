namespace Jellyfin.Plugin.DeezerTagger;

public sealed class LocalTrack
{
    public Guid Id { get; init; }

    public string Title { get; init; } = string.Empty;

    public string? Album { get; init; }

    public int? IndexNumber { get; init; }
}

public sealed class TrackAssignment
{
    public Guid TrackId { get; init; }

    public string TrackTitle { get; init; } = string.Empty;

    public string AlbumTitle { get; init; } = string.Empty;

    public int TrackNumber { get; init; }

    public string ProviderAlbumId { get; init; } = string.Empty;

    public string ProviderTrackId { get; init; } = string.Empty;

    public IReadOnlyList<string> Genres { get; init; } = [];

    public IReadOnlyList<string> TrackArtists { get; init; } = [];

    public IReadOnlyList<string> AlbumArtists { get; init; } = [];

    public int? Year { get; init; }

    public bool IsSingleRelease { get; init; }
}

public sealed class AlbumAssignmentSummary
{
    public string AlbumTitle { get; init; } = string.Empty;

    public int TrackCount { get; init; }
}

public sealed class ArtistMatchResult
{
    public string Artist { get; init; } = string.Empty;

    public string ProviderArtistId { get; init; } = string.Empty;

    public int AlbumsScanned { get; init; }

    public IReadOnlyList<TrackAssignment> Assignments { get; init; } = [];

    public IReadOnlyList<AlbumAssignmentSummary> AlbumSummaries { get; init; } = [];

    public int SingleReleaseCount { get; init; }

    public int UnmatchedCount { get; init; }
}

public sealed class AlbumMatcherOptions
{
    public double MinTitleSimilarity { get; init; } = 0.72;

    public IReadOnlyList<string> IgnoreTitleMarkers { get; init; } = Titles.DefaultIgnoreTitleMarkers;
}

public static class AlbumMatcher
{
    public static ArtistMatchResult Match(
        string artist,
        IReadOnlyList<LocalTrack> localTracks,
        IReadOnlyList<CatalogAlbum> catalogAlbums,
        AlbumMatcherOptions options)
    {
        var minSim = options.MinTitleSimilarity;
        var markers = options.IgnoreTitleMarkers;
        var studioAlbums = catalogAlbums.Where(a => !a.IsSingle).ToList();
        var singleReleases = catalogAlbums.Where(a => a.IsSingle).ToList();

        var scored = studioAlbums
            .Select(album => new ScoredAlbum(album, ScoreAlbum(localTracks, album, minSim, markers)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Ratio)
            .ThenBy(x => x.Album.IsCompilation ? 1 : 0)
            .ThenBy(x => x.Album.Tracks.Count)
            .ToList();

        var assigned = new HashSet<Guid>();
        var assignments = new List<TrackAssignment>();
        var albumCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in scored)
        {
            foreach (var local in localTracks)
            {
                if (assigned.Contains(local.Id))
                {
                    continue;
                }

                var match = TrackMatcher.MatchTrack(local.Title, entry.Album.Tracks, minSim, markers);
                if (match is null)
                {
                    continue;
                }

                assigned.Add(local.Id);
                assignments.Add(new TrackAssignment
                {
                    TrackId = local.Id,
                    TrackTitle = local.Title,
                    AlbumTitle = entry.Album.Title,
                    TrackNumber = match.TrackPosition,
                    ProviderAlbumId = entry.Album.AlbumId,
                    ProviderTrackId = match.TrackId,
                    Genres = entry.Album.Genres,
                    TrackArtists = ArtistsForTrack(match, entry.Album, artist),
                    AlbumArtists = AlbumArtistsFor(entry.Album, artist),
                    Year = entry.Album.Year
                });

                albumCounts[entry.Album.Title] = albumCounts.GetValueOrDefault(entry.Album.Title) + 1;
            }
        }

        foreach (var local in localTracks)
        {
            if (assigned.Contains(local.Id))
            {
                continue;
            }

            if (TryMatchSingleRelease(local, singleReleases, artist, minSim, markers) is not { } singleAssignment)
            {
                continue;
            }

            assigned.Add(local.Id);
            assignments.Add(singleAssignment);
            albumCounts[singleAssignment.AlbumTitle] = albumCounts.GetValueOrDefault(singleAssignment.AlbumTitle) + 1;
        }

        var summaries = albumCounts
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => new AlbumAssignmentSummary { AlbumTitle = x.Key, TrackCount = x.Value })
            .ToList();

        return new ArtistMatchResult
        {
            Artist = artist,
            AlbumsScanned = catalogAlbums.Count,
            Assignments = assignments,
            AlbumSummaries = summaries,
            SingleReleaseCount = assignments.Count(x => x.IsSingleRelease),
            UnmatchedCount = localTracks.Count - assignments.Count
        };
    }

    private static TrackAssignment? TryMatchSingleRelease(
        LocalTrack local,
        IReadOnlyList<CatalogAlbum> singles,
        string artist,
        double minSimilarity,
        IReadOnlyList<string> markers)
    {
        CatalogAlbum? bestAlbum = null;
        CatalogTrack? bestTrack = null;
        var bestScore = -1.0;
        var bestTitleScore = -1.0;

        foreach (var single in singles)
        {
            var match = TrackMatcher.MatchTrack(local.Title, single.Tracks, minSimilarity, markers);
            if (match is null)
            {
                continue;
            }

            var titleScore = TrackMatcher.TitleMatchScore(local.Title, single.Title, markers);
            var trackScore = TrackMatcher.TitleMatchScore(local.Title, match.Title, markers);
            var score = Math.Max(titleScore, trackScore);
            if (score > bestScore || (Math.Abs(score - bestScore) < 0.0001 && titleScore > bestTitleScore))
            {
                bestScore = score;
                bestTitleScore = titleScore;
                bestAlbum = single;
                bestTrack = match;
            }
        }

        if (bestAlbum is null || bestTrack is null)
        {
            return null;
        }

        var trackNumber = bestTrack.TrackPosition > 0 ? bestTrack.TrackPosition : 1;
        return new TrackAssignment
        {
            TrackId = local.Id,
            TrackTitle = local.Title,
            AlbumTitle = bestAlbum.Title,
            TrackNumber = trackNumber,
            ProviderAlbumId = bestAlbum.AlbumId,
            ProviderTrackId = bestTrack.TrackId,
            Genres = bestAlbum.Genres,
            TrackArtists = ArtistsForTrack(bestTrack, bestAlbum, artist),
            AlbumArtists = AlbumArtistsFor(bestAlbum, artist),
            Year = bestAlbum.Year,
            IsSingleRelease = true
        };
    }

    private static IReadOnlyList<string> AlbumArtistsFor(CatalogAlbum album, string fallbackArtist)
        => album.AlbumArtists.Count > 0 ? album.AlbumArtists : [fallbackArtist];

    private static IReadOnlyList<string> ArtistsForTrack(CatalogTrack track, CatalogAlbum album, string fallbackArtist)
    {
        if (track.Artists.Count > 0)
        {
            return track.Artists;
        }

        return AlbumArtistsFor(album, fallbackArtist);
    }

    private static int ScoreAlbum(
        IReadOnlyList<LocalTrack> localTracks,
        CatalogAlbum album,
        double minSimilarity,
        IReadOnlyList<string> markers)
    {
        var count = 0;
        foreach (var local in localTracks)
        {
            if (TrackMatcher.MatchTrack(local.Title, album.Tracks, minSimilarity, markers) is not null)
            {
                count++;
            }
        }

        return count;
    }

    private sealed class ScoredAlbum(CatalogAlbum album, int score)
    {
        public CatalogAlbum Album { get; } = album;

        public int Score { get; } = score;

        public double Ratio => Album.Tracks.Count > 0 ? (double)Score / Album.Tracks.Count : 0;
    }
}
