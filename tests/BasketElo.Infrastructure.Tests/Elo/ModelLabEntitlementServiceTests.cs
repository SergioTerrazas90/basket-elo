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
                Status = BillingSubscriptionStatuses.Active
            });
        await dbContext.SaveChangesAsync();

        var entitlement = await CreateService(dbContext).GetAsync(user.Id, CancellationToken.None);

        Assert.True(entitlement.IsPaid);
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
                FreeLeagueName = "ACB"
            }));
}
