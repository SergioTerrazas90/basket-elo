using BasketElo.Api.Auth;
using BasketElo.Infrastructure.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BasketElo.Api.Controllers;

[ApiController]
[Route("api/identity-health")]
[RequireInternalAdmin]
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

    [HttpGet("review-candidates")]
    public async Task<IActionResult> GetReviewCandidates(
        [FromQuery] IdentityReviewQuery query,
        CancellationToken cancellationToken)
    {
        var result = await identityHealthCheckService.GetReviewCandidatesAsync(query, cancellationToken);
        return Ok(result);
    }

    [HttpPost("review-candidates/resolve")]
    public async Task<IActionResult> ResolveReviewCandidate(
        [FromBody] ResolveIdentityPairRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await identityHealthCheckService.ResolveReviewCandidateAsync(
                request ?? new ResolveIdentityPairRequest(),
                cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("distinct-teams")]
    public async Task<IActionResult> GetDistinctTeams(CancellationToken cancellationToken)
    {
        var result = await identityHealthCheckService.GetDistinctTeamDecisionsAsync(cancellationToken);
        return Ok(result);
    }

    [HttpDelete("distinct-teams/{leftTeamId:guid}/{rightTeamId:guid}")]
    public async Task<IActionResult> RemoveDistinctTeams(
        Guid leftTeamId,
        Guid rightTeamId,
        CancellationToken cancellationToken)
    {
        try
        {
            await identityHealthCheckService.RemoveDistinctTeamDecisionAsync(leftTeamId, rightTeamId, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ex.Message);
        }
    }

    [HttpGet("findings/{findingId:guid}/games")]
    public async Task<IActionResult> GetFindingGames(
        Guid findingId,
        [FromQuery] int limit = 12,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await identityHealthCheckService.GetEvidenceGamesAsync(findingId, limit, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ex.Message);
        }
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
