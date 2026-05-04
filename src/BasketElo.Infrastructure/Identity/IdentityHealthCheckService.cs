using BasketElo.Domain.Entities;
using BasketElo.Infrastructure.Backfill;
using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BasketElo.Infrastructure.Identity;

public class IdentityHealthCheckService(
    BasketEloDbContext dbContext,
    IBackfillCatalog backfillCatalog) : IIdentityHealthCheckService
{
    private const double SimilarNameThreshold = 0.86;
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
        new("UA", "Ukraine")
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
            .Select(x => new IdentityCountryOptionDto(x, CountryNameFromCode(x)))
            .Concat(backfillCountries)
            .Concat(DefaultCountryOptions)
            .GroupBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Code)
            .ToList();

        var competitions = await dbContext.Competitions
            .AsNoTracking()
            .OrderBy(x => x.CountryCode)
            .ThenBy(x => x.Name)
            .Select(x => new IdentityCompetitionOptionDto(x.Id, x.Name, x.CountryCode))
            .ToListAsync(cancellationToken);

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

        return await findings
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip((Math.Max(query.Page, 1) - 1) * Math.Clamp(query.Limit, 1, 5000))
            .Take(Math.Clamp(query.Limit, 1, 5000))
            .Select(x => ToDto(x))
            .ToListAsync(cancellationToken);
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
        if (action == "merge_duplicate")
        {
            await MergeTeamsAsync(finding, request, cancellationToken);
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
        var now = DateTime.UtcNow;

        var runs = await dbContext.IdentityHealthCheckRuns
            .Where(x =>
                x.InvalidatedAtUtc == null &&
                x.RulesVersion == IdentityHealthCheckRules.CurrentVersion &&
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
                    "Accept the alias observation under the existing canonical team or edit team metadata.",
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

    private async Task MergeTeamsAsync(
        IdentityHealthCheckFinding finding,
        ResolveIdentityFindingRequest request,
        CancellationToken cancellationToken)
    {
        var targetTeamId = request.TargetTeamId
            ?? finding.AffectedTeamId
            ?? throw new InvalidOperationException("targetTeamId is required to merge teams.");
        var sourceTeamId = finding.AffectedTeamId == targetTeamId
            ? finding.RelatedTeamId
            : finding.AffectedTeamId;

        if (!sourceTeamId.HasValue || sourceTeamId == targetTeamId)
        {
            throw new InvalidOperationException("The finding does not identify a second team to merge.");
        }

        var targetTeam = await dbContext.Teams.FindAsync([targetTeamId], cancellationToken)
            ?? throw new InvalidOperationException("Target team was not found.");
        var sourceTeam = await dbContext.Teams.FindAsync([sourceTeamId.Value], cancellationToken)
            ?? throw new InvalidOperationException("Source team was not found.");

        var targetHasDerivedData = await TeamHasDerivedDataAsync(targetTeam.Id, cancellationToken);
        var sourceHasDerivedData = await TeamHasDerivedDataAsync(sourceTeam.Id, cancellationToken);
        if (targetHasDerivedData && sourceHasDerivedData && !request.ConfirmMergeWithRatings)
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
                    targetHistory.GameId == sourceHistory.GameId &&
                    targetHistory.TeamId == targetTeam.Id))
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

        var duplicateSnapshotIds = await dbContext.RankingSnapshots
            .Where(sourceSnapshot =>
                sourceSnapshot.TeamId == sourceTeam.Id &&
                dbContext.RankingSnapshots.Any(targetSnapshot =>
                    targetSnapshot.SnapshotDate == sourceSnapshot.SnapshotDate &&
                    targetSnapshot.TeamId == targetTeam.Id))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        await dbContext.RankingSnapshots
            .Where(x => duplicateSnapshotIds.Contains(x.Id))
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.RankingSnapshots
            .Where(x => x.TeamId == sourceTeam.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.TeamId, targetTeam.Id), cancellationToken);

        var sourceRating = await dbContext.TeamRatings.FindAsync([sourceTeam.Id], cancellationToken);
        if (sourceRating is not null)
        {
            var targetRating = await dbContext.TeamRatings.FindAsync([targetTeam.Id], cancellationToken);
            if (targetRating is null)
            {
                dbContext.TeamRatings.Remove(sourceRating);
                dbContext.TeamRatings.Add(new TeamRating
                {
                    TeamId = targetTeam.Id,
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

        var storedDecisionKeys = await dbContext.IdentityReviewDecisions
            .AsNoTracking()
            .Select(x => x.DecisionKey)
            .ToListAsync(cancellationToken);
        var resolvedFindingKeys = await dbContext.IdentityHealthCheckFindings
            .AsNoTracking()
            .Where(x =>
                x.Status != IdentityFindingStatus.Open &&
                (x.ResolutionAction == "keep_separate" ||
                    x.ResolutionAction == "accept_alias" ||
                    x.ResolutionAction == "ignore"))
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
            .Where(x => !reviewedKeys.Contains(CreateDecisionKey(x)))
            .ToList();
    }

    private async Task SaveReviewDecisionAsync(
        IdentityHealthCheckFinding finding,
        string action,
        CancellationToken cancellationToken)
    {
        if (action is not ("keep_separate" or "accept_alias" or "ignore"))
        {
            return;
        }

        var decisionKey = CreateDecisionKey(finding);
        var exists = await dbContext.IdentityReviewDecisions
            .AnyAsync(x => x.DecisionKey == decisionKey, cancellationToken);
        if (exists)
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
            Source = string.IsNullOrWhiteSpace(request.Source) ? null : request.Source.Trim().ToLowerInvariant(),
            Season = string.IsNullOrWhiteSpace(request.Season) ? null : request.Season.Trim(),
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
            $"competition={request.CompetitionId?.ToString() ?? "*"}"
        });
    }

    private static string? NormalizeCountryCode(string? countryCode)
    {
        if (string.IsNullOrWhiteSpace(countryCode))
        {
            return null;
        }

        var normalized = countryCode.Trim().ToUpperInvariant();
        return normalized.Length <= 3 ? normalized : normalized[..3];
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
    {
        return countryCode.Trim().ToUpperInvariant() switch
        {
            "BE" or "BEL" => "Belgium",
            "AZ" or "AZE" => "Azerbaijan",
            "BA" or "BIH" => "Bosnia and Herzegovina",
            "BG" or "BGR" => "Bulgaria",
            "HR" or "HRV" => "Croatia",
            "CY" or "CYP" => "Cyprus",
            "CZ" or "CZE" => "Czech Republic",
            "DK" or "DNK" => "Denmark",
            "EE" or "EST" => "Estonia",
            "FI" or "FIN" => "Finland",
            "FR" or "FRA" => "France",
            "GE" or "GEO" => "Georgia",
            "DE" or "GER" or "DEU" => "Germany",
            "GR" or "GRE" or "GRC" => "Greece",
            "HU" or "HUN" => "Hungary",
            "IL" or "ISR" => "Israel",
            "IT" or "ITA" => "Italy",
            "LV" or "LVA" => "Latvia",
            "LT" or "LTU" => "Lithuania",
            "ME" or "MNE" => "Montenegro",
            "NL" or "NLD" => "Netherlands",
            "NO" or "NOR" => "Norway",
            "PL" or "POL" => "Poland",
            "PT" or "PRT" => "Portugal",
            "RO" or "ROU" => "Romania",
            "RU" or "RUS" => "Russia",
            "RS" or "SRB" => "Serbia",
            "XK" or "XKX" => "Kosovo",
            "SK" or "SVK" => "Slovakia",
            "SI" or "SVN" => "Slovenia",
            "ES" or "ESP" => "Spain",
            "SCT" => "Scotland",
            "SE" or "SWE" => "Sweden",
            "CH" or "CHE" => "Switzerland",
            "TR" or "TUR" => "Turkey",
            "UA" or "UKR" => "Ukraine",
            _ => countryCode
        };
    }

    private static string NormalizeResolutionAction(string action)
    {
        var normalized = action.Trim().ToLowerInvariant();
        return normalized is "accept_alias" or "merge_duplicate" or "keep_separate" or "edit_metadata" or "ignore" or "resolve"
            ? normalized
            : throw new InvalidOperationException("Unsupported identity finding resolution action.");
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
            left == right;
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

        return findingType switch
        {
            IdentityFindingType.PossibleDuplicate or
                IdentityFindingType.PossibleCrossSourceMatch or
                IdentityFindingType.PossibleCrossSeasonSplit => $"{findingType}|teams={pairKey}",
            IdentityFindingType.AliasObservation => $"{findingType}|team={affectedTeamId:N}|source={sourceKey}",
            IdentityFindingType.SourceTeamSplit => $"{findingType}|source={sourceKey}",
            _ => $"{findingType}|team={pairKey}|source={sourceKey}|related={relatedSourceKey}"
        };
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

    private static IdentityHealthCheckFindingDto ToDto(IdentityHealthCheckFinding finding)
    {
        return new IdentityHealthCheckFindingDto(
            finding.Id,
            finding.RunId,
            finding.FindingType,
            finding.Severity,
            finding.Status,
            finding.Source,
            finding.SourceTeamId,
            finding.AffectedTeamId,
            finding.AffectedTeam?.CanonicalName,
            finding.RelatedSource,
            finding.RelatedSourceTeamId,
            finding.RelatedTeamId,
            finding.RelatedTeam?.CanonicalName,
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
}
