using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260908020000_AddEuroBasket1991PreQualifierBookGames")]
public partial class AddEuroBasket1991PreQualifierBookGames : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            DECLARE competition_id uuid; season_id uuid; cycle_id uuid;
            BEGIN
                SELECT "Id" INTO competition_id FROM competitions WHERE "Name"='FIBA EuroBasket Pre-Qualifiers' AND "CountryCode" IS NULL;
                IF competition_id IS NULL THEN RAISE EXCEPTION 'FIBA EuroBasket Pre-Qualifiers competition is missing'; END IF;
                INSERT INTO teams ("Id","CanonicalName","CountryCode","IsActive","CreatedAtUtc")
                SELECT md5('team:GIB')::uuid,'Gibraltar','GIB',true,now()
                WHERE NOT EXISTS (SELECT 1 FROM teams WHERE "CanonicalName"='Gibraltar');
                INSERT INTO teams ("Id","CanonicalName","CountryCode","IsActive","CreatedAtUtc")
                SELECT md5('team:SMR')::uuid,'San Marino','SMR',true,now()
                WHERE NOT EXISTS (SELECT 1 FROM teams WHERE "CanonicalName"='San Marino');
                INSERT INTO tournament_cycles ("Id","Key","Family","EditionLabel","DisplayName","CreatedAtUtc") VALUES (md5('tournament-cycle:eurobasket-pre-qualifiers:1991')::uuid,'eurobasket-pre-qualifiers-1991','EuroBasket Pre-Qualifiers','1991','EuroBasket Pre-Qualifiers 1991',now()) ON CONFLICT ("Key") DO NOTHING;
                INSERT INTO seasons ("Id","CompetitionId","Label","StartDateUtc","EndDateUtc","CreatedAtUtc") VALUES (md5('season:eurobasket-pre-qualifiers:1991')::uuid,competition_id,'1991',timestamptz '1991-01-01',timestamptz '1991-12-31 23:59:59',now()) ON CONFLICT ("CompetitionId","Label") DO NOTHING;
                SELECT "Id" INTO season_id FROM seasons WHERE "CompetitionId"=competition_id AND "Label"='1991'; SELECT "Id" INTO cycle_id FROM tournament_cycles WHERE "Key"='eurobasket-pre-qualifiers-1991';
                INSERT INTO games ("Id","Source","SourceGameId","SourceUrl","SourceSeasonKey","SourceFetchedAtUtc","SourceRevision","ParserVersion","CompetitionId","SeasonId","TournamentCycleId","GameDateTimeUtc","HomeTeamId","AwayTeamId","HomeScore","AwayScore","Status","CompetitionPhase","CompetitionRound","IsNeutralSite","EloEligible","EloExclusionReason","HasManualResultOverride","IngestedAtUtc","UpdatedAtUtc")
                SELECT md5('game:book:eurobasket-pre-qualifiers-1991:'||v.slug)::uuid,'Los campeonatos de Europa 1935-1995','eurobasket-pre-qualifiers-1991-'||v.slug,NULL,'1991',now(),'Los campeonatos de Europa 1935-1995 — Carlos Jiménez','manual-book-photo-v1',competition_id,season_id,cycle_id,timestamptz '1991-01-01',h."Id",a."Id",v.hs,v.aws,'finished',v.phase,v.rnd,true,true,NULL,false,now(),now()
                FROM (VALUES
                    ('promo-irl-gib','Ireland','Gibraltar',114,59,'Promotion Cup','Group A'),('promo-isl-smr','Iceland','San Marino',83,75,'Promotion Cup','Group A'),('promo-irl-isl','Ireland','Iceland',71,78,'Promotion Cup','Group A'),('promo-smr-gib','San Marino','Gibraltar',84,54,'Promotion Cup','Group A'),('promo-isl-gib','Iceland','Gibraltar',86,54,'Promotion Cup','Group A'),('promo-irl-smr','Ireland','San Marino',79,74,'Promotion Cup','Group A'),('promo-lux-wal','Luxembourg','Wales',105,78,'Promotion Cup','Group B'),('promo-cyp-mlt','Cyprus','Malta',86,58,'Promotion Cup','Group B'),('promo-cyp-lux','Cyprus','Luxembourg',94,79,'Promotion Cup','Group B'),('promo-mlt-wal','Malta','Wales',79,69,'Promotion Cup','Group B'),('promo-wal-cyp','Wales','Cyprus',74,50,'Promotion Cup','Group B'),('promo-lux-mlt','Luxembourg','Malta',92,70,'Promotion Cup','Group B'),('promo-smr-wal','San Marino','Wales',72,68,'Promotion Cup','Semifinals'),('promo-mlt-gib','Malta','Gibraltar',74,69,'Promotion Cup','Semifinals'),('promo-irl-lux','Ireland','Luxembourg',91,87,'Promotion Cup','Semifinals'),('promo-isl-cyp','Iceland','Cyprus',108,78,'Promotion Cup','Semifinals'),('promo-gib-wal','Gibraltar','Wales',77,75,'Promotion Cup','Finals'),('promo-smr-mlt','San Marino','Malta',82,72,'Promotion Cup','Finals'),('promo-cyp-lux-f','Cyprus','Luxembourg',73,68,'Promotion Cup','Finals'),('promo-isl-irl-f','Iceland','Ireland',86,69,'Promotion Cup','Finals'),
                    ('b-cze-fin','Czechoslovakia','Finland',84,83,'Qualification Round','Challenge Group B'),('b-cze-rom','Czechoslovakia','Romania',107,71,'Qualification Round','Challenge Group B'),('b-rom-fin','Romania','Finland',74,69,'Qualification Round','Challenge Group B'),('b-cze-aut','Czechoslovakia','Austria',106,75,'Qualification Round','Challenge Group B'),('b-aut-fin','Austria','Finland',76,70,'Qualification Round','Challenge Group B'),('b-rom-aut','Romania','Austria',114,83,'Qualification Round','Challenge Group B'),
                    ('c-frg-sco','West Germany','Scotland',127,53,'Qualification Round','Challenge Group C'),('c-pol-tur','Poland','Turkey',69,68,'Qualification Round','Challenge Group C'),('c-alb-sco','Albania','Scotland',101,82,'Qualification Round','Challenge Group C'),('c-frg-tur','West Germany','Turkey',82,70,'Qualification Round','Challenge Group C'),('c-tur-sco','Turkey','Scotland',105,61,'Qualification Round','Challenge Group C'),('c-pol-alb','Poland','Albania',103,85,'Qualification Round','Challenge Group C'),('c-pol-sco','Poland','Scotland',123,72,'Qualification Round','Challenge Group C'),('c-frg-alb','West Germany','Albania',103,73,'Qualification Round','Challenge Group C'),('c-tur-alb','Turkey','Albania',106,88,'Qualification Round','Challenge Group C'),('c-frg-pol','West Germany','Poland',101,82,'Qualification Round','Challenge Group C'),
                    ('d-por-isl','Portugal','Iceland',116,76,'Qualification Round','Challenge Group D'),('d-bel-isr','Belgium','Israel',79,71,'Qualification Round','Challenge Group D'),('d-por-hun','Portugal','Hungary',85,72,'Qualification Round','Challenge Group D'),('d-bel-isl','Belgium','Iceland',130,59,'Qualification Round','Challenge Group D'),
                    ('sa-swe-grc','Sweden','Greece',86,100,'Semifinal Phase','Challenge Semifinal Group A'),('sa-rom-bul','Romania','Bulgaria',76,98,'Semifinal Phase','Challenge Semifinal Group A'),('sa-bul-swe','Bulgaria','Sweden',69,81,'Semifinal Phase','Challenge Semifinal Group A'),('sa-grc-rom','Greece','Romania',97,77,'Semifinal Phase','Challenge Semifinal Group A'),('sa-swe-rom','Sweden','Romania',85,81,'Semifinal Phase','Challenge Semifinal Group A'),('sa-bul-grc','Bulgaria','Greece',84,78,'Semifinal Phase','Challenge Semifinal Group A'),('sa-grc-swe','Greece','Sweden',81,68,'Semifinal Phase','Challenge Semifinal Group A'),('sa-bul-rom','Bulgaria','Romania',87,76,'Semifinal Phase','Challenge Semifinal Group A'),('sa-swe-bul','Sweden','Bulgaria',67,83,'Semifinal Phase','Challenge Semifinal Group A'),('sa-rom-grc','Romania','Greece',80,83,'Semifinal Phase','Challenge Semifinal Group A'),('sa-rom-swe','Romania','Sweden',86,95,'Semifinal Phase','Challenge Semifinal Group A'),('sa-grc-bul','Greece','Bulgaria',112,79,'Semifinal Phase','Challenge Semifinal Group A')
                ) AS v(slug,hn,an,hs,aws,phase,rnd) JOIN teams h ON h."CanonicalName"=v.hn JOIN teams a ON a."CanonicalName"=v.an ON CONFLICT ("Source","SourceGameId") DO NOTHING;
                IF (SELECT count(*) FROM games WHERE "Source"='Los campeonatos de Europa 1935-1995' AND "SourceGameId" LIKE 'eurobasket-pre-qualifiers-1991-%')<>52 THEN RAISE EXCEPTION 'Expected fifty-two EuroBasket Pre-Qualifiers 1991 book games'; END IF;
            END $$;
            """);
    }
    protected override void Down(MigrationBuilder migrationBuilder) { }
}
