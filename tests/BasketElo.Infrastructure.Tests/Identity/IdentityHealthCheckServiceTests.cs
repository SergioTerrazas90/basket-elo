using BasketElo.Domain.Entities;
using BasketElo.Infrastructure.Identity;
using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Identity;

public sealed class IdentityHealthCheckServiceTests
{
    [Fact]
    public async Task GetFindings_InfersMissingMetadataTeamFromEvidence()
    {
        var options = new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var dbContext = new BasketEloDbContext(options);
        var team = new Team
        {
            Id = Guid.NewGuid(),
            CanonicalName = "UBSC Scholl",
            CountryCode = "UNK"
        };
        var run = new IdentityHealthCheckRun
        {
            Id = Guid.NewGuid(),
            ScopeKey = "test"
        };
        var finding = new IdentityHealthCheckFinding
        {
            Id = Guid.NewGuid(),
            RunId = run.Id,
            FindingType = IdentityFindingType.MissingMetadata,
            Severity = IdentityFindingSeverity.Warning,
            Evidence = "Team 'UBSC Scholl' is missing trusted country metadata.",
            SuggestedAction = "Set the country"
        };

        dbContext.Teams.Add(team);
        dbContext.IdentityHealthCheckRuns.Add(run);
        dbContext.IdentityHealthCheckFindings.Add(finding);
        await dbContext.SaveChangesAsync();

        var service = new IdentityHealthCheckService(dbContext, null!);

        var result = await service.GetFindingsAsync(
            new IdentityFindingQuery { Status = IdentityFindingStatus.Open },
            CancellationToken.None);

        var dto = Assert.Single(result);
        Assert.Equal(team.Id, dto.AffectedTeamId);
        Assert.Equal("UBSC Scholl", dto.AffectedTeamName);
    }

    [Fact]
    public async Task GetFindings_InfersMissingAffectedTeamFromSourceAlias()
    {
        var options = new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var dbContext = new BasketEloDbContext(options);
        var sourceTeam = new Team
        {
            Id = Guid.NewGuid(),
            CanonicalName = "FC Barcelona",
            CountryCode = "ES"
        };
        var targetTeam = new Team
        {
            Id = Guid.NewGuid(),
            CanonicalName = "Winterthur FC Barcelona",
            CountryCode = "ES"
        };
        var run = new IdentityHealthCheckRun
        {
            Id = Guid.NewGuid(),
            ScopeKey = "test"
        };
        var finding = new IdentityHealthCheckFinding
        {
            Id = Guid.NewGuid(),
            RunId = run.Id,
            FindingType = IdentityFindingType.PossibleDuplicate,
            Severity = IdentityFindingSeverity.Blocker,
            Source = "acb-official-tournaments",
            SourceTeamId = "fc-barcelona-2",
            RelatedSource = "acb-official-tournaments",
            RelatedSourceTeamId = "winterthur-fc-barcelona-2",
            Evidence = "test",
            SuggestedAction = "merge or keep separate"
        };
        var alias = new TeamAlias
        {
            Id = Guid.NewGuid(),
            TeamId = sourceTeam.Id,
            Source = "acb-official-tournaments",
            SourceTeamId = "fc-barcelona-2",
            AliasName = "FC Barcelona"
        };
        var relatedAlias = new TeamAlias
        {
            Id = Guid.NewGuid(),
            TeamId = targetTeam.Id,
            Source = "acb-official-tournaments",
            SourceTeamId = "winterthur-fc-barcelona-2",
            AliasName = "Winterthur FC Barcelona"
        };

        dbContext.Teams.AddRange(sourceTeam, targetTeam);
        dbContext.IdentityHealthCheckRuns.Add(run);
        dbContext.IdentityHealthCheckFindings.Add(finding);
        dbContext.TeamAliases.AddRange(alias, relatedAlias);
        await dbContext.SaveChangesAsync();

        var service = new IdentityHealthCheckService(dbContext, null!);

        var result = await service.GetFindingsAsync(
            new IdentityFindingQuery { Status = IdentityFindingStatus.Open },
            CancellationToken.None);

        var dto = Assert.Single(result);
        Assert.Equal(sourceTeam.Id, dto.AffectedTeamId);
        Assert.Equal("FC Barcelona", dto.AffectedTeamName);
        Assert.Equal(targetTeam.Id, dto.RelatedTeamId);
        Assert.Equal("Winterthur FC Barcelona", dto.RelatedTeamName);
    }

    [Fact]
    public async Task EditMetadata_RejectsPossibleCrossSourceMatch()
    {
        var options = new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var dbContext = new BasketEloDbContext(options);
        var affectedTeam = new Team
        {
            Id = Guid.NewGuid(),
            CanonicalName = "Team A",
            CountryCode = "ES"
        };
        var relatedTeam = new Team
        {
            Id = Guid.NewGuid(),
            CanonicalName = "Team B",
            CountryCode = "ES"
        };
        var run = new IdentityHealthCheckRun
        {
            Id = Guid.NewGuid(),
            ScopeKey = "test"
        };
        var finding = new IdentityHealthCheckFinding
        {
            Id = Guid.NewGuid(),
            RunId = run.Id,
            FindingType = IdentityFindingType.PossibleCrossSourceMatch,
            Severity = IdentityFindingSeverity.Blocker,
            Source = "source-a",
            SourceTeamId = "a",
            AffectedTeamId = affectedTeam.Id,
            RelatedSource = "source-b",
            RelatedSourceTeamId = "b",
            RelatedTeamId = relatedTeam.Id,
            Evidence = "test",
            SuggestedAction = "merge or keep separate"
        };

        dbContext.Teams.AddRange(affectedTeam, relatedTeam);
        dbContext.IdentityHealthCheckRuns.Add(run);
        dbContext.IdentityHealthCheckFindings.Add(finding);
        await dbContext.SaveChangesAsync();

        var service = new IdentityHealthCheckService(dbContext, null!);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ResolveFindingAsync(
            finding.Id,
            new ResolveIdentityFindingRequest
            {
                Action = "edit_metadata",
                TargetTeamId = affectedTeam.Id,
                CanonicalName = "Renamed team"
            },
            CancellationToken.None));

        var unchangedTeam = await dbContext.Teams.FindAsync(affectedTeam.Id);
        var openFinding = await dbContext.IdentityHealthCheckFindings.FindAsync(finding.Id);

        Assert.Equal("Team A", unchangedTeam!.CanonicalName);
        Assert.Equal(IdentityFindingStatus.Open, openFinding!.Status);
    }

    [Fact]
    public async Task KeepSeparate_PersistsPairLevelDecision()
    {
        var options = new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var dbContext = new BasketEloDbContext(options);
        var firstTeam = new Team { Id = Guid.NewGuid(), CanonicalName = "Caja de Ronda", CountryCode = "ES" };
        var secondTeam = new Team { Id = Guid.NewGuid(), CanonicalName = "Mayoral Maristas", CountryCode = "ES" };
        var run = new IdentityHealthCheckRun { Id = Guid.NewGuid(), ScopeKey = "test" };
        var finding = new IdentityHealthCheckFinding
        {
            Id = Guid.NewGuid(),
            RunId = run.Id,
            FindingType = IdentityFindingType.PossibleDuplicate,
            Severity = IdentityFindingSeverity.Blocker,
            AffectedTeamId = firstTeam.Id,
            RelatedTeamId = secondTeam.Id,
            Evidence = "test",
            SuggestedAction = "merge or keep separate",
            Status = IdentityFindingStatus.Open
        };

        dbContext.Teams.AddRange(firstTeam, secondTeam);
        dbContext.IdentityHealthCheckRuns.Add(run);
        dbContext.IdentityHealthCheckFindings.Add(finding);
        await dbContext.SaveChangesAsync();

        var service = new IdentityHealthCheckService(dbContext, null!);

        await service.ResolveFindingAsync(
            finding.Id,
            new ResolveIdentityFindingRequest
            {
                Action = "keep_separate",
                ResolvedBy = "test",
                Note = "Confirmed different clubs."
            },
            CancellationToken.None);

        var decision = Assert.Single(await dbContext.IdentityReviewDecisions.ToListAsync());
        Assert.StartsWith("distinct_teams|teams=", decision.DecisionKey);
        Assert.Equal("keep_separate", decision.ResolutionAction);

        var decisions = await service.GetDistinctTeamDecisionsAsync(CancellationToken.None);
        var dto = Assert.Single(decisions);
        Assert.Contains(new[] { dto.LeftTeamName, dto.RightTeamName }, name => name == "Caja de Ronda");
        Assert.Contains(new[] { dto.LeftTeamName, dto.RightTeamName }, name => name == "Mayoral Maristas");
    }

    [Fact]
    public async Task ReviewCandidates_GroupsAllFindingTypesForOnePair()
    {
        var options = new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var dbContext = new BasketEloDbContext(options);
        var left = new Team { Id = Guid.NewGuid(), CanonicalName = "Team Alpha", CountryCode = "ES" };
        var right = new Team { Id = Guid.NewGuid(), CanonicalName = "Team Alpha Sponsor", CountryCode = "ES" };
        var run = new IdentityHealthCheckRun { Id = Guid.NewGuid(), ScopeKey = "country=ES" };
        dbContext.Teams.AddRange(left, right);
        dbContext.IdentityHealthCheckRuns.Add(run);
        dbContext.IdentityHealthCheckFindings.AddRange(
            new IdentityHealthCheckFinding
            {
                Id = Guid.NewGuid(),
                RunId = run.Id,
                FindingType = IdentityFindingType.PossibleDuplicate,
                Severity = IdentityFindingSeverity.Blocker,
                AffectedTeamId = left.Id,
                RelatedTeamId = right.Id,
                Evidence = "same observed name",
                Status = IdentityFindingStatus.Open
            },
            new IdentityHealthCheckFinding
            {
                Id = Guid.NewGuid(),
                RunId = run.Id,
                FindingType = IdentityFindingType.PossibleCrossSeasonSplit,
                Severity = IdentityFindingSeverity.Blocker,
                AffectedTeamId = right.Id,
                RelatedTeamId = left.Id,
                Evidence = "nearby seasons",
                Status = IdentityFindingStatus.Open
            });
        await dbContext.SaveChangesAsync();

        var service = new IdentityHealthCheckService(dbContext, null!);

        var candidates = await service.GetReviewCandidatesAsync(
            new IdentityReviewQuery { RunId = run.Id, Status = "open" },
            CancellationToken.None);

        var candidate = Assert.Single(candidates);
        Assert.Equal(2, candidate.TotalFindingCount);
        Assert.Equal(2, candidate.OpenFindingCount);
        Assert.Contains(IdentityFindingType.PossibleDuplicate, candidate.FindingTypes);
        Assert.Contains(IdentityFindingType.PossibleCrossSeasonSplit, candidate.FindingTypes);
    }

    [Fact]
    public async Task ResolveReviewCandidate_KeepSeparate_DeduplicatesPairDecisionAcrossFindingTypes()
    {
        var options = new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var dbContext = new BasketEloDbContext(options);
        var left = new Team { Id = Guid.NewGuid(), CanonicalName = "Team Alpha", CountryCode = "DE" };
        var right = new Team { Id = Guid.NewGuid(), CanonicalName = "Team Beta", CountryCode = "DE" };
        var run = new IdentityHealthCheckRun { Id = Guid.NewGuid(), ScopeKey = "country=DE" };
        dbContext.Teams.AddRange(left, right);
        dbContext.IdentityHealthCheckRuns.Add(run);
        dbContext.IdentityHealthCheckFindings.AddRange(
            new IdentityHealthCheckFinding
            {
                Id = Guid.NewGuid(),
                RunId = run.Id,
                FindingType = IdentityFindingType.PossibleDuplicate,
                Severity = IdentityFindingSeverity.Blocker,
                AffectedTeamId = left.Id,
                RelatedTeamId = right.Id,
                Evidence = "same observed name",
                SuggestedAction = "merge or keep separate",
                Status = IdentityFindingStatus.Open
            },
            new IdentityHealthCheckFinding
            {
                Id = Guid.NewGuid(),
                RunId = run.Id,
                FindingType = IdentityFindingType.PossibleCrossSeasonSplit,
                Severity = IdentityFindingSeverity.Blocker,
                AffectedTeamId = right.Id,
                RelatedTeamId = left.Id,
                Evidence = "nearby seasons",
                SuggestedAction = "merge or keep separate",
                Status = IdentityFindingStatus.Open
            });
        await dbContext.SaveChangesAsync();

        var service = new IdentityHealthCheckService(dbContext, null!);

        await service.ResolveReviewCandidateAsync(new ResolveIdentityPairRequest
        {
            RunId = run.Id,
            LeftTeamId = left.Id,
            RightTeamId = right.Id,
            Action = "keep_separate",
            ResolvedBy = "test"
        }, CancellationToken.None);

        Assert.Single(await dbContext.IdentityReviewDecisions.ToListAsync());
        var findings = await dbContext.IdentityHealthCheckFindings.ToListAsync();
        Assert.All(findings, finding =>
        {
            Assert.Equal(IdentityFindingStatus.Resolved, finding.Status);
            Assert.Equal("keep_separate", finding.ResolutionAction);
        });
    }

    [Fact]
    public async Task ReviewCandidates_CanFilterByUnknownTeamCountry()
    {
        var options = new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var dbContext = new BasketEloDbContext(options);
        var unknownTeam = new Team { Id = Guid.NewGuid(), CanonicalName = "Unknown Team", CountryCode = "UNK" };
        var knownTeam = new Team { Id = Guid.NewGuid(), CanonicalName = "Known Team", CountryCode = "ES" };
        var run = new IdentityHealthCheckRun { Id = Guid.NewGuid(), ScopeKey = "source=*" };
        dbContext.Teams.AddRange(unknownTeam, knownTeam);
        dbContext.IdentityHealthCheckRuns.Add(run);
        dbContext.IdentityHealthCheckFindings.Add(new IdentityHealthCheckFinding
        {
            Id = Guid.NewGuid(),
            RunId = run.Id,
            FindingType = IdentityFindingType.PossibleDuplicate,
            Severity = IdentityFindingSeverity.Blocker,
            AffectedTeamId = unknownTeam.Id,
            RelatedTeamId = knownTeam.Id,
            Evidence = "same observed name",
            SuggestedAction = "merge or keep separate",
            Status = IdentityFindingStatus.Open
        });
        await dbContext.SaveChangesAsync();

        var service = new IdentityHealthCheckService(dbContext, null!);

        var candidates = await service.GetReviewCandidatesAsync(
            new IdentityReviewQuery { RunId = run.Id, Status = "open", TeamCountryCode = "UNK" },
            CancellationToken.None);

        Assert.Single(candidates);
        Assert.Empty(await service.GetReviewCandidatesAsync(
            new IdentityReviewQuery { RunId = run.Id, Status = "open", TeamCountryCode = "FR" },
            CancellationToken.None));
    }

    [Fact]
    public async Task ResolveReviewCandidate_DeferDoesNotCreatePermanentDecision()
    {
        var options = new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var dbContext = new BasketEloDbContext(options);
        var left = new Team { Id = Guid.NewGuid(), CanonicalName = "Team Alpha", CountryCode = "ES" };
        var right = new Team { Id = Guid.NewGuid(), CanonicalName = "Team Beta", CountryCode = "ES" };
        var run = new IdentityHealthCheckRun { Id = Guid.NewGuid(), ScopeKey = "country=ES" };
        dbContext.Teams.AddRange(left, right);
        dbContext.IdentityHealthCheckRuns.Add(run);
        dbContext.IdentityHealthCheckFindings.Add(new IdentityHealthCheckFinding
        {
            Id = Guid.NewGuid(),
            RunId = run.Id,
            FindingType = IdentityFindingType.PossibleDuplicate,
            Severity = IdentityFindingSeverity.Blocker,
            AffectedTeamId = left.Id,
            RelatedTeamId = right.Id,
            Evidence = "review later",
            Status = IdentityFindingStatus.Open
        });
        await dbContext.SaveChangesAsync();

        var service = new IdentityHealthCheckService(dbContext, null!);

        await service.ResolveReviewCandidateAsync(new ResolveIdentityPairRequest
        {
            RunId = run.Id,
            LeftTeamId = left.Id,
            RightTeamId = right.Id,
            Action = "defer_review",
            Note = "Need historical verification."
        }, CancellationToken.None);

        var finding = await dbContext.IdentityHealthCheckFindings.SingleAsync();
        Assert.Equal(IdentityFindingStatus.Resolved, finding.Status);
        Assert.Equal("defer_review", finding.ResolutionAction);
        Assert.Empty(await dbContext.IdentityReviewDecisions.ToListAsync());

        var deferred = await service.GetReviewCandidatesAsync(
            new IdentityReviewQuery { RunId = run.Id, Status = "deferred" },
            CancellationToken.None);
        Assert.Single(deferred);
    }
}
