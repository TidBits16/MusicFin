using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.DeezerTagger;

public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<ContextEngine>();
        serviceCollection.AddSingleton<DeezerContextClient>();
        serviceCollection.AddSingleton<MusicBrainzContextClient>();
        serviceCollection.AddSingleton<ItunesContextClient>();
        serviceCollection.AddSingleton<DiscogsContextClient>();
        serviceCollection.AddSingleton<OpenOpusContextClient>();
        serviceCollection.AddSingleton<MetadataClientFactory>();
        serviceCollection.AddSingleton<HttpCache>();
    }
}
