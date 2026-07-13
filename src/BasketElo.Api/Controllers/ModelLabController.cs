using BasketElo.Api.Auth;
using BasketElo.Domain.Elo;
using BasketElo.Infrastructure.Elo;
using Microsoft.AspNetCore.Mvc;

namespace BasketElo.Api.Controllers;

[ApiController]
[Route("api/model-lab")]
public sealed class ModelLabController(
    IModelLabBacktestService backtestService,
    IModelLabModelService modelService) : ControllerBase
{
    [HttpGet("options")]
    public async Task<ActionResult<ModelLabOptionsResponse>> GetOptions(CancellationToken cancellationToken)
    {
        return Ok(await backtestService.GetOptionsAsync(cancellationToken));
    }

    [HttpGet("models")]
    [RequireInternalUser]
    public async Task<ActionResult<IReadOnlyCollection<ModelLabModelSummaryResponse>>> ListModels(
        [FromQuery] bool includeArchived,
        CancellationToken cancellationToken)
    {
        return Ok(await modelService.ListAsync(GetCurrentUserId(), includeArchived, cancellationToken));
    }

    [HttpGet("models/{modelId:guid}")]
    [RequireInternalUser]
    public async Task<ActionResult<ModelLabModelDetailResponse>> GetModel(
        Guid modelId,
        CancellationToken cancellationToken)
    {
        var model = await modelService.GetAsync(GetCurrentUserId(), modelId, cancellationToken);
        return model is null ? NotFound() : Ok(model);
    }

    [HttpPost("models")]
    [RequireInternalUser]
    public async Task<ActionResult<ModelLabModelDetailResponse>> CreateModel(
        [FromBody] SaveModelLabModelRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var model = await modelService.CreateAsync(GetCurrentUserId(), request, cancellationToken);
            return CreatedAtAction(nameof(GetModel), new { modelId = model.Id }, model);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPut("models/{modelId:guid}")]
    [RequireInternalUser]
    public async Task<ActionResult<ModelLabModelDetailResponse>> UpdateModel(
        Guid modelId,
        [FromBody] SaveModelLabModelRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var model = await modelService.UpdateAsync(GetCurrentUserId(), modelId, request, cancellationToken);
            return model is null ? NotFound() : Ok(model);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("models/{modelId:guid}/archive")]
    [RequireInternalUser]
    public async Task<ActionResult<ModelLabModelDetailResponse>> ArchiveModel(
        Guid modelId,
        [FromBody] ArchiveModelLabModelRequest request,
        CancellationToken cancellationToken)
    {
        var model = await modelService.SetArchivedAsync(
            GetCurrentUserId(),
            modelId,
            request.IsArchived,
            cancellationToken);

        return model is null ? NotFound() : Ok(model);
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

    private Guid GetCurrentUserId()
        => Guid.Parse(Request.Headers[InternalAuthHeaders.UserId].ToString());
}
