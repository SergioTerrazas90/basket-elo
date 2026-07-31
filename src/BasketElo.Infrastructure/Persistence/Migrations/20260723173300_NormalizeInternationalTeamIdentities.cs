using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeInternationalTeamIdentities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                WITH national_team_ids AS (
                    SELECT DISTINCT g."HomeTeamId" AS "TeamId"
                    FROM games g
                    INNER JOIN competitions c ON c."Id" = g."CompetitionId"
                    WHERE c."EloPoolKey" = 'national-teams'
                    UNION
                    SELECT DISTINCT g."AwayTeamId" AS "TeamId"
                    FROM games g
                    INNER JOIN competitions c ON c."Id" = g."CompetitionId"
                    WHERE c."EloPoolKey" = 'national-teams'
                ), identities("Code", "Name") AS (
                    VALUES
                        ('ALB', 'Albania'), ('ALG', 'Algeria'), ('ANG', 'Angola'), ('ARG', 'Argentina'),
                        ('ASA', 'American Samoa'), ('AUS', 'Australia'), ('AUT', 'Austria'), ('BAH', 'Bahamas'),
                        ('BAN', 'Bangladesh'), ('BDI', 'Burundi'), ('BEL', 'Belgium'), ('BEN', 'Benin'),
                        ('BIH', 'Bosnia and Herzegovina'), ('BLR', 'Belarus'), ('BOT', 'Botswana'), ('BRA', 'Brazil'),
                        ('BRN', 'Bahrain'), ('BUL', 'Bulgaria'), ('BUR', 'Burkina Faso'), ('CAF', 'Central African Republic'),
                        ('CAL', 'New Caledonia'), ('CAN', 'Canada'), ('CGO', 'Congo'), ('CHA', 'Chad'),
                        ('CHI', 'Chile'), ('CHN', 'China'), ('CIS', 'Commonwealth of Independent States'),
                        ('CIV', 'Côte d''Ivoire'), ('CMR', 'Cameroon'), ('COD', 'Democratic Republic of the Congo'),
                        ('COL', 'Colombia'), ('CON', 'Congo'), ('CPV', 'Cabo Verde'), ('CRO', 'Croatia'),
                        ('CUB', 'Cuba'), ('CYP', 'Cyprus'), ('CZE', 'Czech Republic'), ('DDR', 'East Germany'),
                        ('DEN', 'Denmark'), ('DOM', 'Dominican Republic'), ('ECU', 'Ecuador'), ('EGY', 'Egypt'),
                        ('ENG', 'England'), ('ESP', 'Spain'), ('EST', 'Estonia'), ('ETH', 'Ethiopia'),
                        ('FIN', 'Finland'), ('FOR', 'Faroe Islands'), ('FPO', 'French Polynesia'), ('FRA', 'France'),
                        ('GAB', 'Gabon'), ('GAM', 'Gambia'), ('GBR', 'Great Britain'), ('GBS', 'Guinea-Bissau'),
                        ('GEO', 'Georgia'), ('GEQ', 'Equatorial Guinea'), ('GER', 'Germany'), ('GRE', 'Greece'),
                        ('GUI', 'Guinea'), ('GUM', 'Guam'), ('HKG', 'Hong Kong'), ('HUN', 'Hungary'),
                        ('INA', 'Indonesia'), ('IND', 'India'), ('IRI', 'Iran'), ('IRL', 'Ireland'),
                        ('IRQ', 'Iraq'), ('ISL', 'Iceland'), ('ISR', 'Israel'), ('ISV', 'U.S. Virgin Islands'),
                        ('ITA', 'Italy'), ('JOR', 'Jordan'), ('JPN', 'Japan'), ('KAZ', 'Kazakhstan'),
                        ('KEN', 'Kenya'), ('KOR', 'South Korea'), ('KSA', 'Saudi Arabia'), ('KUW', 'Kuwait'),
                        ('LAT', 'Latvia'), ('LBA', 'Libya'), ('LBN', 'Lebanon'), ('LTU', 'Lithuania'),
                        ('LUX', 'Luxembourg'), ('MAD', 'Madagascar'), ('MAR', 'Morocco'), ('MAS', 'Malaysia'),
                        ('MAW', 'Malawi'), ('MEX', 'Mexico'), ('MGL', 'Mongolia'), ('MKD', 'North Macedonia'),
                        ('MLI', 'Mali'), ('MLT', 'Malta'), ('MNE', 'Montenegro'), ('MOZ', 'Mozambique'),
                        ('MTN', 'Mauritania'), ('NED', 'Netherlands'), ('NGR', 'Nigeria'), ('NIG', 'Niger'),
                        ('NZL', 'New Zealand'), ('OMA', 'Oman'), ('PAN', 'Panama'), ('PAR', 'Paraguay'),
                        ('PER', 'Peru'), ('PHI', 'Philippines'), ('PLE', 'Palestine'), ('POL', 'Poland'),
                        ('POR', 'Portugal'), ('PUR', 'Puerto Rico'), ('QAT', 'Qatar'), ('ROU', 'Romania'),
                        ('RSA', 'South Africa'), ('RUS', 'Russia'), ('RWA', 'Rwanda'), ('SAM', 'Samoa'),
                        ('SCG', 'Serbia and Montenegro'), ('SCO', 'Scotland'), ('SEN', 'Senegal'),
                        ('SEY', 'Seychelles'), ('SGP', 'Singapore'), ('SLO', 'Slovenia'), ('SOM', 'Somalia'),
                        ('SRB', 'Serbia'), ('SRI', 'Sri Lanka'), ('SSD', 'South Sudan'), ('SUD', 'Sudan'),
                        ('SUI', 'Switzerland'), ('SVK', 'Slovakia'), ('SWE', 'Sweden'), ('SYR', 'Syria'),
                        ('TAH', 'Tahiti'), ('TAN', 'Tanzania'), ('TCH', 'Czechoslovakia'), ('THA', 'Thailand'),
                        ('TOG', 'Togo'), ('TPE', 'Chinese Taipei'), ('TUN', 'Tunisia'), ('TUR', 'Turkey'),
                        ('UAE', 'United Arab Emirates'), ('UAR', 'United Arab Republic'), ('UGA', 'Uganda'),
                        ('UKR', 'Ukraine'), ('URS', 'Soviet Union'), ('URU', 'Uruguay'), ('USA', 'United States'),
                        ('VEN', 'Venezuela'), ('VIE', 'Vietnam'), ('YUG', 'Yugoslavia'), ('ZAM', 'Zambia'),
                        ('ZAR', 'Zaire'), ('ZIM', 'Zimbabwe')
                )
                UPDATE teams t
                SET "CanonicalName" = identities."Name",
                    "CountryCode" = identities."Code"
                FROM national_team_ids n
                INNER JOIN team_aliases a ON a."TeamId" = n."TeamId"
                INNER JOIN identities ON identities."Code" = UPPER(a."SourceTeamId")
                WHERE t."Id" = n."TeamId"
                  AND (
                      UPPER(t."CanonicalName") = identities."Code"
                      OR t."CountryCode" = 'UNK'
                      OR UPPER(a."AliasName") = identities."Code"
                  );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Canonical names are intentionally not reverted: source aliases
            // and later ingestions may already depend on the normalized form.
        }
    }
}
