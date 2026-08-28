namespace Jellyfin.Plugin.DeezerTagger;

public static class TrackMatcher
{
    public static CatalogTrack? MatchTrack(
        string title,
        IReadOnlyList<CatalogTrack> tracks,
        double minSimilarity = 0.72,
        IReadOnlyList<string>? ignoreTitleMarkers = null,
        string? albumArtist = null)
    {
        var cleaned = albumArtist is { Length: > 0 }
            ? Titles.StripTrailingArtist(title, albumArtist)
            : title;
        var want = Titles.Norm(cleaned, ignoreTitleMarkers);
        if (want.Length == 0 || tracks.Count == 0)
        {
            return null;
        }

        CatalogTrack? best = null;
        var bestScore = -1.0;
        var bestExact = false;
        var bestTitleLength = int.MaxValue;
        var bestExplicitRank = -1;
        foreach (var t in tracks)
        {
            var score = ScoreTitles(want, t.Title, ignoreTitleMarkers, out var exact, out var gotLength);
            if (score < minSimilarity)
            {
                continue;
            }

            var explicitRank = ExplicitRank(t.Explicit);
            if (score > bestScore
                || (Math.Abs(score - bestScore) < 0.0001 && (
                    exact && !bestExact
                    || exact == bestExact && gotLength < bestTitleLength
                    || exact == bestExact && gotLength == bestTitleLength && explicitRank > bestExplicitRank)))
            {
                best = t;
                bestScore = score;
                bestExact = exact;
                bestTitleLength = gotLength;
                bestExplicitRank = explicitRank;
            }
        }

        return best;
    }

    public static double TitleMatchScore(
        string title,
        string candidate,
        IReadOnlyList<string>? ignoreTitleMarkers = null,
        string? albumArtist = null)
    {
        var cleaned = albumArtist is { Length: > 0 }
            ? Titles.StripTrailingArtist(title, albumArtist)
            : title;
        var want = Titles.Norm(cleaned, ignoreTitleMarkers);
        if (want.Length == 0)
        {
            return 0;
        }

        return ScoreTitles(want, candidate, ignoreTitleMarkers, out _, out _);
    }

    private static double ScoreTitles(
        string wantNorm,
        string candidate,
        IReadOnlyList<string>? ignoreTitleMarkers,
        out bool exact,
        out int gotLength)
    {
        exact = false;
        gotLength = 0;
        var got = Titles.Norm(candidate, ignoreTitleMarkers);
        if (got.Length == 0)
        {
            return 0;
        }

        gotLength = got.Length;
        if (got == wantNorm)
        {
            exact = true;
            return 1;
        }

        var core = Titles.Norm(Titles.StripShortParenthetical(candidate), ignoreTitleMarkers);
        if (core.Length > 0 && core == wantNorm)
        {
            return 1;
        }

        var score = Similarity.Ratio(got, wantNorm);
        if (got.Contains(wantNorm, StringComparison.Ordinal) || wantNorm.Contains(got, StringComparison.Ordinal))
        {
            score = Math.Max(score, 0.84);
        }

        return score;
    }

    private static int ExplicitRank(bool? explicitFlag)
        => explicitFlag switch
        {
            true => 2,
            false => 1,
            _ => 0
        };
}
