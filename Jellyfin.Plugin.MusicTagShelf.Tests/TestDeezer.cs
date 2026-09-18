using System.Net.Http;
using Jellyfin.Plugin.MusicTagShelf;

namespace Jellyfin.Plugin.MusicTagShelf.Tests;

internal static class TestDeezer
{
    public static DeezerContextClient CreateClient()
    {
        var cacheDir = Path.Combine(Path.GetTempPath(), "musictagshelf-tests", Guid.NewGuid().ToString("N"));
        return new DeezerContextClient(new DefaultHttpClientFactory(), new HttpCache(cacheDir), new NullDeezerLogger());
    }

    private sealed class DefaultHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class NullDeezerLogger : Microsoft.Extensions.Logging.ILogger<DeezerContextClient>
    {
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
