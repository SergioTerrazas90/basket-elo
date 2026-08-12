using BasketElo.Api.Auth;
using BasketElo.Domain.Competitions;
using BasketElo.Domain.Entities;
using BasketElo.Infrastructure.Identity;
using BasketElo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace BasketElo.Api.Controllers;

[ApiController]
[Route("api/admin/competitions")]
[RequireInternalAdmin]
public class AdminCompetitionsController(BasketEloDbContext dbContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<CompetitionAdminListResponse>> GetCompetitions(
        [FromQuery] string? search,
        [FromQuery] string? supportPolicy,
        [FromQuery] bool? active,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 200);
        var query = dbContext.Competitions.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(supportPolicy))
        {
            var normalizedPolicy = NormalizePolicy(supportPolicy);
            if (normalizedPolicy is null) return BadRequest("Unknown support policy.");
            query = query.Where(x => x.SupportPolicy == normalizedPolicy);
        }

        if (active.HasValue)
        {
            query = query.Where(x => x.IsActive == active.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x =>
                EF.Functions.ILike(x.Name, $"%{term}%") ||
                EF.Functions.ILike(x.Type, $"%{term}%") ||
                x.Aliases.Any(alias => EF.Functions.ILike(alias.AliasName, $"%{term}%")) ||
                x.Aliases.Any(alias => EF.Functions.ILike(alias.SourceCompetitionId, $"%{term}%")));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        page = Math.Min(page, totalPages);
        var rows = await query
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.Type,
                x.CountryCode,
                x.EloPoolKey,
                x.Tier,
                x.IsActive,
                x.SupportPolicy,
                AliasCount = dbContext.CompetitionAliases.Count(alias => alias.CompetitionId == x.Id),
                GameCount = dbContext.Games.Count(game => game.CompetitionId == x.Id),
                OpenReviewCount = dbContext.CurrentResultReviews.Count(review =>
                    review.Status == "open" && review.SuggestedCompetitionName == x.Name)
            })
            .ToListAsync(cancellationToken);

        return Ok(new CompetitionAdminListResponse(
            rows.Select(x => new CompetitionAdminListItem(
                x.Id, x.Name, x.Type, x.CountryCode, x.EloPoolKey, x.Tier, x.IsActive,
                x.SupportPolicy, x.AliasCount, x.GameCount, x.OpenReviewCount)).ToList(),
            page, pageSize, totalCount, totalPages));
    }

    [HttpGet("options")]
    public async Task<ActionResult<IReadOnlyList<CompetitionAdminOption>>> GetOptions(
        CancellationToken cancellationToken)
    {
        var options = await dbContext.Competitions
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new CompetitionAdminOption(x.Id, x.Name, x.CountryCode, x.SupportPolicy))
            .ToListAsync(cancellationToken);
        return Ok(options);
    }

    [HttpGet("{competitionId:guid}")]
    public async Task<ActionResult<CompetitionAdminDetail>> GetCompetition(
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        var competition = await dbContext.Competitions
            .AsNoTracking()
            .Include(x => x.Aliases)
            .SingleOrDefaultAsync(x => x.Id == competitionId, cancellationToken);
        return competition is null
            ? NotFound("Competition was not found.")
            : Ok(await BuildDetailAsync(competition, cancellationToken));
    }

    [HttpPost]
    public async Task<ActionResult<CompetitionAdminDetail>> CreateCompetition(
        [FromBody] CreateCompetitionAdminRequest request,
        CancellationToken cancellationToken)
    {
        (string Name, string Type, string? CountryCode, string SupportPolicy) values;
        try { values = Validate(request.Name, request.Type, request.CountryCode, request.SupportPolicy); }
        catch (ArgumentException exception) { return BadRequest(exception.Message); }
        if (await dbContext.Competitions.AnyAsync(
                x => x.Name == values.Name && x.CountryCode == values.CountryCode, cancellationToken))
        {
            return Conflict("A competition with this name and country already exists.");
        }

        var competition = new Competition
        {
            Id = Guid.NewGuid(),
            Name = values.Name,
            Type = values.Type,
            CountryCode = values.CountryCode,
            EloPoolKey = request.EloPoolKey?.Trim(),
            Tier = Math.Max(0, request.Tier),
            IsActive = request.IsActive,
            SupportPolicy = values.SupportPolicy,
            CreatedAtUtc = DateTime.UtcNow
        };
        dbContext.Competitions.Add(competition);
        await dbContext.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(GetCompetition), new { competitionId = competition.Id }, await BuildDetailAsync(competition, cancellationToken));
    }

    [HttpPatch("{competitionId:guid}")]
    public async Task<ActionResult<CompetitionAdminDetail>> UpdateCompetition(
        Guid competitionId,
        [FromBody] UpdateCompetitionAdminRequest request,
        CancellationToken cancellationToken)
    {
        var competition = await dbContext.Competitions
            .Include(x => x.Aliases)
            .SingleOrDefaultAsync(x => x.Id == competitionId, cancellationToken);
        if (competition is null) return NotFound("Competition was not found.");

        (string Name, string Type, string? CountryCode, string SupportPolicy) values;
        try { values = Validate(request.Name, request.Type, request.CountryCode, request.SupportPolicy); }
        catch (ArgumentException exception) { return BadRequest(exception.Message); }
        if (await dbContext.Competitions.AnyAsync(
                x => x.Id != competitionId && x.Name == values.Name && x.CountryCode == values.CountryCode, cancellationToken))
        {
            return Conflict("A competition with this name and country already exists.");
        }

        competition.Name = values.Name;
        competition.Type = values.Type;
        competition.CountryCode = values.CountryCode;
        competition.EloPoolKey = request.EloPoolKey?.Trim();
        competition.Tier = Math.Max(0, request.Tier);
        competition.IsActive = request.IsActive;
        competition.SupportPolicy = values.SupportPolicy;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(await BuildDetailAsync(competition, cancellationToken));
    }

    [HttpPost("{competitionId:guid}/aliases")]
    public async Task<ActionResult<CompetitionAdminDetail>> AddAlias(
        Guid competitionId,
        [FromBody] AddCompetitionAdminAliasRequest request,
        CancellationToken cancellationToken)
    {
        var competition = await dbContext.Competitions
            .Include(x => x.Aliases)
            .SingleOrDefaultAsync(x => x.Id == competitionId, cancellationToken);
        if (competition is null) return NotFound("Competition was not found.");

        string source;
        string aliasName;
        try
        {
            source = Required(request.Source, "Source", 50);
            aliasName = Required(request.AliasName, "Alias name", 200);
        }
        catch (ArgumentException exception) { return BadRequest(exception.Message); }
        var sourceCompetitionId = request.SourceCompetitionId?.Trim() ?? string.Empty;
        if (sourceCompetitionId.Length > 100) return BadRequest("Source competition ID cannot exceed 100 characters.");

        var existing = await dbContext.CompetitionAliases
            .Where(x => x.Source == source)
            .ToListAsync(cancellationToken);
        var conflict = existing.FirstOrDefault(x =>
            (!string.IsNullOrWhiteSpace(sourceCompetitionId) && x.SourceCompetitionId == sourceCompetitionId) ||
            Normalize(x.AliasName) == Normalize(aliasName));
        if (conflict is not null && conflict.CompetitionId != competitionId)
        {
            return Conflict("This source competition alias is already mapped to another competition.");
        }
        if (conflict is null)
        {
            dbContext.CompetitionAliases.Add(new CompetitionAlias
            {
                Id = Guid.NewGuid(),
                CompetitionId = competitionId,
                Source = source,
                SourceCompetitionId = sourceCompetitionId,
                AliasName = aliasName,
                CreatedAtUtc = DateTime.UtcNow
            });
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Ok(await BuildDetailAsync(competition, cancellationToken));
    }

    [HttpDelete("{competitionId:guid}/aliases/{aliasId:guid}")]
    public async Task<ActionResult<CompetitionAdminDetail>> DeleteAlias(
        Guid competitionId,
        Guid aliasId,
        CancellationToken cancellationToken)
    {
        var alias = await dbContext.CompetitionAliases
            .SingleOrDefaultAsync(x => x.Id == aliasId && x.CompetitionId == competitionId, cancellationToken);
        if (alias is null) return NotFound("Alias was not found for this competition.");
        dbContext.CompetitionAliases.Remove(alias);
        await dbContext.SaveChangesAsync(cancellationToken);
        var competition = await dbContext.Competitions
            .AsNoTracking()
            .Include(x => x.Aliases)
            .SingleAsync(x => x.Id == competitionId, cancellationToken);
        return Ok(await BuildDetailAsync(competition, cancellationToken));
    }

    private async Task<CompetitionAdminDetail> BuildDetailAsync(Competition competition, CancellationToken cancellationToken)
    {
        var gameCount = await dbContext.Games.CountAsync(x => x.CompetitionId == competition.Id, cancellationToken);
        var openReviewCount = await dbContext.CurrentResultReviews.CountAsync(
            x => x.Status == "open" && x.SuggestedCompetitionName == competition.Name, cancellationToken);
        var aliases = competition.Aliases
            .OrderBy(x => x.Source)
            .ThenBy(x => x.AliasName)
            .Select(x => new CompetitionAdminAlias(
                x.Id,
                x.Source,
                string.IsNullOrWhiteSpace(x.SourceCompetitionId) ? null : x.SourceCompetitionId,
                x.AliasName,
                x.CreatedAtUtc,
                0,
                0))
            .ToList();
        return new CompetitionAdminDetail(
            competition.Id, competition.Name, competition.Type, competition.CountryCode,
            competition.EloPoolKey, competition.Tier, competition.IsActive, competition.SupportPolicy,
            competition.CreatedAtUtc, gameCount, openReviewCount, aliases);
    }

    private static (string Name, string Type, string? CountryCode, string SupportPolicy) Validate(
        string name, string type, string? countryCode, string supportPolicy)
    {
        var normalizedName = Required(name, "Name", 200);
        var normalizedType = Required(type, "Type", 50);
        var normalizedPolicy = NormalizePolicy(supportPolicy) ?? throw new ArgumentException("Unknown support policy.");
        var normalizedCountry = CountryCodeCatalog.Normalize(countryCode);
        return (normalizedName, normalizedType, normalizedCountry, normalizedPolicy);
    }

    private static string Required(string? value, string label, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) throw new ArgumentException($"{label} is required.");
        if (trimmed.Length > maxLength) throw new ArgumentException($"{label} cannot exceed {maxLength} characters.");
        return trimmed;
    }

    private static string? NormalizePolicy(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return CompetitionSupportPolicies.IsValid(normalized) ? normalized : null;
    }

    private static string Normalize(string value) =>
        Regex.Replace(value.Trim().ToLowerInvariant(), "[^a-z0-9]+", " ").Trim();

}
