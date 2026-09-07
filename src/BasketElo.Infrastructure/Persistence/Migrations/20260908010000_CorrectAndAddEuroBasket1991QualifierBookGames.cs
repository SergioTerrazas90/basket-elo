using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260908010000_CorrectAndAddEuroBasket1991QualifierBookGames")]
public partial class CorrectAndAddEuroBasket1991QualifierBookGames : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            DECLARE competition_id uuid; season_id uuid; cycle_id uuid;
            BEGIN
                SELECT "Id" INTO competition_id FROM competitions WHERE "Name"='EuroBasket Qualifiers' AND "CountryCode" IS NULL;
                IF competition_id IS NULL THEN RAISE EXCEPTION 'EuroBasket Qualifiers competition is missing'; END IF;
                INSERT INTO tournament_cycles ("Id","Key","Family","EditionLabel","DisplayName","CreatedAtUtc")
                VALUES (md5('tournament-cycle:eurobasket-qualifiers:1991')::uuid,'eurobasket-qualifiers-1991','EuroBasket Qualifiers','1991','EuroBasket Qualifiers 1991',now()) ON CONFLICT ("Key") DO NOTHING;
                INSERT INTO seasons ("Id","CompetitionId","Label","StartDateUtc","EndDateUtc","CreatedAtUtc")
                VALUES (md5('season:eurobasket-qualifiers:1991')::uuid,competition_id,'1991',timestamptz '1991-01-01',timestamptz '1991-12-31 23:59:59',now()) ON CONFLICT ("CompetitionId","Label") DO NOTHING;
                SELECT "Id" INTO season_id FROM seasons WHERE "CompetitionId"=competition_id AND "Label"='1991';
                SELECT "Id" INTO cycle_id FROM tournament_cycles WHERE "Key"='eurobasket-qualifiers-1991';
                UPDATE games SET "TournamentCycleId"=cycle_id WHERE "CompetitionId"=competition_id AND "SeasonId"=season_id;
                INSERT INTO games ("Id","Source","SourceGameId","SourceUrl","SourceSeasonKey","SourceFetchedAtUtc","SourceRevision","ParserVersion","CompetitionId","SeasonId","TournamentCycleId","GameDateTimeUtc","HomeTeamId","AwayTeamId","HomeScore","AwayScore","Status","CompetitionPhase","CompetitionRound","IsNeutralSite","EloEligible","EloExclusionReason","HasManualResultOverride","IngestedAtUtc","UpdatedAtUtc")
                SELECT md5('game:book:eurobasket-qualifiers-1991:'||v.slug)::uuid,'Los campeonatos de Europa 1935-1995','eurobasket-qualifiers-1991-'||v.slug,NULL,'1991',now(),'Los campeonatos de Europa 1935-1995 — Carlos Jiménez','manual-book-photo-v1',competition_id,season_id,cycle_id,timestamptz '1991-01-01',h."Id",a."Id",v.hs,v.aws,'finished',v.phase,v.rnd,true,true,NULL,false,now(),now()
                FROM (VALUES
                    ('a-eng-den','England','Denmark',74,55,'Qualification Round','Group A'),('a-sui-nor','Switzerland','Norway',66,65,'Qualification Round','Group A'),('a-nor-den','Norway','Denmark',85,77,'Qualification Round','Group A'),('a-swe-eng','Sweden','England',92,78,'Qualification Round','Group A'),('a-swe-den','Sweden','Denmark',96,57,'Qualification Round','Group A'),('a-eng-sui','England','Switzerland',86,68,'Qualification Round','Group A'),('a-eng-nor','England','Norway',74,64,'Qualification Round','Group A'),('a-swe-sui','Sweden','Switzerland',102,65,'Qualification Round','Group A'),('a-den-sui','Denmark','Switzerland',79,78,'Qualification Round','Group A'),('a-swe-nor','Sweden','Norway',84,81,'Qualification Round','Group A'),
                    ('b-fin-isr','Finland','Israel',106,102,'Qualification Round','Group B'),('b-bul-nor','Bulgaria','Norway',102,84,'Qualification Round','Group B'),('b-fin-nor','Finland','Norway',89,77,'Qualification Round','Group B'),('b-isr-bul','Israel','Bulgaria',92,90,'Qualification Round','Group B'),('b-isr-nor','Israel','Norway',95,74,'Qualification Round','Group B'),('b-bul-fin','Bulgaria','Finland',101,98,'Qualification Round','Group B'),
                    ('c-fra-isl','France','Iceland',104,63,'Qualification Round','Group C'),('c-sui-den','Switzerland','Denmark',89,82,'Qualification Round','Group C'),('c-fra-sui','France','Switzerland',106,60,'Qualification Round','Group C'),('c-den-isl','Denmark','Iceland',76,73,'Qualification Round','Group C'),('c-sui-isl','Switzerland','Iceland',112,100,'Qualification Round','Group C'),('c-fra-den','France','Denmark',123,95,'Qualification Round','Group C'),
                    ('d-eng-swe','England','Sweden',115,108,'Qualification Round','Group D'),('d-tur-sco','Turkey','Scotland',87,71,'Qualification Round','Group D'),('d-eng-sco','England','Scotland',120,80,'Qualification Round','Group D'),('d-swe-tur','Sweden','Turkey',87,82,'Qualification Round','Group D'),('d-swe-sco','Sweden','Scotland',109,85,'Qualification Round','Group D'),('d-eng-tur','England','Turkey',84,83,'Qualification Round','Group D'),
                    ('sa-grc-eng','Greece','England',107,66,'Semifinal Phase','Semifinal Group A'),('sa-cze-ned','Czechoslovakia','Netherlands',79,86,'Semifinal Phase','Semifinal Group A'),('sa-grc-ned','Greece','Netherlands',102,62,'Semifinal Phase','Semifinal Group A'),('sa-eng-cze','England','Czechoslovakia',103,101,'Semifinal Phase','Semifinal Group A'),('sa-cze-grc','Czechoslovakia','Greece',76,89,'Semifinal Phase','Semifinal Group A'),('sa-ned-eng','Netherlands','England',93,75,'Semifinal Phase','Semifinal Group A'),('sa-eng-grc','England','Greece',76,84,'Semifinal Phase','Semifinal Group A'),('sa-ned-cze','Netherlands','Czechoslovakia',89,67,'Semifinal Phase','Semifinal Group A'),('sa-ned-grc','Netherlands','Greece',66,93,'Semifinal Phase','Semifinal Group A'),('sa-cze-eng','Czechoslovakia','England',107,80,'Semifinal Phase','Semifinal Group A'),('sa-grc-cze','Greece','Czechoslovakia',107,85,'Semifinal Phase','Semifinal Group A'),('sa-eng-ned','England','Netherlands',71,65,'Semifinal Phase','Semifinal Group A'),
                    ('sb-yug-bul','Yugoslavia','Bulgaria',140,91,'Semifinal Phase','Semifinal Group B'),('sb-swe-frg','Sweden','West Germany',85,95,'Semifinal Phase','Semifinal Group B'),('sb-yug-swe','Yugoslavia','Sweden',117,86,'Semifinal Phase','Semifinal Group B'),('sb-bul-frg','Bulgaria','West Germany',93,88,'Semifinal Phase','Semifinal Group B'),('sb-frg-yug','West Germany','Yugoslavia',101,114,'Semifinal Phase','Semifinal Group B'),('sb-swe-bul','Sweden','Bulgaria',86,88,'Semifinal Phase','Semifinal Group B'),('sb-bul-yug','Bulgaria','Yugoslavia',97,74,'Semifinal Phase','Semifinal Group B'),('sb-frg-swe','West Germany','Sweden',89,92,'Semifinal Phase','Semifinal Group B'),('sb-swe-yug','Sweden','Yugoslavia',78,106,'Semifinal Phase','Semifinal Group B'),('sb-frg-bul','West Germany','Bulgaria',96,105,'Semifinal Phase','Semifinal Group B'),('sb-bul-swe','Bulgaria','Sweden',85,69,'Semifinal Phase','Semifinal Group B'),('sb-yug-frg','Yugoslavia','West Germany',116,78,'Semifinal Phase','Semifinal Group B')
                ) AS v(slug,hn,an,hs,aws,phase,rnd) JOIN teams h ON h."CanonicalName"=v.hn JOIN teams a ON a."CanonicalName"=v.an ON CONFLICT ("Source","SourceGameId") DO NOTHING;
                IF (SELECT count(*) FROM games WHERE "Source"='Los campeonatos de Europa 1935-1995' AND "SourceGameId" LIKE 'eurobasket-qualifiers-1991-%')<>52 THEN RAISE EXCEPTION 'Expected fifty-two EuroBasket Qualifiers 1991 book games'; END IF;
            END $$;
            """);
    }
    protected override void Down(MigrationBuilder migrationBuilder) { }
}
