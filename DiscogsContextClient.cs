using System.Globalization;
using System.Text.Json;
using Jellyfin.Plugin.DeezerTagger.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DeezerTagger;

public sealed class DiscogsContextClient : IContextMetadataClient
{
    private const string Base = "https://api.discogs.com";
    private const int MaxMastersToFetch = 80;
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(30);

    private readonly PacedHttp _http;
    private readonly object _gate = new();
    private readonly Dictionary<string, List<CatalogArtistInfo>> _artCandidates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CatalogAlbum> _byMasterId = new(StringComparer.Ordinal);

    public DiscogsContextClient(IHttpClientFactory factory, HttpCache cache, ILogger<DiscogsContextClient> logger)
    {
        _ = logger;
        _http = new PacedHttp(
            factory,
            cache,
            TimeSpan.FromMilliseconds(1100),
            maxInFlight: 1,
            userAgent: "MusicFin/1.0 +https://github.com/TidBits16/MusicFin",
            extraHeaders: BuildAuthHeaders());
    }

    public string ProviderKey => "Discogs";

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

        var payload = await GetAsync(
            "database/search",
            new Dictionary<string, string>
            {
                ["q"] = name,
                ["type"] = "artist",
                ["per_page"] = "20"
            },
            cancellationToken).ConfigureAwait(false);

        var candidates = payload is { } p
            ? RankArtistSearchResults(JsonUtil.Arr(p, "results"), want)
            : [];

        lock (_gate)
        {
            _artCandidates[want] = candidates;
        }

        return candidates;
    }

    internal static List<CatalogArtistInfo> RankArtistSearchResults(IEnumerable<JsonElement> data, string wantNorm)
    {
        var ranked = new List<(CatalogArtistInfo Info, double Score)>();
        foreach (var raw in data)
        {
            if (!JsonUtil.Str(raw, "type").Equals("artist", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var got = JsonUtil.Str(raw, "title").Trim();
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
                    Picture = JsonUtil.Str(raw, "thumb").Trim()
                },
                score));
        }

        return ranked
            .OrderByDescending(x => x.Score)
            .Select(x => x.Info)
            .ToList();
    }

    public async Task<IReadOnlyList<CatalogAlbum>> GetArtistDiscographyAsync(
        string artistId,
        string artistName,
        int albumFetchWorkers,
        CancellationToken cancellationToken)
    {
        _ = albumFetchWorkers;
        if (!int.TryParse(artistId, NumberStyles.None, CultureInfo.InvariantCulture, out var artistNumericId) || artistNumericId <= 0)
        {
            return [];
        }

        var masterIds = new List<string>();
        var seenTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var page = 1;

        while (masterIds.Count < MaxMastersToFetch)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var payload = await GetAsync(
                "artists/" + artistNumericId + "/releases",
                new Dictionary<string, string>
                {
                    ["per_page"] = "100",
                    ["page"] = page.ToString(CultureInfo.InvariantCulture),
                    ["sort"] = "year",
                    ["sort_order"] = "desc"
                },
                cancellationToken).ConfigureAwait(false);

            if (payload is null)
            {
                break;
            }

            var batch = JsonUtil.Arr(payload.Value, "releases").ToList();
            if (batch.Count == 0)
            {
                break;
            }

            foreach (var raw in batch)
            {
                if (!JsonUtil.Str(raw, "type").Equals("master", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var title = JsonUtil.Str(raw, "title").Trim();
                if (title.Length == 0)
                {
                    continue;
                }

                var role = JsonUtil.Str(raw, "role").Trim();
                if (role.Length > 0 && !role.Equals("Main", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var listArtist = JsonUtil.Str(raw, "artist").Trim();
                if (listArtist.Length > 0 && !ArtistMatches(listArtist, artistName))
                {
                    continue;
                }

                if (!seenTitles.Add(title))
                {
                    continue;
                }

                var masterId = ((int)JsonUtil.Num(raw, "id")).ToString(CultureInfo.InvariantCulture);
                if (masterId == "0")
                {
                    continue;
                }

                masterIds.Add(masterId);
                if (masterIds.Count >= MaxMastersToFetch)
                {
                    break;
                }
            }

            var pages = JsonUtil.Obj(payload.Value, "pagination") is { } pagination
                ? (int)JsonUtil.Num(pagination, "pages")
                : page;
            if (page >= pages)
            {
                break;
            }

            page++;
        }

        var results = new List<CatalogAlbum>();
        foreach (var masterId in masterIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var album = await MasterByIdAsync(masterId, artistName, cancellationToken).ConfigureAwait(false);
            if (album.AlbumId.Length == 0 || album.Tracks.Count == 0)
            {
                continue;
            }

            if (!CatalogFilters.IsOwnedByArtist(artistName, album))
            {
                continue;
            }

            results.Add(album);
        }

        return results;
    }

    private async Task<CatalogAlbum> MasterByIdAsync(string masterId, string artistName, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_byMasterId.TryGetValue(masterId, out var hit))
            {
                return hit;
            }
        }

        var payload = await GetAsync("masters/" + masterId, null, cancellationToken).ConfigureAwait(false);
        if (payload is null)
        {
            return new CatalogAlbum();
        }

        var p = payload.Value;
        var title = JsonUtil.Str(p, "title").Trim();
        if (title.Length == 0)
        {
            return new CatalogAlbum();
        }

        var albumArtists = AlbumArtistsFrom(p);
        if (albumArtists.Count == 0)
        {
            albumArtists = [artistName];
        }

        var tracks = TracksFrom(p);
        if (tracks.Count == 0)
        {
            return new CatalogAlbum();
        }

        var year = (int)JsonUtil.Num(p, "year");
        DateTime? releaseDate = year is >= 1000 and <= 2100 ? new DateTime(year, 1, 1) : null;
        var album = new CatalogAlbum
        {
            AlbumId = masterId,
            Title = title,
            AlbumArtists = albumArtists,
            Tracks = tracks,
            Genres = GenresFrom(p),
            ReleaseDate = releaseDate,
            RecordType = MapRecordType(p),
            Source = "discogs-master:" + masterId,
            CoverUrl = CoverUrlFrom(p)
        };

        lock (_gate)
        {
            _byMasterId[masterId] = album;
        }

        return album;
    }

    private static string CoverUrlFrom(JsonElement payload)
    {
        string? primary = null;
        string? any = null;
        foreach (var img in JsonUtil.Arr(payload, "images"))
        {
            var uri = JsonUtil.Str(img, "uri").Trim();
            if (uri.Length == 0)
            {
                continue;
            }

            any ??= uri;
            var type = JsonUtil.Str(img, "type").Trim();
            if (type.Equals("primary", StringComparison.OrdinalIgnoreCase))
            {
                primary = uri;
                break;
            }
        }

        return primary ?? any ?? string.Empty;
    }

    private static List<string> AlbumArtistsFrom(JsonElement payload)
    {
        var names = new List<string>();
        foreach (var raw in JsonUtil.Arr(payload, "artists"))
        {
            var name = JsonUtil.Str(raw, "name").Trim();
            if (name.Length > 0)
            {
                names.Add(name);
            }
        }

        return Titles.DistinctNames(names);
    }

    private static List<CatalogTrack> TracksFrom(JsonElement payload)
    {
        var items = new List<CatalogTrack>();
        var position = 0;
        foreach (var raw in JsonUtil.Arr(payload, "tracklist"))
        {
            var type = JsonUtil.Str(raw, "type_").Trim();
            if (type.Length > 0 &&
                !type.Equals("track", StringComparison.OrdinalIgnoreCase) &&
                !type.Equals("Track", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var title = JsonUtil.Str(raw, "title").Trim();
            if (title.Length == 0)
            {
                continue;
            }

            position++;
            var trackPos = ParseTrackPosition(JsonUtil.Str(raw, "position"), position);
            var trackId = JsonUtil.Str(raw, "id").Trim();
            items.Add(new CatalogTrack
            {
                Title = title,
                TrackId = trackId,
                TrackPosition = trackPos,
                DiskNumber = 1
            });
        }

        return items;
    }

    internal static int ParseTrackPosition(string raw, int fallback)
    {
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > 0)
        {
            return n;
        }

        return fallback;
    }

    private static List<string> GenresFrom(JsonElement payload)
    {
        var names = JsonUtil.Arr(payload, "genres")
            .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString()?.Trim() ?? string.Empty : e.ToString().Trim())
            .Concat(JsonUtil.Arr(payload, "styles")
                .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString()?.Trim() ?? string.Empty : e.ToString().Trim()))
            .Where(x => x.Length > 0);
        return Genres.PrettyList(names, 3);
    }

    private static string MapRecordType(JsonElement payload)
    {
        foreach (var raw in JsonUtil.Arr(payload, "formats"))
        {
            var text = raw.ValueKind == JsonValueKind.String
                ? raw.GetString() ?? string.Empty
                : raw.ToString();
            if (text.Contains("Single", StringComparison.OrdinalIgnoreCase))
            {
                return "single";
            }

            if (text.Contains("Compilation", StringComparison.OrdinalIgnoreCase))
            {
                return "compilation";
            }
        }

        return "album";
    }

    private static bool ArtistMatches(string got, string wantArtist)
    {
        if (CatalogFilters.IsVariousArtists(got))
        {
            return false;
        }

        var want = Titles.Norm(wantArtist);
        if (want.Length == 0)
        {
            return true;
        }

        var gotN = Titles.Norm(got);
        return gotN == want
            || gotN.Contains(want, StringComparison.Ordinal)
            || want.Contains(gotN, StringComparison.Ordinal);
    }

    private async Task<JsonElement?> GetAsync(
        string path,
        Dictionary<string, string>? query,
        CancellationToken cancellationToken)
    {
        path = path.TrimStart('/');
        return await _http.GetJsonAsync("discogs/" + path, Base + "/" + path, query, Ttl, cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyDictionary<string, string>? BuildAuthHeaders()
    {
        var token = Plugin.Instance?.Configuration.DiscogsToken?.Trim();
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        return new Dictionary<string, string>
        {
            ["Authorization"] = "Discogs token=" + token
        };
    }
}
