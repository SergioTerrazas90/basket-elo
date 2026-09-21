using System.Globalization;
using System.Text.RegularExpressions;
using BasketElo.Api.Auth;
using BasketElo.Domain.Entities;
using BasketElo.Domain.Games;
using BasketElo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BasketElo.Api.Controllers;

[ApiController]
[Route("api/admin/games")]
[RequireInternalAdmin]
public sealed class AdminGamesController(BasketEloDbContext dbContext) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ManualGameCreatedResponse>> CreateManualGame(
        [FromBody] CreateManualGameRequest request,
        CancellationToken cancellationToken)
    {
        var seasonLabel = request.SeasonLabel?.Trim();
        if (string.IsNullOrWhiteSpace(seasonLabel))
        {
            return BadRequest("Season is required.");
        }

        if (seasonLabel.Length > 20)
        {
            return BadRequest("Season cannot exceed 20 characters.");
        }

        var status = request.Status?.Trim().ToLowerInvariant();
        if (!ManualGameStatuses.IsValid(status))
        {
            return BadRequest("Status must be scheduled, finished, postponed, or cancelled.");
        }
        var normalizedStatus = status!;

        if (request.HomeTeamId == request.AwayTeamId)
        {
            return BadRequest("Home and away teams must be different.");
        }

        if (request.HomeScore.HasValue != request.AwayScore.HasValue)
        {
            return BadRequest("Both scores are required together.");
        }

        if (request.HomeScore is < 0 || request.AwayScore is < 0)
        {
            return BadRequest("Scores cannot be negative.");
        }

        if (normalizedStatus == ManualGameStatuses.Finished && (!request.HomeScore.HasValue || !request.AwayScore.HasValue))
        {
            return BadRequest("A finished game requires both scores.");
        }

        if (normalizedStatus == ManualGameStatuses.Finished && request.HomeScore == request.AwayScore)
        {
            return BadRequest("A finished basketball game cannot end in a tie.");
        }

        if (normalizedStatus != ManualGameStatuses.Finished && (request.HomeScore.HasValue || request.AwayScore.HasValue))
        {
            return BadRequest("Scores can only be entered for a finished game.");
        }

        if (request.CompetitionPhase?.Trim().Length > 100 || request.CompetitionRound?.Trim().Length > 100)
        {
            return BadRequest("Phase and round cannot exceed 100 characters.");
        }

        DateTime gameDateTimeUtc;
        try
        {
            gameDateTimeUtc = ConvertToUtc(request.LocalDateTime, request.TimeZoneId);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(exception.Message);
        }

        var competition = await dbContext.Competitions
            .SingleOrDefaultAsync(x => x.Id == request.CompetitionId, cancellationToken);
        if (competition is null)
        {
            return BadRequest("Competition was not found.");
        }

        var teams = await dbContext.Teams
            .Where(x => x.Id == request.HomeTeamId || x.Id == request.AwayTeamId)
            .ToListAsync(cancellationToken);
        var homeTeam = teams.SingleOrDefault(x => x.Id == request.HomeTeamId);
        var awayTeam = teams.SingleOrDefault(x => x.Id == request.AwayTeamId);
        if (homeTeam is null || awayTeam is null)
        {
            return BadRequest("One or both teams were not found.");
        }

        var duplicateExists = await dbContext.Games.AnyAsync(
            x => x.CompetitionId == competition.Id &&
                 x.GameDateTimeUtc == gameDateTimeUtc &&
                 ((x.HomeTeamId == homeTeam.Id && x.AwayTeamId == awayTeam.Id) ||
                  (x.HomeTeamId == awayTeam.Id && x.AwayTeamId == homeTeam.Id)),
            cancellationToken);
        if (duplicateExists)
        {
            return Conflict("A game between these teams already exists at this date and time.");
        }

        var season = await dbContext.Seasons.SingleOrDefaultAsync(
            x => x.CompetitionId == competition.Id && x.Label == seasonLabel,
            cancellationToken);
        if (season is null)
        {
            var bounds = InferSeasonBounds(seasonLabel, gameDateTimeUtc);
            season = new Season
            {
                Id = Guid.NewGuid(),
                CompetitionId = competition.Id,
                Label = seasonLabel,
                StartDateUtc = bounds.StartUtc,
                EndDateUtc = bounds.EndUtc,
                CreatedAtUtc = DateTime.UtcNow
            };
            dbContext.Seasons.Add(season);
        }

        var now = DateTime.UtcNow;
        var eloEligible = normalizedStatus == ManualGameStatuses.Finished;
        var game = new Game
        {
            Id = Guid.NewGuid(),
            Source = "manual",
            SourceGameId = $"manual-{Guid.NewGuid():N}",
            SourceSeasonKey = seasonLabel,
            SourceFetchedAtUtc = now,
            ParserVersion = "manual-admin-v1",
            CompetitionId = competition.Id,
            SeasonId = season.Id,
            GameDateTimeUtc = gameDateTimeUtc,
            HomeTeamId = homeTeam.Id,
            AwayTeamId = awayTeam.Id,
            HomeScore = request.HomeScore,
            AwayScore = request.AwayScore,
            Status = normalizedStatus,
            CompetitionPhase = NullIfWhiteSpace(request.CompetitionPhase),
            CompetitionRound = NullIfWhiteSpace(request.CompetitionRound),
            IsNeutralSite = request.IsNeutralSite,
            EloEligible = eloEligible,
            EloExclusionReason = eloEligible || normalizedStatus == ManualGameStatuses.Scheduled ? null : "manual_result_not_eligible",
            HasManualResultOverride = normalizedStatus != ManualGameStatuses.Scheduled,
            IngestedAtUtc = now,
            UpdatedAtUtc = now
        };
        dbContext.Games.Add(game);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Created(
            $"/api/games?search={game.Id}",
            new ManualGameCreatedResponse(
                game.Id,
                competition.Name,
                season.Label,
                game.GameDateTimeUtc,
                homeTeam.CanonicalName,
                awayTeam.CanonicalName,
                game.HomeScore,
                game.AwayScore,
                game.Status,
                game.EloEligible));
    }

    private static DateTime ConvertToUtc(DateTime localDateTime, string? timeZoneId)
    {
        if (localDateTime == default)
        {
            throw new ArgumentException("Game date and time are required.");
        }

        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            throw new ArgumentException("Browser time zone is required.");
        }

        TimeZoneInfo timeZone;
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
        }
        catch (TimeZoneNotFoundException)
        {
            throw new ArgumentException($"Time zone '{timeZoneId}' is not supported.");
        }
        catch (InvalidTimeZoneException)
        {
            throw new ArgumentException($"Time zone '{timeZoneId}' is not valid.");
        }

        var unspecified = DateTime.SpecifyKind(localDateTime, DateTimeKind.Unspecified);
        if (timeZone.IsInvalidTime(unspecified))
        {
            throw new ArgumentException("That local time does not exist because of a daylight-saving clock change.");
        }

        return TimeZoneInfo.ConvertTimeToUtc(unspecified, timeZone);
    }

    private static (DateTime StartUtc, DateTime EndUtc) InferSeasonBounds(string label, DateTime gameUtc)
    {
        var match = Regex.Match(label, @"(?<!\d)(?<start>\d{4})(?:\s*[-/]\s*(?<end>\d{2}|\d{4}))?(?!\d)");
        if (!match.Success || !int.TryParse(match.Groups["start"].Value, CultureInfo.InvariantCulture, out var startYear))
        {
            return CalendarYearBounds(gameUtc.Year);
        }

        if (!match.Groups["end"].Success)
        {
            return CalendarYearBounds(startYear);
        }

        var endText = match.Groups["end"].Value;
        var endYear = endText.Length == 2
            ? (startYear / 100 * 100) + int.Parse(endText, CultureInfo.InvariantCulture)
            : int.Parse(endText, CultureInfo.InvariantCulture);
        if (endYear < startYear)
        {
            endYear += 100;
        }

        return (
            new DateTime(startYear, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(endYear, 6, 30, 23, 59, 59, DateTimeKind.Utc));
    }

    private static (DateTime StartUtc, DateTime EndUtc) CalendarYearBounds(int year) =>
        (new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc),
         new DateTime(year, 12, 31, 23, 59, 59, DateTimeKind.Utc));

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
