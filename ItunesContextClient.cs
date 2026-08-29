using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DeezerTagger;

public sealed class ItunesContextClient : IContextMetadataClient
{
    private const string Base = "https://itunes.apple.com";
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(30);

    private readonly PacedHttp _http;
    private readonly object _gate = new();
    private readonly Dictionary<string, List<CatalogArtistInfo>> _artCandidates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CatalogAlbum> _byCollectionId = new(StringComparer.Ordinal);

    public ItunesContextClient(IHttpClientFactory factory, HttpCache cache, ILogger<ItunesContextClient> logger)
    {
        _ = logger;
        _http = new PacedHttp(factory, cache, TimeSpan.FromMilliseconds(200), maxInFlight: 2, userAgent: "MusicFin/1.0");
    }

    public string ProviderKey => "iTunes";

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
            "itunes/search/artist",
            Base + "/search",
            new Dictionary<string, string>
            {
                ["term"] = name,
                ["entity"] = "musicArtist",
                ["limit"] = "20"
            },
            Ttl,
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
            if (!JsonUtil.Str(raw, "wrapperType").Equals("artist", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var got = JsonUtil.Str(raw, "artistName").Trim();
            if (got.Length == 0)
            {
                continue;
            }

            var id = ((int)JsonUtil.Num(raw, "artistId")).ToString(CultureInfo.InvariantCulture);
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
                    ArtistId = id
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
        if (!int.TryParse(artistId, NumberStyles.None, CultureInfo.InvariantCulture, out var artistNumericId) || artistNumericId <= 0)
        {
            return [];
        }

        var payload = await _http.GetJsonAsync(
            "itunes/lookup/albums/" + artistId,
            Base + "/lookup",
            new Dictionary<string, string>
            {
                ["id"] = artistNumericId.ToString(CultureInfo.InvariantCulture),
                ["entity"] = "album",
                ["limit"] = "200"
            },
            Ttl,
            cancellationToken).ConfigureAwait(false);

        if (payload is null)
        {
            return [];
        }

        var collectionIds = JsonUtil.Arr(payload.Value, "results")
            .Where(r => JsonUtil.Str(r, "wrapperType").Equals("collection", StringComparison.OrdinalIgnoreCase))
            .Where(r => ShouldFetchCollection(r, artistName))
            .Select(r => ((int)JsonUtil.Num(r, "collectionId")).ToString(CultureInfo.InvariantCulture))
            .Where(id => id != "0")
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var workers = Math.Clamp(albumFetchWorkers, 1, 4);
        using var gate = new SemaphoreSlim(workers, workers);
        var results = new System.Collections.Concurrent.ConcurrentBag<CatalogAlbum>();

        await Task.WhenAll(collectionIds.Select(async id =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var album = await CollectionByIdAsync(id, artistName, cancellationToken).ConfigureAwait(false);
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

    internal static bool ShouldFetchCollection(JsonElement raw, string artistName)
    {
        var collectionType = JsonUtil.Str(raw, "collectionType").Trim();
        if (collectionType.Length > 0 &&
            !collectionType.Equals("Album", StringComparison.OrdinalIgnoreCase) &&
            !collectionType.Equals("Compilation", StringComparison.OrdinalIgnoreCase) &&
            !collectionType.Equals("Single", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var albumArtist = JsonUtil.Str(raw, "artistName").Trim();
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

    private async Task<CatalogAlbum> CollectionByIdAsync(string collectionId, string artistName, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_byCollectionId.TryGetValue(collectionId, out var hit))
            {
                return hit;
            }
        }

        var payload = await _http.GetJsonAsync(
            "itunes/lookup/tracks/" + collectionId,
            Base + "/lookup",
            new Dictionary<string, string>
            {
                ["id"] = collectionId,
                ["entity"] = "song",
                ["limit"] = "200"
            },
            Ttl,
            cancellationToken).ConfigureAwait(false);

        if (payload is null)
        {
            return new CatalogAlbum();
        }

        var results = JsonUtil.Arr(payload.Value, "results").ToList();
        if (results.Count == 0)
        {
            return new CatalogAlbum();
        }

        var header = results[0];
        var title = JsonUtil.Str(header, "collectionName").Trim();
        if (title.Length == 0)
        {
            title = JsonUtil.Str(header, "collectionCensoredName").Trim();
        }

        if (title.Length == 0)
        {
            return new CatalogAlbum();
        }

        var tracks = new List<CatalogTrack>();
        var position = 0;
        foreach (var raw in results.Skip(1))
        {
            if (!JsonUtil.Str(raw, "wrapperType").Equals("track", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var trackTitle = JsonUtil.Str(raw, "trackName").Trim();
            if (trackTitle.Length == 0)
            {
                continue;
            }

            position++;
            var trackNumber = (int)JsonUtil.Num(raw, "trackNumber");
            if (trackNumber <= 0)
            {
                trackNumber = (int)JsonUtil.Num(raw, "trackCount") > 0 ? position : position;
            }

            if (trackNumber <= 0)
            {
                trackNumber = position;
            }

            var trackId = ((int)JsonUtil.Num(raw, "trackId")).ToString(CultureInfo.InvariantCulture);
            tracks.Add(new CatalogTrack
            {
                Title = trackTitle,
                TrackId = trackId == "0" ? string.Empty : trackId,
                TrackPosition = trackNumber,
                DiskNumber = (int)JsonUtil.Num(raw, "discNumber"),
                ReleaseDate = ParseRelease(JsonUtil.Str(raw, "releaseDate"))
            });
        }

        if (tracks.Count == 0)
        {
            return new CatalogAlbum();
        }

        var albumArtist = JsonUtil.Str(header, "artistName").Trim();
        var genres = GenresFrom(header);
        var album = new CatalogAlbum
        {
            AlbumId = collectionId,
            Title = title,
            AlbumArtists = albumArtist.Length > 0 ? [albumArtist] : [artistName],
            Tracks = tracks,
            Genres = genres,
            ReleaseDate = ParseRelease(JsonUtil.Str(header, "releaseDate")),
            RecordType = MapRecordType(JsonUtil.Str(header, "collectionType")),
            Source = "itunes:" + collectionId,
            CoverUrl = ArtworkUrlFrom(header)
        };

        lock (_gate)
        {
            _byCollectionId[collectionId] = album;
        }

        return album;
    }

    private static List<string> GenresFrom(JsonElement payload)
    {
        var genre = JsonUtil.Str(payload, "primaryGenreName").Trim();
        return genre.Length > 0 ? Genres.PrettyList([genre], 3) : [];
    }

    private static string ArtworkUrlFrom(JsonElement payload)
    {
        var url = JsonUtil.Str(payload, "artworkUrl100").Trim();
        if (url.Length == 0)
        {
            url = JsonUtil.Str(payload, "artworkUrl60").Trim();
        }

        if (url.Length == 0)
        {
            return string.Empty;
        }

        // iTunes serves larger art by swapping the size token in the URL.
        return url
            .Replace("100x100bb", "600x600bb", StringComparison.Ordinal)
            .Replace("60x60bb", "600x600bb", StringComparison.Ordinal);
    }

    private static string MapRecordType(string collectionType)
        => collectionType.Trim().ToLowerInvariant() switch
        {
            "single" => "single",
            "compilation" => "compilation",
            _ => "album"
        };

    private static DateTime? ParseRelease(string raw)
    {
        var s = raw.Trim();
        if (s.Length < 4)
        {
            return null;
        }

        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d)
            && d.Year is >= 1000 and <= 2100)
        {
            return d.Date;
        }

        return null;
    }
}
