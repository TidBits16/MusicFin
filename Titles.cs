using System.Text;

namespace Jellyfin.Plugin.MusicFin;

public static class Titles
{
    public static readonly IReadOnlyList<string> DefaultIgnoreTitleMarkers = ["🅴", "[Explicit]"];

    public static string StripMark(string name, IReadOnlyList<string>? markers = null)
    {
        var s = name.Trim();
        foreach (var token in markers ?? DefaultIgnoreTitleMarkers)
        {
            s = StripToken(s, token);
        }

        return s.Trim();
    }

    private static string StripToken(string name, string token)
    {
        var mark = token.Trim();
        if (mark.Length == 0)
        {
            return name;
        }

        var s = name;
        if (s.StartsWith(mark, StringComparison.Ordinal))
        {
            s = s[mark.Length..];
        }

        if (s.EndsWith(mark, StringComparison.Ordinal))
        {
            s = s[..^mark.Length];
        }

        return s.Trim();
    }

    public static string Norm(string text, IReadOnlyList<string>? markers = null)
    {
        var s = StripMark(text, markers).ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormKC);
        s = FoldQuotes(s);
        s = s.Replace("&", " and ", StringComparison.Ordinal);
        var b = new StringBuilder();
        var prevSpace = false;
        foreach (var r in s)
        {
            if ((r is >= 'a' and <= 'z') || (r is >= '0' and <= '9') || r == ' ')
            {
                if (r == ' ')
                {
                    if (prevSpace)
                    {
                        continue;
                    }

                    prevSpace = true;
                }
                else
                {
                    prevSpace = false;
                }

                b.Append(r);
                continue;
            }

            if (!prevSpace)
            {
                b.Append(' ');
                prevSpace = true;
            }
        }

        return b.ToString().Trim();
    }

    /// <summary>Removes spaces for compound-title compares (Black Box Warrior vs BlackBoxWarrior).</summary>
    public static string CompactNorm(string norm)
        => norm.Replace(" ", "", StringComparison.Ordinal);

    /// <summary>
    /// When an exact normalized artist name is present, drop near-misses
    /// (e.g. keep femtanyl, drop Fentanyl at 0.875 similarity).
    /// </summary>
    public static List<CatalogArtistInfo> PreferExactArtistMatches(
        IEnumerable<CatalogArtistInfo> ranked,
        string wantNorm)
    {
        var list = ranked as List<CatalogArtistInfo> ?? ranked.ToList();
        if (list.Count == 0 || wantNorm.Length == 0)
        {
            return list;
        }

        var exact = list.Where(x => Norm(x.Name) == wantNorm).ToList();
        return exact.Count > 0 ? exact : list;
    }

    /// <summary>
    /// True when local and catalog match after stripping IgnoreTitleMarkers and normalizing.
    /// </summary>
    public static bool SameTitleIgnoringMarks(
        string current,
        string catalogTitle,
        IReadOnlyList<string>? markers = null)
        => Norm(current, markers) == Norm(catalogTitle, markers);

    /// <summary>
    /// Whether WriteAlbumNames should replace <paramref name="current"/> with
    /// <paramref name="catalogTitle"/>. False when they only differ by ignore markers,
    /// when local is more specific, or when catalog is a secondary (comp/live) demotion.
    /// </summary>
    public static bool ShouldReplaceAlbumTitle(
        string current,
        string catalogTitle,
        IReadOnlyList<string>? markers = null)
        => !SameTitleIgnoringMarks(current, catalogTitle, markers)
            && !IsMoreSpecificAlbumTitle(current, catalogTitle, markers)
            && !(current.Length > 0
                && !IsSecondaryAlbumTitle(current)
                && IsSecondaryAlbumTitle(catalogTitle));

    /// <summary>
    /// Maps digits commonly used as letter lookalikes in stylized titles (2econd --> second).
    /// </summary>
    public static string FoldLeetDigits(string norm)
    {
        if (norm.Length == 0)
        {
            return norm;
        }

        var b = new StringBuilder(norm.Length);
        foreach (var ch in norm)
        {
            b.Append(ch switch
            {
                '0' => 'o',
                '1' => 'i',
                '2' => 's',
                '3' => 'e',
                '4' => 'a',
                '5' => 's',
                '7' => 't',
                _ => ch
            });
        }

        return b.ToString();
    }

    /// <summary>
    /// Strips a trailing " - Artist" (or similar dash) when the suffix matches the album artist,
    /// including close typos like "Rainbow Kitten Suprise".
    /// </summary>
    public static string StripTrailingArtist(string title, string artist)
    {
        var t = title.Trim();
        var a = artist.Trim();
        if (t.Length == 0 || a.Length == 0)
        {
            return t;
        }

        foreach (var sep in new[] { " - ", " – ", " - ", " -- " })
        {
            var idx = t.LastIndexOf(sep, StringComparison.Ordinal);
            if (idx <= 0)
            {
                continue;
            }

            var suffix = t[(idx + sep.Length)..].Trim();
            if (suffix.Length == 0)
            {
                continue;
            }

            var want = Norm(a);
            var got = Norm(suffix);
            if (got == want || Similarity.Ratio(got, want) >= 0.82)
            {
                return t[..idx].TrimEnd();
            }
        }

        return t;
    }

    /// <summary>
    /// Strips a short trailing parenthetical alternate title (e.g. "Sailboat"), but leaves
    /// version markers like (Live)/(Remix) and longer descriptors like
    /// "Live from Athens Georgia" intact for scoring.
    /// </summary>
    public static string StripShortParenthetical(string title)
    {
        var t = title.Trim();
        if (t.Length == 0 || t[^1] != ')')
        {
            return t;
        }

        var open = t.LastIndexOf('(');
        if (open <= 0)
        {
            return t;
        }

        var inner = t[(open + 1)..^1].Trim();
        if (inner.Length == 0)
        {
            return t;
        }

        if (IsVersionParenthetical(inner))
        {
            return t;
        }

        var words = 0;
        var inWord = false;
        foreach (var ch in inner)
        {
            if (char.IsLetterOrDigit(ch))
            {
                if (!inWord)
                {
                    words++;
                    inWord = true;
                }
            }
            else
            {
                inWord = false;
            }
        }

        if (words == 0 || words > 2 || inner.Length > 24)
        {
            return t;
        }

        return t[..open].TrimEnd();
    }

    /// <summary>
    /// True when parenthetical text marks a recording version/edition rather than an alternate title.
    /// </summary>
    public static bool IsVersionParenthetical(string inner)
    {
        var s = inner.Trim().ToLowerInvariant();
        if (s.Length == 0)
        {
            return false;
        }

        // Single-token version markers.
        if (s is "live" or "remix" or "acoustic" or "instrumental" or "demo"
            or "remaster" or "remastered" or "edit" or "mix" or "cover"
            or "karaoke" or "clean" or "explicit")
        {
            return true;
        }

        // Short multi-word version phrases.
        if (s.StartsWith("live ", StringComparison.Ordinal)
            || s.StartsWith("remix ", StringComparison.Ordinal)
            || s.EndsWith(" remix", StringComparison.Ordinal)
            || s.EndsWith(" mix", StringComparison.Ordinal)
            || s.EndsWith(" edit", StringComparison.Ordinal)
            || s.EndsWith(" remaster", StringComparison.Ordinal)
            || s.EndsWith(" remastered", StringComparison.Ordinal)
            || s is "radio edit" or "live edit" or "acoustic version"
            || s is "instrumental version" or "deluxe edition" or "bonus track")
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// True when <paramref name="localAlbum"/> is a more-specific variant of <paramref name="catalogAlbum"/>
    /// (e.g. "THE ANTIHUMAN" vs "ANTIHUMAN") and should not be demoted on write.
    /// </summary>
    public static bool IsMoreSpecificAlbumTitle(
        string localAlbum,
        string catalogAlbum,
        IReadOnlyList<string>? markers = null)
    {
        var local = Norm(localAlbum, markers);
        var catalog = Norm(catalogAlbum, markers);
        if (catalog.Length == 0 || local.Length <= catalog.Length)
        {
            return false;
        }

        var idx = local.IndexOf(catalog, StringComparison.Ordinal);
        if (idx < 0)
        {
            return false;
        }

        var beforeOk = idx == 0 || local[idx - 1] == ' ';
        var after = idx + catalog.Length;
        return beforeOk && (after == local.Length || local[after] == ' ');
    }

    /// <summary>Greatest-hits / classics / best-of style titles that vacuum up singles.</summary>
    public static bool LooksLikeCompilationTitle(string title)
    {
        var t = Norm(title);
        if (t.Length == 0)
        {
            return false;
        }

        return t.Contains("greatest hits", StringComparison.Ordinal)
            || t.Contains("classics collection", StringComparison.Ordinal)
            || t.Contains("classic collection", StringComparison.Ordinal)
            || t.Contains("best of", StringComparison.Ordinal)
            || t.Contains(" anthology", StringComparison.Ordinal)
            || t.StartsWith("anthology ", StringComparison.Ordinal)
            || t.EndsWith(" collection", StringComparison.Ordinal)
            || t.Contains(" the collection", StringComparison.Ordinal);
    }

    /// <summary>Live tour / concert album titles (e.g. Live in '25, The Land: The Live Album).</summary>
    public static bool LooksLikeLiveTourTitle(string title)
    {
        var t = Norm(title);
        if (t.Length == 0)
        {
            return false;
        }

        // Do not treat song/EP titles like "FNAFdom (Live)" as tour albums.
        return t.StartsWith("live in ", StringComparison.Ordinal)
            || t.StartsWith("live from ", StringComparison.Ordinal)
            || t.StartsWith("live at ", StringComparison.Ordinal)
            || t.Contains(" live in ", StringComparison.Ordinal)
            || t.Contains(" live from ", StringComparison.Ordinal)
            || t.Contains(" live at ", StringComparison.Ordinal)
            || t.Contains("live album", StringComparison.Ordinal)
            || t.EndsWith(" live album", StringComparison.Ordinal);
    }

    /// <summary>Deluxe / spilled / expanded edition markers in album titles.</summary>
    public static bool LooksLikeDeluxeTitle(string title)
    {
        var t = Norm(title);
        if (t.Length == 0)
        {
            return false;
        }

        return t.Contains("spilled", StringComparison.Ordinal)
            || t.Contains("deluxe", StringComparison.Ordinal)
            || t.Contains("expanded", StringComparison.Ordinal)
            || t.Contains("extended edition", StringComparison.Ordinal)
            || t.Contains("anniversary", StringComparison.Ordinal)
            || t.Contains("super deluxe", StringComparison.Ordinal)
            || t.Contains(" bonus", StringComparison.Ordinal)
            || t.EndsWith(" bonus", StringComparison.Ordinal)
            || t.Contains("complete edition", StringComparison.Ordinal);
    }

    /// <summary>
    /// Secondary album tags we should not lock onto during recovery
    /// (compilations / live tours already written by a bad prior match).
    /// </summary>
    public static bool IsSecondaryAlbumTitle(string title)
        => LooksLikeCompilationTitle(title) || LooksLikeLiveTourTitle(title);

    private static string FoldQuotes(string text)
    {
        return text
            .Replace('\u2018', '\'')
            .Replace('\u2019', '\'')
            .Replace('\u201A', '\'')
            .Replace('\u2032', '\'')
            .Replace('\u201C', '"')
            .Replace('\u201D', '"')
            .Replace('\u201E', '"')
            .Replace('\u2033', '"');
    }

    public static List<string> DistinctNames(IEnumerable<string> names)
    {
        var output = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            var text = name.Trim();
            if (text.Length == 0 || !seen.Add(text))
            {
                continue;
            }

            output.Add(text);
        }

        return output;
    }

    public static bool SameNames(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (!a[i].Equals(b[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
