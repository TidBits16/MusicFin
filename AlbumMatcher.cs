namespace Jellyfin.Plugin.MusicFin;

public sealed class LocalTrack
{
    public Guid Id { get; init; }

    public string Title { get; init; } = string.Empty;

    public string? Album { get; init; }

    public int? IndexNumber { get; init; }

    /// <summary>Jellyfin MusicAlbum parent id; used for folder consensus over lead singles.</summary>
    public Guid? ParentAlbumId { get; init; }
}

public sealed class TrackAssignment
{
    public Guid TrackId { get; init; }

    public string TrackTitle { get; init; } = string.Empty;

    public string AlbumTitle { get; init; } = string.Empty;

    public int TrackNumber { get; init; }

    public int DiscNumber { get; init; }

    public string ProviderAlbumId { get; init; } = string.Empty;

    public string ProviderTrackId { get; init; } = string.Empty;

    public IReadOnlyList<string> Genres { get; init; } = [];

    public IReadOnlyList<string> TrackArtists { get; init; } = [];

    public IReadOnlyList<string> AlbumArtists { get; init; } = [];

    public int? Year { get; init; }

    public string CoverUrl { get; init; } = string.Empty;

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

        var candidates = catalogAlbums.ToList();
        var scored = candidates
            .Select(album => new ScoredAlbum(
                album,
                ScoreAlbum(localTracks, album, candidates, artist, minSim, markers)))
            .Where(x => x.Score > 0)
            .ToDictionary(x => x.Album.AlbumId, StringComparer.Ordinal);

        var parentSizes = localTracks
            .Where(t => t.ParentAlbumId is { } id && id != Guid.Empty)
            .GroupBy(t => t.ParentAlbumId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        var assignments = new List<TrackAssignment>();

        foreach (var local in localTracks)
        {
            var parentSize = local.ParentAlbumId is { } pid && parentSizes.TryGetValue(pid, out var n)
                ? n
                : 0;
            if (TryAssignAlbum(local, artist, candidates, scored, minSim, markers, parentSize) is not { } assignment)
            {
                continue;
            }

            assignments.Add(assignment);
        }

        assignments = ReconcileParentConsensus(
            localTracks,
            assignments,
            candidates,
            scored,
            artist,
            minSim,
            markers,
            parentSizes);

        var albumCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var assignment in assignments)
        {
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

    /// <summary>
    /// Inside a multi-track Jellyfin album folder, pull lead-single outliers onto the
    /// majority studio/primary release when the song appears on that release.
    /// </summary>
    private static List<TrackAssignment> ReconcileParentConsensus(
        IReadOnlyList<LocalTrack> localTracks,
        List<TrackAssignment> assignments,
        IReadOnlyList<CatalogAlbum> candidates,
        IReadOnlyDictionary<string, ScoredAlbum> scored,
        string artist,
        double minSimilarity,
        IReadOnlyList<string> markers,
        IReadOnlyDictionary<Guid, int> parentSizes)
    {
        var assignmentIndex = assignments
            .Select((a, i) => (a, i))
            .ToDictionary(x => x.a.TrackId, x => x.i);

        foreach (var group in localTracks
                     .Where(t => t.ParentAlbumId is { } id && id != Guid.Empty)
                     .GroupBy(t => t.ParentAlbumId!.Value))
        {
            if (!parentSizes.TryGetValue(group.Key, out var parentSize) || parentSize < 3)
            {
                continue;
            }

            var groupAssignments = new List<TrackAssignment>();
            foreach (var local in group)
            {
                if (assignmentIndex.TryGetValue(local.Id, out var idx))
                {
                    groupAssignments.Add(assignments[idx]);
                }
            }

            if (groupAssignments.Count == 0)
            {
                continue;
            }

            // Majority non-single primary among matched tracks in this folder.
            var primaryVotes = groupAssignments
                .Where(a => !a.IsSingleRelease)
                .GroupBy(a => a.AlbumTitle, StringComparer.OrdinalIgnoreCase)
                .Select(g => (Title: g.Key, Count: g.Count(), ProviderAlbumId: g.First().ProviderAlbumId))
                .OrderByDescending(x => x.Count)
                .ToList();

            if (primaryVotes.Count == 0)
            {
                continue;
            }

            var winner = primaryVotes[0];
            if (winner.Count * 2 < groupAssignments.Count)
            {
                continue;
            }

            if (primaryVotes.Count > 1 && primaryVotes[1].Count == winner.Count)
            {
                continue;
            }

            var winAlbum = candidates.FirstOrDefault(c =>
                string.Equals(c.AlbumId, winner.ProviderAlbumId, StringComparison.Ordinal)
                || string.Equals(c.Title, winner.Title, StringComparison.OrdinalIgnoreCase));
            if (winAlbum is null || winAlbum.IsSingle || !scored.ContainsKey(winAlbum.AlbumId))
            {
                continue;
            }

            foreach (var local in group)
            {
                if (!assignmentIndex.TryGetValue(local.Id, out var idx))
                {
                    continue;
                }

                var current = assignments[idx];
                if (!current.IsSingleRelease
                    && string.Equals(current.AlbumTitle, winAlbum.Title, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var match = TrackMatcher.MatchTrack(local.Title, winAlbum.Tracks, minSimilarity, markers, artist);
                if (match is null)
                {
                    continue;
                }

                assignments[idx] = BuildAssignment(local, winAlbum, match, artist);
            }
        }

        return assignments;
    }

    private static TrackAssignment BuildAssignment(
        LocalTrack local,
        CatalogAlbum album,
        CatalogTrack match,
        string artist)
    {
        var trackNumber = match.TrackPosition > 0 ? match.TrackPosition : 1;
        var discNumber = match.DiskNumber > 0 ? match.DiskNumber : 1;
        return new TrackAssignment
        {
            TrackId = local.Id,
            TrackTitle = local.Title,
            AlbumTitle = album.Title,
            TrackNumber = trackNumber,
            DiscNumber = discNumber,
            ProviderAlbumId = album.AlbumId,
            ProviderTrackId = match.TrackId,
            Genres = album.Genres,
            TrackArtists = ArtistsForTrack(match, album, artist),
            AlbumArtists = AlbumArtistsFor(album, artist),
            Year = album.Year,
            CoverUrl = album.CoverUrl,
            IsSingleRelease = album.IsSingle
        };
    }

    private static TrackAssignment? TryAssignAlbum(
        LocalTrack local,
        string artist,
        IReadOnlyList<CatalogAlbum> candidates,
        IReadOnlyDictionary<string, ScoredAlbum> scored,
        double minSimilarity,
        IReadOnlyList<string> markers,
        int parentSize)
    {
        CatalogAlbum? bestAlbum = null;
        CatalogTrack? bestTrack = null;
        var bestTrackScore = -1.0;
        var bestLocalAlbumScore = -1.0;
        var bestFitness = -1.0;
        var bestExact = false;
        var bestTitleLength = int.MaxValue;
        var bestRatio = -1.0;
        var bestAlbumScore = -1;

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

            var localAlbumScore = 0.0;
            if (local.Album is { Length: > 0 } && !Titles.IsSecondaryAlbumTitle(local.Album))
            {
                // Ignore already-wrong compilation/live tags so recovery can prefer studio releases.
                localAlbumScore = TrackMatcher.TitleMatchScore(local.Album, album.Title, markers, artist);
            }

            var want = Titles.Norm(Titles.StripTrailingArtist(local.Title, artist), markers);
            var got = Titles.Norm(match.Title, markers);
            var exact = got == want;

            if (IsBetterCandidate(
                    trackScore,
                    localAlbumScore,
                    albumScore.Fitness,
                    albumScore.Ratio,
                    albumScore.Score,
                    exact,
                    got.Length,
                    album,
                    bestTrackScore,
                    bestLocalAlbumScore,
                    bestFitness,
                    bestRatio,
                    bestAlbumScore,
                    bestExact,
                    bestTitleLength,
                    bestAlbum,
                    parentSize))
            {
                bestAlbum = album;
                bestTrack = match;
                bestTrackScore = trackScore;
                bestLocalAlbumScore = localAlbumScore;
                bestFitness = albumScore.Fitness;
                bestExact = exact;
                bestTitleLength = got.Length;
                bestRatio = albumScore.Ratio;
                bestAlbumScore = albumScore.Score;
            }
        }

        if (bestAlbum is null || bestTrack is null)
        {
            return null;
        }

        return BuildAssignment(local, bestAlbum, bestTrack, artist);
    }

    private static bool IsBetterCandidate(
        double trackScore,
        double localAlbumScore,
        double fitness,
        double ratio,
        int albumScore,
        bool exact,
        int titleLength,
        CatalogAlbum album,
        double bestTrackScore,
        double bestLocalAlbumScore,
        double bestFitness,
        double bestRatio,
        int bestAlbumScore,
        bool bestExact,
        int bestTitleLength,
        CatalogAlbum? bestAlbum,
        int parentSize)
    {
        if (TitleBand(trackScore) > TitleBand(bestTrackScore) + 0.0001)
        {
            return true;
        }

        if (Math.Abs(TitleBand(trackScore) - TitleBand(bestTrackScore)) > 0.0001)
        {
            return false;
        }

        // Multi-track folders: prefer a well-covered primary over a same-titled single
        // even when a poisoned local album tag names the single (drivers license in SOUR).
        // Standalone 1–2 track single folders keep localAlbumScore precedence below.
        if (parentSize >= 3
            && bestAlbum is not null
            && album.IsSingle != bestAlbum.IsSingle)
        {
            var albumStrong = IsStrongPrimary(album, ratio, albumScore, localAlbumScore);
            var bestStrong = IsStrongPrimary(bestAlbum, bestRatio, bestAlbumScore, bestLocalAlbumScore);
            if (albumStrong != bestStrong)
            {
                return albumStrong;
            }
        }

        // Prefer catalog albums whose title matches the local album tag (THE ANTIHUMAN vs ANTIHUMAN).
        if (TitleBand(localAlbumScore) > TitleBand(bestLocalAlbumScore) + 0.0001)
        {
            return true;
        }

        if (Math.Abs(TitleBand(localAlbumScore) - TitleBand(bestLocalAlbumScore)) > 0.0001)
        {
            return false;
        }

        if (localAlbumScore > bestLocalAlbumScore + 0.0001)
        {
            return true;
        }

        if (Math.Abs(localAlbumScore - bestLocalAlbumScore) > 0.0001)
        {
            return false;
        }

        // Prefer studio/primary releases over compilations and live-tour sets.
        var secondary = SecondaryPenalty(album);
        var bestSecondary = bestAlbum is null ? int.MaxValue : SecondaryPenalty(bestAlbum);
        if (secondary < bestSecondary)
        {
            return true;
        }

        if (secondary > bestSecondary)
        {
            return false;
        }

        // Prefer standard editions over deluxe/spilled/expanded when both match.
        if (bestAlbum is not null)
        {
            if (TitlesSuggestExpansion(album.Title, bestAlbum.Title)
                && album.Tracks.Count < bestAlbum.Tracks.Count
                && Titles.LooksLikeDeluxeTitle(bestAlbum.Title))
            {
                return true;
            }

            if (TitlesSuggestExpansion(bestAlbum.Title, album.Title)
                && bestAlbum.Tracks.Count < album.Tracks.Count
                && Titles.LooksLikeDeluxeTitle(album.Title))
            {
                return false;
            }

            var deluxe = DeluxePenalty(album);
            var bestDeluxe = DeluxePenalty(bestAlbum);
            if (deluxe < bestDeluxe)
            {
                return true;
            }

            if (deluxe > bestDeluxe)
            {
                return false;
            }
        }

        // Prefer a well-covered album/EP over a same-titled single (SOUR vs Drivers License).
        // Keep singles winning for one-off ownership of a track on a huge unrelated album.
        if (bestAlbum is not null && album.IsSingle != bestAlbum.IsSingle)
        {
            var albumStrong = IsStrongPrimary(album, ratio, albumScore, localAlbumScore);
            var bestStrong = IsStrongPrimary(bestAlbum, bestRatio, bestAlbumScore, bestLocalAlbumScore);
            if (albumStrong != bestStrong)
            {
                return albumStrong;
            }
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

        return album.Tracks.Count < (bestAlbum?.Tracks.Count ?? int.MaxValue);
    }

    /// <summary>Compilations and live-tour albums that steal tracks from studio releases.</summary>
    private static int SecondaryPenalty(CatalogAlbum album)
    {
        if (album.IsCompilation || Titles.LooksLikeCompilationTitle(album.Title))
        {
            return 2;
        }

        if (Titles.LooksLikeLiveTourTitle(album.Title))
        {
            return 2;
        }

        return 0;
    }

    private static int DeluxePenalty(CatalogAlbum album)
        => Titles.LooksLikeDeluxeTitle(album.Title) ? 1 : 0;

    private static bool IsStrongPrimary(CatalogAlbum album, double ratio, int score, double localAlbumScore)
        => !album.IsSingle
            && !album.IsCompilation
            && SecondaryPenalty(album) == 0
            && (ratio >= 0.5 || score >= 3 || localAlbumScore >= 0.999);

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
                : match.DiskNumber + ":" + match.TrackPosition + ":" + match.Title;
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

            // Only treat as deluxe/expanded when titles relate (Dreamland ⊂ Dreamland + Bonus).
            // Avoid false subsets from unrelated short-title fuzzy matches.
            if (!TitlesSuggestExpansion(other.Title, album.Title))
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

    private static bool TitlesSuggestExpansion(string baseTitle, string expandedTitle)
    {
        var b = Titles.Norm(baseTitle);
        var e = Titles.Norm(expandedTitle);
        if (b.Length < 4 || e.Length < 4)
        {
            return false;
        }

        return e.Contains(b, StringComparison.Ordinal) || b.Contains(e, StringComparison.Ordinal);
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
            var match = TrackMatcher.MatchTrack(track.Title, expanded.Tracks, minSimilarity, markers, artist);
            if (match is null)
            {
                return false;
            }

            // Require a strong title fit so unrelated short titles don't form fake subsets.
            if (TrackMatcher.TitleMatchScore(track.Title, match.Title, markers, artist) < 0.95)
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
        /// matchCount * ratio^2 - rewards both owning more songs and covering the release.
        /// </summary>
        public double Fitness => Score * Ratio * Ratio;
    }
}
