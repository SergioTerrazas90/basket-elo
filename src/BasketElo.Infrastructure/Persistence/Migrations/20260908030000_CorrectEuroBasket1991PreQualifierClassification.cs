using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260908030000_CorrectEuroBasket1991PreQualifierClassification")]
public partial class CorrectEuroBasket1991PreQualifierClassification : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            BEGIN
                DELETE FROM games
                WHERE "Source"='Los campeonatos de Europa 1935-1995'
                  AND "SourceGameId" IN (
                    'eurobasket-pre-qualifiers-1991-d-cze-urs',
                    'eurobasket-pre-qualifiers-1991-d-isr-fra',
                    'eurobasket-pre-qualifiers-1991-d-fra-cze',
                    'eurobasket-pre-qualifiers-1991-d-urs-isr',
                    'eurobasket-pre-qualifiers-1991-d-cze-isr',
                    'eurobasket-pre-qualifiers-1991-d-fra-urs',
                    'eurobasket-pre-qualifiers-1991-d-urs-cze',
                    'eurobasket-pre-qualifiers-1991-d-fra-isr',
                    'eurobasket-pre-qualifiers-1991-d-cze-fra',
                    'eurobasket-pre-qualifiers-1991-d-isr-urs',
                    'eurobasket-pre-qualifiers-1991-d-isr-cze',
                    'eurobasket-pre-qualifiers-1991-d-urs-fra'
                  );
                IF (SELECT count(*) FROM games WHERE "Source"='Los campeonatos de Europa 1935-1995' AND "SourceGameId" LIKE 'eurobasket-pre-qualifiers-1991-%')<>52
                THEN RAISE EXCEPTION 'Expected fifty-two EuroBasket Pre-Qualifiers 1991 book games after classification correction'; END IF;
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) { }
}
