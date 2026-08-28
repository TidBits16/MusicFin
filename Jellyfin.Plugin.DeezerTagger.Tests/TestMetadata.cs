using System.Net.Http;
using Jellyfin.Plugin.DeezerTagger;

namespace Jellyfin.Plugin.DeezerTagger.Tests;

internal static class TestMetadata
{
    public static string CacheDir { get; } = Path.Combine(Path.GetTempPath(), "smarter-music-tagging-tests", Guid.NewGuid().ToString("N"));

    public static DeezerContextClient CreateDeezer()
        => new(new DefaultHttpClientFactory(), new HttpCache(CacheDir), NullLogger<DeezerContextClient>.Instance);

    public static ItunesContextClient CreateItunes()
        => new(new DefaultHttpClientFactory(), new HttpCache(CacheDir), NullLogger<ItunesContextClient>.Instance);

    public static DiscogsContextClient CreateDiscogs()
        => new(new DefaultHttpClientFactory(), new HttpCache(CacheDir), NullLogger<DiscogsContextClient>.Instance);

    public static OpenOpusContextClient CreateOpenOpus()
        => new(new DefaultHttpClientFactory(), new HttpCache(CacheDir), NullLogger<OpenOpusContextClient>.Instance);

    public static MusicBrainzContextClient CreateMusicBrainz()
        => new(new DefaultHttpClientFactory(), new HttpCache(Path.Combine(CacheDir, "mb")), NullLogger<MusicBrainzContextClient>.Instance);

    private sealed class DefaultHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class NullLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
    {
        public static readonly NullLogger<T> Instance = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => false;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }
    }
}
