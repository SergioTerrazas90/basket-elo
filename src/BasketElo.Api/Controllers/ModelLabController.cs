using BasketElo.Api.Auth;
using BasketElo.Domain.Elo;
using BasketElo.Infrastructure.Elo;
using Microsoft.AspNetCore.Mvc;

namespace BasketElo.Api.Controllers;

[ApiController]
[Route("api/model-lab")]
public sealed class ModelLabController(
    IModelLabBacktestService backtestService,
    IModelLabModelService modelService,
    IModelLabRunService runService,
    IModelLabEntitlementService entitlementService) : ControllerBase
{
    [HttpGet("options")]
    public async Task<ActionResult<ModelLabOptionsResponse>> GetOptions(CancellationToken cancellationToken)
    {
        return Ok(await backtestService.GetOptionsAsync(cancellationToken));
    }

    [HttpGet("entitlement")]
    public async Task<ActionResult<ModelLabEntitlementResponse>> GetEntitlement(CancellationToken cancellationToken)
    {
        return Ok(ToEntitlementResponse(await GetRequestEntitlementAsync(cancellationToken)));
    }

    [HttpGet("models")]
    [RequireInternalUser]
    public async Task<ActionResult<IReadOnlyCollection<ModelLabModelSummaryResponse>>> ListModels(
        [FromQuery] bool includeArchived,
        CancellationToken cancellationToken)
    {
        if (!TryRequireRealUser(out var loginResult))
        {
            return loginResult;
        }

        return Ok(await modelService.ListAsync(GetCurrentUserId(), includeArchived, cancellationToken));
    }

    [HttpGet("models/{modelId:guid}")]
    [RequireInternalUser]
    public async Task<ActionResult<ModelLabModelDetailResponse>> GetModel(
        Guid modelId,
        CancellationToken cancellationToken)
    {
        if (!TryRequireRealUser(out var loginResult))
        {
            return loginResult;
        }

        var model = await modelService.GetAsync(GetCurrentUserId(), modelId, cancellationToken);
        return model is null ? NotFound() : Ok(model);
    }

    [HttpPost("models")]
    [RequireInternalUser]
    public async Task<ActionResult<ModelLabModelDetailResponse>> CreateModel(
        [FromBody] SaveModelLabModelRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryRequireRealUser(out var loginResult))
        {
            return loginResult;
        }

        try
        {
            var ownerUserId = GetCurrentUserId();
            var entitlement = await entitlementService.GetAsync(ownerUserId, cancellationToken);
            var model = await modelService.CreateAsync(ownerUserId, entitlement, request, cancellationToken);
            return CreatedAtAction(nameof(GetModel), new { modelId = model.Id }, model);
        }
        catch (ModelLabLimitException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ToLimitError(ex));
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
        if (!TryRequireRealUser(out var loginResult))
        {
            return loginResult;
        }

        try
        {
            var ownerUserId = GetCurrentUserId();
            var entitlement = await entitlementService.GetAsync(ownerUserId, cancellationToken);
            var model = await modelService.UpdateAsync(ownerUserId, entitlement, modelId, request, cancellationToken);
            return model is null ? NotFound() : Ok(model);
        }
        catch (ModelLabLimitException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ToLimitError(ex));
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
        if (!TryRequireRealUser(out var loginResult))
        {
            return loginResult;
        }

        try
        {
            var ownerUserId = GetCurrentUserId();
            var entitlement = await entitlementService.GetAsync(ownerUserId, cancellationToken);
            var model = await modelService.SetArchivedAsync(
                ownerUserId,
                entitlement,
                modelId,
                request.IsArchived,
                cancellationToken);

            return model is null ? NotFound() : Ok(model);
        }
        catch (ModelLabLimitException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ToLimitError(ex));
        }
    }

    [HttpGet("runs")]
    [RequireInternalUser]
    public async Task<ActionResult<IReadOnlyCollection<ModelLabRunSummaryResponse>>> ListRuns(
        [FromQuery] int take,
        CancellationToken cancellationToken)
    {
        if (!TryRequireRealUser(out var loginResult))
        {
            return loginResult;
        }

        return Ok(await runService.ListAsync(GetCurrentUserId(), take <= 0 ? 50 : take, cancellationToken));
    }

    [HttpGet("runs/{runId:guid}")]
    [RequireInternalUser]
    public async Task<ActionResult<ModelLabRunDetailResponse>> GetRun(
        Guid runId,
        CancellationToken cancellationToken)
    {
        if (!TryRequireRealUser(out var loginResult))
        {
            return loginResult;
        }

        var run = await runService.GetAsync(GetCurrentUserId(), runId, cancellationToken);
        return run is null ? NotFound() : Ok(run);
    }

    [HttpGet("runs/{runId:guid}/predictions")]
    [RequireInternalUser]
    public async Task<ActionResult<ModelLabRunPredictionPageResponse>> GetRunPredictions(
        Guid runId,
        [FromQuery] int skip,
        [FromQuery] int take,
        CancellationToken cancellationToken)
    {
        if (!TryRequireRealUser(out var loginResult))
        {
            return loginResult;
        }

        var page = await runService.GetPredictionsAsync(
            GetCurrentUserId(),
            runId,
            skip,
            take <= 0 ? 100 : take,
            cancellationToken);

        return page is null ? NotFound() : Ok(page);
    }

    [HttpPost("runs")]
    [RequireInternalUser]
    public async Task<ActionResult<ModelLabRunCreateResponse>> CreateRun(
        [FromBody] CreateModelLabRunRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryRequireRealUser(out var loginResult))
        {
            return loginResult;
        }

        try
        {
            var ownerUserId = GetCurrentUserId();
            var entitlement = await entitlementService.GetAsync(ownerUserId, cancellationToken);
            var run = await runService.CreateAsync(ownerUserId, entitlement, request, cancellationToken);
            return run is null ? NotFound() : CreatedAtAction(nameof(GetRun), new { runId = run.RunId }, run);
        }
        catch (ModelLabLimitException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ToLimitError(ex));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("backtests")]
    public async Task<ActionResult<ModelLabBacktestResponse>> RunBacktest(
        [FromBody] ModelLabBacktestRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            EnforceBacktestScopeLimit(await GetRequestEntitlementAsync(cancellationToken), request);
            return Ok(await backtestService.RunAsync(request, cancellationToken));
        }
        catch (ModelLabLimitException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ToLimitError(ex));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    private async Task<ModelLabEntitlement> GetRequestEntitlementAsync(CancellationToken cancellationToken)
    {
        var authMode = Request.Headers[InternalAuthHeaders.AuthMode].ToString();
        if (string.Equals(authMode, "google", StringComparison.OrdinalIgnoreCase) &&
            Guid.TryParse(Request.Headers[InternalAuthHeaders.UserId].ToString(), out var userId))
        {
            return await entitlementService.GetAsync(userId, cancellationToken);
        }

        return entitlementService.GetAnonymous();
    }

    private static void EnforceBacktestScopeLimit(ModelLabEntitlement entitlement, ModelLabBacktestRequest request)
    {
        var scopeType = string.IsNullOrWhiteSpace(request.ScopeType)
            ? ModelLabScopeTypes.SingleCompetition
            : request.ScopeType.Trim();

        if (!string.IsNullOrWhiteSpace(entitlement.RequiredLeagueName) &&
            (!string.Equals(scopeType, ModelLabScopeTypes.SingleCompetition, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(request.LeagueName, entitlement.RequiredLeagueName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ModelLabLimitException(
                "league_restricted",
                $"{entitlement.PlanKey} users can run Model Lab backtests for {entitlement.RequiredLeagueName} only.",
                true,
                entitlement.SavedModelLimit,
                entitlement.RequiredLeagueName);
        }
    }

    private Guid GetCurrentUserId()
        => Guid.Parse(Request.Headers[InternalAuthHeaders.UserId].ToString());

    private bool TryRequireRealUser(out ActionResult loginResult)
    {
        var authMode = Request.Headers[InternalAuthHeaders.AuthMode].ToString();
        if (string.Equals(authMode, "google", StringComparison.OrdinalIgnoreCase))
        {
            loginResult = Ok();
            return true;
        }

        loginResult = Unauthorized(new ModelLabLimitErrorResponse(
            "login_required",
            "Sign in to save models.",
            false,
            null,
            "ACB"));
        return false;
    }

    private static ModelLabLimitErrorResponse ToLimitError(ModelLabLimitException ex)
        => new(ex.Code, ex.Message, ex.UpgradeRequired, ex.SavedModelLimit, ex.AllowedLeagueName);

    private static ModelLabEntitlementResponse ToEntitlementResponse(ModelLabEntitlement entitlement)
        => new(
            entitlement.PlanKey,
            entitlement.CanSaveModels,
            entitlement.IsPaid,
            entitlement.SavedModelLimit,
            entitlement.RequiredLeagueName);
}
