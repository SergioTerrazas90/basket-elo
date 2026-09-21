using System.Security.Cryptography;
using System.Text;

namespace BasketElo.Domain.Teams;

public static class TeamAliasCollisionReview
{
    public const string FindingType = "alias_collision";
    public const string AcceptedAction = "accept_alias_group";

    public static string NormalizeAlias(string value) =>
        value.Trim().ToUpperInvariant();

    public static string NormalizeCountry(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "UNK" : value.Trim().ToUpperInvariant();

    public static string CreateDecisionKey(
        string aliasName,
        string? countryCode,
        IEnumerable<Guid> teamIds)
    {
        var teamKey = string.Join(",", teamIds
            .Distinct()
            .OrderBy(x => x.ToString("N"), StringComparer.Ordinal)
            .Select(x => x.ToString("N")));
        var signature = $"{NormalizeCountry(countryCode)}|{NormalizeAlias(aliasName)}|{teamKey}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(signature));
        return $"alias_collision|{Convert.ToHexString(hash).ToLowerInvariant()}";
    }
}
