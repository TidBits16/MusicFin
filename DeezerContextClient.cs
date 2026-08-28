using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DeezerTagger;

public sealed class DeezerContextClient : IContextMetadataClient
{
    private const string Base = "https://api.deezer.com";
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(30);

    private readonly PacedHttp _http;
    private readonly object _gate = new();
    private readonly Dictionary<string, CatalogAlbum> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<CatalogArtistInfo>> _artCandidates = new(StringComparer.Ordinal);

    public DeezerContextClient(IHttpClientFactory factory, HttpCache cache, ILogger<DeezerContextClient> logger)
    {
        _ = logger;
        _http = new PacedHttp(factory, cache, TimeSpan.FromMilliseconds(120), maxInFlight: 4);
    }

    public string ProviderKey => "Deezer";

    public int HttpCount => _http.HttpCount;

    public int CacheHits => _http.CacheHits;

    public async Task<IReadOnlyList<CatalogArtistInfo>> GetArtistCandidatesAsync(string name, CancellationToken cancellationToken)
    {
        var want = Titles.Norm(name);
        if (want.Length == 0)
        {
            return [];
        }

        lock (_gate)
        {
            if (_artCandidates.TryGetValue(want, out var cached))
            {
                return cached;
            }
        }

        var payload = await _http.GetJsonAsync(
            "deezer/search/artist",
            Base + "/search/artist",
            new Dictionary<string, string> { ["q"] = name, ["limit"] = "20" },
            Ttl,
            cancellationToken).ConfigureAwait(false);

        var candidates = payload is { } p
            ? RankArtistSearchResults(JsonUtil.Arr(p, "data"), want)
            : [];

        lock (_gate)
        {
            _artCandidates[want] = candidates;
        }

        return candidates;
    }

    public static List<CatalogArtistInfo> RankArtistSearchResults(IEnumerable<JsonElement> data, string wantNorm)
    {
        var ranked = new List<(CatalogArtistInfo Info, double Score, int Fans, int Albums)>();
        foreach (var raw in data)
        {
            var got = JsonUtil.Str(raw, "name").Trim();
            if (got.Length == 0)
            {
                continue;
            }

            var id = ((int)JsonUtil.Num(raw, "id")).ToString(CultureInfo.InvariantCulture);
            if (id == "0")
            {
                continue;
            }

            var gotN = Titles.Norm(got);
            var score = Similarity.Ratio(gotN, wantNorm);
            if (gotN == wantNorm)
            {
                score = 1;
            }

            if (score < 0.86)
            {
                continue;
            }

            ranked.Add((
                new CatalogArtistInfo
                {
                    Name = got,
                    ArtistId = id,
                    Picture = PictureUrl(raw)
                },
                score,
                (int)JsonUtil.Num(raw, "nb_fan"),
                (int)JsonUtil.Num(raw, "nb_album")));
        }

        return ranked
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Fans)
            .ThenByDescending(x => x.Albums)
            .Select(x => x.Info)
            .ToList();
    }

    public async Task<IReadOnlyList<CatalogAlbum>> GetArtistDiscographyAsync(
        string artistId,
        string artistName,
        int albumFetchWorkers,
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(artistId, NumberStyles.None, CultureInfo.InvariantCulture, out var artistNumericId) || artistNumericId <= 0)
        {
            return [];
        }

        var albumIds = new List<int>();
        var path = "artist/" + artistNumericId.ToString(CultureInfo.InvariantCulture) + "/albums";
        var query = new Dictionary<string, string> { ["limit"] = "100" };

        while (path.Length > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var payload = await GetAsync(path, query, cancellationToken).ConfigureAwait(false);
            if (payload is null)
            {
                break;
            }

            foreach (var raw in JsonUtil.Arr(payload.Value, "data"))
            {
                if (!ShouldFetchAlbumFromList(raw, artistName))
                {
                    continue;
                }

                var id = (int)JsonUtil.Num(raw, "id");
                if (id > 0)
                {
                    albumIds.Add(id);
                }
            }

            var next = JsonUtil.Str(payload.Value, "next").Trim();
            if (next.Length == 0)
            {
                break;
            }

            if (!Uri.TryCreate(next, UriKind.Absolute, out var u))
            {
                break;
            }

            path = u.AbsolutePath.TrimStart('/');
            if (path.StartsWith("2.0/", StringComparison.Ordinal))
            {
                path = path[4..];
            }

            query = [];
            foreach (var part in u.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = part.IndexOf('=');
                if (eq < 0)
                {
                    continue;
                }

                query[Uri.UnescapeDataString(part[..eq])] = Uri.UnescapeDataString(part[(eq + 1)..]);
            }
        }

        var ids = albumIds.Distinct().ToList();
        var workers = Math.Clamp(albumFetchWorkers, 1, 4);
        using var gate = new SemaphoreSlim(workers, workers);
        var results = new System.Collections.Concurrent.ConcurrentBag<CatalogAlbum>();

        await Task.WhenAll(ids.Select(async id =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var album = await AlbumByIdAsync(id, cancellationToken).ConfigureAwait(false);
                if (album.AlbumId.Length == 0 || album.Tracks.Count == 0)
                {
                    return;
                }

                if (!CatalogFilters.IsOwnedByArtist(artistName, album))
                {
                    return;
                }

                results.Add(album);
            }
            finally
            {
                gate.Release();
            }
        })).ConfigureAwait(false);

        return results.ToList();
    }

    public static bool ShouldFetchAlbumFromList(JsonElement raw, string artistName)
    {
        var recordType = JsonUtil.Str(raw, "record_type").Trim();
        if (recordType.Length > 0 &&
            !recordType.Equals("album", StringComparison.OrdinalIgnoreCase) &&
            !recordType.Equals("compilation", StringComparison.OrdinalIgnoreCase) &&
            !recordType.Equals("single", StringComparison.OrdinalIgnoreCase) &&
            !recordType.Equals("ep", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var albumArtist = JsonUtil.Obj(raw, "artist") is { } artistEl
            ? JsonUtil.Str(artistEl, "name").Trim()
            : string.Empty;
        if (albumArtist.Length == 0)
        {
            return true;
        }

        if (CatalogFilters.IsVariousArtists(albumArtist))
        {
            return false;
        }

        var want = Titles.Norm(artistName);
        if (want.Length == 0)
        {
            return true;
        }

        var got = Titles.Norm(albumArtist);
        return got == want
            || got.Contains(want, StringComparison.Ordinal)
            || want.Contains(got, StringComparison.Ordinal);
    }

    private async Task<CatalogAlbum> AlbumByIdAsync(int id, CancellationToken cancellationToken)
    {
        var key = id.ToString(CultureInfo.InvariantCulture);
        lock (_gate)
        {
            if (_byId.TryGetValue(key, out var hit))
            {
                return hit;
            }
        }

        var payload = await GetAsync("album/" + id, null, cancellationToken).ConfigureAwait(false);
        if (payload is null || payload.Value.TryGetProperty("error", out _) || !payload.Value.TryGetProperty("id", out _))
        {
            return new CatalogAlbum();
        }

        var p = payload.Value;
        var albumArtists = Titles.DistinctNames(ArtistNames(p, mainOnly: true));
        if (albumArtists.Count == 0)
        {
            albumArtists = Titles.DistinctNames(ArtistNames(p, mainOnly: false).Take(1));
        }

        var embedded = JsonUtil.Obj(p, "tracks") is { } tracksObj ? JsonUtil.Arr(tracksObj, "data") : [];
        var tracks = await AlbumTracksAsync(id, embedded, p.TryGetProperty("nb_tracks", out var nb) ? nb : default, cancellationToken).ConfigureAwait(false);
        var m = new CatalogAlbum
        {
            Genres = GenresFrom(p),
            Source = "album:" + id,
            AlbumId = key,
            Title = JsonUtil.Str(p, "title"),
            AlbumArtists = albumArtists,
            Tracks = tracks,
            ReleaseDate = ParseRelease(JsonUtil.Str(p, "release_date")),
            RecordType = JsonUtil.Str(p, "record_type").Trim()
        };

        lock (_gate)
        {
            _byId[key] = m;
        }

        return m;
    }

    private async Task<List<CatalogTrack>> AlbumTracksAsync(int id, IEnumerable<JsonElement> embedded, JsonElement nb, CancellationToken cancellationToken)
    {
        var items = new List<CatalogTrack>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddTrack(JsonElement raw, int listIndex, bool allowSyntheticPosition)
        {
            if (TrackFromAlbumPayload(raw) is not { } t)
            {
                return;
            }

            if (!seen.Add(t.Title))
            {
                return;
            }

            var hasRealPosition = JsonUtil.Num(raw, "track_position") > 0;
            if (!hasRealPosition && allowSyntheticPosition && listIndex > 0)
            {
                t = WithPosition(t, listIndex);
            }

            items.Add(t);
        }

        var embeddedList = embedded.ToList();
        var embedMissingPositions = embeddedList.Any(raw => JsonUtil.Num(raw, "track_position") <= 0);
        var embedMissingDiscs = embeddedList.Count > 0 && embeddedList.Any(raw => JsonUtil.Num(raw, "disk_number") <= 0);

        // Prefer /tracks whenever the album payload omits real positions/discs.
        // Otherwise list-index fallback invents 1..N and hides multi-disc releases.
        var expected = nb.ValueKind == JsonValueKind.Number ? (int)nb.GetDouble() : 0;
        var needTrackEndpoint = embedMissingPositions || embedMissingDiscs
            || (expected > 0 && embeddedList.Count < expected);

        if (!needTrackEndpoint)
        {
            for (var i = 0; i < embeddedList.Count; i++)
            {
                AddTrack(embeddedList[i], i + 1, allowSyntheticPosition: false);
            }

            return items;
        }

        var listIndex = 0;
        var path = "album/" + id + "/tracks";
        var query = new Dictionary<string, string> { ["limit"] = "100" };
        while (path.Length > 0)
        {
            var page = await GetAsync(path, query, cancellationToken).ConfigureAwait(false);
            if (page is null)
            {
                break;
            }

            foreach (var raw in JsonUtil.Arr(page.Value, "data"))
            {
                listIndex++;
                AddTrack(raw, listIndex, allowSyntheticPosition: true);
            }

            var next = JsonUtil.Str(page.Value, "next").Trim();
            if (next.Length == 0)
            {
                break;
            }

            if (!Uri.TryCreate(next, UriKind.Absolute, out var u))
            {
                break;
            }

            path = u.AbsolutePath.TrimStart('/');
            if (path.StartsWith("2.0/", StringComparison.Ordinal))
            {
                path = path[4..];
            }

            query = [];
            foreach (var part in u.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = part.IndexOf('=');
                if (eq < 0)
                {
                    continue;
                }

                query[Uri.UnescapeDataString(part[..eq])] = Uri.UnescapeDataString(part[(eq + 1)..]);
            }
        }

        if (items.Count == 0)
        {
            for (var i = 0; i < embeddedList.Count; i++)
            {
                AddTrack(embeddedList[i], i + 1, allowSyntheticPosition: true);
            }
        }

        return items;
    }

    private static CatalogTrack? TrackFromAlbumPayload(JsonElement raw)
    {
        var title = JsonUtil.Str(raw, "title").Trim();
        if (title.Length == 0)
        {
            return null;
        }

        var trackId = JsonUtil.IdStr(raw, "id");
        return new CatalogTrack
        {
            Title = title,
            Explicit = ExplicitFrom(raw),
            Artists = Titles.DistinctNames(ArtistNames(raw, mainOnly: false)),
            TrackId = trackId.Length == 0 || trackId == "0" ? string.Empty : trackId,
            TrackPosition = (int)JsonUtil.Num(raw, "track_position"),
            DiskNumber = (int)JsonUtil.Num(raw, "disk_number"),
            ReleaseDate = ParseRelease(JsonUtil.Str(raw, "release_date"))
        };
    }

    private async Task<JsonElement?> GetAsync(string path, Dictionary<string, string>? query, CancellationToken cancellationToken)
    {
        path = path.TrimStart('/');
        var url = path.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? path : Base + "/" + path;
        return await _http.GetJsonAsync("deezer/" + path, url, query, Ttl, cancellationToken).ConfigureAwait(false);
    }

    private static List<string> GenresFrom(JsonElement payload)
    {
        var names = new List<string>();
        var skip = new HashSet<string>(StringComparer.Ordinal) { "unclassified", "unknown", "other", "none" };
        var g = JsonUtil.Obj(payload, "genres");
        if (g is null)
        {
            return names;
        }

        foreach (var raw in JsonUtil.Arr(g.Value, "data"))
        {
            var name = JsonUtil.Str(raw, "name").Trim();
            if (name.Length == 0 || skip.Contains(Titles.Norm(name)))
            {
                continue;
            }

            names.Add(name);
        }

        return Genres.PrettyList(names, 3);
    }

    private static IEnumerable<string> ArtistNames(JsonElement payload, bool mainOnly)
    {
        var artist = JsonUtil.Obj(payload, "artist");
        if (artist is not null)
        {
            var name = JsonUtil.Str(artist.Value, "name").Trim();
            if (name.Length > 0)
            {
                yield return name;
            }
        }

        if (mainOnly)
        {
            yield break;
        }

        foreach (var p in JsonUtil.Arr(payload, "contributors"))
        {
            var name = JsonUtil.Str(p, "name").Trim();
            if (name.Length > 0)
            {
                yield return name;
            }
        }
    }

    private static string PictureUrl(JsonElement payload)
    {
        foreach (var k in new[] { "picture_xl", "picture_big", "picture" })
        {
            var s = JsonUtil.Str(payload, k).Trim();
            if (s.Length > 0)
            {
                return s;
            }
        }

        return string.Empty;
    }

    private static DateTime? ParseRelease(string raw)
    {
        var s = raw.Trim();
        if (s.Length < 4 || s.StartsWith("0000", StringComparison.Ordinal))
        {
            return null;
        }

        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) && d.Year is >= 1000 and <= 2100)
        {
            return d.Date;
        }

        if (int.TryParse(s.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var y) && y is >= 1000 and <= 2100)
        {
            return new DateTime(y, 1, 1);
        }

        return null;
    }

    private static CatalogTrack WithPosition(CatalogTrack t, int position)
    {
        if (t.TrackPosition > 0)
        {
            return t;
        }

        return new CatalogTrack
        {
            Title = t.Title,
            Explicit = t.Explicit,
            Artists = t.Artists,
            TrackId = t.TrackId,
            TrackPosition = position,
            DiskNumber = t.DiskNumber,
            ReleaseDate = t.ReleaseDate
        };
    }

    private static bool? ExplicitFrom(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (payload.TryGetProperty("explicit_content_lyrics", out var code) && code.ValueKind != JsonValueKind.Null)
        {
            var n = code.ValueKind == JsonValueKind.Number ? (int)code.GetDouble() : 0;
            if (n == 1)
            {
                return true;
            }

            if (n is 0 or 3)
            {
                return false;
            }

            if (n == 2)
            {
                return null;
            }
        }

        return JsonUtil.Bool(payload, "explicit_lyrics");
    }
}
