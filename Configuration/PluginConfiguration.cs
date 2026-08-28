using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.DeezerTagger.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    public bool WriteAlbumNames { get; set; } = true;

    public bool WriteTrackNumbers { get; set; } = true;

    public bool WriteTrackArtists { get; set; } = true;

    public bool WriteAlbumArtists { get; set; } = true;

    public bool WriteYear { get; set; } = true;

    /// <summary>All providers in UI order (checked and unchecked).</summary>
    public MetadataProvider[] MetadataProviderOrder { get; set; } = [];

    /// <summary>Checked providers to try, in order.</summary>
    public MetadataProvider[] MetadataProviders { get; set; } = [];

    /// <summary>Legacy single provider; used only when <see cref="MetadataProviders"/> is empty.</summary>
    public MetadataProvider MetadataProvider { get; set; } = MetadataProvider.Deezer;

    /// <summary>Legacy fallback toggle; used only when <see cref="MetadataProviders"/> is empty.</summary>
    public bool? FallbackToOtherProvider { get; set; }

    public IReadOnlyList<MetadataProvider> EffectiveMetadataProviderOrder
    {
        get
        {
            if (MetadataProviderOrder is { Length: > 0 })
            {
                return NormalizeProviderOrder(MetadataProviderOrder);
            }

            if (MetadataProviders is { Length: > 0 })
            {
                return NormalizeProviderOrder(MetadataProviders);
            }

            var primary = MetadataProvider;
            if (FallbackToOtherProvider == true)
            {
                return NormalizeProviderOrder(
                    MetadataClientFactory.AllProvidersInOrder
                        .Where(p => p == primary)
                        .Concat(MetadataClientFactory.AllProvidersInOrder.Where(p => p != primary)));
            }

            return NormalizeProviderOrder([primary]);
        }
    }

    public IReadOnlyList<MetadataProvider> EffectiveMetadataProviders
    {
        get
        {
            if (MetadataProviders is { Length: > 0 })
            {
                var enabled = new HashSet<MetadataProvider>(MetadataProviders);
                return EffectiveMetadataProviderOrder.Where(enabled.Contains).ToList();
            }

            var primary = MetadataProvider;
            if (FallbackToOtherProvider == true)
            {
                return MetadataClientFactory.AllProvidersInOrder
                    .Where(p => p == primary)
                    .Concat(MetadataClientFactory.AllProvidersInOrder.Where(p => p != primary))
                    .ToList();
            }

            return [primary];
        }
    }

    private static IReadOnlyList<MetadataProvider> NormalizeProviderOrder(IEnumerable<MetadataProvider> order)
    {
        var list = new List<MetadataProvider>();
        var seen = new HashSet<MetadataProvider>();
        foreach (var provider in order)
        {
            if (seen.Add(provider))
            {
                list.Add(provider);
            }
        }

        foreach (var provider in MetadataClientFactory.AllProvidersInOrder)
        {
            if (seen.Add(provider))
            {
                list.Add(provider);
            }
        }

        return list;
    }

    /// <summary>Contact URL or email required by MusicBrainz for API User-Agent.</summary>
    public string MusicBrainzContact { get; set; } = "https://github.com/TidBits16/MusicFin";

    /// <summary>Optional Discogs personal access token (higher rate limits). From discogs.com/settings/developers.</summary>
    public string DiscogsToken { get; set; } = string.Empty;

    public bool WriteGenres { get; set; } = true;

    /// <summary>
    /// When WriteGenres is on, copy album genres onto assigned tracks so they stay consistent.
    /// When off, WriteGenres still updates albums; track genres are left as-is.
    /// </summary>
    public bool ApplyAlbumGenresToTracks { get; set; } = true;

    public double MinTitleSimilarity { get; set; } = 0.72;

    /// <summary>Comma-separated suffix/prefix markers stripped from titles before matching (e.g. explicit tags).</summary>
    public string IgnoreTitleMarkers { get; set; } = "🅴,[Explicit]";

    /// <summary>Gets or sets worker count for parallel artist processing. 0 means 1.</summary>
    public int Workers { get; set; }

    public string SkipArtists { get; set; } = string.Empty;

    /// <summary>Parallel album fetches per artist. 0 = default (1). Capped at 4 to stay gentle on CPU and network.</summary>
    public int AlbumFetchWorkers { get; set; }

    public int EffectiveAlbumFetchWorkers
        => AlbumFetchWorkers <= 0 ? 1 : Math.Clamp(AlbumFetchWorkers, 1, 4);

    public IReadOnlyList<string> EffectiveSkipArtists
        => (SkipArtists ?? string.Empty)
            .Split([',', ';', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public IReadOnlyList<string> EffectiveIgnoreTitleMarkers
        => ParseList(IgnoreTitleMarkers, Titles.DefaultIgnoreTitleMarkers);

    private static IReadOnlyList<string> ParseList(string raw, IReadOnlyList<string> fallback)
    {
        var items = (raw ?? string.Empty)
            .Split([',', ';', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return items.Count > 0 ? items : fallback;
    }
}
