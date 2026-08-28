namespace Jellyfin.Plugin.DeezerTagger;

public interface IContextMetadataClient
{
    string ProviderKey { get; }

    int HttpCount { get; }

    int CacheHits { get; }

    Task<IReadOnlyList<CatalogArtistInfo>> GetArtistCandidatesAsync(string name, CancellationToken cancellationToken);

    Task<IReadOnlyList<CatalogAlbum>> GetArtistDiscographyAsync(
        string artistId,
        string artistName,
        int fetchWorkers,
        CancellationToken cancellationToken);
}
