using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DeezerTagger;

public sealed class OpenOpusContextClient : IContextMetadataClient
{
    private const string Base = "https://api.openopus.org";
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(30);

    private readonly PacedHttp _http;
    private readonly object _gate = new();
    private readonly Dictionary<string, List<CatalogArtistInfo>> _artCandidates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CatalogAlbum> _byWorkId = new(StringComparer.Ordinal);

    public OpenOpusContextClient(IHttpClientFactory factory, HttpCache cache, ILogger<OpenOpusContextClient> logger)
    {
        _ = logger;
        _http = new PacedHttp(factory, cache, TimeSpan.FromMilliseconds(250), maxInFlight: 2, userAgent: "MusicFin/1.0");
    }

    public string ProviderKey => "OpenOpus";

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

        var query = SearchToken(name);
        var payload = await GetAsync("composer/list/search/" + Uri.EscapeDataString(query) + ".json", null, cancellationToken).ConfigureAwait(false);
        var candidates = payload is { } p
            ? RankComposerSearchResults(JsonUtil.Arr(p, "composers"), want, name)
            : [];

        if (candidates.Count == 0)
        {
            payload = await GetAsync("omnisearch/" + Uri.EscapeDataString(query) + "/0.json", null, cancellationToken).ConfigureAwait(false);
            if (payload is { } omnisearch)
            {
                var composers = JsonUtil.Arr(omnisearch, "results")
                    .Select(r => JsonUtil.Obj(r, "composer"))
                    .Where(c => c is not null)
                    .Select(c => c!.Value);
                candidates = RankComposerSearchResults(composers, want, name);
            }
        }

        lock (_gate)
        {
            _artCandidates[want] = candidates;
        }

        return candidates;
    }

    internal static List<CatalogArtistInfo> RankComposerSearchResults(IEnumerable<JsonElement> data, string wantNorm, string rawName)
    {
        var ranked = new List<(CatalogArtistInfo Info, double Score)>();
        foreach (var raw in data)
        {
            var complete = JsonUtil.Str(raw, "complete_name").Trim();
            var shortName = JsonUtil.Str(raw, "name").Trim();
            var got = complete.Length > 0 ? complete : shortName;
            if (got.Length == 0)
            {
                continue;
            }

            var id = JsonUtil.Str(raw, "id").Trim();
            if (id.Length == 0)
            {
                continue;
            }

            var gotN = Titles.Norm(got);
            var shortN = Titles.Norm(shortName);
            var score = Math.Max(Similarity.Ratio(gotN, wantNorm), Similarity.Ratio(shortN, wantNorm));
            if (gotN == wantNorm || shortN == wantNorm)
            {
                score = 1;
            }

            if (score < 0.72 && !ContainsAllNameParts(got, rawName))
            {
                continue;
            }

            ranked.Add((
                new CatalogArtistInfo
                {
                    Name = got,
                    ArtistId = id,
                    Picture = JsonUtil.Str(raw, "portrait").Trim()
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
        if (string.IsNullOrWhiteSpace(artistId))
        {
            return [];
        }

        var payload = await GetAsync(
            "work/list/composer/" + Uri.EscapeDataString(artistId) + "/genre/all.json",
            null,
            cancellationToken).ConfigureAwait(false);

        if (payload is null)
        {
            return [];
        }

        var composerEpoch = JsonUtil.Obj(payload.Value, "composer") is { } composer
            ? JsonUtil.Str(composer, "epoch").Trim()
            : string.Empty;
        var composerName = JsonUtil.Obj(payload.Value, "composer") is { } composerInfo
            ? JsonUtil.Str(composerInfo, "complete_name").Trim()
            : string.Empty;
        if (composerName.Length == 0)
        {
            composerName = artistName;
        }

        var results = new List<CatalogAlbum>();
        foreach (var raw in JsonUtil.Arr(payload.Value, "works"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var workId = JsonUtil.Str(raw, "id").Trim();
            var title = JsonUtil.Str(raw, "title").Trim();
            if (workId.Length == 0 || title.Length == 0)
            {
                continue;
            }

            var subtitle = JsonUtil.Str(raw, "subtitle").Trim();
            var trackTitle = subtitle.Length > 0 ? title + ": " + subtitle : title;
            var workGenre = JsonUtil.Str(raw, "genre").Trim();
            var genres = GenresForWork(workGenre, composerEpoch);
            var album = WorkAlbum(workId, title, trackTitle, composerName, genres);
            if (album.Tracks.Count > 0)
            {
                results.Add(album);
            }
        }

        return results;
    }

    private CatalogAlbum WorkAlbum(string workId, string albumTitle, string trackTitle, string artistName, List<string> genres)
    {
        lock (_gate)
        {
            if (_byWorkId.TryGetValue(workId, out var hit))
            {
                return hit;
            }
        }

        var album = new CatalogAlbum
        {
            AlbumId = workId,
            Title = albumTitle,
            AlbumArtists = [artistName],
            Tracks =
            [
                new CatalogTrack
                {
                    Title = trackTitle,
                    TrackId = workId,
                    TrackPosition = 1,
                    DiskNumber = 1
                }
            ],
            Genres = genres,
            RecordType = "single",
            Source = "openopus:" + workId
        };

        lock (_gate)
        {
            _byWorkId[workId] = album;
        }

        return album;
    }

    internal static List<string> GenresForWork(string workGenre, string epoch)
    {
        var names = new List<string>();
        if (workGenre.Length > 0 && !workGenre.Equals("Popular", StringComparison.OrdinalIgnoreCase))
        {
            names.Add(workGenre);
        }

        if (epoch.Length > 0)
        {
            names.Add(epoch);
        }

        names.Add("Classical");
        return Genres.PrettyList(names, 3);
    }

    private static string SearchToken(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var token = parts.Length > 0 ? parts[^1] : name;
        token = token.Trim().ToLowerInvariant();
        return token.Length >= 3 ? token : name.Trim().ToLowerInvariant();
    }

    private static bool ContainsAllNameParts(string candidate, string rawName)
    {
        var parts = rawName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        var got = Titles.Norm(candidate);
        return parts.All(p => got.Contains(Titles.Norm(p), StringComparison.Ordinal));
    }

    private async Task<JsonElement?> GetAsync(string path, Dictionary<string, string>? query, CancellationToken cancellationToken)
    {
        path = path.TrimStart('/');
        return await _http.GetJsonAsync("openopus/" + path, Base + "/" + path, query, Ttl, cancellationToken).ConfigureAwait(false);
    }
}
