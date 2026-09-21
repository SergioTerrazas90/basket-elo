using BasketElo.Domain.CurrentResults;

namespace BasketElo.Infrastructure.CurrentResults;

public interface ICurrentResultsIngestionService
{
    Task<CurrentResultsRunSummary> RunAsync(
        DateOnly fromDate,
        DateOnly toDate,
        bool dryRun,
        CancellationToken cancellationToken);

    Task<CurrentResultReviewResolutionDto> ResolveReviewAsync(
        Guid reviewId,
        CurrentResultReviewResolutionRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CurrentResultReviewTeamCandidateDto>> GetReviewTeamCandidatesAsync(
        Guid reviewId,
        string side,
        string? search,
        CancellationToken cancellationToken);

    Task<CurrentResultReviewTeamMappingDto> MapReviewTeamAsync(
        Guid reviewId,
        CurrentResultReviewTeamMappingRequest request,
        CancellationToken cancellationToken);

    Task<CurrentResultReviewTeamMappingDto> CreateReviewTeamAsync(
        Guid reviewId,
        CurrentResultReviewTeamCreateRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CurrentResultTeamMappingHistoryDto>> GetRecentTeamMappingsAsync(
        int days,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CurrentResultsUnmatchedCompetitionDto>> GetUnmatchedCompetitionsAsync(
        CancellationToken cancellationToken);

    Task<int> MergeUnmatchedCompetitionAsync(
        MergeUnmatchedCompetitionRequest request,
        CancellationToken cancellationToken);

    Task<int> ReprocessMergedCompetitionReviewsAsync(CancellationToken cancellationToken);

    Task<int> IgnoreUnmatchedCompetitionAsync(
        IgnoreUnmatchedCompetitionRequest request,
        CancellationToken cancellationToken);
}
