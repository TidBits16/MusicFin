using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DeezerTagger;

public class ContextTaggerTask : IScheduledTask
{
    private readonly ContextEngine _engine;
    private readonly ILogger<ContextTaggerTask> _logger;

    public ContextTaggerTask(ContextEngine engine, ILogger<ContextTaggerTask> logger)
    {
        _engine = engine;
        _logger = logger;
    }

    public string Name => "MusicFin: Smarter Music Tagging";

    public string Key => "DeezerTaggerLibrary";

    public string Description =>
        "Assigns albums, track numbers, years, and genres using context-based metadata matching (Deezer or MusicBrainz).";

    public string Category => "Library";

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        try
        {
            await _engine.RunAsync(progress, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Smarter Music Tagging failed");
            throw;
        }
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return
        [
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.IntervalTrigger,
                IntervalTicks = TimeSpan.FromDays(7).Ticks
            }
        ];
    }
}
