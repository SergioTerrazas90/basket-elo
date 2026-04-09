using BasketElo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BasketElo.Api.Controllers;

[ApiController]
[Route("api/system")]
public class SystemController(BasketEloDbContext dbContext) : ControllerBase
{
    [HttpGet("db-status")]
    public async Task<IActionResult> GetDbStatus(CancellationToken cancellationToken)
    {
        var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);
        var pendingMigrations = canConnect
            ? await dbContext.Database.GetPendingMigrationsAsync(cancellationToken)
            : Array.Empty<string>();

        return Ok(new
        {
            canConnect,
            pendingMigrationsCount = pendingMigrations.Count(),
            serverUtc = DateTime.UtcNow
        });
    }
}
