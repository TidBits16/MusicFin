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

        // Singles compete with albums/EPs: a 1/1 single (100%) beats a 1/20 album (5%).
        var candidates = catalogAlbums.ToList();
        var scored = candidates
            .Select(album => new ScoredAlbum(album, ScoreAlbum(localTracks, album, artist, minSim, markers)))
            .Where(x => x.Score > 0)
            .ToDictionary(x => x.Album.AlbumId, StringComparer.Ordinal);

        var assignments = new List<TrackAssignment>();
        var albumCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var local in localTracks)
        {
            if (TryAssignAlbum(local, artist, candidates, scored, minSim, markers) is not { } assignment)
            {
                continue;
            }

            assignments.Add(assignment);
            albumCounts[assignment.AlbumTitle] = albumCounts.GetValueOrDefault(assignment.AlbumTitle) + 1;
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

    private static TrackAssignment? TryAssignAlbum(
        LocalTrack local,
        string artist,
        IReadOnlyList<CatalogAlbum> candidates,
        IReadOnlyDictionary<string, ScoredAlbum> scored,
        double minSimilarity,
        IReadOnlyList<string> markers)
    {
        CatalogAlbum? bestAlbum = null;
        CatalogTrack? bestTrack = null;
        var bestTrackScore = -1.0;
        var bestExact = false;
        var bestTitleLength = int.MaxValue;
        var bestRatio = -1.0;
        var bestAlbumScore = -1;
        var bestAlbumSize = int.MaxValue;

        foreach (var album in candidates)
        {
            if (!scored.TryGetValue(album.AlbumId, out var albumScore))
            {
                continue;
            }

            var match = TrackMatcher.MatchTrack(local.Title, album.Tracks, minSimilarity, markers, artist);
            if (match is null)
            {
                continue;
            }

            var trackScore = TrackMatcher.TitleMatchScore(local.Title, match.Title, markers, artist);
            // Singles are often titled after the song; treat album-title fit as part of the track score.
            if (album.IsSingle)
            {
                var albumTitleScore = TrackMatcher.TitleMatchScore(local.Title, album.Title, markers, artist);
                trackScore = Math.Max(trackScore, albumTitleScore);
            }

            var want = Titles.Norm(Titles.StripTrailingArtist(local.Title, artist), markers);
            var got = Titles.Norm(match.Title, markers);
            var exact = got == want;

            if (IsBetterCandidate(
                    trackScore,
                    album.IsSingle,
                    albumScore.Score,
                    albumScore.Ratio,
                    exact,
                    got.Length,
                    album.Tracks.Count,
                    bestTrackScore,
                    bestAlbum?.IsSingle ?? false,
                    bestAlbumScore,
                    bestRatio,
                    bestExact,
                    bestTitleLength,
                    bestAlbumSize))
            {
                bestAlbum = album;
                bestTrack = match;
                bestTrackScore = trackScore;
                bestExact = exact;
                bestTitleLength = got.Length;
                bestRatio = albumScore.Ratio;
                bestAlbumScore = albumScore.Score;
                bestAlbumSize = album.Tracks.Count;
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
            IsSingleRelease = bestAlbum.IsSingle
        };
    }

    private static bool IsBetterCandidate(
        double trackScore,
        bool isSingle,
        int albumScore,
        double ratio,
        bool exact,
        int titleLength,
        int albumSize,
        double bestTrackScore,
        bool bestIsSingle,
        int bestAlbumScore,
        double bestRatio,
        bool bestExact,
        int bestTitleLength,
        int bestAlbumSize)
    {
        if (trackScore > bestTrackScore + 0.0001)
        {
            return true;
        }

        if (Math.Abs(trackScore - bestTrackScore) > 0.0001)
        {
            return false;
        }

        // Singles compete on coverage % (1/1 = 100% beats 1/20 = 5%).
        // Albums/EPs compete on how many local tracks they cover, then %.
        if (isSingle || bestIsSingle)
        {
            if (ratio > bestRatio + 0.0001)
            {
                return true;
            }

            if (Math.Abs(ratio - bestRatio) > 0.0001)
            {
                return false;
            }

            if (albumScore > bestAlbumScore)
            {
                return true;
            }

            if (albumScore < bestAlbumScore)
            {
                return false;
            }
        }
        else
        {
            if (albumScore > bestAlbumScore)
            {
                return true;
            }

            if (albumScore < bestAlbumScore)
            {
                return false;
            }

            if (ratio > bestRatio + 0.0001)
            {
                return true;
            }

            if (Math.Abs(ratio - bestRatio) > 0.0001)
            {
                return false;
            }
        }

        if (exact && !bestExact)
        {
            return true;
        }

        if (exact != bestExact)
        {
            return false;
        }

        if (titleLength < bestTitleLength)
        {
            return true;
        }

        if (titleLength > bestTitleLength)
        {
            return false;
        }

        return albumSize < bestAlbumSize;
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
        string artist,
        double minSimilarity,
        IReadOnlyList<string> markers)
    {
        var count = 0;
        foreach (var local in localTracks)
        {
            if (TrackMatcher.MatchTrack(local.Title, album.Tracks, minSimilarity, markers, artist) is not null)
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
