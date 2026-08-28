using Jellyfin.Plugin.DeezerTagger.Configuration;

namespace Jellyfin.Plugin.DeezerTagger;

public class MetadataClientFactory
{
    public static readonly MetadataProvider[] AllProvidersInOrder =
    [
        MetadataProvider.Deezer,
        MetadataProvider.MusicBrainz,
        MetadataProvider.Itunes,
        MetadataProvider.Discogs,
        MetadataProvider.OpenOpus
    ];

    private readonly DeezerContextClient _deezer;
    private readonly MusicBrainzContextClient _musicBrainz;
    private readonly ItunesContextClient _itunes;
    private readonly DiscogsContextClient _discogs;
    private readonly OpenOpusContextClient _openOpus;

    public MetadataClientFactory(
        DeezerContextClient deezer,
        MusicBrainzContextClient musicBrainz,
        ItunesContextClient itunes,
        DiscogsContextClient discogs,
        OpenOpusContextClient openOpus)
    {
        _deezer = deezer;
        _musicBrainz = musicBrainz;
        _itunes = itunes;
        _discogs = discogs;
        _openOpus = openOpus;
    }

    public IContextMetadataClient Get(MetadataProvider provider)
        => provider switch
        {
            MetadataProvider.MusicBrainz => _musicBrainz,
            MetadataProvider.Itunes => _itunes,
            MetadataProvider.Discogs => _discogs,
            MetadataProvider.OpenOpus => _openOpus,
            _ => _deezer
        };

    public IReadOnlyList<IContextMetadataClient> GetClients(IEnumerable<MetadataProvider> providers)
        => providers.Select(Get).ToList();
}
