using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260822150000_ConsolidateWorldCupQualifierCompetition")]
public partial class ConsolidateWorldCupQualifierCompetition : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            DECLARE
                canonical_id uuid;
                duplicate_id uuid;
                source_season record;
                target_season_id uuid;
            BEGIN
                SELECT c."Id"
                INTO canonical_id
                FROM competitions c
                JOIN competition_aliases a ON a."CompetitionId" = c."Id"
                WHERE c."Name" = 'FIBA Basketball World Cup Qualifiers'
                  AND c."CountryCode" IS NULL
                  AND a."Source" = 'fiba'
                  AND a."SourceCompetitionId" = '200-fiba-basketball-world-cup-qualifiers'
                ORDER BY c."CreatedAtUtc", c."Id"
                LIMIT 1;

                IF canonical_id IS NULL THEN
                    RAISE EXCEPTION 'Canonical FIBA World Cup qualifier competition was not found.';
                END IF;

                FOR duplicate_id IN
                    SELECT c."Id"
                    FROM competitions c
                    WHERE c."Name" = 'FIBA Basketball World Cup Qualifiers'
                      AND c."CountryCode" IS NULL
                      AND c."Id" <> canonical_id
                    ORDER BY c."CreatedAtUtc", c."Id"
                LOOP
                    IF EXISTS (
                        SELECT 1
                        FROM competition_aliases
                        WHERE "CompetitionId" = duplicate_id
                    ) THEN
                        RAISE EXCEPTION 'Duplicate World Cup qualifier competition % has aliases; manual review required.', duplicate_id;
                    END IF;

                    FOR source_season IN
                        SELECT "Id", "Label"
                        FROM seasons
                        WHERE "CompetitionId" = duplicate_id
                        ORDER BY "Label", "Id"
                    LOOP
                        SELECT "Id"
                        INTO target_season_id
                        FROM seasons
                        WHERE "CompetitionId" = canonical_id
                          AND "Label" = source_season."Label"
                        ORDER BY "Id"
                        LIMIT 1;

                        IF target_season_id IS NULL THEN
                            UPDATE seasons
                            SET "CompetitionId" = canonical_id
                            WHERE "Id" = source_season."Id";
                        ELSE
                            UPDATE games
                            SET "CompetitionId" = canonical_id,
                                "SeasonId" = target_season_id,
                                "UpdatedAtUtc" = CURRENT_TIMESTAMP
                            WHERE "SeasonId" = source_season."Id";

                            DELETE FROM seasons
                            WHERE "Id" = source_season."Id";
                        END IF;
                    END LOOP;

                    UPDATE games
                    SET "CompetitionId" = canonical_id,
                        "UpdatedAtUtc" = CURRENT_TIMESTAMP
                    WHERE "CompetitionId" = duplicate_id;

                    DELETE FROM competitions
                    WHERE "Id" = duplicate_id;
                END LOOP;
            END $$;

            CREATE UNIQUE INDEX IF NOT EXISTS "IX_competitions_Name_NullCountryCode"
                ON competitions ("Name")
                WHERE "CountryCode" IS NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS "IX_competitions_Name_NullCountryCode";
            """);
    }
}
