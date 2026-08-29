using System.Text;

namespace Jellyfin.Plugin.DeezerTagger;

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
        foreach (var edge in new[] { mark, mark + " ", " " + mark })
        {
            if (s.StartsWith(edge, StringComparison.Ordinal))
            {
                s = s[edge.Length..].TrimStart();
                break;
            }
        }

        foreach (var edge in new[] { mark, " " + mark, mark + " " })
        {
            if (s.EndsWith(edge, StringComparison.Ordinal))
            {
                s = s[..^edge.Length].TrimEnd();
                break;
            }
        }

        return s;
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
    /// longer descriptors like "Live from Athens Georgia" intact for scoring.
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
