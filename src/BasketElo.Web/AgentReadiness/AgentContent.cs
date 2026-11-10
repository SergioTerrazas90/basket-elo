using System.Globalization;

namespace BasketElo.Web.AgentReadiness;

public static class AgentContent
{
    public const string HomepageOverviewEnglish =
        "BasketElo is an independent basketball rating service that turns completed game results into continuously updated ELO ratings. " +
        "The homepage compares NBA franchises, European clubs, and men's national teams in separate rating pools so that every position reflects games played within a meaningful competitive context. " +
        "Each ranking exposes the team's current rating, games played, peak rating, recent form, and links to deeper rating history. " +
        "Ratings begin from a common baseline and move after each rated result according to opponent strength, venue, score margin, and competition context. " +
        "The published method explains the formula, model versions, data coverage, and known limitations. Visitors can switch between current rankings, rating graphs, upcoming fixtures, recent results, historical seasons, and country-specific European club tables. " +
        "BasketElo is not an official NBA, EuroLeague, ACB, or FIBA ranking; it is a transparent analytical view intended for comparison and research.";

    public const string HomepageOverviewSpanish =
        "BasketElo es un servicio independiente de valoración de baloncesto que convierte los resultados finalizados en puntuaciones ELO actualizadas continuamente. " +
        "La página principal compara franquicias de la NBA, clubes europeos y selecciones masculinas en grupos separados para que cada posición refleje partidos de un contexto competitivo relevante. " +
        "Cada clasificación muestra la puntuación actual, los partidos disputados, el máximo histórico, la forma reciente y enlaces al historial de cada equipo. " +
        "Las puntuaciones parten de una base común y cambian después de cada resultado según la fuerza del rival, la sede, el margen y el contexto de la competición. " +
        "El método publicado explica la fórmula, las versiones del modelo, la cobertura de datos y sus limitaciones conocidas. También se pueden consultar gráficos, próximos partidos, resultados recientes, temporadas históricas y tablas de clubes europeos por país. " +
        "BasketElo no es una clasificación oficial de la NBA, la Euroliga, la ACB ni la FIBA, sino una herramienta analítica transparente para comparar e investigar.";

    public const string MarkdownHomepage = """
        # Basketball ELO rankings

        BasketElo is an independent basketball rating service for NBA franchises, European clubs, and men's national teams. It turns completed game results into continuously updated ELO ratings while keeping those three competitive groups in separate pools.

        ## What the rankings show

        Each ranking includes a team's current ELO rating, games played, peak rating, recent form, and links to its rating history. Ratings begin from a common baseline and change after every rated result according to opponent strength, venue, score margin, and competition context. The rankings are an analytical view, not an official NBA, EuroLeague, ACB, or FIBA table.

        ## Explore BasketElo

        - [Overall rankings](/)
        - [NBA ELO rankings](/nba-elo)
        - [ACB ELO rankings](/acb-elo)
        - [EuroLeague ELO rankings](/euroleague-elo)
        - [European basketball club rankings](/european-basketball-rankings)
        - [Men's national-team rankings](/basketball-national-team-rankings)
        - [Rating method and assumptions](/how-it-works)
        - [Data sources and coverage](/data-sources)

        The HTML version of the site also provides interactive filters, rating graphs, fixtures, results, historical season pages, and country-specific European club tables. The methodology and source notes document model versions, coverage, and known limitations so that every rating can be interpreted in context.
        """;

    public static string HomepageOverview(CultureInfo culture) =>
        string.Equals(culture.TwoLetterISOLanguageName, "es", StringComparison.OrdinalIgnoreCase)
            ? HomepageOverviewSpanish
            : HomepageOverviewEnglish;

    public static string Robots(string siteRoot) => $$"""
        User-agent: ChatGPT-User
        Allow: /

        User-agent: OAI-SearchBot
        Allow: /

        User-agent: Claude-User
        Allow: /

        User-agent: PerplexityBot
        Allow: /

        User-agent: *
        Allow: /

        Sitemap: {{siteRoot}}sitemap.xml
        """;
}
