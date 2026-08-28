namespace Jellyfin.Plugin.DeezerTagger;

public static class TrackMatcher
{
    public static CatalogTrack? MatchTrack(
        string title,
        IReadOnlyList<CatalogTrack> tracks,
        double minSimilarity = 0.72,
        IReadOnlyList<string>? ignoreTitleMarkers = null)
    {
        var want = Titles.Norm(title, ignoreTitleMarkers);
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
            var got = Titles.Norm(t.Title, ignoreTitleMarkers);
            if (got.Length == 0)
            {
                continue;
            }

            var score = Similarity.Ratio(got, want);
            var exact = got == want;
            if (exact)
            {
                score = 1;
            }
            else if (got.Contains(want, StringComparison.Ordinal) || want.Contains(got, StringComparison.Ordinal))
            {
                score = Math.Max(score, 0.84);
            }

            if (score < minSimilarity)
            {
                continue;
            }

            var explicitRank = ExplicitRank(t.Explicit);
            if (score > bestScore
                || (Math.Abs(score - bestScore) < 0.0001 && (
                    exact && !bestExact
                    || exact == bestExact && got.Length < bestTitleLength
                    || exact == bestExact && got.Length == bestTitleLength && explicitRank > bestExplicitRank)))
            {
                best = t;
                bestScore = score;
                bestExact = exact;
                bestTitleLength = got.Length;
                bestExplicitRank = explicitRank;
            }
        }

        return best;
    }

    public static double TitleMatchScore(string title, string candidate, IReadOnlyList<string>? ignoreTitleMarkers = null)
    {
        var want = Titles.Norm(title, ignoreTitleMarkers);
        var got = Titles.Norm(candidate, ignoreTitleMarkers);
        if (want.Length == 0 || got.Length == 0)
        {
            return 0;
        }

        if (got == want)
        {
            return 1;
        }

        var score = Similarity.Ratio(got, want);
        if (got.Contains(want, StringComparison.Ordinal) || want.Contains(got, StringComparison.Ordinal))
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
