using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using BasketElo.Infrastructure.Persistence;

#nullable disable

namespace BasketElo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BasketEloDbContext))]
[Migration("20260804120000_NormalizeCountryCodes")]
public partial class NormalizeCountryCodes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TEMP TABLE country_code_mappings (old_code text PRIMARY KEY, new_code text NOT NULL) ON COMMIT DROP;
            INSERT INTO country_code_mappings (old_code, new_code) VALUES
                ('AEK','GR'), ('ALG','DZ'), ('ANG','AO'), ('ARG','AR'), ('ARM','AM'), ('ASA','AS'), ('AUS','AU'), ('AUT','AT'), ('AZE','AZ'),
                ('BAH','BS'), ('BAN','BD'), ('BAR','BB'), ('BDI','BI'), ('BEL','BE'), ('BEN','BJ'), ('BER','BM'), ('BFA','BF'), ('BGR','BG'),
                ('BHR','BH'), ('BHU','BT'), ('BIH','BA'), ('BIZ','BZ'), ('BLB','BL'), ('BLR','BY'), ('BLZ','BZ'), ('BOL','BO'), ('BOT','BW'),
                ('BRA','BR'), ('BRN','BH'), ('BRU','BN'), ('BUL','BG'), ('BUR','BF'), ('CAF','CF'), ('CAL','NC'), ('CAM','CM'), ('CAN','CA'),
                ('CAY','KY'), ('CGO','CG'), ('CON','CG'), ('CHA','TD'), ('CHI','CL'), ('CHN','CN'), ('CIV','CI'), ('CMR','CM'), ('COD','CD'), ('COL','CO'),
                ('CPV','CV'), ('CRC','CR'), ('CRO','HR'), ('CUB','CU'), ('CYP','CY'), ('CZE','CZ'), ('DEN','DK'), ('DMA','DM'), ('DOM','DO'),
                ('DOR','DO'), ('ECU','EC'), ('EGY','EG'), ('ELS','SV'), ('EQG','GQ'), ('ERI','ER'), ('ESP','ES'), ('EST','EE'), ('ETH','ET'),
                ('FIN','FI'), ('FIJ','FJ'), ('FOR','FO'), ('FPO','PF'), ('FRA','FR'), ('GAB','GA'), ('GAM','GM'), ('GBR','GB'), ('GBS','GW'),
                ('GEO','GE'), ('GEQ','GQ'), ('GER','DE'), ('GRE','GR'), ('GRN','GD'), ('GUA','GT'), ('GUI','GN'), ('GUM','GU'), ('GUY','GY'),
                ('HAI','HT'), ('HKG','HK'), ('HOL','NL'), ('HON','HN'), ('HUN','HU'), ('INA','ID'), ('IND','IN'), ('IRA','IR'), ('IRE','IE'),
                ('IRI','IR'), ('IRL','IE'), ('IRQ','IQ'), ('ISL','IS'), ('ISR','IL'), ('ISV','VI'), ('ITA','IT'), ('IVB','VG'), ('JAM','JM'),
                ('JER','JE'), ('JOR','JO'), ('JPN','JP'), ('KAZ','KZ'), ('KEN','KE'), ('KGZ','KG'), ('KOR','KR'), ('KOS','XK'), ('KSA','SA'),
                ('KUW','KW'), ('LAT','LV'), ('LBA','LY'), ('LBN','LB'), ('LBR','LR'), ('LCA','LC'), ('LES','LS'), ('LTU','LT'), ('LUX','LU'),
                ('MAC','MO'), ('MAD','MG'), ('MAR','MA'), ('MAS','MY'), ('MAW','MW'), ('MDA','MD'), ('MDV','MV'), ('MEX','MX'), ('MGL','MN'),
                ('MKD','MK'), ('MLI','ML'), ('MLT','MT'), ('MNE','ME'), ('MOZ','MZ'), ('MTN','MR'), ('NCA','NI'), ('NED','NL'), ('NEP','NP'),
                ('NGR','NG'), ('NIG','NE'), ('NOR','NO'), ('NZL','NZ'), ('OMA','OM'), ('PAK','PK'), ('PAN','PA'), ('PAR','PY'), ('PER','PE'),
                ('PHI','PH'), ('PLE','PS'), ('POL','PL'), ('POR','PT'), ('PRK','KP'), ('PUR','PR'), ('QAT','QA'), ('ROM','RO'), ('ROU','RO'),
                ('RSA','ZA'), ('RUS','RU'), ('RWA','RW'), ('SAM','WS'), ('SEN','SN'), ('SEY','SC'), ('SGP','SG'), ('SLO','SI'), ('SOM','SO'),
                ('SRI','LK'), ('SSD','SS'), ('SUD','SD'), ('SUI','CH'), ('SUR','SR'), ('SVG','VC'), ('SVK','SK'), ('SVN','SI'), ('SWE','SE'),
                ('SWI','CH'), ('SYR','SY'), ('TAH','PF'), ('TAI','TW'), ('TAN','TZ'), ('TCI','TC'), ('THA','TH'), ('TOG','TG'), ('TPE','TW'),
                ('TTO','TT'), ('TUN','TN'), ('TUR','TR'), ('UAE','AE'), ('UGA','UG'), ('UK','GB'), ('UKR','UA'), ('URU','UY'), ('USA','US'),
                ('UZB','UZ'), ('VEN','VE'), ('VIE','VN'), ('VIN','VC'), ('VIR','VI'), ('VNM','VN'), ('XKX','XK'), ('SMN','SCG'), ('ZAM','ZM'), ('ZIM','ZW'),
                ('DEU','DE'), ('DNK','DK'), ('GRC','GR'), ('HRV','HR'), ('LVA','LV'), ('NLD','NL'), ('PRT','PT'), ('SRB','RS'), ('CHE','CH');

            UPDATE teams AS target
            SET "CountryCode" = mapping.new_code
            FROM country_code_mappings AS mapping
            WHERE UPPER(TRIM(target."CountryCode")) = mapping.old_code;

            UPDATE competitions AS target
            SET "CountryCode" = mapping.new_code
            FROM country_code_mappings AS mapping
            WHERE UPPER(TRIM(target."CountryCode")) = mapping.old_code;

            UPDATE identity_health_check_runs AS target
            SET "CountryCode" = mapping.new_code,
                "ScopeKey" = REPLACE(target."ScopeKey", 'country=' || mapping.old_code, 'country=' || mapping.new_code)
            FROM country_code_mappings AS mapping
            WHERE UPPER(TRIM(target."CountryCode")) = mapping.old_code
               OR target."ScopeKey" LIKE '%country=' || mapping.old_code || '%';

            UPDATE identity_health_check_findings AS target
            SET "CountryCode" = mapping.new_code
            FROM country_code_mappings AS mapping
            WHERE UPPER(TRIM(target."CountryCode")) = mapping.old_code;

            UPDATE current_result_reviews AS target
            SET "SuggestedCompetitionCountryCode" = mapping.new_code
            FROM country_code_mappings AS mapping
            WHERE UPPER(TRIM(target."SuggestedCompetitionCountryCode")) = mapping.old_code;

            UPDATE model_lab_run_scopes AS target
            SET "CountryCode" = mapping.new_code
            FROM country_code_mappings AS mapping
            WHERE UPPER(TRIM(target."CountryCode")) = mapping.old_code;

            UPDATE teams SET "CountryCode" = UPPER(TRIM("CountryCode"))
            WHERE "CountryCode" IS NOT NULL AND "CountryCode" <> UPPER(TRIM("CountryCode"));
            UPDATE competitions SET "CountryCode" = UPPER(TRIM("CountryCode"))
            WHERE "CountryCode" IS NOT NULL AND "CountryCode" <> UPPER(TRIM("CountryCode"));
            UPDATE identity_health_check_runs SET "CountryCode" = UPPER(TRIM("CountryCode"))
            WHERE "CountryCode" IS NOT NULL AND "CountryCode" <> UPPER(TRIM("CountryCode"));
            UPDATE identity_health_check_findings SET "CountryCode" = UPPER(TRIM("CountryCode"))
            WHERE "CountryCode" IS NOT NULL AND "CountryCode" <> UPPER(TRIM("CountryCode"));
            UPDATE current_result_reviews SET "SuggestedCompetitionCountryCode" = UPPER(TRIM("SuggestedCompetitionCountryCode"))
            WHERE "SuggestedCompetitionCountryCode" IS NOT NULL AND "SuggestedCompetitionCountryCode" <> UPPER(TRIM("SuggestedCompetitionCountryCode"));
            UPDATE model_lab_run_scopes SET "CountryCode" = UPPER(TRIM("CountryCode"))
            WHERE "CountryCode" IS NOT NULL AND "CountryCode" <> UPPER(TRIM("CountryCode"));
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Country-code normalization is intentionally one-way: alpha-3 values are
        // provider aliases, not authoritative persisted values.
    }
}
