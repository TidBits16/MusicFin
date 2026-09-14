using System.Net.Mime;
using MediaBrowser.Common.Api;
using MediaBrowser.Model.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.MusicFin;

[Authorize(Policy = Policies.RequiresElevation)]
[ApiController]
[Produces(MediaTypeNames.Application.Json)]
[Route("MusicFin")]
public sealed class MusicFinController : ControllerBase
{
    private readonly ContextEngine _engine;
    private readonly ITaskManager _tasks;

    public MusicFinController(ContextEngine engine, ITaskManager tasks)
    {
        _engine = engine;
        _tasks = tasks;
    }

    /// <summary>Queue a force refresh that clears cache and overwrites genres/covers.</summary>
    [HttpPost("RefreshAll")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<RefreshAllResponse> RefreshAll()
    {
        _engine.RequestForce();
        _tasks.CancelIfRunningAndQueue<ContextTaggerTask>();
        return Ok(new RefreshAllResponse { Queued = true });
    }
}

public sealed class RefreshAllResponse
{
    public bool Queued { get; set; }
}
