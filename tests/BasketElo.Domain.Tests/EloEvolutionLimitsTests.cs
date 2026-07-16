using BasketElo.Domain.Elo;
using Xunit;

namespace BasketElo.Domain.Tests;

public class EloEvolutionLimitsTests
{
    [Theory]
    [InlineData(0, EloEvolutionLimits.DefaultPointsPerTeam)]
    [InlineData(-1, EloEvolutionLimits.DefaultPointsPerTeam)]
    [InlineData(1, EloEvolutionLimits.MinimumPointsPerTeam)]
    [InlineData(60, 60)]
    [InlineData(121, EloEvolutionLimits.MaximumPointsPerTeam)]
    public void NormalizePointsPerTeamAlwaysReturnsABoundedLimit(int requested, int expected)
    {
        Assert.Equal(expected, EloEvolutionLimits.NormalizePointsPerTeam(requested));
    }

    [Fact]
    public void EvenlySamplePreservesTheFullSpanAndRequestedSize()
    {
        var source = Enumerable.Range(0, 1_000).ToList();

        var sampled = EloEvolutionLimits.EvenlySample(source, 20);

        Assert.Equal(20, sampled.Count);
        Assert.Equal(source[0], sampled[0]);
        Assert.Equal(source[^1], sampled[^1]);
        Assert.Equal(sampled.Count, sampled.Distinct().Count());
    }
}
