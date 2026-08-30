using BasketElo.Api.Elo;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Elo;

public class EloResponseCacheTests
{
    [Fact]
    public void EvolutionKeyIsIndependentOfTeamIdOrder()
    {
        var first = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var second = Guid.Parse("11111111-1111-1111-1111-111111111111");

        var forward = EloResponseCache.EvolutionKey(
            "europe-clubs", "adjusted-v1", [first, second], null, null, null, null, null, 60);
        var reverse = EloResponseCache.EvolutionKey(
            "europe-clubs", "adjusted-v1", [second, first], null, null, null, null, null, 60);

        Assert.Equal(forward, reverse);
    }
}
