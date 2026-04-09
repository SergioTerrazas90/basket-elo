using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BasketElo.Infrastructure.Persistence;

public class BasketEloDbContextFactory : IDesignTimeDbContextFactory<BasketEloDbContext>
{
    public BasketEloDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<BasketEloDbContext>();
        optionsBuilder.UseNpgsql(
            "Host=localhost;Port=5432;Database=basket_elo;Username=basket_elo;Password=basket_elo");

        return new BasketEloDbContext(optionsBuilder.Options);
    }
}
