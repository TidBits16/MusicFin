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

        // Candidates compete on title fit, then fitness = matchCount * ratio^2.
        var candidates = catalogAlbums.ToList();
        var scored = candidates
            .Select(album => new ScoredAlbum(
                album,
                ScoreAlbum(localTracks, album, candidates, artist, minSim, markers)))
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
        var bestFitness = -1.0;
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

            // Bonus-only tracks that exist as their own single should not be claimed by a deluxe edition.
            if (IsExclusiveSingleOnExpandedEdition(local, album, candidates, artist, minSimilarity, markers))
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
                    albumScore.Fitness,
                    albumScore.Ratio,
                    albumScore.Score,
                    exact,
                    got.Length,
                    album.Tracks.Count,
                    bestTrackScore,
                    bestFitness,
                    bestRatio,
                    bestAlbumScore,
                    bestExact,
                    bestTitleLength,
                    bestAlbumSize))
            {
                bestAlbum = album;
                bestTrack = match;
                bestTrackScore = trackScore;
                bestFitness = albumScore.Fitness;
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
        double fitness,
        double ratio,
        int albumScore,
        bool exact,
        int titleLength,
        int albumSize,
        double bestTrackScore,
        double bestFitness,
        double bestRatio,
        int bestAlbumScore,
        bool bestExact,
        int bestTitleLength,
        int bestAlbumSize)
    {
        if (TitleBand(trackScore) > TitleBand(bestTrackScore) + 0.0001)
        {
            return true;
        }

        if (Math.Abs(TitleBand(trackScore) - TitleBand(bestTrackScore)) > 0.0001)
        {
            return false;
        }

        // Mixed size + completion: matchCount * ratio^2.
        // 10/10 album (10) beats 10/20 tour set (2.5); 4/4 EP (4) still beats the tour set.
        if (fitness > bestFitness + 0.0001)
        {
            return true;
        }

        if (Math.Abs(fitness - bestFitness) > 0.0001)
        {
            return false;
        }

        // Within the same title band, prefer the raw title score (exact over contains).
        if (trackScore > bestTrackScore + 0.0001)
        {
            return true;
        }

        if (Math.Abs(trackScore - bestTrackScore) > 0.0001)
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

        if (albumScore > bestAlbumScore)
        {
            return true;
        }

        if (albumScore < bestAlbumScore)
        {
            return false;
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

    /// <summary>
    /// Groups strong title matches so album fitness can decide between a live/edit single
    /// and the studio album when both are "good enough" contains-level hits.
    /// </summary>
    private static double TitleBand(double trackScore)
        => trackScore >= 0.999 ? 1.0
            : trackScore >= 0.84 ? 0.84
            : trackScore;

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
        IReadOnlyList<CatalogAlbum> allAlbums,
        string artist,
        double minSimilarity,
        IReadOnlyList<string> markers)
    {
        var matchedCatalogTracks = new HashSet<string>(StringComparer.Ordinal);
        foreach (var local in localTracks)
        {
            var match = TrackMatcher.MatchTrack(local.Title, album.Tracks, minSimilarity, markers, artist);
            if (match is null)
            {
                continue;
            }

            // Bonus-only tracks that exist as their own single should not inflate deluxe editions.
            if (IsExclusiveSingleOnExpandedEdition(local, album, allAlbums, artist, minSimilarity, markers))
            {
                continue;
            }

            var key = match.TrackId.Length > 0
                ? match.TrackId
                : match.TrackPosition + ":" + match.Title;
            matchedCatalogTracks.Add(key);
        }

        return matchedCatalogTracks.Count;
    }

    /// <summary>
    /// True when <paramref name="local"/> matches <paramref name="album"/>, matches a single release,
    /// and does not match some smaller non-single album that is a track-subset of <paramref name="album"/>.
    /// </summary>
    private static bool IsExclusiveSingleOnExpandedEdition(
        LocalTrack local,
        CatalogAlbum album,
        IReadOnlyList<CatalogAlbum> allAlbums,
        string artist,
        double minSimilarity,
        IReadOnlyList<string> markers)
    {
        if (album.IsSingle)
        {
            return false;
        }

        var matchesSingle = false;
        foreach (var single in allAlbums)
        {
            if (!single.IsSingle)
            {
                continue;
            }

            if (TrackMatcher.MatchTrack(local.Title, single.Tracks, minSimilarity, markers, artist) is not null)
            {
                matchesSingle = true;
                break;
            }
        }

        if (!matchesSingle)
        {
            return false;
        }

        foreach (var other in allAlbums)
        {
            if (other.IsSingle || other.AlbumId == album.AlbumId)
            {
                continue;
            }

            if (!IsExpandedEditionOf(other, album, artist, minSimilarity, markers))
            {
                continue;
            }

            // Exclusive to the expanded edition (not on the base album).
            if (TrackMatcher.MatchTrack(local.Title, other.Tracks, minSimilarity, markers, artist) is null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when every track on <paramref name="baseAlbum"/> appears on <paramref name="expanded"/>,
    /// and expanded has additional tracks.
    /// </summary>
    private static bool IsExpandedEditionOf(
        CatalogAlbum baseAlbum,
        CatalogAlbum expanded,
        string artist,
        double minSimilarity,
        IReadOnlyList<string> markers)
    {
        if (expanded.Tracks.Count <= baseAlbum.Tracks.Count)
        {
            return false;
        }

        foreach (var track in baseAlbum.Tracks)
        {
            if (TrackMatcher.MatchTrack(track.Title, expanded.Tracks, minSimilarity, markers, artist) is null)
            {
                return false;
            }
        }

        return true;
    }

    private sealed class ScoredAlbum(CatalogAlbum album, int score)
    {
        public CatalogAlbum Album { get; } = album;

        public int Score { get; } = score;

        public double Ratio
        {
            get
            {
                if (Album.Tracks.Count <= 0 || Score <= 0)
                {
                    return 0;
                }

                return Math.Min(1.0, (double)Score / Album.Tracks.Count);
            }
        }

        /// <summary>
        /// matchCount * ratio^2 — rewards both owning more songs and covering the release.
        /// </summary>
        public double Fitness => Score * Ratio * Ratio;
    }
}
