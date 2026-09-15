using System.Collections.Concurrent;
using System.Globalization;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MusicFin.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MusicFin;

public class ContextEngine
{
    private static readonly HashSet<string> SkipArtistNames = new(StringComparer.OrdinalIgnoreCase)
    {
        string.Empty,
        "Various Artists",
        "Various"
    };

    private readonly ILibraryManager _library;
    private readonly IProviderManager _providers;
    private readonly MetadataClientFactory _metadata;
    private readonly HttpCache _cache;
    private readonly ILogger<ContextEngine> _logger;
    private int _forceNext;

    public ContextEngine(
        ILibraryManager library,
        IProviderManager providers,
        MetadataClientFactory metadata,
        HttpCache cache,
        ILogger<ContextEngine> logger)
    {
        _library = library;
        _providers = providers;
        _metadata = metadata;
        _cache = cache;
        _logger = logger;
    }

    public void RequestForce() => Interlocked.Exchange(ref _forceNext, 1);

    public Task RunAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var force = Interlocked.Exchange(ref _forceNext, 0) == 1;
        return RunAsync(force, progress, cancellationToken);
    }

    public async Task RunAsync(bool force, IProgress<double> progress, CancellationToken cancellationToken)
    {
        if (force)
        {
            _cache.Clear();
            _logger.LogInformation("SmarterMusicTagging: force refresh requested (cache cleared)");
        }

        var cfg = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var providerList = cfg.EffectiveMetadataProviders;
        var clients = _metadata.GetClients(providerList);
        if (clients.Count == 0)
        {
            clients = _metadata.GetClients(
            [
                Configuration.MetadataProvider.Deezer,
                Configuration.MetadataProvider.Discogs
            ]);
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

        var musicArtists = _library.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.MusicArtist],
            Recursive = true
        }).OfType<MusicArtist>()
            .Where(a => a.Id != Guid.Empty && !string.IsNullOrWhiteSpace(a.Name))
            .GroupBy(a => a.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<MusicArtist>)g.ToList(), StringComparer.OrdinalIgnoreCase);

        var skipSet = new HashSet<string>(cfg.EffectiveSkipArtists, StringComparer.OrdinalIgnoreCase);
        var grouped = tracks
            .GroupBy(AlbumArtistOf, StringComparer.OrdinalIgnoreCase)
            .Where(g => !SkipArtistNames.Contains(g.Key) && !skipSet.Contains(g.Key))
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _logger.LogInformation(
            "SmarterMusicTagging: {Tracks} tracks, {Artists} artists, providers {Providers}, {Workers} workers ({Mode})",
            tracks.Count,
            grouped.Count,
            string.Join(" --> ", clients.Select(c => c.ProviderKey)),
            workers,
            force ? "force all" : "normal");

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
                    clients.Select(c => c.ProviderKey).ToList(),
                    patches,
                    albumPatches,
                    albums,
                    musicArtists,
                    force,
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
        IReadOnlyList<string> providerKeys,
        ConcurrentDictionary<Guid, Patch> patches,
        ConcurrentDictionary<Guid, Patch> albumPatches,
        IReadOnlyDictionary<Guid, MusicAlbum> albums,
        IReadOnlyDictionary<string, IReadOnlyList<MusicArtist>> musicArtists,
        bool force,
        CancellationToken cancellationToken)
    {
        if (!force
            && ArtistLooksSettled(artist, artistTracks, providerKeys, cfg.EffectiveIgnoreTitleMarkers))
        {
            _logger.LogInformation(
                "SmarterMusicTagging: {Artist}: skipped ({Count} tracks already tagged)",
                artist,
                artistTracks.Count);
            if (cfg.WriteArtistGenres)
            {
                WriteArtistGenresFromTracks(
                    artist,
                    artistTracks,
                    patches,
                    albumPatches,
                    musicArtists,
                    force);
            }

            return;
        }

        var resolved = await ResolveArtistDiscographyAsync(
            artist,
            artistTracks.Count,
            primaryClient,
            fallbackClients,
            cfg.EffectiveAlbumFetchWorkers,
            cancellationToken).ConfigureAwait(false);
        if (resolved is null)
        {
            if (cfg.WriteArtistGenres)
            {
                WriteArtistGenresFromTracks(
                    artist,
                    artistTracks,
                    patches,
                    albumPatches,
                    musicArtists,
                    force);
            }

            return;
        }

        var (matchedArtist, discography, metadataClient) = resolved.Value;
        var writeGenresFromProvider = cfg.WriteGenres
            && string.Equals(metadataClient.ProviderKey, primaryClient.ProviderKey, StringComparison.OrdinalIgnoreCase);

        var localTracks = artistTracks.Select(t =>
        {
            var pathTitle = t.IsFileProtocol ? Titles.TitleFromStoragePath(t.Path) : string.Empty;
            var pathAlbum = t.IsFileProtocol ? Titles.AlbumFromStoragePath(t.Path, artist) : string.Empty;
            return new LocalTrack
            {
                Id = t.Id,
                // Prefer on-disk names so a bad Jellyfin tag (e.g. Live in '25 / Control) can self-heal.
                Title = pathTitle.Length > 0 ? pathTitle : (t.Name ?? string.Empty),
                Album = pathAlbum.Length > 0 ? pathAlbum : t.Album,
                IndexNumber = t.IndexNumber,
                ParentAlbumId = t.GetParent() is MusicAlbum parent ? parent.Id : null
            };
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
                "Check Jellyfin track titles, MinTitleSimilarity ({MinSim}), or delete stale files under Jellyfin's cache/musicfin folder.",
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

            var trackPatch = BuildTrackPatch(
                track,
                assignment,
                cfg,
                metadataClient.ProviderKey,
                matchedArtist.Name,
                writeGenresFromProvider,
                force);
            if (trackPatch is not null)
            {
                patches.AddOrUpdate(track.Id, trackPatch, (_, existing) => existing.Merge(trackPatch));
            }

            if (writeGenresFromProvider && assignment.Genres.Count > 0)
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

                var parentTracks = artistTracks
                    .Where(t => t.GetParent() is MusicAlbum parent && parent.Id == albumId)
                    .ToList();
                if (!AlbumRename.ShouldRenameAlbumEntity(parentTracks.Count, assignments))
                {
                    continue;
                }

                var markers = cfg.EffectiveIgnoreTitleMarkers;
                var catalogTitle = assignments[0].AlbumTitle;
                var current = albumItem.Name ?? string.Empty;
                var folderTitle = string.Empty;
                foreach (var track in parentTracks)
                {
                    if (!track.IsFileProtocol)
                    {
                        continue;
                    }

                    folderTitle = Titles.AlbumFromStoragePath(track.Path, artist);
                    if (folderTitle.Length > 0)
                    {
                        break;
                    }
                }

                // Catalog may only have the combo (Seven + Mary); keep each EP folder's name.
                var desired = Titles.PreferredAlbumWriteTitle(
                    folderTitle.Length > 0 ? folderTitle : current,
                    catalogTitle,
                    markers);

                if (Titles.SameTitleIgnoringMarks(current, desired, markers))
                {
                    continue;
                }

                // Restoring folder EP over an already-written combo, or normal catalog replace.
                if (Titles.SameTitleIgnoringMarks(desired, catalogTitle, markers)
                    && !Titles.ShouldReplaceAlbumTitle(current, catalogTitle, markers))
                {
                    continue;
                }

                var patch = new Patch { ItemId = albumId, Item = albumItem, Name = desired };
                albumPatches.AddOrUpdate(albumId, patch, (_, existing) => existing.Merge(patch));
            }
        }

        if (writeGenresFromProvider)
        {
            foreach (var entry in albumGenres)
            {
                foreach (var albumItem in AlbumsMatchingName(
                             albums.Values, artist, entry.Key, cfg.EffectiveIgnoreTitleMarkers))
                {
                    if (GenreWant(entry.Value, albumItem.Genres, force) is not { } want)
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
                foreach (var albumItem in AlbumsMatchingName(
                             albums.Values, artist, entry.Key, cfg.EffectiveIgnoreTitleMarkers))
                {
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
                foreach (var albumItem in AlbumsMatchingName(
                             albums.Values, artist, entry.Key, cfg.EffectiveIgnoreTitleMarkers))
                {
                    if (ArtistWant(entry.Value, albumItem.AlbumArtists) is not { } want)
                    {
                        continue;
                    }

                    var patch = new Patch { ItemId = albumItem.Id, Item = albumItem, AlbumArtists = want };
                    albumPatches.AddOrUpdate(albumItem.Id, patch, (_, existing) => existing.Merge(patch));
                }
            }
        }

        if (cfg.WriteAlbumCovers)
        {
            var coversByAlbum = new Dictionary<Guid, List<string>>();
            foreach (var track in artistTracks)
            {
                if (!assignmentByTrack.TryGetValue(track.Id, out var assignment)
                    || string.IsNullOrWhiteSpace(assignment.CoverUrl))
                {
                    continue;
                }

                if (track.GetParent() is not MusicAlbum parentAlbum)
                {
                    continue;
                }

                if (!coversByAlbum.TryGetValue(parentAlbum.Id, out var list))
                {
                    list = [];
                    coversByAlbum[parentAlbum.Id] = list;
                }

                list.Add(assignment.CoverUrl);
            }

            foreach (var (albumId, urls) in coversByAlbum)
            {
                if (!albums.TryGetValue(albumId, out var albumItem) || albumItem is not MusicAlbum musicAlbum)
                {
                    continue;
                }

                if (!AlbumRename.HasMajorityCoverage(
                        artistTracks.Count(t =>
                            t.GetParent() is MusicAlbum parent && parent.Id == albumId),
                        urls.Count))
                {
                    continue;
                }

                var distinct = urls
                    .Where(u => !string.IsNullOrWhiteSpace(u))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (distinct.Count != 1)
                {
                    continue;
                }

                // Force refresh always replaces art; otherwise only when renaming the album.
                var renameForce = albumPatches.TryGetValue(albumId, out var existing)
                    && existing.Name is not null
                    && !string.Equals(musicAlbum.Name, existing.Name, StringComparison.Ordinal);

                var patch = new Patch
                {
                    ItemId = albumId,
                    Item = musicAlbum,
                    CoverUrl = distinct[0],
                    CoverForce = force || renameForce
                };
                albumPatches.AddOrUpdate(albumId, patch, (_, prev) => prev.Merge(patch));
            }
        }

        if (cfg.WriteArtistGenres)
        {
            WriteArtistGenresFromTracks(
                artist,
                artistTracks,
                patches,
                albumPatches,
                musicArtists,
                force);
        }
    }

    /// <summary>
    /// Rolls up track genres for this album artist onto matching MusicArtist entities.
    /// </summary>
    private static void WriteArtistGenresFromTracks(
        string artist,
        IReadOnlyList<Audio> artistTracks,
        ConcurrentDictionary<Guid, Patch> patches,
        ConcurrentDictionary<Guid, Patch> albumPatches,
        IReadOnlyDictionary<string, IReadOnlyList<MusicArtist>> musicArtists,
        bool force)
    {
        if (!musicArtists.TryGetValue(artist, out var artistItems) || artistItems.Count == 0)
        {
            return;
        }

        var perTrack = new List<IReadOnlyList<string>>(artistTracks.Count);
        foreach (var track in artistTracks)
        {
            if (patches.TryGetValue(track.Id, out var patch) && patch.Genres is { Count: > 0 })
            {
                perTrack.Add(patch.Genres);
                continue;
            }

            perTrack.Add(track.Genres ?? []);
        }

        var rolled = Genres.PrettyList(perTrack.SelectMany(g => g));
        if (rolled.Count == 0)
        {
            return;
        }

        foreach (var musicArtist in artistItems)
        {
            if (GenreWant(rolled, musicArtist.Genres, force) is not { } want)
            {
                continue;
            }

            var patch = new Patch { ItemId = musicArtist.Id, Item = musicArtist, Genres = want };
            albumPatches.AddOrUpdate(musicArtist.Id, patch, (_, existing) => existing.Merge(patch));
        }
    }

    private static readonly TimeSpan ProviderMissTtl = TimeSpan.FromDays(7);

    private async Task<(CatalogArtistInfo Artist, List<CatalogAlbum> Discography, IContextMetadataClient Client)?> ResolveArtistDiscographyAsync(
        string artist,
        int trackCount,
        IContextMetadataClient primaryClient,
        IReadOnlyList<IContextMetadataClient> fallbackClients,
        int fetchWorkers,
        CancellationToken cancellationToken)
    {
        var primary = await TryResolveWithClientAsync(artist, primaryClient, fetchWorkers, cancellationToken)
            .ConfigureAwait(false);
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

            var fallback = await TryResolveWithClientAsync(artist, fallbackClient, fetchWorkers, cancellationToken)
                .ConfigureAwait(false);
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
        if (HasCachedProviderMiss(metadataClient.ProviderKey, artist))
        {
            _logger.LogInformation(
                "SmarterMusicTagging: {Artist}: skipping {Provider} (cached empty)",
                artist,
                metadataClient.ProviderKey);
            return null;
        }

        var candidates = await metadataClient.GetArtistCandidatesAsync(artist, cancellationToken).ConfigureAwait(false);
        if (candidates.Count == 0)
        {
            CacheProviderMiss(metadataClient.ProviderKey, artist);
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
            CacheProviderMiss(metadataClient.ProviderKey, artist);
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
                "SmarterMusicTagging: {Artist}: using {Provider} id {Id} ({Name}) - id {SkippedId} had no usable releases",
                artist,
                metadataClient.ProviderKey,
                matchedArtist.ArtistId,
                matchedArtist.Name,
                candidates[0].ArtistId);
        }

        return (matchedArtist, discography);
    }

    private bool HasCachedProviderMiss(string providerKey, string artist)
        => _cache.TryGet(ProviderMissKey(providerKey, artist), ProviderMissTtl, out _);

    private void CacheProviderMiss(string providerKey, string artist)
        => _cache.SetObject(ProviderMissKey(providerKey, artist), new { empty = true });

    private static string ProviderMissKey(string providerKey, string artist)
        => "resolve-miss/" + providerKey + "/" + Titles.Norm(artist);

    /// <summary>
    /// True when every track already has a provider id and on-disk album names agree with Jellyfin
    /// (so a bad Live in '25 tag still re-runs when the folder name differs).
    /// </summary>
    private static bool ArtistLooksSettled(
        string artist,
        IReadOnlyList<Audio> tracks,
        IReadOnlyList<string> providerKeys,
        IReadOnlyList<string> markers)
    {
        if (tracks.Count == 0 || providerKeys.Count == 0)
        {
            return false;
        }

        foreach (var track in tracks)
        {
            var tagged = false;
            foreach (var key in providerKeys)
            {
                if (!string.IsNullOrEmpty(track.GetProviderId(key)))
                {
                    tagged = true;
                    break;
                }
            }

            if (!tagged)
            {
                return false;
            }

            if (!track.IsFileProtocol)
            {
                continue;
            }

            var pathAlbum = Titles.AlbumFromStoragePath(track.Path, artist);
            if (pathAlbum.Length > 0
                && !Titles.SameTitleIgnoringMarks(track.Album ?? string.Empty, pathAlbum, markers))
            {
                return false;
            }
        }

        return true;
    }

    private static Patch? BuildTrackPatch(
        Audio track,
        TrackAssignment assignment,
        PluginConfiguration cfg,
        string providerKey,
        string catalogArtistName,
        bool writeGenresFromProvider,
        bool force)
    {
        string? albumWrite = null;
        if (cfg.WriteAlbumNames)
        {
            var markers = cfg.EffectiveIgnoreTitleMarkers;
            var current = track.Album ?? string.Empty;
            var folderTitle = track.IsFileProtocol
                ? Titles.AlbumFromStoragePath(track.Path, catalogArtistName)
                : string.Empty;
            var desired = Titles.PreferredAlbumWriteTitle(
                folderTitle.Length > 0 ? folderTitle : current,
                assignment.AlbumTitle,
                markers);
            if (!Titles.SameTitleIgnoringMarks(current, desired, markers)
                && (!Titles.SameTitleIgnoringMarks(desired, assignment.AlbumTitle, markers)
                    || Titles.ShouldReplaceAlbumTitle(current, assignment.AlbumTitle, markers)))
            {
                albumWrite = desired;
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
        if (writeGenresFromProvider && cfg.ApplyAlbumGenresToTracks && assignment.Genres.Count > 0)
        {
            if (GenreWant(assignment.Genres, track.Genres, force) is { } genres)
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
        IReadOnlyList<string> fromCatalog = assignment.AlbumArtists.Count > 0
            ? assignment.AlbumArtists
            : catalogArtistName.Length > 0 ? [catalogArtistName] : [];

        // Keep the library artist spelling when the provider only differs by case.
        if (catalogArtistName.Length == 0 || fromCatalog.Count == 0)
        {
            return fromCatalog;
        }

        return fromCatalog
            .Select(name => name.Equals(catalogArtistName, StringComparison.OrdinalIgnoreCase)
                ? catalogArtistName
                : name)
            .ToList();
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

        if (item is MusicAlbum coverAlbum && !string.IsNullOrWhiteSpace(p.CoverUrl))
        {
            await TrySaveAlbumCoverAsync(coverAlbum, p.CoverUrl, p.CoverForce, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<bool> TrySaveAlbumCoverAsync(
        MusicAlbum album,
        string url,
        bool force,
        CancellationToken cancellationToken)
    {
        if (!force && album.HasImage(ImageType.Primary, 0))
        {
            return false;
        }

        try
        {
            await _providers.SaveImage(album, url, ImageType.Primary, null, cancellationToken)
                .ConfigureAwait(false);
            _logger.LogInformation(
                "SmarterMusicTagging: saved album cover for {Album} ({Id})",
                album.Name,
                album.Id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "SmarterMusicTagging: could not save album cover for {Album}", album.Name);
            return false;
        }
    }

    /// <summary>
    /// Provider genres win when force is on (overwrite), or when current genres are empty.
    /// Never invents genres from local cleanup alone.
    /// </summary>
    private static List<string>? GenreWant(IReadOnlyList<string> provider, IReadOnlyList<string>? current, bool force)
    {
        if (provider.Count == 0)
        {
            return null;
        }

        var raw = current ?? [];
        if (!force && raw.Count > 0)
        {
            return null;
        }

        return NeedList(provider, raw) ? provider.ToList() : null;
    }

    private static bool NeedList(IReadOnlyList<string> want, IReadOnlyList<string> got)
        => want.Count > 0 && !Titles.SameNames(want, got);

    private static string AlbumArtistOf(Audio item)
        => item.AlbumArtists.Count > 0 ? item.AlbumArtists[0]
            : item.Artists.Count > 0 ? item.Artists[0] : string.Empty;

    private static IEnumerable<MusicAlbum> AlbumsMatchingName(
        IEnumerable<MusicAlbum> albums,
        string artist,
        string albumKey,
        IReadOnlyList<string> markers)
    {
        foreach (var albumItem in albums)
        {
            var albumArtists = albumItem.AlbumArtists;
            if (albumArtists.Count > 0
                && !albumArtists.Any(a => a.Equals(artist, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var name = Titles.StripMark(albumItem.Name ?? string.Empty, markers);
            if (name.Equals(albumKey, StringComparison.OrdinalIgnoreCase))
            {
                yield return albumItem;
            }
        }
    }

    /// <summary>Most common casing in the group - avoids "toby fox" winning over "Toby Fox".</summary>
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

        public string? CoverUrl { get; init; }

        public bool CoverForce { get; init; }

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
            ProductionYear = src.ProductionYear ?? ProductionYear,
            CoverUrl = src.CoverUrl ?? CoverUrl,
            CoverForce = CoverForce || src.CoverForce
        };
    }
}
