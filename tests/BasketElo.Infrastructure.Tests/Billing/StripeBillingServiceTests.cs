using BasketElo.Domain.Entities;
using BasketElo.Infrastructure.Persistence;
using BasketElo.Web.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stripe;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Billing;

public class StripeBillingServiceTests
{
    [Fact]
    public async Task AvailabilityCarriesTheConfiguredPlanPrices()
    {
        await using var dbContext = new BasketEloDbContext(
            new DbContextOptionsBuilder<BasketEloDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
        var service = new StripeBillingService(
            dbContext,
            Options.Create(new StripeBillingOptions
            {
                Enabled = true,
                SecretKey = "sk_test_local",
                PremiumMonthlyPriceId = "price_monthly",
                PremiumAnnualPriceId = "price_annual",
                PremiumMonthlyPriceAmount = 10m,
                PremiumAnnualPriceAmount = 100m,
                PremiumPriceCurrency = "EUR"
            }),
            new FakeSubscriptionGateway(),
            NullLogger<StripeBillingService>.Instance);

        var availability = service.GetAvailability();

        Assert.True(availability.MonthlyEnabled);
        Assert.True(availability.AnnualEnabled);
        Assert.Equal(10m, availability.Pricing.MonthlyAmount);
        Assert.Equal(100m, availability.Pricing.AnnualAmount);
        Assert.Equal(20m, availability.Pricing.AnnualSavings);
        Assert.Equal("EUR", availability.Pricing.Currency);
    }

    [Fact]
    public async Task CancellationAndReactivationStayInsideApplicationService()
    {
        await using var dbContext = new BasketEloDbContext(
            new DbContextOptionsBuilder<BasketEloDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            DisplayName = "Premium user",
            Email = "premium@example.com",
            NormalizedEmail = "PREMIUM@EXAMPLE.COM"
        };
        var subscription = new BillingSubscription
        {
            Id = Guid.NewGuid(),
            User = user,
            UserId = user.Id,
            StripeSubscriptionId = "sub_native_cancel",
            StripeCustomerId = "cus_native_cancel",
            StripePriceId = "price_monthly",
            IsPremium = true,
            Status = BillingSubscriptionStatuses.Active
        };
        dbContext.AddRange(user, subscription);
        await dbContext.SaveChangesAsync();
        var gateway = new FakeSubscriptionGateway();
        var service = new StripeBillingService(
            dbContext,
            Options.Create(new StripeBillingOptions
            {
                Enabled = true,
                SecretKey = "sk_test_local",
                PremiumMonthlyPriceId = "price_monthly"
            }),
            gateway,
            NullLogger<StripeBillingService>.Instance);

        var canceled = await service.SetCancelAtPeriodEndAsync(user.Id, true, CancellationToken.None);
        var resumed = await service.SetCancelAtPeriodEndAsync(user.Id, false, CancellationToken.None);

        Assert.True(canceled.CancelAtPeriodEnd);
        Assert.False(resumed.CancelAtPeriodEnd);
        Assert.Equal([true, false], gateway.CancellationValues);
        var stored = await dbContext.BillingSubscriptions.SingleAsync();
        Assert.False(stored.CancelAtPeriodEnd);
        Assert.Equal(FakeSubscriptionGateway.PeriodEndUtc, stored.CurrentPeriodEndUtc);
        Assert.Equal(FakeSubscriptionGateway.StartedAtUtc, stored.PremiumStartedAtUtc);
    }

    private sealed class FakeSubscriptionGateway : IStripeSubscriptionGateway
    {
        public static readonly DateTime StartedAtUtc = new(2026, 8, 17, 10, 0, 0, DateTimeKind.Utc);
        public static readonly DateTime PeriodEndUtc = new(2026, 9, 17, 10, 0, 0, DateTimeKind.Utc);

        public List<bool> CancellationValues { get; } = [];

        public Task<Subscription> GetAsync(string subscriptionId, CancellationToken cancellationToken)
            => Task.FromResult(CreateSubscription(subscriptionId, false));

        public Task<Subscription> SetCancelAtPeriodEndAsync(
            string subscriptionId,
            bool cancelAtPeriodEnd,
            CancellationToken cancellationToken)
        {
            CancellationValues.Add(cancelAtPeriodEnd);
            return Task.FromResult(CreateSubscription(subscriptionId, cancelAtPeriodEnd));
        }

        private static Subscription CreateSubscription(string subscriptionId, bool cancelAtPeriodEnd)
            => new()
            {
                Id = subscriptionId,
                CustomerId = "cus_native_cancel",
                Status = BillingSubscriptionStatuses.Active,
                CancelAtPeriodEnd = cancelAtPeriodEnd,
                StartDate = StartedAtUtc,
                Items = new StripeList<SubscriptionItem>
                {
                    Data =
                    [
                        new SubscriptionItem
                        {
                            Price = new Price { Id = "price_monthly" },
                            CurrentPeriodStart = StartedAtUtc,
                            CurrentPeriodEnd = PeriodEndUtc
                        }
                    ]
                }
            };
    }
}
