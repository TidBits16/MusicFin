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
        var b = new StringBuilder();
        var prevSpace = false;
        foreach (var r in s)
        {
            if ((r is >= 'a' and <= 'z') || (r is >= '0' and <= '9') || r == '&' || r == ' ')
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
