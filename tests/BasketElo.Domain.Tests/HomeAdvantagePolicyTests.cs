using BasketElo.Domain.Elo;
using Xunit;

namespace BasketElo.Domain.Tests;

public class HomeAdvantagePolicyTests
{
    [Theory]
    [InlineData("Final Four", null)]
    [InlineData(null, "Final 8 tournament")]
    [InlineData("Final Four", "Championship game")]
    [InlineData("Top Four", "Championship game")]
    [InlineData("Final Day", "Championship game")]
    public void AutomaticPolicyTreatsHostedFinalTournamentsAsNeutral(string? phase, string? round)
    {
        var ruleset = EloCalculator.GetRulesetParameters(EloRulesetVersions.AdjustedV1);

        var applied = HomeAdvantagePolicy.Apply(
            ruleset,
            null,
            HomeAdvantagePolicies.Automatic,
            "Euroleague",
            "international",
            phase,
            round);

        Assert.Equal(0m, applied.HomeAdvantageElo);
    }

    [Fact]
    public void CompetitionPolicyCanMakeAnEntireCompetitionNeutral()
    {
        var ruleset = EloCalculator.GetRulesetParameters(EloRulesetVersions.AdjustedV1);

        var applied = HomeAdvantagePolicy.Apply(
            ruleset,
            null,
            HomeAdvantagePolicies.Neutral,
            "League Cup",
            "domestic_cup",
            "Quarterfinal",
            "Game 1");

        Assert.Equal(0m, applied.HomeAdvantageElo);
    }

    [Fact]
    public void ExplicitGameOverrideWinsOverCompetitionAndAutomaticInference()
    {
        var ruleset = EloCalculator.GetRulesetParameters(EloRulesetVersions.AdjustedV1);

        var forcedHome = HomeAdvantagePolicy.Apply(
            ruleset,
            false,
            HomeAdvantagePolicies.Neutral,
            "Euroleague",
            "international",
            "Final Four",
            null);
        var forcedNeutral = HomeAdvantagePolicy.Apply(
            ruleset,
            true,
            HomeAdvantagePolicies.Home,
            "ACB",
            "league",
            "Regular Season",
            "Round 1");

        Assert.Equal(ruleset.HomeAdvantageElo, forcedHome.HomeAdvantageElo);
        Assert.Equal(0m, forcedNeutral.HomeAdvantageElo);
    }

    [Fact]
    public void AutomaticPolicyDoesNotTurnAnOrdinaryFinalIntoANeutralGame()
    {
        var ruleset = EloCalculator.GetRulesetParameters(EloRulesetVersions.AdjustedV1);

        var applied = HomeAdvantagePolicy.Apply(
            ruleset,
            null,
            HomeAdvantagePolicies.Automatic,
            "Eurocup",
            "international",
            "Playoffs",
            "Final");

        Assert.Equal(ruleset.HomeAdvantageElo, applied.HomeAdvantageElo);
    }

    [Theory]
    [InlineData("EuroBasket")]
    [InlineData("FIBA EuroBasket Division B")]
    [InlineData("FIBA EuroBasket")]
    [InlineData("AfroBasket")]
    [InlineData("FIBA Basketball World Cup")]
    [InlineData("FIBA World Cup")]
    [InlineData("FIBA AmeriCup")]
    [InlineData("Asian Games")]
    [InlineData("Summer Olympics")]
    public void AutomaticPolicyTreatsFibaFinalTournamentsAsNeutral(string competitionName)
    {
        var ruleset = EloCalculator.GetRulesetParameters(EloRulesetVersions.AdjustedV1);

        var applied = HomeAdvantagePolicy.Apply(
            ruleset,
            null,
            HomeAdvantagePolicies.Automatic,
            competitionName,
            "national_team",
            "Group Phase",
            "Group A");

        Assert.Equal(0m, applied.HomeAdvantageElo);
    }

    [Fact]
    public void AutomaticPolicyKeepsFibaQualifiersAsHomeAndAway()
    {
        var ruleset = EloCalculator.GetRulesetParameters(EloRulesetVersions.AdjustedV1);

        var applied = HomeAdvantagePolicy.Apply(
            ruleset,
            null,
            HomeAdvantagePolicies.Automatic,
            "FIBA EuroBasket Qualifiers",
            "national_team",
            "Group Phase",
            "Group A");

        Assert.Equal(ruleset.HomeAdvantageElo, applied.HomeAdvantageElo);
    }
}
