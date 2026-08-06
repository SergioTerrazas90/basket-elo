using BasketElo.Domain.Entities;
using BasketElo.Domain.Elo;
using BasketElo.Infrastructure.Backfill;
using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BasketElo.Infrastructure.Identity;

public class IdentityHealthCheckService(
    BasketEloDbContext dbContext,
    IBackfillCatalog backfillCatalog) : IIdentityHealthCheckService
{
    private const double SimilarNameThreshold = 0.86;
    private const string DistinctTeamsDecisionPrefix = "distinct_teams|teams=";
    private static readonly IReadOnlyCollection<IdentityCountryOptionDto> DefaultCountryOptions =
    [
        new("AZ", "Azerbaijan"),
        new("BE", "Belgium"),
        new("BA", "Bosnia and Herzegovina"),
        new("BG", "Bulgaria"),
        new("HR", "Croatia"),
        new("CY", "Cyprus"),
        new("CZ", "Czech Republic"),
        new("DK", "Denmark"),
        new("EE", "Estonia"),
        new("FI", "Finland"),
        new("FR", "France"),
        new("GE", "Georgia"),
        new("DE", "Germany"),
        new("GR", "Greece"),
        new("HU", "Hungary"),
        new("IL", "Israel"),
        new("IT", "Italy"),
        new("LV", "Latvia"),
        new("LT", "Lithuania"),
        new("ME", "Montenegro"),
        new("NL", "Netherlands"),
        new("NO", "Norway"),
        new("PL", "Poland"),
        new("PT", "Portugal"),
        new("RO", "Romania"),
        new("RU", "Russia"),
        new("RS", "Serbia"),
        new("XK", "Kosovo"),
        new("SK", "Slovakia"),
        new("SI", "Slovenia"),
        new("ES", "Spain"),
        new("SCT", "Scotland"),
        new("SE", "Sweden"),
        new("CH", "Switzerland"),
        new("TR", "Turkey"),
        new("UA", "Ukraine"),
        new("US", "United States")
    ];

    public async Task<IdentityHealthCheckRunDto> RunAsync(IdentityHealthCheckRequest request, CancellationToken cancellationToken)
    {
        var normalizedRequest = NormalizeRequest(request);
        var scopeKey = BuildScopeKey(normalizedRequest);

        if (!normalizedRequest.Force)
        {
            var reusableRun = await dbContext.IdentityHealthCheckRuns
                .AsNoTracking()
                .Where(x =>
                    x.ScopeKey == scopeKey &&
                    x.RulesVersion == IdentityHealthCheckRules.CurrentVersion &&
                    x.InvalidatedAtUtc == null)
                .OrderByDescending(x => x.CheckedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

            if (reusableRun is not null)
            {
                return ToDto(reusableRun);
            }
        }

        var now = DateTime.UtcNow;
        var gameRows = await LoadScopedGameRowsAsync(normalizedRequest, cancellationToken);
        var scopedTeamIds = gameRows
            .SelectMany(x => new[] { x.HomeTeamId, x.AwayTeamId })
            .Distinct()
            .ToHashSet();

        var aliases = await LoadScopedAliasesAsync(normalizedRequest, scopedTeamIds, cancellationToken);
        var teams = aliases
            .Select(x => x.Team)
            .Concat(await LoadTeamsWithoutAliasesAsync(normalizedRequest, scopedTeamIds, cancellationToken))
            .GroupBy(x => x.Id)
            .Select(x => x.First())
            .ToList();

        var run = new IdentityHealthCheckRun
        {
            Id = Guid.NewGuid(),
            Source = normalizedRequest.Source,
            Season = normalizedRequest.Season,
            CountryCode = normalizedRequest.CountryCode,
            CompetitionId = normalizedRequest.CompetitionId,
            ScopeKey = scopeKey,
            RulesVersion = IdentityHealthCheckRules.CurrentVersion,
            Forced = normalizedRequest.Force,
            CheckedAtUtc = now,
            CreatedAtUtc = now
        };

        var findings = new List<IdentityHealthCheckFinding>();
        findings.AddRange(BuildMissingMetadataFindings(run, teams, gameRows, now));
        findings.AddRange(BuildAliasObservationFindings(run, aliases, now));
        findings.AddRange(BuildSourceTeamSplitFindings(run, aliases, now));
        findings.AddRange(BuildSimilarAliasFindings(run, aliases, gameRows, now));
        findings.AddRange(BuildCrossSeasonSplitFindings(run, aliases, gameRows, now));
        findings = await RemoveReviewedFindingsAsync(findings, cancellationToken);

        run.FindingsCount = findings.Count;
        run.UnresolvedBlockersCount = findings.Count(x => x.Severity == IdentityFindingSeverity.Blocker);
        run.Status = run.UnresolvedBlockersCount > 0
            ? IdentityHealthCheckStatus.Blockers
            : findings.Count > 0
                ? IdentityHealthCheckStatus.Warnings
                : IdentityHealthCheckStatus.Clean;

        dbContext.IdentityHealthCheckRuns.Add(run);
        dbContext.IdentityHealthCheckFindings.AddRange(findings);
        await dbContext.SaveChangesAsync(cancellationToken);

        return ToDto(run);
    }

    public async Task<IdentityHealthOptionsDto> GetOptionsAsync(CancellationToken cancellationToken)
    {
        var gameSources = await dbContext.Games
            .AsNoTracking()
            .Select(x => x.Source)
            .Distinct()
            .ToListAsync(cancellationToken);
        var aliasSources = await dbContext.TeamAliases
            .AsNoTracking()
            .Select(x => x.Source)
            .Distinct()
            .ToListAsync(cancellationToken);
        var sources = gameSources
            .Concat(aliasSources)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();

        var seasons = await dbContext.Seasons
            .AsNoTracking()
            .Select(x => x.Label)
            .Distinct()
            .OrderByDescending(x => x)
            .ToListAsync(cancellationToken);

        var competitionCountries = await dbContext.Competitions
            .AsNoTracking()
            .Where(x => x.CountryCode != null && x.CountryCode != "UNK")
            .Select(x => x.CountryCode!)
            .Distinct()
            .ToListAsync(cancellationToken);
        var teamCountries = await dbContext.Teams
            .AsNoTracking()
            .Where(x => x.CountryCode != "" && x.CountryCode != "UNK")
            .Select(x => x.CountryCode)
            .Distinct()
            .ToListAsync(cancellationToken);
        var backfillCountries = backfillCatalog.GetLeagues()
            .Select(x => x.Country)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(NameToCountryOption)
            .Where(x => x is not null)
            .Select(x => x!)
            .ToList();
        var countries = competitionCountries
            .Concat(teamCountries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(CountryCodeCatalog.Normalize)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => new IdentityCountryOptionDto(x!, CountryNameFromCode(x!)))
            .Concat(backfillCountries)
            .Concat(DefaultCountryOptions)
            .GroupBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Code)
            .ToList();

        var competitionRows = await dbContext.Competitions
            .AsNoTracking()
            .OrderBy(x => x.CountryCode)
            .ThenBy(x => x.Name)
            .Select(x => new { x.Id, x.Name, x.CountryCode })
            .ToListAsync(cancellationToken);
        var competitions = competitionRows
            .Select(x => new IdentityCompetitionOptionDto(x.Id, x.Name, CountryCodeCatalog.Normalize(x.CountryCode)))
            .ToList();

        return new IdentityHealthOptionsDto(sources, seasons, countries, competitions);
    }

    public async Task<IReadOnlyList<IdentityHealthCheckRunDto>> GetRunsAsync(IdentityHealthCheckQuery query, CancellationToken cancellationToken)
    {
        var runs = dbContext.IdentityHealthCheckRuns.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Source))
        {
            var source = query.Source.Trim().ToLowerInvariant();
            runs = runs.Where(x => x.Source == null || x.Source == source);
        }

        if (!string.IsNullOrWhiteSpace(query.Season))
        {
            var season = query.Season.Trim();
            runs = runs.Where(x => x.Season == season);
        }

        if (!string.IsNullOrWhiteSpace(query.CountryCode))
        {
            var countryCode = NormalizeCountryCode(query.CountryCode);
            runs = runs.Where(x => x.CountryCode == countryCode);
        }

        if (query.CompetitionId.HasValue)
        {
            runs = runs.Where(x => x.CompetitionId == query.CompetitionId);
        }

        return await runs
            .Include(x => x.Findings)
            .OrderByDescending(x => x.CheckedAtUtc)
            .Take(Math.Clamp(query.Limit, 1, 1000))
            .Select(x => ToDto(x))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<IdentityHealthCheckFindingDto>> GetFindingsAsync(IdentityFindingQuery query, CancellationToken cancellationToken)
    {
        var findings = dbContext.IdentityHealthCheckFindings
            .AsNoTracking()
            .Include(x => x.Run)
            .Include(x => x.AffectedTeam)
            .Include(x => x.RelatedTeam)
            .AsQueryable();

        if (query.RunId.HasValue)
        {
            findings = findings.Where(x => x.RunId == query.RunId);
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var status = query.Status.Trim().ToLowerInvariant();
            findings = findings.Where(x => x.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Severity))
        {
            var severity = query.Severity.Trim().ToLowerInvariant();
            findings = findings.Where(x => x.Severity == severity);
        }

        if (!string.IsNullOrWhiteSpace(query.Source))
        {
            var source = query.Source.Trim().ToLowerInvariant();
            findings = findings.Where(x =>
                x.Run.Source == null ||
                x.Run.Source == source ||
                x.Source == source ||
                x.RelatedSource == source);
        }

        if (!string.IsNullOrWhiteSpace(query.Season))
        {
            var season = query.Season.Trim();
            findings = findings.Where(x => x.Season == season);
        }

        if (!string.IsNullOrWhiteSpace(query.CountryCode))
        {
            var countryCode = NormalizeCountryCode(query.CountryCode);
            findings = findings.Where(x => x.CountryCode == countryCode);
        }

        if (query.CompetitionId.HasValue)
        {
            findings = findings.Where(x => x.CompetitionId == query.CompetitionId);
        }

        var findingRows = await findings
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip((Math.Max(query.Page, 1) - 1) * Math.Clamp(query.Limit, 1, 5000))
            .Take(Math.Clamp(query.Limit, 1, 5000))
            .ToListAsync(cancellationToken);

        var missingTeamKeys = new HashSet<SourceTeamKey>();
        foreach (var row in findingRows)
        {
            if (!row.AffectedTeamId.HasValue &&
                !string.IsNullOrWhiteSpace(row.Source) &&
                !string.IsNullOrWhiteSpace(row.SourceTeamId))
            {
                missingTeamKeys.Add(new SourceTeamKey(row.Source!, row.SourceTeamId!));
            }

            if (!row.RelatedTeamId.HasValue &&
                !string.IsNullOrWhiteSpace(row.RelatedSource) &&
                !string.IsNullOrWhiteSpace(row.RelatedSourceTeamId))
            {
                missingTeamKeys.Add(new SourceTeamKey(row.RelatedSource!, row.RelatedSourceTeamId!));
            }
        }

        var inferredAliasTeams = new Dictionary<SourceTeamKey, Team>();
        if (missingTeamKeys.Count > 0)
        {
            var sourceValues = missingTeamKeys.Select(x => x.Source).Distinct().ToList();
            var sourceTeamIdValues = missingTeamKeys.Select(x => x.SourceTeamId).Distinct().ToList();
            var sourceAliases = await dbContext.TeamAliases
                .AsNoTracking()
                .Include(x => x.Team)
                .Where(x => sourceValues.Contains(x.Source) && sourceTeamIdValues.Contains(x.SourceTeamId))
                .ToListAsync(cancellationToken);

            inferredAliasTeams = sourceAliases
                .Where(x => missingTeamKeys.Contains(new SourceTeamKey(x.Source, x.SourceTeamId)))
                .GroupBy(x => new SourceTeamKey(x.Source, x.SourceTeamId))
                .Where(x => x.Select(alias => alias.TeamId).Distinct().Count() == 1)
                .ToDictionary(x => x.Key, x => x.First().Team);
        }

        var missingMetadataNames = findingRows
            .Where(x => x.FindingType == IdentityFindingType.MissingMetadata && !x.AffectedTeamId.HasValue)
            .Select(x => ExtractMissingMetadataTeamName(x.Evidence))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var inferredMetadataTeams = missingMetadataNames.Count == 0
            ? []
            : await dbContext.Teams
                .AsNoTracking()
                .Where(x => missingMetadataNames.Contains(x.CanonicalName))
                .ToListAsync(cancellationToken);

        return findingRows
            .Select(x =>
            {
                var affectedKey = !string.IsNullOrWhiteSpace(x.Source) && !string.IsNullOrWhiteSpace(x.SourceTeamId)
                    ? new SourceTeamKey(x.Source, x.SourceTeamId)
                    : null;
                var relatedKey = !string.IsNullOrWhiteSpace(x.RelatedSource) && !string.IsNullOrWhiteSpace(x.RelatedSourceTeamId)
                    ? new SourceTeamKey(x.RelatedSource, x.RelatedSourceTeamId)
                    : null;
                var inferredAffectedTeam = affectedKey is not null && inferredAliasTeams.TryGetValue(affectedKey, out var affectedTeam)
                    ? affectedTeam
                    : inferredMetadataTeams.FirstOrDefault(team => team.CanonicalName == ExtractMissingMetadataTeamName(x.Evidence));
                var inferredRelatedTeam = relatedKey is not null && inferredAliasTeams.TryGetValue(relatedKey, out var relatedTeam)
                    ? relatedTeam
                    : null;
                return ToDto(x, inferredAffectedTeam, inferredRelatedTeam);
            })
            .ToList();
    }

    public async Task<IReadOnlyList<IdentityReviewCandidateDto>> GetReviewCandidatesAsync(
        IdentityReviewQuery query,
        CancellationToken cancellationToken)
    {
        var runId = query.RunId;
        if (!runId.HasValue)
        {
            var runs = dbContext.IdentityHealthCheckRuns.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(query.CountryCode))
            {
                var countryCode = NormalizeCountryCode(query.CountryCode);
                runs = runs.Where(x => x.CountryCode == countryCode);
            }

            runId = await runs
                .OrderByDescending(x => x.CheckedAtUtc)
                .Select(x => (Guid?)x.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (!runId.HasValue)
        {
            return [];
        }

        var findings = await GetFindingsAsync(new IdentityFindingQuery
        {
            RunId = runId,
            CountryCode = query.CountryCode,
            Limit = 5000
        }, cancellationToken);

        var pairFindings = findings
            .Where(x => x.AffectedTeamId.HasValue && x.RelatedTeamId.HasValue &&
                x.FindingType is IdentityFindingType.PossibleDuplicate or
                    IdentityFindingType.PossibleCrossSourceMatch or
                    IdentityFindingType.PossibleCrossSeasonSplit)
            .GroupBy(x => OrderedPairKey(x.AffectedTeamId!.Value, x.RelatedTeamId!.Value))
            .ToList();

        if (pairFindings.Count == 0)
        {
            return [];
        }

        var teamIds = pairFindings
            .SelectMany(x => x.SelectMany(f => new[] { f.AffectedTeamId!.Value, f.RelatedTeamId!.Value }))
            .Distinct()
            .ToList();
        var teamRows = await dbContext.Teams
            .AsNoTracking()
            .Where(x => teamIds.Contains(x.Id))
            .Select(x => new
            {
                x.Id,
                x.CanonicalName,
                x.CountryCode,
                x.IsActive,
                AliasCount = dbContext.TeamAliases.Count(alias => alias.TeamId == x.Id),
                GameCount = dbContext.Games.Count(game => game.HomeTeamId == x.Id || game.AwayTeamId == x.Id),
                LastGameUtc = dbContext.Games
                    .Where(game => game.HomeTeamId == x.Id || game.AwayTeamId == x.Id)
                    .Select(game => (DateTime?)game.GameDateTimeUtc)
                    .Max()
            })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var candidates = new List<IdentityReviewCandidateDto>();
        var teamCountryCode = NormalizeCountryCode(query.TeamCountryCode);
        foreach (var group in pairFindings)
        {
            var ids = group
                .SelectMany(x => new[] { x.AffectedTeamId!.Value, x.RelatedTeamId!.Value })
                .Distinct()
                .OrderBy(x => x.ToString("N"), StringComparer.Ordinal)
                .ToArray();
            if (ids.Length != 2 || !teamRows.TryGetValue(ids[0], out var left) || !teamRows.TryGetValue(ids[1], out var right))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(teamCountryCode) &&
                !CountryMatchesTeam(left.CountryCode, teamCountryCode) &&
                !CountryMatchesTeam(right.CountryCode, teamCountryCode))
            {
                continue;
            }

            var status = group.Any(x => x.Status == IdentityFindingStatus.Open)
                ? "open"
                : group.Any(x => x.ResolutionAction == "defer_review")
                    ? "deferred"
                    : "completed";
            if (!string.IsNullOrWhiteSpace(query.Status) &&
                !string.Equals(query.Status, "all", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(query.Status, status, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var primary = group
                .OrderBy(x => x.Status == IdentityFindingStatus.Open ? 0 : 1)
                .ThenByDescending(x => x.Severity == IdentityFindingSeverity.Blocker)
                .ThenByDescending(x => x.CreatedAtUtc)
                .First();
            candidates.Add(new IdentityReviewCandidateDto(
                runId.Value,
                status,
                group.Any(x => x.Severity == IdentityFindingSeverity.Blocker)
                    ? IdentityFindingSeverity.Blocker
                    : IdentityFindingSeverity.Warning,
                new IdentityReviewTeamDto(left.Id, left.CanonicalName, left.CountryCode, left.IsActive, left.GameCount, left.AliasCount, left.LastGameUtc),
                new IdentityReviewTeamDto(right.Id, right.CanonicalName, right.CountryCode, right.IsActive, right.GameCount, right.AliasCount, right.LastGameUtc),
                group.Select(x => x.FindingType).Distinct().OrderBy(x => x).ToList(),
                group.Count(),
                group.Count(x => x.Status == IdentityFindingStatus.Open),
                primary.Id,
                group.Select(x => x.Id).ToList(),
                group.Select(x => x.Evidence).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().Take(5).ToList()));
        }

        return candidates
            .OrderBy(x => x.Status == "open" ? 0 : x.Status == "deferred" ? 1 : 2)
            .ThenBy(x => x.Severity == IdentityFindingSeverity.Blocker ? 0 : 1)
            .ThenByDescending(x => x.LeftTeam.GameCount + x.RightTeam.GameCount)
            .ThenBy(x => x.LeftTeam.Name)
            .Take(Math.Clamp(query.Limit, 1, 1000))
            .ToList();
    }

    public async Task<IdentityReviewCandidateDto> ResolveReviewCandidateAsync(
        ResolveIdentityPairRequest request,
        CancellationToken cancellationToken)
    {
        if (request.LeftTeamId == request.RightTeamId)
        {
            throw new InvalidOperationException("The two teams must be different.");
        }

        var action = NormalizeResolutionAction(request.Action);
        if (action is not ("merge_duplicate" or "keep_separate" or "defer_review" or "ignore"))
        {
            throw new InvalidOperationException("This review action is not supported for a team pair.");
        }

        var findings = await dbContext.IdentityHealthCheckFindings
            .Include(x => x.AffectedTeam)
            .Include(x => x.RelatedTeam)
            .Where(x => x.RunId == request.RunId &&
                x.Status == IdentityFindingStatus.Open &&
                x.AffectedTeamId.HasValue && x.RelatedTeamId.HasValue &&
                ((x.AffectedTeamId == request.LeftTeamId && x.RelatedTeamId == request.RightTeamId) ||
                    (x.AffectedTeamId == request.RightTeamId && x.RelatedTeamId == request.LeftTeamId)))
            .OrderByDescending(x => x.Severity == IdentityFindingSeverity.Blocker)
            .ThenBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        if (findings.Count == 0)
        {
            throw new InvalidOperationException("No open identity findings remain for this team pair.");
        }

        var candidateBeforeResolution = (await GetReviewCandidatesAsync(new IdentityReviewQuery
        {
            RunId = request.RunId,
            Status = "all",
            Limit = 1000
        }, cancellationToken)).FirstOrDefault(x =>
            (x.LeftTeam.Id == request.LeftTeamId && x.RightTeam.Id == request.RightTeamId) ||
            (x.LeftTeam.Id == request.RightTeamId && x.RightTeam.Id == request.LeftTeamId));

        if (action == "merge_duplicate")
        {
            if (!request.TargetTeamId.HasValue ||
                (request.TargetTeamId != request.LeftTeamId && request.TargetTeamId != request.RightTeamId))
            {
                throw new InvalidOperationException("Choose one of the two teams as the merge target.");
            }

            if (!request.ConfirmMergeWithRatings)
            {
                throw new InvalidOperationException("Confirm that the merge changes historical game and rating ownership.");
            }

            await MergeFindingTeamsAsync(findings[0], new ResolveIdentityFindingRequest
            {
                Action = action,
                TargetTeamId = request.TargetTeamId,
                ConfirmMergeWithRatings = true,
                ResolvedBy = request.ResolvedBy,
                Note = request.Note
            }, cancellationToken);
        }
        else if (action == "keep_separate")
        {
            await PopulateFindingTeamIdsAsync(findings[0], cancellationToken);
        }

        foreach (var finding in findings)
        {
            finding.Status = action == "ignore" ? IdentityFindingStatus.Ignored : IdentityFindingStatus.Resolved;
            finding.ResolutionAction = action;
            finding.ResolvedBy = string.IsNullOrWhiteSpace(request.ResolvedBy) ? "admin" : request.ResolvedBy.Trim();
            finding.ResolvedAtUtc = DateTime.UtcNow;
            finding.ResolutionNote = request.Note?.Trim();
            await SaveReviewDecisionAsync(finding, action, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await RefreshRunCountsAsync(request.RunId, cancellationToken);

        return (await GetReviewCandidatesAsync(new IdentityReviewQuery
        {
            RunId = request.RunId,
            Status = "all",
            Limit = 1000
        }, cancellationToken)).FirstOrDefault(x =>
            (x.LeftTeam.Id == request.LeftTeamId && x.RightTeam.Id == request.RightTeamId) ||
            (x.LeftTeam.Id == request.RightTeamId && x.RightTeam.Id == request.LeftTeamId))
            ?? (candidateBeforeResolution is null
                ? null
                : candidateBeforeResolution with
                {
                    Status = action == "defer_review" ? "deferred" : "completed",
                    OpenFindingCount = 0
                })
            ?? throw new InvalidOperationException("The review candidate no longer exists.");
    }

    public async Task<IdentityEvidenceGamesResponseDto> GetEvidenceGamesAsync(
        Guid findingId,
        int limit,
        CancellationToken cancellationToken)
    {
        var finding = await dbContext.IdentityHealthCheckFindings
            .AsNoTracking()
            .Include(x => x.Run)
            .Include(x => x.AffectedTeam)
            .Include(x => x.RelatedTeam)
            .FirstOrDefaultAsync(x => x.Id == findingId, cancellationToken)
            ?? throw new InvalidOperationException("Identity finding was not found.");

        var season = finding.Season ?? finding.Run.Season;
        var countryCode = finding.CountryCode ?? finding.Run.CountryCode;
        var competitionId = finding.CompetitionId ?? finding.Run.CompetitionId;

        var affectedTeamId = finding.AffectedTeamId ?? await ResolveEvidenceTeamIdAsync(
            finding.Source,
            finding.SourceTeamId,
            cancellationToken);
        var relatedTeamId = finding.RelatedTeamId ?? await ResolveEvidenceTeamIdAsync(
            finding.RelatedSource,
            finding.RelatedSourceTeamId,
            cancellationToken);

        var teamIds = new[] { affectedTeamId, relatedTeamId }
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToList();
        var teamNames = teamIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await dbContext.Teams
                .AsNoTracking()
                .Where(x => teamIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.CanonicalName, cancellationToken);

        var affectedGames = await BuildEvidenceTeamGamesAsync(
            affectedTeamId,
            finding.AffectedTeam?.CanonicalName,
            finding.Source,
            finding.SourceTeamId,
            teamNames,
            season,
            countryCode,
            competitionId,
            limit,
            cancellationToken);
        var relatedGames = await BuildEvidenceTeamGamesAsync(
            relatedTeamId,
            finding.RelatedTeam?.CanonicalName,
            finding.RelatedSource,
            finding.RelatedSourceTeamId,
            teamNames,
            season,
            countryCode,
            competitionId,
            limit,
            cancellationToken);

        return new IdentityEvidenceGamesResponseDto(
            finding.FindingType,
            affectedGames,
            relatedGames);
    }

    private async Task<Guid?> ResolveEvidenceTeamIdAsync(
        string? source,
        string? sourceTeamId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(sourceTeamId))
        {
            return null;
        }

        var teamIds = await dbContext.TeamAliases
            .AsNoTracking()
            .Where(x => x.Source == source && x.SourceTeamId == sourceTeamId)
            .Select(x => x.TeamId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return teamIds.Count == 1 ? teamIds[0] : null;
    }

    private async Task<IdentityEvidenceTeamGamesDto?> BuildEvidenceTeamGamesAsync(
        Guid? teamId,
        string? displayName,
        string? source,
        string? sourceTeamId,
        IReadOnlyDictionary<Guid, string> teamNames,
        string? season,
        string? countryCode,
        Guid? competitionId,
        int limit,
        CancellationToken cancellationToken)
    {
        if (!teamId.HasValue)
        {
            return null;
        }

        var games = dbContext.Games
            .AsNoTracking()
            .Where(x => x.HomeTeamId == teamId.Value || x.AwayTeamId == teamId.Value);

        if (!string.IsNullOrWhiteSpace(season))
        {
            games = games.Where(x => x.Season.Label == season);
        }

        if (!string.IsNullOrWhiteSpace(countryCode))
        {
            games = games.Where(x => x.Competition.CountryCode == countryCode);
        }

        if (competitionId.HasValue)
        {
            games = games.Where(x => x.CompetitionId == competitionId.Value);
        }

        var gameRows = await games
            .OrderByDescending(x => x.GameDateTimeUtc)
            .ThenByDescending(x => x.Id)
            .Take(Math.Clamp(limit, 1, 100))
            .Select(x => new IdentityEvidenceGameDto(
                x.Id,
                x.Source,
                x.SourceGameId,
                x.SourceUrl,
                x.GameDateTimeUtc,
                x.Competition.CountryCode,
                x.Competition.Name,
                x.Season.Label,
                x.HomeTeam.CanonicalName,
                x.AwayTeam.CanonicalName,
                x.HomeScore,
                x.AwayScore,
                x.Status))
            .ToListAsync(cancellationToken);

        var resolvedDisplayName = !string.IsNullOrWhiteSpace(displayName)
            ? displayName
            : teamNames.TryGetValue(teamId.Value, out var canonicalName)
                ? canonicalName
                : "Unknown team";

        return new IdentityEvidenceTeamGamesDto(
            teamId.Value,
            resolvedDisplayName,
            source,
            sourceTeamId,
            gameRows);
    }

    public async Task<IdentityHealthCheckFindingDto> ResolveFindingAsync(
        Guid findingId,
        ResolveIdentityFindingRequest request,
        CancellationToken cancellationToken)
    {
        var finding = await dbContext.IdentityHealthCheckFindings
            .Include(x => x.AffectedTeam)
            .Include(x => x.RelatedTeam)
            .FirstOrDefaultAsync(x => x.Id == findingId, cancellationToken)
            ?? throw new InvalidOperationException("Identity finding was not found.");

        if (finding.Status != IdentityFindingStatus.Open)
        {
            throw new InvalidOperationException("Only open identity findings can be resolved.");
        }

        var action = NormalizeResolutionAction(request.Action);
        if (action == "edit_metadata" && finding.FindingType != IdentityFindingType.MissingMetadata)
        {
            throw new InvalidOperationException("Only missing metadata findings can be resolved by editing team metadata.");
        }

        if (action == "keep_separate")
        {
            await PopulateFindingTeamIdsAsync(finding, cancellationToken);
        }

        if (action == "merge_duplicate")
        {
            await MergeFindingTeamsAsync(finding, request, cancellationToken);
        }
        else if (action == "edit_metadata")
        {
            await EditMetadataAsync(finding, request, cancellationToken);
        }

        finding.Status = action == "ignore" ? IdentityFindingStatus.Ignored : IdentityFindingStatus.Resolved;
        finding.ResolutionAction = action;
        finding.ResolvedBy = string.IsNullOrWhiteSpace(request.ResolvedBy) ? "admin" : request.ResolvedBy.Trim();
        finding.ResolvedAtUtc = DateTime.UtcNow;
        finding.ResolutionNote = request.Note?.Trim();

        await SaveReviewDecisionAsync(finding, action, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        await RefreshRunCountsAsync(finding.RunId, cancellationToken);

        return ToDto(finding);
    }

    public async Task<IReadOnlyList<IdentityDistinctTeamsDecisionDto>> GetDistinctTeamDecisionsAsync(
        CancellationToken cancellationToken)
    {
        var decisions = await dbContext.IdentityReviewDecisions
            .AsNoTracking()
            .Include(x => x.AffectedTeam)
            .Include(x => x.RelatedTeam)
            .Where(x =>
                x.ResolutionAction == "keep_separate" &&
                x.AffectedTeamId.HasValue &&
                x.RelatedTeamId.HasValue &&
                x.AffectedTeam != null &&
                x.RelatedTeam != null)
            .ToListAsync(cancellationToken);

        return decisions
            .GroupBy(x => CreateDistinctTeamsDecisionKey(x.AffectedTeamId!.Value, x.RelatedTeamId!.Value), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var decision = group.OrderByDescending(x => x.CreatedAtUtc).First();
                var leftId = decision.AffectedTeamId!.Value;
                var rightId = decision.RelatedTeamId!.Value;
                var left = decision.AffectedTeam!;
                var right = decision.RelatedTeam!;
                if (string.CompareOrdinal(leftId.ToString("N"), rightId.ToString("N")) > 0)
                {
                    (leftId, rightId) = (rightId, leftId);
                    (left, right) = (right, left);
                }

                return new IdentityDistinctTeamsDecisionDto(
                    leftId,
                    left.CanonicalName,
                    rightId,
                    right.CanonicalName,
                    decision.Note,
                    decision.CreatedBy,
                    decision.CreatedAtUtc);
            })
            .OrderBy(x => x.LeftTeamName)
            .ThenBy(x => x.RightTeamName)
            .ToList();
    }

    public async Task RemoveDistinctTeamDecisionAsync(
        Guid leftTeamId,
        Guid rightTeamId,
        CancellationToken cancellationToken)
    {
        if (leftTeamId == rightTeamId)
        {
            throw new InvalidOperationException("The two teams must be different.");
        }

        var decisions = await dbContext.IdentityReviewDecisions
            .Where(x =>
                x.ResolutionAction == "keep_separate" &&
                ((x.AffectedTeamId == leftTeamId && x.RelatedTeamId == rightTeamId) ||
                    (x.AffectedTeamId == rightTeamId && x.RelatedTeamId == leftTeamId)))
            .ToListAsync(cancellationToken);

        if (decisions.Count == 0)
        {
            throw new InvalidOperationException("The distinct-team decision was not found.");
        }

        dbContext.IdentityReviewDecisions.RemoveRange(decisions);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteRunAsync(Guid runId, CancellationToken cancellationToken)
    {
        var run = await dbContext.IdentityHealthCheckRuns
            .FirstOrDefaultAsync(x => x.Id == runId, cancellationToken)
            ?? throw new InvalidOperationException("Identity health check run was not found.");

        dbContext.IdentityHealthCheckRuns.Remove(run);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task InvalidateChangedScopeAsync(IdentityChangedScope changedScope, CancellationToken cancellationToken)
    {
        var source = string.IsNullOrWhiteSpace(changedScope.Source)
            ? null
            : changedScope.Source.Trim().ToLowerInvariant();
        var season = string.IsNullOrWhiteSpace(changedScope.Season)
            ? null
            : changedScope.Season.Trim();
        var countryCode = NormalizeCountryCode(changedScope.CountryCode);
        var poolKey = NormalizePoolKey(changedScope.EloPoolKey);
        var now = DateTime.UtcNow;

        var runs = await dbContext.IdentityHealthCheckRuns
            .Where(x =>
                x.InvalidatedAtUtc == null &&
                x.RulesVersion == IdentityHealthCheckRules.CurrentVersion &&
                (poolKey == null || x.ScopeKey.Contains($"pool={poolKey}")) &&
                (x.Source == null || source == null || x.Source == source) &&
                (x.Season == null || season == null || x.Season == season) &&
                (x.CountryCode == null || countryCode == null || x.CountryCode == countryCode) &&
                (x.CompetitionId == null || changedScope.CompetitionId == null || x.CompetitionId == changedScope.CompetitionId))
            .ToListAsync(cancellationToken);

        foreach (var run in runs)
        {
            run.InvalidatedAtUtc = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<List<ScopedGameRow>> LoadScopedGameRowsAsync(
        IdentityHealthCheckRequest request,
        CancellationToken cancellationToken)
    {
        var games = dbContext.Games
            .AsNoTracking()
            .Include(x => x.Competition)
            .Include(x => x.Season)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Source))
        {
            games = games.Where(x => x.Source == request.Source);
        }

        if (!string.IsNullOrWhiteSpace(request.EloPoolKey))
        {
            games = games.Where(x => x.Competition.EloPoolKey == request.EloPoolKey);
        }

        if (!string.IsNullOrWhiteSpace(request.Season))
        {
            games = games.Where(x => x.Season.Label == request.Season);
        }

        if (!string.IsNullOrWhiteSpace(request.CountryCode))
        {
            games = games.Where(x => x.Competition.CountryCode == request.CountryCode);
        }

        if (request.CompetitionId.HasValue)
        {
            games = games.Where(x => x.CompetitionId == request.CompetitionId);
        }

        return await games
            .Select(x => new ScopedGameRow(
                x.Source,
                x.CompetitionId,
                x.Competition.CountryCode,
                x.Season.Label,
                x.Season.StartDateUtc,
                x.Season.EndDateUtc,
                x.HomeTeamId,
                x.AwayTeamId))
            .ToListAsync(cancellationToken);
    }

    private async Task<List<TeamAlias>> LoadScopedAliasesAsync(
        IdentityHealthCheckRequest request,
        HashSet<Guid> scopedTeamIds,
        CancellationToken cancellationToken)
    {
        var aliases = dbContext.TeamAliases
            .AsNoTracking()
            .Include(x => x.Team)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Source))
        {
            aliases = aliases.Where(x => x.Source == request.Source);
        }

        if (scopedTeamIds.Count > 0)
        {
            aliases = aliases.Where(x => scopedTeamIds.Contains(x.TeamId));
        }

        if (!string.IsNullOrWhiteSpace(request.CountryCode))
        {
            aliases = aliases.Where(x => x.Team.CountryCode == request.CountryCode);
        }

        return await aliases.ToListAsync(cancellationToken);
    }

    private async Task<List<Team>> LoadTeamsWithoutAliasesAsync(
        IdentityHealthCheckRequest request,
        HashSet<Guid> scopedTeamIds,
        CancellationToken cancellationToken)
    {
        if (scopedTeamIds.Count == 0)
        {
            return [];
        }

        var teams = dbContext.Teams.AsNoTracking().Where(x => scopedTeamIds.Contains(x.Id));

        if (!string.IsNullOrWhiteSpace(request.CountryCode))
        {
            teams = teams.Where(x => x.CountryCode == request.CountryCode);
        }

        return await teams.ToListAsync(cancellationToken);
    }

    private static IEnumerable<IdentityHealthCheckFinding> BuildMissingMetadataFindings(
        IdentityHealthCheckRun run,
        IReadOnlyCollection<Team> teams,
        IReadOnlyCollection<ScopedGameRow> gameRows,
        DateTime now)
    {
        var suggestedCountriesByTeam = BuildSuggestedCountriesByTeam(gameRows);

        return teams
            .Where(x => string.IsNullOrWhiteSpace(x.CountryCode) || x.CountryCode == "UNK")
            .Select(x =>
            {
                suggestedCountriesByTeam.TryGetValue(x.Id, out var suggestedCountryCode);
                var suggestion = string.IsNullOrWhiteSpace(suggestedCountryCode)
                    ? "Edit canonical team metadata before relying on country filters."
                    : $"Set canonical team country to '{suggestedCountryCode}' if this matches the team identity.";

                return NewFinding(
                    run,
                    IdentityFindingType.MissingMetadata,
                    IdentityFindingSeverity.Warning,
                    run.Source,
                    null,
                    x.Id,
                    null,
                    null,
                    null,
                    $"Team '{x.CanonicalName}' is missing trusted country metadata.",
                    suggestion,
                    now,
                    suggestedCountryCode);
            });
    }

    private static IEnumerable<IdentityHealthCheckFinding> BuildAliasObservationFindings(
        IdentityHealthCheckRun run,
        IReadOnlyCollection<TeamAlias> aliases,
        DateTime now)
    {
        return aliases
            .GroupBy(x => new { x.Source, x.SourceTeamId, x.TeamId })
            .Where(x => x.Select(a => NormalizeDisplayName(a.AliasName)).Distinct().Count() > 1)
            .Select(x =>
            {
                var first = x.First();
                var names = string.Join(", ", x.Select(a => $"'{a.AliasName}'").Distinct().Order());
                return NewFinding(
                    run,
                    IdentityFindingType.AliasObservation,
                    IdentityFindingSeverity.Warning,
                    first.Source,
                    first.SourceTeamId,
                    first.TeamId,
                    null,
                    null,
                    null,
                    $"Source team '{first.Source}:{first.SourceTeamId}' has multiple observed names: {names}.",
                    "Accept the alias observation under the existing canonical team.",
                    now);
            });
    }

    private static IEnumerable<IdentityHealthCheckFinding> BuildSourceTeamSplitFindings(
        IdentityHealthCheckRun run,
        IReadOnlyCollection<TeamAlias> aliases,
        DateTime now)
    {
        return aliases
            .GroupBy(x => new { x.Source, x.SourceTeamId })
            .Where(x => x.Select(a => a.TeamId).Distinct().Count() > 1)
            .Select(x =>
            {
                var first = x.First();
                var second = x.First(a => a.TeamId != first.TeamId);
                return NewFinding(
                    run,
                    IdentityFindingType.SourceTeamSplit,
                    IdentityFindingSeverity.Blocker,
                    first.Source,
                    first.SourceTeamId,
                    first.TeamId,
                    second.Source,
                    second.SourceTeamId,
                    second.TeamId,
                    $"Source team '{first.Source}:{first.SourceTeamId}' maps to multiple canonical teams.",
                    "Merge the duplicate teams or move aliases under one canonical team before rebuilding ELO.",
                    now);
            });
    }

    private static IEnumerable<IdentityHealthCheckFinding> BuildSimilarAliasFindings(
        IdentityHealthCheckRun run,
        IReadOnlyList<TeamAlias> aliases,
        IReadOnlyCollection<ScopedGameRow> gameRows,
        DateTime now)
    {
        var findings = new List<IdentityHealthCheckFinding>();
        var rows = aliases
            .GroupBy(x => new { x.TeamId, x.Source, x.SourceTeamId, NormalizedName = NormalizeTeamName(x.AliasName) })
            .Select(x => x.First())
            .ToList();
        var teamCompetitionIds = BuildTeamCompetitionIds(gameRows);
        var seen = new HashSet<string>();

        for (var i = 0; i < rows.Count; i++)
        {
            for (var j = i + 1; j < rows.Count; j++)
            {
                var left = rows[i];
                var right = rows[j];

                if (left.TeamId == right.TeamId ||
                    !AreCountriesCompatible(left.Team.CountryCode, right.Team.CountryCode) ||
                    !HaveCompetitionOverlap(left.TeamId, right.TeamId, teamCompetitionIds) ||
                    !AreSimilarNames(left.AliasName, right.AliasName))
                {
                    continue;
                }

                var sameSource = left.Source == right.Source;
                var findingType = sameSource
                    ? IdentityFindingType.PossibleDuplicate
                    : IdentityFindingType.PossibleCrossSourceMatch;
                var key = $"{findingType}:{OrderedPairKey(left.TeamId, right.TeamId)}:{NormalizeTeamName(left.AliasName)}";

                if (!seen.Add(key))
                {
                    continue;
                }

                findings.Add(NewFinding(
                    run,
                    findingType,
                    IdentityFindingSeverity.Blocker,
                    left.Source,
                    left.SourceTeamId,
                    left.TeamId,
                    right.Source,
                    right.SourceTeamId,
                    right.TeamId,
                    $"Teams '{left.Team.CanonicalName}' and '{right.Team.CanonicalName}' have similar observed names in overlapping competition data.",
                    sameSource
                        ? "Review whether different provider ids represent one team; merge or keep separate."
                        : "Review whether cross-source team observations should map to one canonical team.",
                    now));
            }
        }

        return findings;
    }

    private static IEnumerable<IdentityHealthCheckFinding> BuildCrossSeasonSplitFindings(
        IdentityHealthCheckRun run,
        IReadOnlyList<TeamAlias> aliases,
        IReadOnlyCollection<ScopedGameRow> gameRows,
        DateTime now)
    {
        var findings = new List<IdentityHealthCheckFinding>();
        var appearances = BuildTeamSeasonAppearances(gameRows);
        var aliasesByTeam = aliases
            .GroupBy(x => x.TeamId)
            .ToDictionary(x => x.Key, x => x.ToList());
        var seen = new HashSet<string>();

        foreach (var left in appearances)
        {
            foreach (var right in appearances)
            {
                if (left.TeamId == right.TeamId ||
                    left.CompetitionId != right.CompetitionId ||
                    !AreNearbySeasons(left, right))
                {
                    continue;
                }

                if (!aliasesByTeam.TryGetValue(left.TeamId, out var leftAliases) ||
                    !aliasesByTeam.TryGetValue(right.TeamId, out var rightAliases))
                {
                    continue;
                }

                var matchingLeftAlias = leftAliases.FirstOrDefault(a =>
                    rightAliases.Any(b => AreSimilarNames(a.AliasName, b.AliasName)));
                var matchingRightAlias = matchingLeftAlias is null
                    ? null
                    : rightAliases.First(a => AreSimilarNames(matchingLeftAlias.AliasName, a.AliasName));

                if (matchingLeftAlias is null || matchingRightAlias is null)
                {
                    continue;
                }

                var key = $"cross-season:{OrderedPairKey(left.TeamId, right.TeamId)}:{left.CompetitionId}";
                if (!seen.Add(key))
                {
                    continue;
                }

                findings.Add(NewFinding(
                    run,
                    IdentityFindingType.PossibleCrossSeasonSplit,
                    IdentityFindingSeverity.Blocker,
                    matchingLeftAlias.Source,
                    matchingLeftAlias.SourceTeamId,
                    left.TeamId,
                    matchingRightAlias.Source,
                    matchingRightAlias.SourceTeamId,
                    right.TeamId,
                    $"Teams '{matchingLeftAlias.Team.CanonicalName}' and '{matchingRightAlias.Team.CanonicalName}' have similar observed names '{matchingLeftAlias.AliasName}' and '{matchingRightAlias.AliasName}' in nearby seasons '{left.Season}' and '{right.Season}' for the same competition.",
                    "Review whether this is a sponsor/name change, duplicate provider id, or separate team.",
                    now));
            }
        }

        return findings;
    }

    public async Task<IdentityTeamMergeResultDto> MergeTeamsAsync(
        Guid sourceTeamId,
        Guid targetTeamId,
        bool confirmMergeWithRatings,
        CancellationToken cancellationToken)
    {
        await MergeTeamsCoreAsync(sourceTeamId, targetTeamId, confirmMergeWithRatings, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await InvalidateChangedScopeAsync(new IdentityChangedScope(), cancellationToken);

        var targetTeam = await dbContext.Teams
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == targetTeamId, cancellationToken)
            ?? throw new InvalidOperationException("Target team was not found after the merge.");

        return new IdentityTeamMergeResultDto(targetTeam.Id, sourceTeamId, targetTeam.CanonicalName);
    }

    private async Task MergeFindingTeamsAsync(
        IdentityHealthCheckFinding finding,
        ResolveIdentityFindingRequest request,
        CancellationToken cancellationToken)
    {
        var affectedTeamId = finding.AffectedTeamId ?? await ResolveSourceAliasTeamIdAsync(
            finding.Source,
            finding.SourceTeamId,
            cancellationToken);
        var targetTeamId = request.TargetTeamId ?? affectedTeamId;
        var relatedTeamId = finding.RelatedTeamId;
        if (affectedTeamId == targetTeamId && !relatedTeamId.HasValue)
        {
            relatedTeamId = await ResolveSourceAliasTeamIdAsync(
                finding.RelatedSource,
                finding.RelatedSourceTeamId,
                cancellationToken);
        }

        var sourceTeamId = affectedTeamId == targetTeamId
            ? relatedTeamId
            : affectedTeamId;

        if (!sourceTeamId.HasValue)
        {
            throw new InvalidOperationException("The finding does not identify a second team to merge.");
        }

        await MergeTeamsCoreAsync(sourceTeamId.Value, targetTeamId, request.ConfirmMergeWithRatings, cancellationToken);
    }

    private async Task PopulateFindingTeamIdsAsync(
        IdentityHealthCheckFinding finding,
        CancellationToken cancellationToken)
    {
        finding.AffectedTeamId ??= await ResolveSourceAliasTeamIdAsync(
            finding.Source,
            finding.SourceTeamId,
            cancellationToken);
        finding.RelatedTeamId ??= await ResolveSourceAliasTeamIdAsync(
            finding.RelatedSource,
            finding.RelatedSourceTeamId,
            cancellationToken);
    }

    private async Task MergeTeamsCoreAsync(
        Guid sourceTeamId,
        Guid targetTeamId,
        bool confirmMergeWithRatings,
        CancellationToken cancellationToken)
    {
        if (sourceTeamId == targetTeamId)
        {
            throw new InvalidOperationException("Source and target teams must be different.");
        }

        var targetTeam = await dbContext.Teams.FindAsync([targetTeamId], cancellationToken)
            ?? throw new InvalidOperationException("Target team was not found.");
        var sourceTeam = await dbContext.Teams.FindAsync([sourceTeamId], cancellationToken)
            ?? throw new InvalidOperationException("Source team was not found.");

        var targetHasDerivedData = await TeamHasDerivedDataAsync(targetTeam.Id, cancellationToken);
        var sourceHasDerivedData = await TeamHasDerivedDataAsync(sourceTeam.Id, cancellationToken);
        if (targetHasDerivedData && sourceHasDerivedData && !confirmMergeWithRatings)
        {
            throw new InvalidOperationException("Both teams have games or rating history. Resubmit with confirmMergeWithRatings=true to merge.");
        }

        var sourceAliases = await dbContext.TeamAliases
            .Where(x => x.TeamId == sourceTeam.Id)
            .ToListAsync(cancellationToken);
        foreach (var alias in sourceAliases)
        {
            var duplicateAlias = await dbContext.TeamAliases.FirstOrDefaultAsync(
                x =>
                    x.Id != alias.Id &&
                    x.Source == alias.Source &&
                    x.SourceTeamId == alias.SourceTeamId &&
                    x.AliasName == alias.AliasName,
                cancellationToken);

            if (duplicateAlias is not null)
            {
                dbContext.TeamAliases.Remove(alias);
            }
            else
            {
                alias.TeamId = targetTeam.Id;
            }
        }

        await dbContext.Games
            .Where(x => x.HomeTeamId == sourceTeam.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.HomeTeamId, targetTeam.Id), cancellationToken);
        await dbContext.Games
            .Where(x => x.AwayTeamId == sourceTeam.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.AwayTeamId, targetTeam.Id), cancellationToken);

        var duplicateHistoryIds = await dbContext.RatingHistories
            .Where(sourceHistory =>
                sourceHistory.TeamId == sourceTeam.Id &&
                dbContext.RatingHistories.Any(targetHistory =>
                    targetHistory.EloPoolKey == sourceHistory.EloPoolKey &&
                    targetHistory.GameId == sourceHistory.GameId &&
                    targetHistory.TeamId == targetTeam.Id &&
                    targetHistory.RulesetVersion == sourceHistory.RulesetVersion))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        await dbContext.RatingHistories
            .Where(x => duplicateHistoryIds.Contains(x.Id))
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.RatingHistories
            .Where(x => x.TeamId == sourceTeam.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.TeamId, targetTeam.Id), cancellationToken);
        await dbContext.RatingHistories
            .Where(x => x.OpponentTeamId == sourceTeam.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.OpponentTeamId, targetTeam.Id), cancellationToken);

        var sourceRatings = await dbContext.TeamRatings
            .Where(x => x.TeamId == sourceTeam.Id)
            .ToListAsync(cancellationToken);
        foreach (var sourceRating in sourceRatings)
        {
            var targetRating = await dbContext.TeamRatings.FindAsync(
                [sourceRating.EloPoolKey, targetTeam.Id, sourceRating.RulesetVersion],
                cancellationToken);
            if (targetRating is null)
            {
                dbContext.TeamRatings.Remove(sourceRating);
                dbContext.TeamRatings.Add(new TeamRating
                {
                    TeamId = targetTeam.Id,
                    EloPoolKey = sourceRating.EloPoolKey,
                    RulesetVersion = sourceRating.RulesetVersion,
                    Elo = sourceRating.Elo,
                    GamesPlayed = sourceRating.GamesPlayed,
                    LastGameId = sourceRating.LastGameId,
                    UpdatedAtUtc = sourceRating.UpdatedAtUtc
                });
            }
            else
            {
                dbContext.TeamRatings.Remove(sourceRating);
            }
        }

        dbContext.Teams.Remove(sourceTeam);
    }

    private async Task<Guid> ResolveSourceAliasTeamIdAsync(
        string? source,
        string? sourceTeamId,
        CancellationToken cancellationToken)
    {
        var sourceTeamIds = await dbContext.TeamAliases
            .Where(x =>
                x.Source == source &&
                x.SourceTeamId == sourceTeamId)
            .Select(x => x.TeamId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return sourceTeamIds.Count switch
        {
            1 => sourceTeamIds[0],
            0 => throw new InvalidOperationException("The finding does not identify a second team or source alias to merge."),
            _ => throw new InvalidOperationException("The source alias is mapped to multiple teams; resolve the source-team split first.")
        };
    }

    private async Task EditMetadataAsync(
        IdentityHealthCheckFinding finding,
        ResolveIdentityFindingRequest request,
        CancellationToken cancellationToken)
    {
        var teamId = request.TargetTeamId
            ?? finding.AffectedTeamId
            ?? throw new InvalidOperationException("targetTeamId is required to edit metadata.");
        var team = await dbContext.Teams.FindAsync([teamId], cancellationToken)
            ?? throw new InvalidOperationException("Team was not found.");

        if (!string.IsNullOrWhiteSpace(request.CanonicalName))
        {
            team.CanonicalName = request.CanonicalName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.CountryCode))
        {
            team.CountryCode = NormalizeCountryCode(request.CountryCode) ?? "UNK";
        }

        if (request.IsActive.HasValue)
        {
            team.IsActive = request.IsActive.Value;
        }
    }

    private async Task<bool> TeamHasDerivedDataAsync(Guid teamId, CancellationToken cancellationToken)
    {
        return await dbContext.Games.AnyAsync(x => x.HomeTeamId == teamId || x.AwayTeamId == teamId, cancellationToken) ||
            await dbContext.RatingHistories.AnyAsync(x => x.TeamId == teamId || x.OpponentTeamId == teamId, cancellationToken) ||
            await dbContext.TeamRatings.AnyAsync(x => x.TeamId == teamId, cancellationToken);
    }

    private async Task<List<IdentityHealthCheckFinding>> RemoveReviewedFindingsAsync(
        List<IdentityHealthCheckFinding> findings,
        CancellationToken cancellationToken)
    {
        if (findings.Count == 0)
        {
            return findings;
        }

        var storedDecisions = await dbContext.IdentityReviewDecisions
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var storedDecisionKeys = storedDecisions
            .Select(x => x.DecisionKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var distinctTeamDecisionKeys = storedDecisions
            .Where(x =>
                x.ResolutionAction == "keep_separate" &&
                x.AffectedTeamId.HasValue &&
                x.RelatedTeamId.HasValue)
            .Select(x => CreateDistinctTeamsDecisionKey(x.AffectedTeamId!.Value, x.RelatedTeamId!.Value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var resolvedFindingKeys = await dbContext.IdentityHealthCheckFindings
            .AsNoTracking()
            .Where(x =>
                x.Status != IdentityFindingStatus.Open &&
                (x.ResolutionAction == "keep_separate" ||
                    x.ResolutionAction == "accept_alias" ||
                    x.ResolutionAction == "ignore" ||
                    x.ResolutionAction == "merge_duplicate"))
            .Select(x => new
            {
                x.FindingType,
                x.AffectedTeamId,
                x.RelatedTeamId,
                x.Source,
                x.SourceTeamId,
                x.RelatedSource,
                x.RelatedSourceTeamId
            })
            .ToListAsync(cancellationToken);
        var reviewedKeys = storedDecisionKeys
            .Concat(resolvedFindingKeys.Select(x => CreateDecisionKey(
                x.FindingType,
                x.AffectedTeamId,
                x.RelatedTeamId,
                x.Source,
                x.SourceTeamId,
                x.RelatedSource,
                x.RelatedSourceTeamId)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return findings
            .Where(x =>
                !IsDistinctTeamPairSuppressed(x, distinctTeamDecisionKeys) &&
                !reviewedKeys.Contains(CreateDecisionKey(x)))
            .ToList();
    }

    private async Task SaveReviewDecisionAsync(
        IdentityHealthCheckFinding finding,
        string action,
        CancellationToken cancellationToken)
    {
        if (action is not ("keep_separate" or "accept_alias" or "ignore" or "merge_duplicate"))
        {
            return;
        }

        var decisionKey = action == "keep_separate" && finding.AffectedTeamId.HasValue && finding.RelatedTeamId.HasValue
            ? CreateDistinctTeamsDecisionKey(finding.AffectedTeamId.Value, finding.RelatedTeamId.Value)
            : CreateDecisionKey(finding);
        var exists = await dbContext.IdentityReviewDecisions
            .AnyAsync(x => x.DecisionKey == decisionKey, cancellationToken);
        var alreadyPending = dbContext.ChangeTracker
            .Entries<IdentityReviewDecision>()
            .Any(x => x.State != EntityState.Deleted &&
                string.Equals(x.Entity.DecisionKey, decisionKey, StringComparison.OrdinalIgnoreCase));
        if (exists || alreadyPending)
        {
            return;
        }

        dbContext.IdentityReviewDecisions.Add(new IdentityReviewDecision
        {
            Id = Guid.NewGuid(),
            DecisionKey = decisionKey,
            FindingType = finding.FindingType,
            ResolutionAction = action,
            AffectedTeamId = finding.AffectedTeamId,
            RelatedTeamId = finding.RelatedTeamId,
            Source = finding.Source,
            SourceTeamId = finding.SourceTeamId,
            RelatedSource = finding.RelatedSource,
            RelatedSourceTeamId = finding.RelatedSourceTeamId,
            Note = finding.ResolutionNote,
            CreatedBy = finding.ResolvedBy,
            CreatedAtUtc = DateTime.UtcNow
        });
    }

    private async Task RefreshRunCountsAsync(Guid runId, CancellationToken cancellationToken)
    {
        var run = await dbContext.IdentityHealthCheckRuns.FindAsync([runId], cancellationToken);
        if (run is null)
        {
            return;
        }

        run.FindingsCount = await dbContext.IdentityHealthCheckFindings.CountAsync(x => x.RunId == runId, cancellationToken);
        run.UnresolvedBlockersCount = await dbContext.IdentityHealthCheckFindings.CountAsync(
            x => x.RunId == runId &&
                x.Severity == IdentityFindingSeverity.Blocker &&
                x.Status == IdentityFindingStatus.Open,
            cancellationToken);

        if (run.UnresolvedBlockersCount > 0)
        {
            run.Status = IdentityHealthCheckStatus.Blockers;
        }
        else
        {
            var openWarnings = await dbContext.IdentityHealthCheckFindings.AnyAsync(
                x => x.RunId == runId &&
                    x.Severity == IdentityFindingSeverity.Warning &&
                    x.Status == IdentityFindingStatus.Open,
                cancellationToken);
            run.Status = openWarnings ? IdentityHealthCheckStatus.Warnings : IdentityHealthCheckStatus.Clean;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static IdentityHealthCheckFinding NewFinding(
        IdentityHealthCheckRun run,
        string findingType,
        string severity,
        string? source,
        string? sourceTeamId,
        Guid? affectedTeamId,
        string? relatedSource,
        string? relatedSourceTeamId,
        Guid? relatedTeamId,
        string evidence,
        string suggestedAction,
        DateTime now,
        string? countryCode = null)
    {
        return new IdentityHealthCheckFinding
        {
            Id = Guid.NewGuid(),
            RunId = run.Id,
            FindingType = findingType,
            Severity = severity,
            Status = IdentityFindingStatus.Open,
            Source = source,
            SourceTeamId = sourceTeamId,
            AffectedTeamId = affectedTeamId,
            RelatedSource = relatedSource,
            RelatedSourceTeamId = relatedSourceTeamId,
            RelatedTeamId = relatedTeamId,
            Season = run.Season,
            CountryCode = countryCode ?? run.CountryCode,
            CompetitionId = run.CompetitionId,
            Evidence = evidence,
            SuggestedAction = suggestedAction,
            CreatedAtUtc = now
        };
    }

    private static IdentityHealthCheckRequest NormalizeRequest(IdentityHealthCheckRequest request)
    {
        return new IdentityHealthCheckRequest
        {
            EloPoolKey = NormalizePoolKey(request.EloPoolKey),
            Source = string.IsNullOrWhiteSpace(request.Source) ? null : request.Source.Trim().ToLowerInvariant(),
            Season = string.IsNullOrWhiteSpace(request.Season) ? null : SeasonLabelNormalizer.ToFullSeasonLabel(request.Season),
            CountryCode = NormalizeCountryCode(request.CountryCode),
            CompetitionId = request.CompetitionId,
            Force = request.Force
        };
    }

    private static string BuildScopeKey(IdentityHealthCheckRequest request)
    {
        return string.Join("|", new[]
        {
            $"source={request.Source ?? "*"}",
            $"season={request.Season ?? "*"}",
            $"country={request.CountryCode ?? "*"}",
            $"competition={request.CompetitionId?.ToString() ?? "*"}",
            $"pool={request.EloPoolKey ?? "*"}"
        });
    }

    private static string? NormalizePoolKey(string? poolKey)
    {
        if (string.IsNullOrWhiteSpace(poolKey))
        {
            return null;
        }

        var normalized = poolKey.Trim().ToLowerInvariant();
        if (!EloPoolKeys.IsSupported(normalized))
        {
            throw new ArgumentException($"Unsupported ELO pool '{poolKey}'.", nameof(poolKey));
        }

        return normalized;
    }

    private static string? NormalizeCountryCode(string? countryCode)
        => CountryCodeCatalog.Normalize(countryCode);

    private static bool CountryMatchesTeam(string? teamCountryCode, string requestedCountryCode)
    {
        var normalizedTeamCountryCode = NormalizeCountryCode(teamCountryCode);
        return requestedCountryCode == "UNK"
            ? string.IsNullOrWhiteSpace(normalizedTeamCountryCode) || normalizedTeamCountryCode == "UNK"
            : normalizedTeamCountryCode == requestedCountryCode;
    }

    private static IdentityCountryOptionDto? NameToCountryOption(string country)
    {
        return country.Trim() switch
        {
            "Belgium" => new IdentityCountryOptionDto("BE", "Belgium"),
            "Azerbaijan" => new IdentityCountryOptionDto("AZ", "Azerbaijan"),
            "Cyprus" => new IdentityCountryOptionDto("CY", "Cyprus"),
            "Czech Republic" => new IdentityCountryOptionDto("CZ", "Czech Republic"),
            "France" => new IdentityCountryOptionDto("FR", "France"),
            "Germany" => new IdentityCountryOptionDto("DE", "Germany"),
            "Greece" => new IdentityCountryOptionDto("GR", "Greece"),
            "Israel" => new IdentityCountryOptionDto("IL", "Israel"),
            "Italy" => new IdentityCountryOptionDto("IT", "Italy"),
            "Latvia" => new IdentityCountryOptionDto("LV", "Latvia"),
            "Lithuania" => new IdentityCountryOptionDto("LT", "Lithuania"),
            "Poland" => new IdentityCountryOptionDto("PL", "Poland"),
            "Russia" => new IdentityCountryOptionDto("RU", "Russia"),
            "Romania" => new IdentityCountryOptionDto("RO", "Romania"),
            "Slovakia" => new IdentityCountryOptionDto("SK", "Slovakia"),
            "Spain" => new IdentityCountryOptionDto("ES", "Spain"),
            "Switzerland" => new IdentityCountryOptionDto("CH", "Switzerland"),
            "Turkey" => new IdentityCountryOptionDto("TR", "Turkey"),
            _ => null
        };
    }

    private static string CountryNameFromCode(string countryCode)
        => InternationalTeamCatalog.TryGetCanonicalName(countryCode, out var internationalName)
            ? internationalName
            : CountryCodeCatalog.DisplayName(countryCode);

    private static string NormalizeResolutionAction(string action)
    {
        var normalized = action.Trim().ToLowerInvariant();
        return normalized is "accept_alias" or "merge_duplicate" or "keep_separate" or "edit_metadata" or "ignore" or "defer_review" or "resolve"
            ? normalized
            : throw new InvalidOperationException("Unsupported identity finding resolution action.");
    }

    private static string? ExtractMissingMetadataTeamName(string evidence)
    {
        const string prefix = "Team '";
        const string suffix = "' is missing trusted country metadata.";

        if (!evidence.StartsWith(prefix, StringComparison.Ordinal) ||
            !evidence.EndsWith(suffix, StringComparison.Ordinal))
        {
            return null;
        }

        return evidence[prefix.Length..^suffix.Length];
    }

    private static string NormalizeDisplayName(string value)
    {
        return value.Trim().ToUpperInvariant();
    }

    private static string NormalizeTeamName(string value)
    {
        var characters = value
            .ToUpperInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray();

        return new string(characters);
    }

    private static bool AreSimilarNames(string left, string right)
    {
        var normalizedLeft = NormalizeTeamName(left);
        var normalizedRight = NormalizeTeamName(right);

        if (normalizedLeft.Length < 4 || normalizedRight.Length < 4)
        {
            return normalizedLeft == normalizedRight;
        }

        if (normalizedLeft == normalizedRight ||
            normalizedLeft.Contains(normalizedRight, StringComparison.Ordinal) ||
            normalizedRight.Contains(normalizedLeft, StringComparison.Ordinal))
        {
            return true;
        }

        var maxLength = Math.Max(normalizedLeft.Length, normalizedRight.Length);
        var distance = LevenshteinDistance(normalizedLeft, normalizedRight);
        var similarity = 1 - (double)distance / maxLength;
        return similarity >= SimilarNameThreshold;
    }

    private static int LevenshteinDistance(string left, string right)
    {
        var distances = new int[left.Length + 1, right.Length + 1];

        for (var i = 0; i <= left.Length; i++)
        {
            distances[i, 0] = i;
        }

        for (var j = 0; j <= right.Length; j++)
        {
            distances[0, j] = j;
        }

        for (var i = 1; i <= left.Length; i++)
        {
            for (var j = 1; j <= right.Length; j++)
            {
                var cost = left[i - 1] == right[j - 1] ? 0 : 1;
                distances[i, j] = Math.Min(
                    Math.Min(distances[i - 1, j] + 1, distances[i, j - 1] + 1),
                    distances[i - 1, j - 1] + cost);
            }
        }

        return distances[left.Length, right.Length];
    }

    private static bool AreCountriesCompatible(string? left, string? right)
    {
        return string.IsNullOrWhiteSpace(left) ||
            string.IsNullOrWhiteSpace(right) ||
            left == "UNK" ||
            right == "UNK" ||
            CountryCodeCatalog.AreEquivalent(left, right);
    }

    private static bool HaveCompetitionOverlap(
        Guid leftTeamId,
        Guid rightTeamId,
        IReadOnlyDictionary<Guid, HashSet<Guid>> teamCompetitionIds)
    {
        if (!teamCompetitionIds.TryGetValue(leftTeamId, out var leftCompetitions) ||
            !teamCompetitionIds.TryGetValue(rightTeamId, out var rightCompetitions))
        {
            return true;
        }

        return leftCompetitions.Overlaps(rightCompetitions);
    }

    private static Dictionary<Guid, HashSet<Guid>> BuildTeamCompetitionIds(IEnumerable<ScopedGameRow> gameRows)
    {
        var teamCompetitionIds = new Dictionary<Guid, HashSet<Guid>>();

        foreach (var game in gameRows)
        {
            AddTeamCompetition(game.HomeTeamId, game.CompetitionId);
            AddTeamCompetition(game.AwayTeamId, game.CompetitionId);
        }

        return teamCompetitionIds;

        void AddTeamCompetition(Guid teamId, Guid competitionId)
        {
            if (!teamCompetitionIds.TryGetValue(teamId, out var competitions))
            {
                competitions = [];
                teamCompetitionIds[teamId] = competitions;
            }

            competitions.Add(competitionId);
        }
    }

    private static Dictionary<Guid, string> BuildSuggestedCountriesByTeam(IEnumerable<ScopedGameRow> gameRows)
    {
        return gameRows
            .SelectMany(x => new[]
            {
                new { TeamId = x.HomeTeamId, x.CountryCode },
                new { TeamId = x.AwayTeamId, x.CountryCode }
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.CountryCode))
            .GroupBy(x => x.TeamId)
            .Select(x => new
            {
                TeamId = x.Key,
                Countries = x.Select(row => row.CountryCode!).Distinct().OrderBy(country => country).ToList()
            })
            .Where(x => x.Countries.Count == 1)
            .ToDictionary(x => x.TeamId, x => x.Countries[0]);
    }

    private static List<TeamSeasonAppearance> BuildTeamSeasonAppearances(IEnumerable<ScopedGameRow> gameRows)
    {
        return gameRows
            .SelectMany(x => new[]
            {
                new TeamSeasonAppearance(x.HomeTeamId, x.CompetitionId, x.Season, x.SeasonStartUtc, x.SeasonEndUtc),
                new TeamSeasonAppearance(x.AwayTeamId, x.CompetitionId, x.Season, x.SeasonStartUtc, x.SeasonEndUtc)
            })
            .Distinct()
            .ToList();
    }

    private static bool AreNearbySeasons(TeamSeasonAppearance left, TeamSeasonAppearance right)
    {
        if (left.Season == right.Season)
        {
            return false;
        }

        var gap = left.SeasonEndUtc <= right.SeasonStartUtc
            ? right.SeasonStartUtc - left.SeasonEndUtc
            : left.SeasonStartUtc - right.SeasonEndUtc;

        return gap.TotalDays >= 0 && gap.TotalDays <= 370;
    }

    private static string OrderedPairKey(Guid left, Guid right)
    {
        return string.CompareOrdinal(left.ToString("N"), right.ToString("N")) <= 0
            ? $"{left:N}:{right:N}"
            : $"{right:N}:{left:N}";
    }

    private static string CreateDecisionKey(IdentityHealthCheckFinding finding)
    {
        return CreateDecisionKey(
            finding.FindingType,
            finding.AffectedTeamId,
            finding.RelatedTeamId,
            finding.Source,
            finding.SourceTeamId,
            finding.RelatedSource,
            finding.RelatedSourceTeamId);
    }

    private static string CreateDecisionKey(
        string findingType,
        Guid? affectedTeamId,
        Guid? relatedTeamId,
        string? source,
        string? sourceTeamId,
        string? relatedSource,
        string? relatedSourceTeamId)
    {
        var pairKey = affectedTeamId.HasValue && relatedTeamId.HasValue
            ? OrderedPairKey(affectedTeamId.Value, relatedTeamId.Value)
            : affectedTeamId?.ToString("N") ?? "*";

        var sourceKey = $"{source ?? "*"}:{sourceTeamId ?? "*"}";
        var relatedSourceKey = $"{relatedSource ?? "*"}:{relatedSourceTeamId ?? "*"}";
        var incompletePairSuffix = affectedTeamId.HasValue && relatedTeamId.HasValue
            ? string.Empty
            : $"|source={sourceKey}|related={relatedSourceKey}";

        return findingType switch
        {
            IdentityFindingType.PossibleDuplicate or
                IdentityFindingType.PossibleCrossSourceMatch or
                IdentityFindingType.PossibleCrossSeasonSplit => $"{findingType}|teams={pairKey}{incompletePairSuffix}",
            IdentityFindingType.AliasObservation => $"{findingType}|team={affectedTeamId:N}|source={sourceKey}",
            IdentityFindingType.SourceTeamSplit => $"{findingType}|source={sourceKey}",
            _ => $"{findingType}|team={pairKey}|source={sourceKey}|related={relatedSourceKey}"
        };
    }

    private static bool IsDistinctTeamPairSuppressed(
        IdentityHealthCheckFinding finding,
        IReadOnlySet<string> distinctTeamDecisionKeys)
    {
        return (finding.FindingType is
                IdentityFindingType.PossibleDuplicate or
                IdentityFindingType.PossibleCrossSourceMatch or
                IdentityFindingType.PossibleCrossSeasonSplit) &&
            finding.AffectedTeamId.HasValue &&
            finding.RelatedTeamId.HasValue &&
            distinctTeamDecisionKeys.Contains(CreateDistinctTeamsDecisionKey(
                finding.AffectedTeamId.Value,
                finding.RelatedTeamId.Value));
    }

    private static string CreateDistinctTeamsDecisionKey(Guid leftTeamId, Guid rightTeamId)
    {
        return $"{DistinctTeamsDecisionPrefix}{OrderedPairKey(leftTeamId, rightTeamId)}";
    }

    private static IdentityHealthCheckRunDto ToDto(IdentityHealthCheckRun run)
    {
        var findings = run.Findings ?? [];
        var typeSummaries = findings
            .GroupBy(x => x.FindingType)
            .Select(x => new IdentityFindingTypeSummaryDto(
                x.Key,
                x.Count(f => f.Status == IdentityFindingStatus.Open),
                x.Count(f => f.Status == IdentityFindingStatus.Resolved),
                x.Count(f => f.Status == IdentityFindingStatus.Ignored)))
            .OrderByDescending(x => x.OpenCount)
            .ThenBy(x => x.FindingType)
            .ToList();

        return new IdentityHealthCheckRunDto(
            run.Id,
            run.Source,
            run.Season,
            run.CountryCode,
            run.CompetitionId,
            run.ScopeKey,
            run.RulesVersion,
            run.Status,
            run.FindingsCount,
            run.UnresolvedBlockersCount,
            findings.Count(x => x.Status == IdentityFindingStatus.Open),
            findings.Count(x => x.Status == IdentityFindingStatus.Open && x.Severity == IdentityFindingSeverity.Warning),
            findings.Count(x => x.Status == IdentityFindingStatus.Open && x.Severity == IdentityFindingSeverity.Blocker),
            findings.Count(x => x.Status == IdentityFindingStatus.Resolved),
            findings.Count(x => x.Status == IdentityFindingStatus.Ignored),
            typeSummaries,
            run.Forced,
            run.CheckedAtUtc,
            run.InvalidatedAtUtc);
    }

    private static IdentityHealthCheckFindingDto ToDto(
        IdentityHealthCheckFinding finding,
        Team? inferredAffectedTeam = null,
        Team? inferredRelatedTeam = null)
    {
        var affectedTeam = finding.AffectedTeam ?? inferredAffectedTeam;
        var relatedTeam = finding.RelatedTeam ?? inferredRelatedTeam;

        return new IdentityHealthCheckFindingDto(
            finding.Id,
            finding.RunId,
            finding.FindingType,
            finding.Severity,
            finding.Status,
            finding.Source,
            finding.SourceTeamId,
            affectedTeam?.Id,
            affectedTeam?.CanonicalName,
            affectedTeam?.CountryCode,
            affectedTeam?.IsActive,
            finding.RelatedSource,
            finding.RelatedSourceTeamId,
            relatedTeam?.Id,
            relatedTeam?.CanonicalName,
            finding.Season,
            finding.CountryCode,
            finding.CompetitionId,
            finding.FindingType == IdentityFindingType.MissingMetadata ? finding.CountryCode : null,
            finding.Evidence,
            finding.SuggestedAction,
            finding.ResolutionAction,
            finding.ResolutionNote,
            finding.CreatedAtUtc,
            finding.ResolvedAtUtc);
    }

    private sealed record ScopedGameRow(
        string Source,
        Guid CompetitionId,
        string? CountryCode,
        string Season,
        DateTime SeasonStartUtc,
        DateTime SeasonEndUtc,
        Guid HomeTeamId,
        Guid AwayTeamId);

    private sealed record TeamSeasonAppearance(
        Guid TeamId,
        Guid CompetitionId,
        string Season,
        DateTime SeasonStartUtc,
        DateTime SeasonEndUtc);

    private sealed record SourceTeamKey(string Source, string SourceTeamId);
}
