using BasketElo.Domain.Elo;
using BasketElo.Domain.Entities;
using BasketElo.Infrastructure.Elo;
using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Elo;

public class ModelLabEntitlementServiceTests
{
    [Fact]
    public async Task AdminUsersReceivePaidEntitlementWithoutBeingListedAsPaid()
    {
        await using var dbContext = CreateDbContext();
        var adminRole = new ApplicationRole
        {
            Id = Guid.NewGuid(),
            Key = ApplicationRoleKeys.Admin,
            Name = "Admin"
        };
        var admin = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            DisplayName = "Admin",
            Email = "admin@example.com",
            NormalizedEmail = "ADMIN@EXAMPLE.COM"
        };
        dbContext.AddRange(
            adminRole,
            admin,
            new ApplicationUserRole
            {
                UserId = admin.Id,
                RoleId = adminRole.Id,
                User = admin,
                Role = adminRole
            });
        await dbContext.SaveChangesAsync();

        var entitlement = await CreateService(dbContext).GetAsync(admin.Id, CancellationToken.None);

        Assert.Equal("paid", entitlement.PlanKey);
        Assert.True(entitlement.CanSaveModels);
        Assert.True(entitlement.IsPaid);
        Assert.Null(entitlement.SavedModelLimit);
        Assert.Equal(100, entitlement.StoredRunLimit);
        Assert.Equal(200, entitlement.MonthlyRunLimit);
        Assert.Null(entitlement.RequiredLeagueName);
        Assert.Null(entitlement.MinimumSeasonStartYear);
    }

    [Fact]
    public async Task NonAdminUsersStillUseConfiguredPaidEmailList()
    {
        await using var dbContext = CreateDbContext();
        var paidUser = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            DisplayName = "Paid user",
            Email = "paid@example.com",
            NormalizedEmail = "PAID@EXAMPLE.COM"
        };
        dbContext.Add(paidUser);
        await dbContext.SaveChangesAsync();

        var entitlement = await CreateService(dbContext, "paid@example.com").GetAsync(paidUser.Id, CancellationToken.None);

        Assert.True(entitlement.IsPaid);
    }

    [Fact]
    public async Task ActivePremiumStripeSubscriptionGrantsPaidEntitlement()
    {
        await using var dbContext = CreateDbContext();
        var user = CreateUser("subscriber@example.com");
        var now = DateTime.UtcNow;
        var twoMonthsAgo = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-2);
        var anniversaryDay = Math.Max(1, now.Day - 1);
        var premiumStartedAtUtc = twoMonthsAgo.AddDays(anniversaryDay - 1);
        dbContext.AddRange(
            user,
            new BillingSubscription
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                User = user,
                StripeSubscriptionId = "sub_premium",
                StripeCustomerId = "cus_premium",
                StripePriceId = "price_premium",
                IsPremium = true,
                Status = BillingSubscriptionStatuses.Active,
                PremiumStartedAtUtc = premiumStartedAtUtc
            });
        await dbContext.SaveChangesAsync();

        var entitlement = await CreateService(dbContext).GetAsync(user.Id, CancellationToken.None);

        Assert.True(entitlement.IsPaid);
        Assert.Equal(premiumStartedAtUtc.AddMonths(2), entitlement.MonthlyRunWindowStartUtc);
        Assert.Equal(premiumStartedAtUtc.AddMonths(3), entitlement.MonthlyRunWindowEndUtc);
    }

    [Theory]
    [InlineData(BillingSubscriptionStatuses.Canceled, true)]
    [InlineData(BillingSubscriptionStatuses.Unpaid, true)]
    [InlineData(BillingSubscriptionStatuses.Active, false)]
    public async Task IneligibleStripeSubscriptionDoesNotGrantPaidEntitlement(string status, bool isPremium)
    {
        await using var dbContext = CreateDbContext();
        var user = CreateUser("free@example.com");
        dbContext.AddRange(
            user,
            new BillingSubscription
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                User = user,
                StripeSubscriptionId = $"sub_{status}_{isPremium}",
                StripeCustomerId = $"cus_{status}_{isPremium}",
                IsPremium = isPremium,
                Status = status
            });
        await dbContext.SaveChangesAsync();

        var entitlement = await CreateService(dbContext).GetAsync(user.Id, CancellationToken.None);

        Assert.False(entitlement.IsPaid);
        Assert.Null(entitlement.RequiredLeagueName);
        Assert.Equal(2020, entitlement.MinimumSeasonStartYear);
    }

    [Fact]
    public void AnonymousAccessRetainsPreviewCompetitionAndStartsAt2020()
    {
        using var dbContext = CreateDbContext();

        var entitlement = CreateService(dbContext).GetAnonymous();

        Assert.Equal("ACB", entitlement.RequiredLeagueName);
        Assert.Equal(2020, entitlement.MinimumSeasonStartYear);
    }

    [Fact]
    public void FreeOptionsBeginAt2020WhilePaidOptionsKeepFullHistory()
    {
        var oldSeason = new ModelLabSeasonOption(
            "2019-2020",
            new DateTime(2019, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2020, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        var allowedSeason = new ModelLabSeasonOption(
            "2020-2021",
            new DateTime(2020, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2021, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        var options = new ModelLabOptionsResponse(
            new ModelLabParameterSet(1500m, 20, 70m, 400m, true, 28m, 1m),
            [],
            [],
            [],
            [oldSeason, allowedSeason],
            oldSeason.FirstGameUtc,
            allowedSeason.LastGameUtc);
        var free = new ModelLabEntitlement("free", true, false, 1, 3, null, null, 2020);
        var paid = new ModelLabEntitlement("paid", true, true, null, 100, 200, null);

        var freeOptions = ModelLabAccessPolicy.FilterOptions(free, options);
        var paidOptions = ModelLabAccessPolicy.FilterOptions(paid, options);

        Assert.Equal("2020-2021", Assert.Single(freeOptions.Seasons).Label);
        Assert.Equal(allowedSeason.FirstGameUtc, freeOptions.FirstGameUtc);
        Assert.Collection(paidOptions.Seasons, _ => { }, _ => { });
        Assert.Equal(oldSeason.FirstGameUtc, paidOptions.FirstGameUtc);
    }

    private static ApplicationUser CreateUser(string email)
        => new()
        {
            Id = Guid.NewGuid(),
            DisplayName = "Subscriber",
            Email = email,
            NormalizedEmail = email.ToUpperInvariant()
        };

    private static BasketEloDbContext CreateDbContext()
        => new(new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static ModelLabEntitlementService CreateService(
        BasketEloDbContext dbContext,
        string paidEmails = "")
        => new(
            dbContext,
            Options.Create(new ModelLabPlanOptions
            {
                PaidEmails = paidEmails,
                PaidStoredRunLimit = 100,
                PaidMonthlyRunLimit = 200,
                FreeSavedModelLimit = 1,
                FreeStoredRunLimit = 3,
                FreeMinimumSeasonStartYear = 2020,
                AnonymousLeagueName = "ACB"
            }));
}
