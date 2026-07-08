using BasketElo.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BasketElo.Api.Controllers;

[ApiController]
[Route("api/identity-health")]
public class IdentityHealthController(IIdentityHealthCheckService identityHealthCheckService) : ControllerBase
{
    [HttpPost("checks")]
    public async Task<IActionResult> RunCheck([FromBody] IdentityHealthCheckRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await identityHealthCheckService.RunAsync(request ?? new IdentityHealthCheckRequest(), cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("options")]
    public async Task<IActionResult> GetOptions(CancellationToken cancellationToken)
    {
        var result = await identityHealthCheckService.GetOptionsAsync(cancellationToken);
        return Ok(result);
    }

    [HttpGet("checks")]
    public async Task<IActionResult> GetChecks([FromQuery] IdentityHealthCheckQuery query, CancellationToken cancellationToken)
    {
        var result = await identityHealthCheckService.GetRunsAsync(query, cancellationToken);
        return Ok(result);
    }

    [HttpDelete("checks/{runId:guid}")]
    public async Task<IActionResult> DeleteCheck(Guid runId, CancellationToken cancellationToken)
    {
        try
        {
            await identityHealthCheckService.DeleteRunAsync(runId, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ex.Message);
        }
    }

    [HttpGet("findings")]
    public async Task<IActionResult> GetFindings([FromQuery] IdentityFindingQuery query, CancellationToken cancellationToken)
    {
        var result = await identityHealthCheckService.GetFindingsAsync(query, cancellationToken);
        return Ok(result);
    }

    [HttpPost("findings/{findingId:guid}/resolve")]
    public async Task<IActionResult> ResolveFinding(
        Guid findingId,
        [FromBody] ResolveIdentityFindingRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await identityHealthCheckService.ResolveFindingAsync(
                findingId,
                request ?? new ResolveIdentityFindingRequest(),
                cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
