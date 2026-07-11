using BasketElo.Domain.Elo;
using BasketElo.Infrastructure.Elo;
using Microsoft.AspNetCore.Mvc;

namespace BasketElo.Api.Controllers;

[ApiController]
[Route("api/model-lab")]
public sealed class ModelLabController(IModelLabBacktestService backtestService) : ControllerBase
{
    [HttpGet("options")]
    public async Task<ActionResult<ModelLabOptionsResponse>> GetOptions(CancellationToken cancellationToken)
    {
        return Ok(await backtestService.GetOptionsAsync(cancellationToken));
    }

    [HttpPost("backtests")]
    public async Task<ActionResult<ModelLabBacktestResponse>> RunBacktest(
        [FromBody] ModelLabBacktestRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await backtestService.RunAsync(request, cancellationToken));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
