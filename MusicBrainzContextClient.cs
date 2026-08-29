using System.Globalization;
using System.Text.Json;
using Jellyfin.Plugin.DeezerTagger.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DeezerTagger;

public sealed class MusicBrainzContextClient : IContextMetadataClient
{
    private const string Base = "https://musicbrainz.org/ws/2";
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(30);

    private readonly PacedHttp _http;
    private readonly object _gate = new();
    private readonly Dictionary<string, List<CatalogArtistInfo>> _artCandidates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CatalogAlbum> _byReleaseGroup = new(StringComparer.Ordinal);

    public MusicBrainzContextClient(IHttpClientFactory factory, HttpCache cache, ILogger<MusicBrainzContextClient> logger)
    {
        _ = logger;
        _http = new PacedHttp(
            factory,
            cache,
            TimeSpan.FromMilliseconds(1100),
            maxInFlight: 1,
            userAgent: BuildUserAgent());
    }

    public string ProviderKey => "MusicBrainz";

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
            "artist",
            new Dictionary<string, string>
            {
                ["query"] = "artist:\"" + name.Replace('"', ' ') + "\"",
                ["limit"] = "20",
                ["fmt"] = "json"
            },
            cancellationToken).ConfigureAwait(false);

        var candidates = payload is { } p
            ? RankArtistSearchResults(JsonUtil.Arr(p, "artists"), want)
            : [];

        lock (_gate)
        {
            _artCandidates[want] = candidates;
        }

        return candidates;
    }

    internal static List<CatalogArtistInfo> RankArtistSearchResults(IEnumerable<JsonElement> data, string wantNorm)
    {
        var ranked = new List<(CatalogArtistInfo Info, double Score, int RankHint)>();
        foreach (var raw in data)
        {
            var got = JsonUtil.Str(raw, "name").Trim();
            var id = JsonUtil.Str(raw, "id").Trim();
            if (got.Length == 0 || id.Length == 0)
            {
                continue;
            }

            var gotN = Titles.Norm(got);
            var score = Similarity.Ratio(gotN, wantNorm);
            if (gotN == wantNorm)
            {
                score = 1;
            }

            var mbScore = JsonUtil.Num(raw, "score");
            if (mbScore > 0)
            {
                score = Math.Max(score, mbScore / 100.0);
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
                score,
                (int)mbScore));
        }

        return ranked
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.RankHint)
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
        var releaseGroups = new List<(string Id, string Title, string RecordType, DateTime? ReleaseDate, List<string> AlbumArtists)>();
        var offset = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var payload = await GetAsync(
                "release-group",
                new Dictionary<string, string>
                {
                    ["artist"] = artistId,
                    ["limit"] = "100",
                    ["offset"] = offset.ToString(CultureInfo.InvariantCulture),
                    ["fmt"] = "json"
                },
                cancellationToken).ConfigureAwait(false);

            if (payload is null)
            {
                break;
            }

            foreach (var raw in JsonUtil.Arr(payload.Value, "release-groups"))
            {
                if (!ShouldFetchReleaseGroup(raw, artistName))
                {
                    continue;
                }

                var id = JsonUtil.Str(raw, "id").Trim();
                var title = JsonUtil.Str(raw, "title").Trim();
                if (id.Length == 0 || title.Length == 0)
                {
                    continue;
                }

                releaseGroups.Add((
                    id,
                    title,
                    MapRecordType(raw),
                    ParseRelease(JsonUtil.Str(raw, "first-release-date")),
                    AlbumArtistsFrom(raw)));
            }

            var count = (int)JsonUtil.Num(payload.Value, "release-group-count");
            offset += 100;
            if (offset >= count)
            {
                break;
            }
        }

        var results = new List<CatalogAlbum>();
        foreach (var group in releaseGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var album = await ReleaseGroupAlbumAsync(group, artistName, cancellationToken).ConfigureAwait(false);
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

    internal static bool ShouldFetchReleaseGroup(JsonElement raw, string artistName)
    {
        var primary = JsonUtil.Str(raw, "primary-type").Trim();
        if (primary.Length > 0 &&
            !primary.Equals("Album", StringComparison.OrdinalIgnoreCase) &&
            !primary.Equals("Single", StringComparison.OrdinalIgnoreCase) &&
            !primary.Equals("EP", StringComparison.OrdinalIgnoreCase))
        {
            var secondary = JsonUtil.Arr(raw, "secondary-types")
                .Select(JsonString)
                .ToList();
            if (!secondary.Any(x => x.Equals("Compilation", StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
        }

        var artists = AlbumArtistsFrom(raw);
        if (artists.Count == 0)
        {
            return true;
        }

        foreach (var artist in artists)
        {
            if (CatalogFilters.IsVariousArtists(artist))
            {
                return false;
            }
        }

        var want = Titles.Norm(artistName);
        if (want.Length == 0)
        {
            return true;
        }

        return artists.Any(aa =>
        {
            var got = Titles.Norm(aa);
            return got == want
                || got.Contains(want, StringComparison.Ordinal)
                || want.Contains(got, StringComparison.Ordinal);
        });
    }

    private async Task<CatalogAlbum> ReleaseGroupAlbumAsync(
        (string Id, string Title, string RecordType, DateTime? ReleaseDate, List<string> AlbumArtists) group,
        string artistName,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_byReleaseGroup.TryGetValue(group.Id, out var hit))
            {
                return hit;
            }
        }

        var payload = await GetAsync(
            "release",
            new Dictionary<string, string>
            {
                ["release-group"] = group.Id,
                ["inc"] = "recordings",
                ["limit"] = "1",
                ["fmt"] = "json"
            },
            cancellationToken).ConfigureAwait(false);

        if (payload is null)
        {
            return new CatalogAlbum();
        }

        var releases = JsonUtil.Arr(payload.Value, "releases").ToList();
        if (releases.Count == 0)
        {
            return new CatalogAlbum();
        }

        var tracks = TracksFromRelease(releases[0]);
        var albumArtists = group.AlbumArtists.Count > 0 ? group.AlbumArtists : [artistName];
        var album = new CatalogAlbum
        {
            AlbumId = group.Id,
            Title = group.Title,
            AlbumArtists = albumArtists,
            Tracks = tracks,
            ReleaseDate = group.ReleaseDate,
            RecordType = group.RecordType,
            Source = "release-group:" + group.Id,
            // Cover Art Archive front art for the release-group (404 when none exists).
            CoverUrl = "https://coverartarchive.org/release-group/" + group.Id + "/front-500"
        };

        lock (_gate)
        {
            _byReleaseGroup[group.Id] = album;
        }

        return album;
    }

    private static List<CatalogTrack> TracksFromRelease(JsonElement release)
    {
        var items = new List<CatalogTrack>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var position = 0;

        foreach (var medium in JsonUtil.Arr(release, "media"))
        {
            foreach (var raw in JsonUtil.Arr(medium, "tracks"))
            {
                var title = JsonUtil.Str(raw, "title").Trim();
                if (title.Length == 0)
                {
                    title = JsonUtil.Obj(raw, "recording") is { } rec
                        ? JsonUtil.Str(rec, "title").Trim()
                        : string.Empty;
                }

                if (title.Length == 0 || !seen.Add(title))
                {
                    continue;
                }

                position++;
                var trackPos = (int)JsonUtil.Num(raw, "position");
                if (trackPos <= 0)
                {
                    trackPos = (int)JsonUtil.Num(raw, "number");
                }

                if (trackPos <= 0)
                {
                    trackPos = position;
                }

                var recordingId = JsonUtil.Obj(raw, "recording") is { } recording
                    ? JsonUtil.Str(recording, "id").Trim()
                    : string.Empty;

                items.Add(new CatalogTrack
                {
                    Title = title,
                    TrackId = recordingId,
                    TrackPosition = trackPos,
                    DiskNumber = (int)JsonUtil.Num(medium, "position")
                });
            }
        }

        return items;
    }

    private static List<string> AlbumArtistsFrom(JsonElement raw)
    {
        var names = new List<string>();
        foreach (var entry in JsonUtil.Arr(raw, "artist-credit"))
        {
            if (JsonUtil.Obj(entry, "artist") is { } artist)
            {
                var name = JsonUtil.Str(artist, "name").Trim();
                if (name.Length > 0)
                {
                    names.Add(name);
                }
            }
        }

        return Titles.DistinctNames(names);
    }

    private static string MapRecordType(JsonElement raw)
    {
        var primary = JsonUtil.Str(raw, "primary-type").Trim();
        if (primary.Equals("Single", StringComparison.OrdinalIgnoreCase))
        {
            return "single";
        }

        if (primary.Equals("EP", StringComparison.OrdinalIgnoreCase))
        {
            return "ep";
        }

        var secondary = JsonUtil.Arr(raw, "secondary-types")
            .Select(JsonString)
            .ToList();
        if (secondary.Any(x => x.Equals("Compilation", StringComparison.OrdinalIgnoreCase)))
        {
            return "compilation";
        }

        return "album";
    }

    private async Task<JsonElement?> GetAsync(
        string path,
        Dictionary<string, string>? query,
        CancellationToken cancellationToken)
    {
        path = path.TrimStart('/');
        return await _http.GetJsonAsync("musicbrainz/" + path, Base + "/" + path, query, Ttl, cancellationToken).ConfigureAwait(false);
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

    private static string JsonString(JsonElement el)
        => el.ValueKind == JsonValueKind.String ? el.GetString()?.Trim() ?? string.Empty : el.ToString().Trim();

    private static string BuildUserAgent()
    {
        var contact = Plugin.Instance?.Configuration.MusicBrainzContact?.Trim();
        if (string.IsNullOrEmpty(contact))
        {
            contact = "https://github.com/TidBits16/MusicFin";
        }

        return "MusicFin/1.0 ( " + contact + " )";
    }
}
