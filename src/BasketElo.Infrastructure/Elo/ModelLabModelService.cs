using System.Text.Json;
using BasketElo.Domain.Elo;
using BasketElo.Domain.Entities;
using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BasketElo.Infrastructure.Elo;

public sealed class ModelLabModelService(BasketEloDbContext dbContext) : IModelLabModelService
{
    private const string ParameterSchemaVersion = "model-lab-v1";
    private const int MaxNameLength = 120;
    private const int MaxDescriptionLength = 1000;
    private const int MaxExtensionDataLength = 8000;

    public async Task<IReadOnlyCollection<ModelLabModelSummaryResponse>> ListAsync(
        Guid ownerUserId,
        bool includeArchived,
        CancellationToken cancellationToken)
    {
        var models = await dbContext.ModelLabModels
            .AsNoTracking()
            .Include(x => x.Versions)
            .Where(x => x.OwnerUserId == ownerUserId && (includeArchived || !x.IsArchived))
            .OrderBy(x => x.IsArchived)
            .ThenByDescending(x => x.UpdatedAtUtc)
            .ToListAsync(cancellationToken);

        return models.Select(ToSummaryResponse).ToList();
    }

    public async Task<ModelLabModelDetailResponse?> GetAsync(
        Guid ownerUserId,
        Guid modelId,
        CancellationToken cancellationToken)
    {
        var model = await FindOwnedModel(ownerUserId, modelId)
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);

        return model is null ? null : ToDetailResponse(model);
    }

    public async Task<ModelLabModelDetailResponse> CreateAsync(
        Guid ownerUserId,
        SaveModelLabModelRequest request,
        CancellationToken cancellationToken)
    {
        var normalized = NormalizeAndValidate(request);
        var now = DateTime.UtcNow;

        await EnsureOwnerUserExistsAsync(ownerUserId, now, cancellationToken);

        var model = new ModelLabModel
        {
            OwnerUserId = ownerUserId,
            Name = normalized.Name,
            Description = normalized.Description,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Versions =
            [
                CreateVersion(1, normalized.Parameters, normalized.ExtensionDataJson, now)
            ]
        };

        dbContext.ModelLabModels.Add(model);
        await dbContext.SaveChangesAsync(cancellationToken);

        return ToDetailResponse(model);
    }

    public async Task<ModelLabModelDetailResponse?> UpdateAsync(
        Guid ownerUserId,
        Guid modelId,
        SaveModelLabModelRequest request,
        CancellationToken cancellationToken)
    {
        var normalized = NormalizeAndValidate(request);
        var model = await FindOwnedModel(ownerUserId, modelId)
            .SingleOrDefaultAsync(cancellationToken);

        if (model is null)
        {
            return null;
        }

        var now = DateTime.UtcNow;
        model.Name = normalized.Name;
        model.Description = normalized.Description;
        model.UpdatedAtUtc = now;

        var currentVersion = GetCurrentVersion(model);
        if (!HasSameParameters(currentVersion, normalized.Parameters, normalized.ExtensionDataJson))
        {
            var newVersion = CreateVersion(
                currentVersion.VersionNumber + 1,
                normalized.Parameters,
                normalized.ExtensionDataJson,
                now);

            model.Versions.Add(newVersion);
            dbContext.ModelLabModelVersions.Add(newVersion);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return ToDetailResponse(model);
    }

    public async Task<ModelLabModelDetailResponse?> SetArchivedAsync(
        Guid ownerUserId,
        Guid modelId,
        bool isArchived,
        CancellationToken cancellationToken)
    {
        var model = await FindOwnedModel(ownerUserId, modelId)
            .SingleOrDefaultAsync(cancellationToken);

        if (model is null)
        {
            return null;
        }

        var now = DateTime.UtcNow;
        model.IsArchived = isArchived;
        model.ArchivedAtUtc = isArchived ? now : null;
        model.UpdatedAtUtc = now;

        await dbContext.SaveChangesAsync(cancellationToken);

        return ToDetailResponse(model);
    }

    private async Task EnsureOwnerUserExistsAsync(Guid ownerUserId, DateTime now, CancellationToken cancellationToken)
    {
        var exists = await dbContext.ApplicationUsers.AnyAsync(x => x.Id == ownerUserId, cancellationToken);
        if (exists)
        {
            return;
        }

        dbContext.ApplicationUsers.Add(new ApplicationUser
        {
            Id = ownerUserId,
            DisplayName = "Local access",
            Email = $"local-{ownerUserId:N}@basket-elo.local",
            NormalizedEmail = $"LOCAL-{ownerUserId:N}@BASKET-ELO.LOCAL",
            CreatedAtUtc = now,
            LastLoginAtUtc = now
        });
    }

    private IQueryable<ModelLabModel> FindOwnedModel(Guid ownerUserId, Guid modelId)
        => dbContext.ModelLabModels
            .Include(x => x.Versions)
            .Where(x => x.OwnerUserId == ownerUserId && x.Id == modelId);

    private static NormalizedModelRequest NormalizeAndValidate(SaveModelLabModelRequest request)
    {
        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Model name is required.");
        }

        if (name.Length > MaxNameLength)
        {
            throw new ArgumentException($"Model name must be {MaxNameLength} characters or fewer.");
        }

        var description = string.IsNullOrWhiteSpace(request.Description)
            ? null
            : request.Description.Trim();
        if (description?.Length > MaxDescriptionLength)
        {
            throw new ArgumentException($"Model description must be {MaxDescriptionLength} characters or fewer.");
        }

        var extensionDataJson = string.IsNullOrWhiteSpace(request.ExtensionDataJson)
            ? null
            : request.ExtensionDataJson.Trim();
        if (extensionDataJson?.Length > MaxExtensionDataLength)
        {
            throw new ArgumentException($"Extension data must be {MaxExtensionDataLength} characters or fewer.");
        }

        if (extensionDataJson is not null)
        {
            try
            {
                using var _ = JsonDocument.Parse(extensionDataJson);
            }
            catch (JsonException ex)
            {
                throw new ArgumentException($"Extension data must be valid JSON: {ex.Message}");
            }
        }

        ModelLabParameterValidator.Validate(request.Parameters);

        return new NormalizedModelRequest(name, description, request.Parameters, extensionDataJson);
    }

    private static ModelLabModelVersion CreateVersion(
        int versionNumber,
        ModelLabParameterSet parameters,
        string? extensionDataJson,
        DateTime createdAtUtc)
        => new()
        {
            VersionNumber = versionNumber,
            ParameterSchemaVersion = ParameterSchemaVersion,
            BaseRating = parameters.BaseRating,
            KFactor = parameters.KFactor,
            HomeAdvantageElo = parameters.HomeAdvantageElo,
            ProbabilityScale = parameters.ProbabilityScale,
            UsesMarginAdjustment = parameters.UsesMarginAdjustment,
            PointsPerEloMargin = parameters.UsesMarginAdjustment ? parameters.PointsPerEloMargin : null,
            CompetitionWeight = parameters.CompetitionWeight,
            ExtensionDataJson = extensionDataJson,
            CreatedAtUtc = createdAtUtc
        };

    private static bool HasSameParameters(
        ModelLabModelVersion version,
        ModelLabParameterSet parameters,
        string? extensionDataJson)
        => version.BaseRating == parameters.BaseRating &&
            version.KFactor == parameters.KFactor &&
            version.HomeAdvantageElo == parameters.HomeAdvantageElo &&
            version.ProbabilityScale == parameters.ProbabilityScale &&
            version.UsesMarginAdjustment == parameters.UsesMarginAdjustment &&
            version.PointsPerEloMargin == (parameters.UsesMarginAdjustment ? parameters.PointsPerEloMargin : null) &&
            version.CompetitionWeight == parameters.CompetitionWeight &&
            string.Equals(version.ExtensionDataJson, extensionDataJson, StringComparison.Ordinal);

    private static ModelLabModelSummaryResponse ToSummaryResponse(ModelLabModel model)
        => new(
            model.Id,
            model.Name,
            model.Description,
            model.IsArchived,
            model.CreatedAtUtc,
            model.UpdatedAtUtc,
            model.ArchivedAtUtc,
            ToVersionResponse(GetCurrentVersion(model)));

    private static ModelLabModelDetailResponse ToDetailResponse(ModelLabModel model)
        => new(
            model.Id,
            model.OwnerUserId,
            model.Name,
            model.Description,
            model.IsArchived,
            model.CreatedAtUtc,
            model.UpdatedAtUtc,
            model.ArchivedAtUtc,
            ToVersionResponse(GetCurrentVersion(model)),
            model.Versions
                .OrderByDescending(x => x.VersionNumber)
                .Select(ToVersionResponse)
                .ToList());

    private static ModelLabModelVersionResponse ToVersionResponse(ModelLabModelVersion version)
        => new(
            version.Id,
            version.VersionNumber,
            version.ParameterSchemaVersion,
            new ModelLabParameterSet(
                version.BaseRating,
                version.KFactor,
                version.HomeAdvantageElo,
                version.ProbabilityScale,
                version.UsesMarginAdjustment,
                version.PointsPerEloMargin,
                version.CompetitionWeight),
            version.ExtensionDataJson,
            version.CreatedAtUtc);

    private static ModelLabModelVersion GetCurrentVersion(ModelLabModel model)
        => model.Versions.MaxBy(x => x.VersionNumber)
            ?? throw new InvalidOperationException("Model Lab model does not have a parameter version.");

    private sealed record NormalizedModelRequest(
        string Name,
        string? Description,
        ModelLabParameterSet Parameters,
        string? ExtensionDataJson);
}
