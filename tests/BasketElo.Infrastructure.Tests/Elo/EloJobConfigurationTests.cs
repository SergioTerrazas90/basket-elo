using BasketElo.Infrastructure.Jobs;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Elo;

public class EloJobConfigurationTests
{
    [Fact]
    public void SystemQueueHasPriorityAndWorkerConcurrencyIsCapped()
    {
        Assert.Equal([EloJobQueues.SystemElo, EloJobQueues.ModelLab], EloJobQueues.InPriorityOrder);
        Assert.True(string.CompareOrdinal(EloJobQueues.SystemElo, EloJobQueues.ModelLab) < 0);
        Assert.Equal(1, new EloJobOptions { WorkerCount = 0 }.EffectiveWorkerCount);
        Assert.Equal(3, new EloJobOptions { WorkerCount = 4 }.EffectiveWorkerCount);
        Assert.Equal(3, new EloJobOptions().EffectiveWorkerCount);
    }
}
