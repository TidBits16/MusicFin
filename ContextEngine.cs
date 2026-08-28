using System.Collections.Concurrent;
using System.Globalization;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.DeezerTagger.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DeezerTagger;

public class ContextEngine
{
    private static readonly HashSet<string> SkipArtistNames = new(StringComparer.OrdinalIgnoreCase)
    {
        string.Empty,
        "Various Artists",
        "Various"
    };

    private readonly ILibraryManager _library;
    private readonly MetadataClientFactory _metadata;
    private readonly ILogger<ContextEngine> _logger;

    public ContextEngine(ILibraryManager library, MetadataClientFactory metadata, ILogger<ContextEngine> logger)
    {
        _library = library;
        _metadata = metadata;
        _logger = logger;
    }

    public async Task RunAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var cfg = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var providerList = cfg.EffectiveMetadataProviders;
        var clients = _metadata.GetClients(providerList);
        if (clients.Count == 0)
        {
            clients = _metadata.GetClients([Configuration.MetadataProvider.Deezer]);
        }

        var primaryClient = clients[0];
        IReadOnlyList<IContextMetadataClient> fallbackClients = clients.Count > 1
            ? clients.Skip(1).ToList()
            : [];
        var workers = cfg.Workers <= 0 ? 1 : Math.Clamp(cfg.Workers, 1, 4);
        using var gate = new SemaphoreSlim(workers, workers);

        var tracks = _library.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Audio],
            Recursive = true
        }).OfType<Audio>().Where(t => t.Id != Guid.Empty).ToList();

        var albums = _library.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.MusicAlbum],
            Recursive = true
        }).OfType<MusicAlbum>().Where(a => a.Id != Guid.Empty).ToDictionary(a => a.Id);

        var skipSet = new HashSet<string>(cfg.EffectiveSkipArtists, StringComparer.OrdinalIgnoreCase);
        var grouped = tracks
            .GroupBy(AlbumArtistOf, StringComparer.OrdinalIgnoreCase)
            .Where(g => !SkipArtistNames.Contains(g.Key) && !skipSet.Contains(g.Key))
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _logger.LogInformation(
            "SmarterMusicTagging: {Tracks} tracks, {Artists} artists, providers {Providers}, {Workers} workers",
            tracks.Count,
            grouped.Count,
            string.Join(" → ", clients.Select(c => c.ProviderKey)),
            workers);

        if (grouped.Count == 0)
        {
            progress.Report(100);
            return;
        }

        var patches = new ConcurrentDictionary<Guid, Patch>();
        var albumPatches = new ConcurrentDictionary<Guid, Patch>();
        var done = 0;

        await Task.WhenAll(grouped.Select(async g =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await ProcessArtistAsync(
                    PreferredArtistName(g),
                    g.ToList(),
                    cfg,
                    primaryClient,
                    fallbackClients,
                    patches,
                    albumPatches,
                    albums,
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
                var n = Interlocked.Increment(ref done);
                progress.Report(5 + 90.0 * n / grouped.Count);
            }
        })).ConfigureAwait(false);

        progress.Report(95);
        var allPatches = patches.Values.Concat(albumPatches.Values).ToList();
        await Task.WhenAll(allPatches.Select(async p =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await ApplyPatchAsync(p, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SmarterMusicTagging failed to update {Id}", p.ItemId);
            }
            finally
            {
                gate.Release();
            }
        })).ConfigureAwait(false);

        progress.Report(100);
        var fallbackStats = fallbackClients.Count == 0
            ? string.Empty
            : ", fallbacks " + string.Join(", ", fallbackClients.Select(c => $"{c.ProviderKey} {c.HttpCount}/{c.CacheHits}"));
        _logger.LogInformation(
            "SmarterMusicTagging finished: {TrackPatches} track writes, {AlbumPatches} album writes, {Primary} http {Http}/{Cache} cache{Fallback}",
            patches.Count,
            albumPatches.Count,
            primaryClient.ProviderKey,
            primaryClient.HttpCount,
            primaryClient.CacheHits,
            fallbackStats);
    }

    private async Task ProcessArtistAsync(
        string artist,
        IReadOnlyList<Audio> artistTracks,
        PluginConfiguration cfg,
        IContextMetadataClient primaryClient,
        IReadOnlyList<IContextMetadataClient> fallbackClients,
        ConcurrentDictionary<Guid, Patch> patches,
        ConcurrentDictionary<Guid, Patch> albumPatches,
        IReadOnlyDictionary<Guid, MusicAlbum> albums,
        CancellationToken cancellationToken)
    {
        var resolved = await ResolveArtistDiscographyAsync(
            artist,
            artistTracks.Count,
            primaryClient,
            fallbackClients,
            cfg.EffectiveAlbumFetchWorkers,
            cancellationToken).ConfigureAwait(false);
        if (resolved is null)
        {
            return;
        }

        var (matchedArtist, discography, metadataClient) = resolved.Value;

        var localTracks = artistTracks.Select(t => new LocalTrack
        {
            Id = t.Id,
            Title = t.Name ?? string.Empty,
            Album = t.Album,
            IndexNumber = t.IndexNumber
        }).ToList();

        var result = AlbumMatcher.Match(
            artist,
            localTracks,
            discography,
            new AlbumMatcherOptions
            {
                MinTitleSimilarity = cfg.MinTitleSimilarity,
                IgnoreTitleMarkers = cfg.EffectiveIgnoreTitleMarkers
            });

        var summary = string.Join(", ", result.AlbumSummaries.Select(s => $"\"{s.AlbumTitle}\" ({s.TrackCount})"));
        _logger.LogInformation(
            "SmarterMusicTagging: {Artist}: {TrackCount} tracks -> {Summary} | {Provider} artist id {ProviderId}, {Albums} releases scanned, {Unmatched} unmatched",
            artist,
            artistTracks.Count,
            summary,
            metadataClient.ProviderKey,
            matchedArtist.ArtistId,
            result.AlbumsScanned,
            result.UnmatchedCount);

        if (result.UnmatchedCount == artistTracks.Count && artistTracks.Count > 0)
        {
            _logger.LogWarning(
                "SmarterMusicTagging: {Artist}: no {Provider} matches for any track. " +
                "Check Jellyfin track titles, MinTitleSimilarity ({MinSim}), or delete stale files under Jellyfin's cache/deezertagger folder.",
                artist,
                metadataClient.ProviderKey,
                cfg.MinTitleSimilarity);
        }
        else if (result.UnmatchedCount > 0)
        {
            _logger.LogWarning(
                "SmarterMusicTagging: {Artist}: {Unmatched} track(s) had no {Provider} album or single release match",
                artist,
                result.UnmatchedCount,
                metadataClient.ProviderKey);
        }

        var assignmentByTrack = result.Assignments.ToDictionary(a => a.TrackId);
        var albumGenres = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var albumYears = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var albumArtistWrites = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var track in artistTracks)
        {
            if (!assignmentByTrack.TryGetValue(track.Id, out var assignment))
            {
                continue;
            }

            var trackPatch = BuildTrackPatch(track, assignment, cfg, metadataClient.ProviderKey, matchedArtist.Name);
            if (trackPatch is not null)
            {
                patches.AddOrUpdate(track.Id, trackPatch, (_, existing) => existing.Merge(trackPatch));
            }

            if (assignment.Genres.Count > 0)
            {
                albumGenres[assignment.AlbumTitle] = assignment.Genres.ToList();
            }

            if (assignment.Year is > 0)
            {
                albumYears[assignment.AlbumTitle] = assignment.Year.Value;
            }

            if (cfg.WriteAlbumArtists)
            {
                albumArtistWrites[assignment.AlbumTitle] = EffectiveAlbumArtists(assignment, matchedArtist.Name).ToList();
            }
        }

        if (cfg.WriteAlbumNames)
        {
            var assignmentsByAlbum = new Dictionary<Guid, List<TrackAssignment>>();
            foreach (var track in artistTracks)
            {
                if (!assignmentByTrack.TryGetValue(track.Id, out var assignment))
                {
                    continue;
                }

                if (track.GetParent() is not MusicAlbum parentAlbum)
                {
                    continue;
                }

                if (!assignmentsByAlbum.TryGetValue(parentAlbum.Id, out var list))
                {
                    list = [];
                    assignmentsByAlbum[parentAlbum.Id] = list;
                }

                list.Add(assignment);
            }

            foreach (var (albumId, assignments) in assignmentsByAlbum)
            {
                if (assignments.Count == 0 || !albums.TryGetValue(albumId, out var albumItem))
                {
                    continue;
                }

                var titles = assignments
                    .Select(a => a.AlbumTitle)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (titles.Count != 1)
                {
                    continue;
                }

                var newName = titles[0];
                var current = albumItem.Name ?? string.Empty;
                if (current.Equals(newName, StringComparison.Ordinal))
                {
                    continue;
                }

                var patch = new Patch { ItemId = albumId, Item = albumItem, Name = newName };
                albumPatches.AddOrUpdate(albumId, patch, (_, existing) => existing.Merge(patch));
            }
        }

        if (cfg.WriteGenres)
        {
            foreach (var entry in albumGenres)
            {
                foreach (var albumItem in albums.Values)
                {
                    var albumArtists = albumItem.AlbumArtists;
                    if (albumArtists.Count > 0 &&
                        !albumArtists.Any(a => a.Equals(artist, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    var name = Titles.StripMark(albumItem.Name ?? string.Empty, cfg.EffectiveIgnoreTitleMarkers);
                    if (!name.Equals(entry.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (GenreWant(entry.Value, albumItem.Genres) is not { } want)
                    {
                        continue;
                    }

                    var patch = new Patch { ItemId = albumItem.Id, Item = albumItem, Genres = want };
                    albumPatches.AddOrUpdate(albumItem.Id, patch, (_, existing) => existing.Merge(patch));
                }
            }
        }

        if (cfg.WriteYear)
        {
            foreach (var entry in albumYears)
            {
                foreach (var albumItem in albums.Values)
                {
                    var albumArtists = albumItem.AlbumArtists;
                    if (albumArtists.Count > 0 &&
                        !albumArtists.Any(a => a.Equals(artist, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    var name = Titles.StripMark(albumItem.Name ?? string.Empty, cfg.EffectiveIgnoreTitleMarkers);
                    if (!name.Equals(entry.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (albumItem.ProductionYear == entry.Value)
                    {
                        continue;
                    }

                    var patch = new Patch { ItemId = albumItem.Id, Item = albumItem, ProductionYear = entry.Value };
                    albumPatches.AddOrUpdate(albumItem.Id, patch, (_, existing) => existing.Merge(patch));
                }
            }
        }

        if (cfg.WriteAlbumArtists)
        {
            foreach (var entry in albumArtistWrites)
            {
                foreach (var albumItem in albums.Values)
                {
                    if (albumItem is not MusicAlbum musicAlbum)
                    {
                        continue;
                    }

                    var albumArtists = musicAlbum.AlbumArtists;
                    if (albumArtists.Count > 0 &&
                        !albumArtists.Any(a => a.Equals(artist, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    var name = Titles.StripMark(musicAlbum.Name ?? string.Empty, cfg.EffectiveIgnoreTitleMarkers);
                    if (!name.Equals(entry.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (ArtistWant(entry.Value, musicAlbum.AlbumArtists) is not { } want)
                    {
                        continue;
                    }

                    var patch = new Patch { ItemId = musicAlbum.Id, Item = musicAlbum, AlbumArtists = want };
                    albumPatches.AddOrUpdate(musicAlbum.Id, patch, (_, existing) => existing.Merge(patch));
                }
            }
        }
    }

    private async Task<(CatalogArtistInfo Artist, List<CatalogAlbum> Discography, IContextMetadataClient Client)?> ResolveArtistDiscographyAsync(
        string artist,
        int trackCount,
        IContextMetadataClient primaryClient,
        IReadOnlyList<IContextMetadataClient> fallbackClients,
        int fetchWorkers,
        CancellationToken cancellationToken)
    {
        var primary = await TryResolveWithClientAsync(artist, primaryClient, fetchWorkers, cancellationToken).ConfigureAwait(false);
        if (primary is not null)
        {
            return (primary.Value.Artist, primary.Value.Discography, primaryClient);
        }

        foreach (var fallbackClient in fallbackClients)
        {
            _logger.LogInformation(
                "SmarterMusicTagging: {Artist}: no usable {Primary} data ({Count} tracks), trying {Fallback}",
                artist,
                primaryClient.ProviderKey,
                trackCount,
                fallbackClient.ProviderKey);

            var fallback = await TryResolveWithClientAsync(artist, fallbackClient, fetchWorkers, cancellationToken).ConfigureAwait(false);
            if (fallback is null)
            {
                continue;
            }

            _logger.LogInformation(
                "SmarterMusicTagging: {Artist}: matched on fallback provider {Fallback}",
                artist,
                fallbackClient.ProviderKey);

            return (fallback.Value.Artist, fallback.Value.Discography, fallbackClient);
        }

        return null;
    }

    private async Task<(CatalogArtistInfo Artist, List<CatalogAlbum> Discography)?> TryResolveWithClientAsync(
        string artist,
        IContextMetadataClient metadataClient,
        int fetchWorkers,
        CancellationToken cancellationToken)
    {
        var candidates = await metadataClient.GetArtistCandidatesAsync(artist, cancellationToken).ConfigureAwait(false);
        if (candidates.Count == 0)
        {
            _logger.LogWarning(
                "SmarterMusicTagging: no {Provider} artist match for {Artist}",
                metadataClient.ProviderKey,
                artist);
            return null;
        }

        CatalogArtistInfo? matchedArtist = null;
        List<CatalogAlbum> discography = [];
        foreach (var candidate in candidates)
        {
            var discs = await metadataClient.GetArtistDiscographyAsync(
                candidate.ArtistId,
                artist,
                fetchWorkers,
                cancellationToken).ConfigureAwait(false);
            if (discs.Count == 0)
            {
                continue;
            }

            matchedArtist = candidate;
            discography = discs.ToList();
            break;
        }

        if (matchedArtist is null || discography.Count == 0)
        {
            var tried = string.Join(", ", candidates.Select(c => c.ArtistId));
            _logger.LogWarning(
                "SmarterMusicTagging: empty discography for {Artist} after trying {Provider} ids [{Ids}]",
                artist,
                metadataClient.ProviderKey,
                tried);
            return null;
        }

        if (candidates[0].ArtistId != matchedArtist.ArtistId)
        {
            _logger.LogInformation(
                "SmarterMusicTagging: {Artist}: using {Provider} id {Id} ({Name}) — id {SkippedId} had no usable releases",
                artist,
                metadataClient.ProviderKey,
                matchedArtist.ArtistId,
                matchedArtist.Name,
                candidates[0].ArtistId);
        }

        return (matchedArtist, discography);
    }

    private static Patch? BuildTrackPatch(Audio track, TrackAssignment assignment, PluginConfiguration cfg, string providerKey, string catalogArtistName)
    {
        string? albumWrite = null;
        if (cfg.WriteAlbumNames)
        {
            var current = track.Album ?? string.Empty;
            if (!current.Equals(assignment.AlbumTitle, StringComparison.Ordinal))
            {
                albumWrite = assignment.AlbumTitle;
            }
        }

        int? indexWrite = null;
        int? discWrite = null;
        if (cfg.WriteTrackNumbers && assignment.TrackNumber > 0)
        {
            if (track.IndexNumber != assignment.TrackNumber)
            {
                indexWrite = assignment.TrackNumber;
            }

            var wantDisc = assignment.DiscNumber > 0 ? assignment.DiscNumber : 1;
            if (track.ParentIndexNumber != wantDisc)
            {
                discWrite = wantDisc;
            }
        }

        List<string>? trackArtistsWrite = null;
        if (cfg.WriteTrackArtists)
        {
            var want = assignment.TrackArtists.Count > 0
                ? assignment.TrackArtists
                : EffectiveAlbumArtists(assignment, catalogArtistName);
            if (ArtistWant(want, track.Artists) is { } artists)
            {
                trackArtistsWrite = artists;
            }
        }

        List<string>? albumArtistsWrite = null;
        if (cfg.WriteAlbumArtists)
        {
            if (ArtistWant(EffectiveAlbumArtists(assignment, catalogArtistName), track.AlbumArtists) is { } artists)
            {
                albumArtistsWrite = artists;
            }
        }

        List<string>? genreWrite = null;
        if (cfg.WriteGenres && cfg.ApplyAlbumGenresToTracks && assignment.Genres.Count > 0)
        {
            if (GenreWant(assignment.Genres, track.Genres) is { } genres)
            {
                genreWrite = genres;
            }
        }

        string? providerTrackIdWrite = null;
        if (assignment.ProviderTrackId.Length > 0)
        {
            var current = track.GetProviderId(providerKey);
            if (!string.Equals(current, assignment.ProviderTrackId, StringComparison.Ordinal))
            {
                providerTrackIdWrite = assignment.ProviderTrackId;
            }
        }

        int? yearWrite = null;
        if (cfg.WriteYear && assignment.Year is > 0 && track.ProductionYear != assignment.Year)
        {
            yearWrite = assignment.Year;
        }

        if (albumWrite is null && indexWrite is null && discWrite is null && trackArtistsWrite is null && albumArtistsWrite is null &&
            genreWrite is null && providerTrackIdWrite is null && yearWrite is null)
        {
            return null;
        }

        return new Patch
        {
            ItemId = track.Id,
            Item = track,
            Album = albumWrite,
            IndexNumber = indexWrite,
            ParentIndexNumber = discWrite,
            Artists = trackArtistsWrite,
            AlbumArtists = albumArtistsWrite,
            Genres = genreWrite,
            ProviderKey = providerTrackIdWrite is null ? null : providerKey,
            ProviderTrackId = providerTrackIdWrite,
            ProductionYear = yearWrite
        };
    }

    private static IReadOnlyList<string> EffectiveAlbumArtists(TrackAssignment assignment, string catalogArtistName)
    {
        if (assignment.AlbumArtists.Count > 0)
        {
            return assignment.AlbumArtists;
        }

        return catalogArtistName.Length > 0 ? [catalogArtistName] : [];
    }

    private static List<string>? ArtistWant(IReadOnlyList<string> want, IReadOnlyList<string>? current)
        => want.Count > 0 && !Titles.SameNames(want, current ?? []) ? want.ToList() : null;

    private async Task ApplyPatchAsync(Patch p, CancellationToken cancellationToken)
    {
        var item = p.Item ?? _library.GetItemById(p.ItemId);
        if (item is null)
        {
            return;
        }

        var dirty = false;
        if (p.Name is not null && item.Name != p.Name)
        {
            item.Name = p.Name;
            dirty = true;
        }

        if (p.Genres is not null)
        {
            item.Genres = p.Genres.ToArray();
            dirty = true;
        }

        if (p.ProductionYear is not null && item.ProductionYear != p.ProductionYear)
        {
            item.ProductionYear = p.ProductionYear;
            dirty = true;
        }

        if (item is Audio audio)
        {
            if (p.Album is not null && audio.Album != p.Album)
            {
                audio.Album = p.Album;
                dirty = true;
            }

            if (p.IndexNumber is not null && audio.IndexNumber != p.IndexNumber)
            {
                audio.IndexNumber = p.IndexNumber;
                dirty = true;
            }

            if (p.ParentIndexNumber is not null && audio.ParentIndexNumber != p.ParentIndexNumber)
            {
                audio.ParentIndexNumber = p.ParentIndexNumber;
                dirty = true;
            }

            if (p.Artists is not null)
            {
                audio.Artists = p.Artists;
                dirty = true;
            }

            if (p.AlbumArtists is not null)
            {
                audio.AlbumArtists = p.AlbumArtists;
                dirty = true;
            }

            if (p.ProviderKey is not null && p.ProviderTrackId is not null)
            {
                audio.SetProviderId(p.ProviderKey, p.ProviderTrackId);
                dirty = true;
            }
        }

        if (item is MusicAlbum musicAlbum && p.AlbumArtists is not null)
        {
            musicAlbum.AlbumArtists = p.AlbumArtists;
            dirty = true;
        }

        if (dirty)
        {
            await _library.UpdateItemAsync(item, item.GetParent() ?? item, ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
        }
    }

    private static List<string>? GenreWant(IReadOnlyList<string> deezer, IReadOnlyList<string>? current)
    {
        var raw = current ?? [];
        if (deezer.Count > 0)
        {
            return NeedList(deezer, raw) || Genres.NeedsRewrite(raw) ? deezer.ToList() : null;
        }

        if (!Genres.NeedsRewrite(raw))
        {
            return null;
        }

        var cleaned = Genres.PrettyList(raw, 0);
        return cleaned.Count > 0 ? cleaned : null;
    }

    private static bool NeedList(IReadOnlyList<string> want, IReadOnlyList<string> got)
        => want.Count > 0 && !Titles.SameNames(want, got);

    private static string AlbumArtistOf(Audio item)
        => item.AlbumArtists.Count > 0 ? item.AlbumArtists[0]
            : item.Artists.Count > 0 ? item.Artists[0] : string.Empty;

    /// <summary>Most common casing in the group — avoids "toby fox" winning over "Toby Fox".</summary>
    private static string PreferredArtistName(IGrouping<string, Audio> group)
        => group
            .Select(AlbumArtistOf)
            .Where(n => n.Length > 0)
            .GroupBy(n => n, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Key.Count(char.IsUpper))
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Key)
            .DefaultIfEmpty(group.Key)
            .First();

    private sealed class Patch
    {
        public Guid ItemId { get; init; }

        public BaseItem? Item { get; init; }

        public string? Name { get; init; }

        public string? Album { get; init; }

        public int? IndexNumber { get; init; }

        public int? ParentIndexNumber { get; init; }

        public List<string>? Artists { get; init; }

        public List<string>? AlbumArtists { get; init; }

        public List<string>? Genres { get; init; }

        public string? ProviderKey { get; init; }

        public string? ProviderTrackId { get; init; }

        public int? ProductionYear { get; init; }

        public Patch Merge(Patch src) => new()
        {
            ItemId = ItemId,
            Item = Item ?? src.Item,
            Name = src.Name ?? Name,
            Album = src.Album ?? Album,
            IndexNumber = src.IndexNumber ?? IndexNumber,
            ParentIndexNumber = src.ParentIndexNumber ?? ParentIndexNumber,
            Artists = src.Artists ?? Artists,
            AlbumArtists = src.AlbumArtists ?? AlbumArtists,
            Genres = src.Genres ?? Genres,
            ProviderKey = src.ProviderKey ?? ProviderKey,
            ProviderTrackId = src.ProviderTrackId ?? ProviderTrackId,
            ProductionYear = src.ProductionYear ?? ProductionYear
        };
    }
}
