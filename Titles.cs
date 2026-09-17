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
    /// when local is more specific, when catalog is a combo expansion (Seven → Seven + Mary),
    /// or when catalog is a secondary (comp/live) demotion.
    /// </summary>
    public static bool ShouldReplaceAlbumTitle(
        string current,
        string catalogTitle,
        IReadOnlyList<string>? markers = null)
        => !SameTitleIgnoringMarks(current, catalogTitle, markers)
            && !IsMoreSpecificAlbumTitle(current, catalogTitle, markers)
            && !IsComboExpansionOf(current, catalogTitle, markers)
            && !(current.Length > 0
                && !IsSecondaryAlbumTitle(current)
                && IsSecondaryAlbumTitle(catalogTitle));

    /// <summary>
    /// Prefer a non-secondary folder/EP title over a catalog combo that merely contains it
    /// (e.g. keep <c>Seven</c> / <c>Mary</c> instead of writing <c>Seven + Mary</c>).
    /// </summary>
    public static string PreferredAlbumWriteTitle(
        string folderOrLocalTitle,
        string catalogTitle,
        IReadOnlyList<string>? markers = null)
    {
        var local = (folderOrLocalTitle ?? string.Empty).Trim();
        var catalog = (catalogTitle ?? string.Empty).Trim();
        if (local.Length > 0
            && catalog.Length > 0
            && !IsSecondaryAlbumTitle(local)
            && IsComboExpansionOf(local, catalog, markers))
        {
            return local;
        }

        return catalog.Length > 0 ? catalog : local;
    }

    /// <summary>
    /// Re-applies IgnoreTitleMarkers from <paramref name="previousTitle"/> onto
    /// <paramref name="newTitle"/> so ExplicitFin marks (e.g. 🅴) survive catalog renames.
    /// </summary>
    public static string PreserveIgnoreMarkers(
        string previousTitle,
        string newTitle,
        IReadOnlyList<string>? markers = null)
    {
        var marks = markers ?? DefaultIgnoreTitleMarkers;
        var prev = (previousTitle ?? string.Empty).Trim();
        var next = (newTitle ?? string.Empty).Trim();
        if (next.Length == 0 || prev.Length == 0)
        {
            return next;
        }

        // Already marked — leave alone.
        if (!string.Equals(next, StripMark(next, marks), StringComparison.Ordinal))
        {
            return next;
        }

        foreach (var token in marks)
        {
            var mark = token.Trim();
            if (mark.Length == 0)
            {
                continue;
            }

            var prepend = prev.StartsWith(mark, StringComparison.Ordinal);
            var append = prev.EndsWith(mark, StringComparison.Ordinal);
            if (!prepend && !append)
            {
                continue;
            }

            return prepend ? mark + " " + next : next + " " + mark;
        }

        return next;
    }

    /// <summary>
    /// True when <paramref name="part"/> is a whole-token phrase inside a longer
    /// <paramref name="whole"/> (Seven ⊂ Seven + Mary / Mary ⊂ Seven + Mary).
    /// </summary>
    public static bool IsComboExpansionOf(
        string part,
        string whole,
        IReadOnlyList<string>? markers = null)
    {
        var a = Norm(part, markers);
        var b = Norm(whole, markers);
        if (a.Length == 0 || b.Length <= a.Length)
        {
            return false;
        }

        var idx = b.IndexOf(a, StringComparison.Ordinal);
        if (idx < 0)
        {
            return false;
        }

        var beforeOk = idx == 0 || b[idx - 1] == ' ';
        var after = idx + a.Length;
        var afterOk = after == b.Length || b[after] == ' ';
        return beforeOk && afterOk;
    }
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
    /// Track title from a storage file name: strips leading disc/track prefixes
    /// (<c>01-07 Stressed Out</c>, <c>07 - Ride</c>, <c>07. Fairly Local</c>).
    /// </summary>
    public static string TitleFromFileName(string fileNameWithoutExtension)
    {
        var s = (fileNameWithoutExtension ?? string.Empty).Trim();
        if (s.Length == 0)
        {
            return s;
        }

        if (TryStripDiscTrackPrefix(s, out var discTrack))
        {
            return discTrack;
        }

        if (TryStripNumberDashPrefix(s, out var dashed))
        {
            return dashed;
        }

        if (TryStripPaddedTrackPrefix(s, out var padded))
        {
            return padded;
        }

        return s;
    }

    /// <summary>Album title from a folder name, removing leading/trailing artist when known.</summary>
    public static string AlbumFromDirectoryName(string directoryName, string? artist = null)
    {
        var s = (directoryName ?? string.Empty).Trim();
        if (s.Length == 0 || string.IsNullOrWhiteSpace(artist))
        {
            return s;
        }

        s = StripTrailingArtist(s, artist);
        return StripLeadingArtist(s, artist);
    }

    public static string TitleFromStoragePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return TitleFromFileName(System.IO.Path.GetFileNameWithoutExtension(path));
    }

    public static string AlbumFromStoragePath(string? path, string? artist = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var dir = System.IO.Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(dir))
        {
            return string.Empty;
        }

        var name = System.IO.Path.GetFileName(
            dir.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
        return AlbumFromDirectoryName(name ?? string.Empty, artist);
    }

    /// <summary>
    /// Strips a leading "Artist - " when the prefix matches the album artist.
    /// </summary>
    public static string StripLeadingArtist(string title, string artist)
    {
        var t = title.Trim();
        var a = artist.Trim();
        if (t.Length == 0 || a.Length == 0)
        {
            return t;
        }

        foreach (var sep in new[] { " - ", " – ", " -- " })
        {
            var idx = t.IndexOf(sep, StringComparison.Ordinal);
            if (idx <= 0)
            {
                continue;
            }

            var prefix = t[..idx].Trim();
            if (prefix.Length == 0)
            {
                continue;
            }

            var want = Norm(a);
            var got = Norm(prefix);
            if (got == want || Similarity.Ratio(got, want) >= 0.82)
            {
                return t[(idx + sep.Length)..].TrimStart();
            }
        }

        return t;
    }

    private static bool TryStripDiscTrackPrefix(string s, out string rest)
    {
        rest = s;
        var i = 0;
        if (!TakeDigits(s, ref i, 1, 2))
        {
            return false;
        }

        if (i >= s.Length || s[i] is not ('-' or '_' or '.'))
        {
            return false;
        }

        i++;
        if (!TakeDigits(s, ref i, 1, 3))
        {
            return false;
        }

        if (i >= s.Length || s[i] is not (' ' or '-' or '_' or '.'))
        {
            return false;
        }

        while (i < s.Length && s[i] is ' ' or '-' or '_' or '.')
        {
            i++;
        }

        if (i >= s.Length)
        {
            return false;
        }

        rest = s[i..].Trim();
        return rest.Length > 0;
    }

    private static bool TryStripNumberDashPrefix(string s, out string rest)
    {
        rest = s;
        var i = 0;
        if (!TakeDigits(s, ref i, 1, 3))
        {
            return false;
        }

        // Require " - " (or en-dash) so "99 Problems" / "7 Years" stay intact.
        if (i + 2 < s.Length && s[i] == ' ' && s[i + 1] is '-' or '–' && s[i + 2] == ' ')
        {
            rest = s[(i + 3)..].Trim();
            return rest.Length > 0;
        }

        return false;
    }

    private static bool TryStripPaddedTrackPrefix(string s, out string rest)
    {
        rest = s;
        // Exactly two digits (01 Title / 07. Title).
        if (s.Length < 3 || !char.IsDigit(s[0]) || !char.IsDigit(s[1]))
        {
            return false;
        }

        var i = 2;
        var sep = s[i];
        if (sep is not (' ' or '-' or '_' or '.'))
        {
            return false;
        }

        // Bare "NN Title" (single space) only when zero-padded — keeps "99 Problems".
        if (sep == ' ' && s[0] != '0')
        {
            return false;
        }

        while (i < s.Length && s[i] is ' ' or '-' or '_' or '.')
        {
            i++;
        }

        if (i >= s.Length || char.IsDigit(s[i]))
        {
            return false;
        }

        rest = s[i..].Trim();
        return rest.Length > 0;
    }

    private static bool TakeDigits(string s, ref int i, int min, int max)
    {
        var start = i;
        while (i < s.Length && i - start < max && char.IsDigit(s[i]))
        {
            i++;
        }

        return i - start >= min;
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

    /// <summary>
    /// True when the lists match ignoring case. Provider casing (e.g. Discogs "nf")
    /// must not rewrite library casing ("NF", "femtanyl").
    /// </summary>
    public static bool SameNames(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (!a[i].Equals(b[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}
