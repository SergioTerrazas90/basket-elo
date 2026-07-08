using BasketElo.Domain.Elo;
using Microsoft.AspNetCore.Mvc;

namespace BasketElo.Api.Controllers;

[ApiController]
[Route("api/elo")]
public class EloController(IEloRebuildService eloRebuildService) : ControllerBase
{
    [HttpGet("rulesets")]
    public IActionResult GetRulesets()
    {
        return Ok(new
        {
            defaultRuleset = EloRulesetVersions.Default,
            rulesets = EloRulesetVersions.All
        });
    }

    [HttpPost("rebuilds")]
    public async Task<ActionResult<IReadOnlyList<EloRebuildResult>>> Rebuild(
        [FromBody] EloRebuildRequest? request,
        CancellationToken cancellationToken)
    {
        try
        {
            var results = await eloRebuildService.RebuildAsync(request?.RulesetVersion, cancellationToken);
            return Ok(results);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
