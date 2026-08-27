using BasketElo.Api.Auth;
using BasketElo.Domain.Teams;
using BasketElo.Infrastructure.Backfill;
using BasketElo.Infrastructure.Identity;
using BasketElo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BasketElo.Api.Controllers;

[ApiController]
[Route("api/admin/teams")]
[RequireInternalAdmin]
public class AdminTeamsController(
    BasketEloDbContext dbContext,
    IIdentityHealthCheckService identityHealthCheckService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<TeamAdminListResponse>> GetTeams(
        [FromQuery] string? country,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 200);

        var normalizedCountry = NormalizeCountryFilter(country);
        var query = dbContext.Teams.AsNoTracking().AsQueryable();
        if (normalizedCountry == "UNK")
        {
            query = query.Where(x => x.CountryCode == "" || x.CountryCode == "UNK");
        }
        else if (!string.IsNullOrWhiteSpace(normalizedCountry))
        {
            query = query.Where(x => x.CountryCode == normalizedCountry);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchTerm = search.Trim();
            var normalizedSearchTerm = InternationalTeamCatalog.NormalizeSearchTerm(searchTerm);
            query = query.Where(x =>
                EF.Functions.ILike(x.CanonicalName, $"%{searchTerm}%") ||
                x.Aliases.Any(alias => EF.Functions.ILike(alias.AliasName, $"%{searchTerm}%")) ||
                x.Aliases.Any(alias => EF.Functions.ILike(alias.SourceTeamId, $"%{searchTerm}%")) ||
                (!string.IsNullOrWhiteSpace(normalizedSearchTerm) &&
                    x.SearchNames.Any(name => name.NormalizedName.Contains(normalizedSearchTerm))));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        page = Math.Min(page, totalPages);

        var rows = await query
            .OrderBy(x => x.CanonicalName)
            .ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.Id,
                x.CanonicalName,
                x.CountryCode,
                x.IsActive,
                AliasCount = dbContext.TeamAliases.Count(alias => alias.TeamId == x.Id),
                GameCount = dbContext.Games.Count(game => game.HomeTeamId == x.Id || game.AwayTeamId == x.Id),
                RatingHistoryCount = dbContext.RatingHistories.Count(history => history.TeamId == x.Id || history.OpponentTeamId == x.Id),
                RatingCount = dbContext.TeamRatings.Count(rating => rating.TeamId == x.Id)
            })
            .ToListAsync(cancellationToken);

        var countries = await BuildCountryOptionsAsync(cancellationToken);
        var teams = rows
            .Select(x => new TeamAdminListItem(
                x.Id,
                x.CanonicalName,
                NormalizeCountryForDisplay(x.CountryCode),
                x.IsActive,
                x.AliasCount,
                x.GameCount,
                x.RatingHistoryCount,
                x.RatingCount))
            .ToList();

        return Ok(new TeamAdminListResponse(teams, countries, page, pageSize, totalCount, totalPages));
    }

    [HttpGet("options")]
    public async Task<ActionResult<IReadOnlyList<TeamAdminOption>>> GetTeamOptions(CancellationToken cancellationToken)
    {
        var rows = await dbContext.Teams
            .AsNoTracking()
            .OrderBy(x => x.CanonicalName)
            .ThenBy(x => x.Id)
            .Select(x => new
            {
                x.Id,
                x.CanonicalName,
                x.CountryCode,
                x.IsActive
            })
            .ToListAsync(cancellationToken);

        var teams = rows
            .Select(x => new TeamAdminOption(
                x.Id,
                x.CanonicalName,
                NormalizeCountryForDisplay(x.CountryCode),
                x.IsActive))
            .ToList();

        return Ok(teams);
    }

    [HttpGet("{teamId:guid}")]
    public async Task<ActionResult<TeamAdminDetail>> GetTeam(Guid teamId, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await BuildTeamDetailAsync(teamId, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ex.Message);
        }
    }

    [HttpPatch("{teamId:guid}")]
    public async Task<ActionResult<TeamAdminDetail>> UpdateTeam(
        Guid teamId,
        [FromBody] UpdateTeamAdminRequest request,
        CancellationToken cancellationToken)
    {
        var team = await dbContext.Teams.FindAsync([teamId], cancellationToken);
        if (team is null)
        {
            return NotFound("Team was not found.");
        }

        var canonicalName = request.CanonicalName?.Trim();
        if (string.IsNullOrWhiteSpace(canonicalName))
        {
            return BadRequest("Canonical name is required.");
        }

        if (canonicalName.Length > 200)
        {
            return BadRequest("Canonical name cannot exceed 200 characters.");
        }

        if (request.Description?.Length > 4000)
        {
            return BadRequest("Description cannot exceed 4000 characters.");
        }

        if (request.PredecessorTeamId == teamId || request.SuccessorTeamId == teamId)
        {
            return BadRequest("A team cannot be its own predecessor or successor.");
        }

        if (request.PredecessorTeamId.HasValue && request.PredecessorTeamId == request.SuccessorTeamId)
        {
            return BadRequest("Predecessor and successor must be different teams.");
        }

        var relatedTeamIds = new[] { request.PredecessorTeamId, request.SuccessorTeamId }
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToList();
        var relatedTeams = await dbContext.Teams
            .Where(x => relatedTeamIds.Contains(x.Id))
            .ToListAsync(cancellationToken);
        if (relatedTeams.Count != relatedTeamIds.Count)
        {
            return BadRequest("One or more related teams were not found.");
        }

        var predecessor = relatedTeams.FirstOrDefault(x => x.Id == request.PredecessorTeamId);
        var successor = relatedTeams.FirstOrDefault(x => x.Id == request.SuccessorTeamId);
        if (predecessor?.SuccessorTeamId is Guid predecessorSuccessorId && predecessorSuccessorId != teamId)
        {
            return BadRequest($"{predecessor.CanonicalName} already has a different successor.");
        }

        if (successor?.PredecessorTeamId is Guid successorPredecessorId && successorPredecessorId != teamId)
        {
            return BadRequest($"{successor.CanonicalName} already has a different predecessor.");
        }

        if (team.PredecessorTeamId is Guid oldPredecessorId && oldPredecessorId != request.PredecessorTeamId)
        {
            var oldPredecessor = await dbContext.Teams.FindAsync([oldPredecessorId], cancellationToken);
            if (oldPredecessor?.SuccessorTeamId == teamId)
            {
                oldPredecessor.SuccessorTeamId = null;
            }
        }

        if (team.SuccessorTeamId is Guid oldSuccessorId && oldSuccessorId != request.SuccessorTeamId)
        {
            var oldSuccessor = await dbContext.Teams.FindAsync([oldSuccessorId], cancellationToken);
            if (oldSuccessor?.PredecessorTeamId == teamId)
            {
                oldSuccessor.PredecessorTeamId = null;
            }
        }

        team.CanonicalName = canonicalName;
        team.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        team.CountryCode = CountryCodeCatalog.Normalize(request.CountryCode) ?? "UNK";
        team.IsActive = request.IsActive;
        team.PredecessorTeamId = request.PredecessorTeamId;
        team.SuccessorTeamId = request.SuccessorTeamId;
        if (predecessor is not null)
        {
            predecessor.SuccessorTeamId = teamId;
        }

        if (successor is not null)
        {
            successor.PredecessorTeamId = teamId;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await identityHealthCheckService.InvalidateChangedScopeAsync(
            new IdentityChangedScope { CountryCode = team.CountryCode },
            cancellationToken);

        return Ok(await BuildTeamDetailAsync(teamId, cancellationToken));
    }

    [HttpDelete("{teamId:guid}")]
    public async Task<IActionResult> DeleteTeam(Guid teamId, CancellationToken cancellationToken)
    {
        var team = await dbContext.Teams
            .Include(x => x.Aliases)
            .FirstOrDefaultAsync(x => x.Id == teamId, cancellationToken);
        if (team is null)
        {
            return NotFound("Team was not found.");
        }

        var gameCount = await dbContext.Games.CountAsync(
            x => x.HomeTeamId == teamId || x.AwayTeamId == teamId,
            cancellationToken);
        var historyCount = await dbContext.RatingHistories.CountAsync(
            x => x.TeamId == teamId || x.OpponentTeamId == teamId,
            cancellationToken);
        var ratingCount = await dbContext.TeamRatings.CountAsync(x => x.TeamId == teamId, cancellationToken);
        if (gameCount > 0 || historyCount > 0 || ratingCount > 0)
        {
            return BadRequest("Only teams with no games, rating history, or current ratings can be removed.");
        }

        dbContext.Teams.Remove(team);
        await dbContext.SaveChangesAsync(cancellationToken);
        await identityHealthCheckService.InvalidateChangedScopeAsync(new IdentityChangedScope(), cancellationToken);

        return NoContent();
    }

    [HttpPost("{teamId:guid}/aliases")]
    public async Task<ActionResult<TeamAdminDetail>> AddAlias(
        Guid teamId,
        [FromBody] AddTeamAdminAliasRequest request,
        CancellationToken cancellationToken)
    {
        if (!await dbContext.Teams.AnyAsync(x => x.Id == teamId, cancellationToken))
        {
            return NotFound("Team was not found.");
        }

        var source = request.Source?.Trim();
        var sourceTeamId = request.SourceTeamId?.Trim();
        var aliasName = request.AliasName?.Trim();
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(sourceTeamId) || string.IsNullOrWhiteSpace(aliasName))
        {
            return BadRequest("Source, source team ID, and alias name are required.");
        }

        if (source.Length > 50 || sourceTeamId.Length > 100 || aliasName.Length > 200)
        {
            return BadRequest("Alias fields exceed their allowed lengths.");
        }

        var exists = await dbContext.TeamAliases.AnyAsync(
            x => x.Source == source && x.SourceTeamId == sourceTeamId && x.AliasName == aliasName,
            cancellationToken);
        if (exists)
        {
            return Conflict("That source/team ID/alias combination already exists.");
        }

        dbContext.TeamAliases.Add(new Domain.Entities.TeamAlias
        {
            Id = Guid.NewGuid(),
            TeamId = teamId,
            Source = source,
            SourceTeamId = sourceTeamId,
            AliasName = aliasName,
            ValidFromUtc = request.ValidFromUtc,
            ValidToUtc = request.ValidToUtc
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        await identityHealthCheckService.InvalidateChangedScopeAsync(
            new IdentityChangedScope { Source = source },
            cancellationToken);

        return Ok(await BuildTeamDetailAsync(teamId, cancellationToken));
    }

    [HttpDelete("{teamId:guid}/aliases/{aliasId:guid}")]
    public async Task<ActionResult<TeamAdminDetail>> DeleteAlias(
        Guid teamId,
        Guid aliasId,
        CancellationToken cancellationToken)
    {
        var alias = await dbContext.TeamAliases
            .FirstOrDefaultAsync(x => x.Id == aliasId && x.TeamId == teamId, cancellationToken);
        if (alias is null)
        {
            return NotFound("Alias was not found for this team.");
        }

        dbContext.TeamAliases.Remove(alias);
        await dbContext.SaveChangesAsync(cancellationToken);
        await identityHealthCheckService.InvalidateChangedScopeAsync(
            new IdentityChangedScope { Source = alias.Source },
            cancellationToken);

        return Ok(await BuildTeamDetailAsync(teamId, cancellationToken));
    }

    [HttpPost("{teamId:guid}/aliases/{aliasId:guid}/extract")]
    public async Task<ActionResult<TeamAdminExtractAliasResponse>> ExtractAlias(
        Guid teamId,
        Guid aliasId,
        CancellationToken cancellationToken)
    {
        var alias = await dbContext.TeamAliases
            .Include(x => x.Team)
            .FirstOrDefaultAsync(x => x.Id == aliasId && x.TeamId == teamId, cancellationToken);
        if (alias is null)
        {
            return NotFound("Alias was not found for this team.");
        }

        var aliasesToExtract = await dbContext.TeamAliases
            .Where(x => x.TeamId == teamId && x.Source == alias.Source && x.SourceTeamId == alias.SourceTeamId)
            .ToListAsync(cancellationToken);
        if (aliasesToExtract.Count == 0)
        {
            return BadRequest("The provider mapping no longer exists.");
        }

        var sourceTeamIdsOnOtherTeams = await dbContext.TeamAliases
            .Where(x => x.TeamId != teamId && x.Source == alias.Source && x.SourceTeamId == alias.SourceTeamId)
            .Select(x => x.TeamId)
            .Distinct()
            .ToListAsync(cancellationToken);
        if (sourceTeamIdsOnOtherTeams.Count > 0)
        {
            return Conflict("This provider team ID is already mapped to another team. Resolve that split before extracting it.");
        }

        var newTeam = new Domain.Entities.Team
        {
            Id = Guid.NewGuid(),
            CanonicalName = alias.AliasName,
            CountryCode = alias.Team.CountryCode,
            IsActive = alias.Team.IsActive,
            CreatedAtUtc = DateTime.UtcNow
        };
        dbContext.Teams.Add(newTeam);
        foreach (var aliasToExtract in aliasesToExtract)
        {
            aliasToExtract.TeamId = newTeam.Id;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await identityHealthCheckService.InvalidateChangedScopeAsync(
            new IdentityChangedScope { Source = alias.Source },
            cancellationToken);

        return Ok(new TeamAdminExtractAliasResponse(
            newTeam.Id,
            newTeam.CanonicalName,
            alias.Source,
            alias.SourceTeamId,
            aliasesToExtract.Count));
    }

    [HttpPost("{teamId:guid}/merge")]
    public async Task<ActionResult<TeamAdminMergeResponse>> MergeTeam(
        Guid teamId,
        [FromBody] MergeTeamAdminRequest request,
        CancellationToken cancellationToken)
    {
        if (teamId == request.TargetTeamId)
        {
            return BadRequest("Source and target teams must be different.");
        }

        try
        {
            var result = await identityHealthCheckService.MergeTeamsAsync(
                teamId,
                request.TargetTeamId,
                request.ConfirmMergeWithRatings,
                cancellationToken);
            return Ok(new TeamAdminMergeResponse(result.TargetTeamId, result.RemovedTeamId, result.TargetTeamName));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    private async Task<TeamAdminDetail> BuildTeamDetailAsync(Guid teamId, CancellationToken cancellationToken)
    {
        var team = await dbContext.Teams
            .AsNoTracking()
            .Include(x => x.Aliases)
            .Include(x => x.PredecessorTeam)
            .Include(x => x.SuccessorTeam)
            .FirstOrDefaultAsync(x => x.Id == teamId, cancellationToken)
            ?? throw new InvalidOperationException("Team was not found.");

        var gameCount = await dbContext.Games.CountAsync(x => x.HomeTeamId == teamId || x.AwayTeamId == teamId, cancellationToken);
        var historyCount = await dbContext.RatingHistories.CountAsync(x => x.TeamId == teamId || x.OpponentTeamId == teamId, cancellationToken);
        var ratingCount = await dbContext.TeamRatings.CountAsync(x => x.TeamId == teamId, cancellationToken);
        var aliasSources = team.Aliases
            .Select(x => x.Source)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var gamesForAliasUsage = aliasSources.Count == 0
            ? []
            : await dbContext.Games
                .AsNoTracking()
                .Where(x =>
                    (x.HomeTeamId == teamId || x.AwayTeamId == teamId) &&
                    aliasSources.Contains(x.Source))
                .Select(x => new AliasUsageGame(x.Source, x.SeasonId, x.GameDateTimeUtc))
                .ToListAsync(cancellationToken);

        return new TeamAdminDetail(
            team.Id,
            team.CanonicalName,
            team.Description,
            NormalizeCountryForDisplay(team.CountryCode),
            team.IsActive,
            team.CreatedAtUtc,
            gameCount,
            historyCount,
            ratingCount,
            team.Aliases
                .OrderBy(x => x.Source)
                .ThenBy(x => x.AliasName)
                .Select(x =>
                {
                    var usageGames = gamesForAliasUsage
                        .Where(game =>
                            string.Equals(game.Source, x.Source, StringComparison.OrdinalIgnoreCase) &&
                            (!x.ValidFromUtc.HasValue || game.GameDateTimeUtc >= x.ValidFromUtc.Value) &&
                            (!x.ValidToUtc.HasValue || game.GameDateTimeUtc <= x.ValidToUtc.Value))
                        .ToList();

                    return new TeamAdminAlias(
                        x.Id,
                        x.Source,
                        x.SourceTeamId,
                        x.AliasName,
                        x.ValidFromUtc,
                        x.ValidToUtc,
                        x.CreatedAtUtc,
                        usageGames.Count,
                        usageGames.Select(game => game.SeasonId).Distinct().Count(),
                        usageGames.Count == 0 ? null : usageGames.Min(game => game.GameDateTimeUtc),
                        usageGames.Count == 0 ? null : usageGames.Max(game => game.GameDateTimeUtc));
                })
                .ToList(),
            team.PredecessorTeam is null ? null : ToTeamOption(team.PredecessorTeam),
            team.SuccessorTeam is null ? null : ToTeamOption(team.SuccessorTeam));
    }

    private sealed record AliasUsageGame(string Source, Guid SeasonId, DateTime GameDateTimeUtc);

    private async Task<IReadOnlyList<TeamAdminCountryOption>> BuildCountryOptionsAsync(CancellationToken cancellationToken)
    {
        var rows = await dbContext.Teams
            .AsNoTracking()
            .GroupBy(x => x.CountryCode)
            .Select(x => new { x.Key, Count = x.Count() })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(x => NormalizeCountryForDisplay(x.Key), StringComparer.OrdinalIgnoreCase)
            .Select(x => new TeamAdminCountryOption(
                x.Key,
                CountryCodeCatalog.DisplayName(x.Key) is { Length: > 0 } name ? name : "Unknown",
                x.Sum(row => row.Count)))
            .OrderBy(x => x.Name)
            .ToList();
    }

    private static string? NormalizeCountryFilter(string? country)
    {
        if (string.IsNullOrWhiteSpace(country))
        {
            return null;
        }

        return CountryCodeCatalog.Normalize(country);
    }

    private static string NormalizeCountryForDisplay(string? countryCode)
        => CountryCodeCatalog.Normalize(countryCode) ?? "UNK";

    private static TeamAdminOption ToTeamOption(Domain.Entities.Team team)
        => new(team.Id, team.CanonicalName, NormalizeCountryForDisplay(team.CountryCode), team.IsActive);
}
