# FIBA international qualification systems

This document explains how men’s national-team qualification has changed over
time and how BasketELO should interpret the historical competitions in the
database. It is deliberately a historical guide, not a single universal rule:
FIBA changed calendars, qualification places, regional structures, hosts, and
competition names from cycle to cycle.

## The central distinction

Older qualification systems usually did not have a standalone competition
called “World Cup Qualifiers” or “AmeriCup Qualifiers”. A continental
championship, a regional championship, or an Olympic qualifying tournament
could simultaneously be:

- a competition in its own right;
- a qualifier for the World Championship/World Cup or Olympic Games; and
- a feeder into another regional round.

The modern system is easier to identify: FIBA publishes separate pre-qualifier
and qualifier families, played in international windows, normally with
home-and-away groups.

For ingestion, keep the official competition family and edition/season
separate. Do not create a new league solely because an edition had a different
qualification purpose. Preserve the precise FIBA event title and URL in the
game provenance and use the phase/round fields for qualifying stages.

## Broad historical eras

### Before the 2017 system

Qualification was mainly tournament-based. Teams qualified through a mixture of:

- the previous continental championship or a regional championship;
- dedicated qualifying tournaments;
- host, defending-champion, or ranking-based places; and
- direct places for established teams in some regions and cycles.

The World Championship/World Cup field was commonly selected from the
continental championships. For example, the 2013 FIBA Americas Championship
sent its top four teams to the 2014 World Cup, while the 2013 AfroBasket also
offered World Cup places through its final stages.

### From 2017 onward

FIBA moved to a four-year major-event cycle and a window-based national-team
calendar. The World Cup qualification campaign first used this model for 2019:
80 teams, six windows, regional groups, and home-and-away games over roughly 15
months. The same general structure continued for 2023 and 2027, with regional
adaptations.

Continental cups also moved toward four-year cycles and their own qualifying
windows. A team can therefore appear in both a continental qualifier and a
World Cup qualifier in the same broader cycle; these are different competitions
and must not be merged.

Sources: [FIBA’s 2017 system announcement](https://www.fiba.basketball/en/news/pr-n-020-central-board-gives-green-light-to-new-format-and-calendar-of-competition),
[the 2019 World Cup qualification summary](https://www.fiba.basketball/en/news/basketballworldcup-2019-news-fiba-basketball-world-cup-2019s-32-team-field-complete-after-qualifiers-come-to-an-end),
and [the 2027 qualification overview](https://www.fiba.basketball/en/events/fiba-basketball-world-cup-2027/how-to-qualify).

## Europe

### Older route

EuroBasket was historically a recurring championship with its own qualifying
rounds. The archive contains several different forms: Division A, semi-final
rounds, additional qualifying rounds, challenge rounds, and named qualification
tournaments held in different cities. This means that “EuroBasket qualifiers”
is a family name, not a single stable format.

Hosts and the strength of the field changed by edition. Some cycles used
multiple divisions or qualifying tournaments; others placed a large number of
teams directly into the championship and used a smaller qualification phase.

Coverage note: for this project, reliable individual-game coverage begins with
the 1989 qualification cycle. FIBA lists older historical labels, but the 1991
and earlier event entries do not provide a complete usable game archive. The
1991 qualification games are therefore sourced from the [Wikipedia match
tables](https://en.wikipedia.org/wiki/FIBA_EuroBasket_1991_qualification), and
we do not claim verified EuroBasket qualifier game data before 1989.

The application groups archive rounds by the EuroBasket they qualify for: 2015
combines FIBA's 2013 first qualifying tournament and 2014 second qualifying
round; 2013 uses the 2012 qualification round; 2011, 2009, 2007, 2003, 2001,
1999, and 1997 use their corresponding historical editions; and 1995 combines
the 1994 semi-final round with the 1995 additional qualifying round.

The FIBA archive has no 2005 edition row and no usable 1991-event game list.
Those source gaps are retained as warnings rather than filled with synthetic
games.

The FIBA archive currently exposes EuroBasket qualifying editions from the
1980s onward, including the 1981–2016 historical rounds and the modern 2021 and
2025 cycles. See the [EuroBasket Qualifiers archive](https://www.fiba.basketball/en/history/205-fiba-eurobasket-qualifiers).

### Modern route

The 2019 World Cup European Qualifiers used 32 teams: 24 teams from EuroBasket
2017 and eight teams from World Cup pre-qualifiers. They played six windows in
two rounds, with the top 12 European teams qualifying for the World Cup.

The current model continues to use a first round and second round with
home-and-away games. For the 2027 cycle, 24 teams came from EuroBasket 2025 and
eight from European World Cup pre-qualifiers; the top three teams in each
second-round group qualify. See [How the 2027 European World Cup Qualifiers
work](https://www.fiba.basketball/en/events/fiba-basketball-world-cup-2027-european-qualifiers/how-to-qualify).

### Ingestion rule

Keep these as separate families:

- `FIBA EuroBasket`
- `FIBA EuroBasket Division B`
- `FIBA EuroBasket Pre-Qualifiers`
- `FIBA EuroBasket Qualifiers`
- `FIBA Basketball World Cup European Qualifiers`
- `FIBA Basketball World Cup European Pre-Qualifiers`

Historical rounds remain under the EuroBasket qualifying family even when FIBA
calls an edition a challenge round or qualification tournament.

FIBA Division B is a separate official European national-team tournament family,
not a qualifier alias for Division A. The current archive exposes the 2007, 2009,
and 2011 editions through the [Division B archive](https://www.fiba.basketball/en/history/206-fiba-eurobasket-division-b).

EuroBasket pre-qualifiers are also kept separate from the main qualifying
rounds. The current archive contains the 2021 and 2025 modern cycles plus older
preliminary/qualifying rounds; when GSA contains the same game in its broad
qualifier competition, the game belongs to the pre-qualifier competition based
on its stage and is reconciled there rather than counted twice. See the
[FIBA Pre-Qualifiers archive](https://www.fiba.basketball/en/history/204-fiba-eurobasket-pre-qualifiers).

## Africa

### Older route

The AfroBasket, formerly the FIBA Africa Championship, was both the principal
African national-team championship and, in several cycles, the route to the
World Championship/World Cup or Olympic qualification. Earlier cycles also used
African qualifying rounds and regional/subregional stages. Qualification was
not always exposed as a separate modern “AfroBasket Qualifiers” competition.

FIBA’s archive shows this evolution directly: the 2005 African Championship
qualifying round, 2017 preliminaries, the 2020 preliminary phase, and the 2021
qualifiers are all listed under the qualifier family. See the [AfroBasket
qualifiers archive](https://www.fiba.basketball/en/history/178-fiba-afrobasket-qualifiers).

### Modern route

For AfroBasket 2025, 20 teams entered a three-window qualifying competition.
Sixteen came from participation in AfroBasket 2021 and four came through
pre-qualifiers. The final AfroBasket field had 16 teams, and under the modern
system those 16 teams also advanced to the following African World Cup
qualifiers.

Sources: [FIBA’s AfroBasket 2025 qualification guide](https://www.fiba.basketball/en/news/afrobasket-2025-qualifiers-news-your-guide-to-the-2025-afrobasket)
and [the AfroBasket 2025 history page](https://www.fiba.basketball/en/events/fiba-afrobasket-2025/history).

### Ingestion rule

Use separate families for:

- `FIBA AfroBasket`
- `FIBA AfroBasket Qualifiers`
- `FIBA Basketball World Cup African Qualifiers`
- `FIBA Basketball World Cup African Pre-Qualifiers`

Do not treat the AfroBasket itself as a friendly or as a duplicate of the
World Cup qualifiers. Its games are official tournament games even when the
edition also determines World Cup places.

## Asia

### Older route

The competition was historically known as the FIBA Asia Championship and is now
the FIBA Asia Cup. Qualification was often organized through Asian regional
groups, zone/subzone competitions, or places carried from the previous
championship. The historical format changed substantially between editions.

### Modern route

The first FIBA Asia Cup qualifying system in the current family was introduced
for the 2021 edition. The 24-team qualifier field combined the 16 teams from
the previous Asia Cup with eight teams advancing from regional pre-qualifiers.

For the 2025 edition, 18 teams began in pre-qualifiers. Eight advanced to join
the 16 teams from Asia Cup 2022, creating a 24-team qualifier pool. The groups
were organized across East and West regions, and third-placed teams could enter
a final qualifying tournament for the remaining places.

Australia and New Zealand began appearing in the Asia Cup system in 2017. This
is why Oceania teams can appear in Asian continental competitions even though
they remain relevant to the separate Oceania/Asia-Pacific World Cup and
Olympic allocation rules.

Sources: [the Asia Cup qualifier archive](https://www.fiba.basketball/en/history/192-fiba-asia-cup-qualifiers),
[FIBA’s explanation of the first modern Asia Cup qualifiers](https://www.fiba.basketball/en/news/asiacup-2021-qualifiers-news-things-to-know-ahead-of-the-fiba-asia-cup-2021-qualifiers-draw),
and [the Asia Cup 2025 qualification route](https://www.fiba.basketball/en/news/everything-you-need-to-know-about-the-fiba-asia-cup-2025).

### Ingestion rule

Keep these distinct:

- `FIBA Asia Cup`
- `FIBA Asia Cup Qualifiers`
- `FIBA Basketball World Cup Asian Qualifiers`
- `FIBA Basketball World Cup Asian Pre-Qualifiers`

The Asia and Oceania World Cup region is a combined qualification region even
when the continental championship is called Asia Cup.

## Oceania

### Older route

The first official Oceania men’s Championship was held in 1971 to qualify teams
for FIBA international competition. For many years the principal route was a
small Oceania Championship, usually centered on Australia and New Zealand but
with Pacific teams appearing in some editions. The archive includes teams such
as Guam, New Caledonia, American Samoa, Fiji, and Samoa in different editions.

The Oceania Championship could serve as the direct route to the Olympic Games
or as a route to an Olympic Qualifying Tournament. In the 2015 Olympic cycle,
the winner qualified directly and the runner-up entered an OQT.

Sources: [FIBA Oceania history](https://about.fiba.basketball/en/regions/oceania/history),
[the Oceania Championship archive](https://www.fiba.basketball/en/history/216-fiba-oceania-championship),
and FIBA’s [2016 Olympic qualification breakdown](https://www.fiba.basketball/en/news/new-olympic-qualifying-tournament-format-to-feature-18-teams-playing-across-three-tournaments).

### Modern route

Since 2017, Australia and New Zealand have participated in the Asia Cup system.
For World Cup qualification, Asia and Oceania are treated as one combined
region. Olympic qualification retains a universality principle, so Oceania may
receive a separately defined regional place or OQT route rather than simply
copying the World Cup allocation.

### Ingestion rule

Retain `FIBA Oceania Championship` as its own historical family. Do not relabel
those games as Asia Cup games. Add the Asia Cup and Asia/Oceania World Cup
qualifier families separately from 2017 onward.

## Americas

### Older route

Before the modern window system, the Americas route was a regional ladder rather
than one qualifier league. Depending on the cycle, teams could come through:

- the South American Championship;
- Centrobasket and COCABA competitions;
- Caribbean Basketball Championships;
- the FIBA Americas Championship, formerly the Tournament of the Americas;
- automatic places for the United States, Canada, hosts, or other cycle-specific
  entries.

The FIBA Americas Championship was often the final continental event and also
determined World Championship, World Cup, or Olympic places. The FIBA archive
includes editions whose official titles were `American Olympic Qualifying
Tournament for Men`, so the 1980, 1984, and 1988 editions should be interpreted
by their edition title rather than by the generic family name.

Sources: [the Americas archive](https://www.fiba.basketball/en/history/184-fiba-americup),
[the Centrobasket archive](https://www.fiba.basketball/en/history/122-centrobasket-championship),
[the COCABA archive](https://www.fiba.basketball/en/history/113-cbc-championship),
[the South American archive](https://www.fiba.basketball/en/history/327-south-american-championship),
and FIBA’s historical explanation of the regional ladder in [It’s oh so
complicated](https://www.fiba.basketball/en/news/its-oh-so-complicated1).

### Modern route

AmeriCup qualification and World Cup qualification are separate systems.
AmeriCup pre-qualifiers began appearing as a distinct FIBA archive family in
2018. They use Caribbean, Central American, and South American stages when the
cycle requires them, and successful teams feed into the AmeriCup qualifying
field.

The World Cup route moved to regional home-and-away windows for the 2019 cycle.
The Americas field was built separately from the European, African, and Asian
fields and included its own pre-qualifiers in later cycles.

### Ingestion rule

Use the following distinction:

- `FIBA AmeriCup`
- `FIBA AmeriCup Qualifiers`
- `FIBA AmeriCup Pre-Qualifiers`
- historical feeder families: `FIBA Americas Championship`, `Centrobasket`,
  `COCABA`, `Caribbean Basketball Championship`, and `South American Championship`
- `FIBA Basketball World Cup Americas Qualifiers`
- `FIBA Basketball World Cup Americas Pre-Qualifiers`

This preserves historical qualification evidence without pretending that a
1960s regional championship used the same system as a 2025 AmeriCup window.

## FIBA Basketball World Cup

### Before 2019

The World Championship, renamed the World Cup from 2014, generally selected its
field through the preceding continental championships. Hosts and defending
champions could receive direct places, and some cycles used wild cards or other
FIBA decisions to complete the field. The continental championship was
therefore both a major tournament and a World Cup qualifier.

For example, FIBA described the 2013 continental championships as qualifying
tournaments for the 2014 World Cup. The 2014 World Cup still had a 24-team
field.

### 2019 onward

The 2019 cycle was the first global window-based World Cup qualification system:

- 80 national teams entered the qualification campaign;
- games were played in six international windows;
- regional groups used home-and-away games;
- the campaign lasted approximately 15 months; and
- the World Cup field expanded to 32 teams.

The 2023 and 2027 cycles retain this architecture with region-specific numbers
of teams and places. The current 2027 overview assigns 16 teams to Africa, 16
to the Americas, 16 to Asia/Oceania, and 32 to Europe before the final World
Cup allocation is completed.

Source: [FIBA’s 2019 qualification summary](https://www.fiba.basketball/en/news/basketballworldcup-2019-news-fiba-basketball-world-cup-2019s-32-team-field-complete-after-qualifiers-come-to-an-end)
and [How to qualify for the 2027 World Cup](https://www.fiba.basketball/en/events/fiba-basketball-world-cup-2027/how-to-qualify).

## Olympic Games

### Historical route

Olympic qualification has changed more often than continental qualification.
Older cycles used dedicated regional Olympic qualifying tournaments, such as
the `American Olympic Qualifying Tournament for Men`, or used continental
championship placements to identify Olympic entrants.

The 2016 Rio cycle illustrates the pre-2019 model: continental championships
provided direct Olympic places and OQT places. The three 2016 OQTs each had six
teams and each winner qualified for the Olympics. The allocation varied by
region: Africa and Asia received fewer OQT places than Europe, while Oceania
had a direct champion place and a runner-up OQT place.

### Modern route

From the Tokyo 2020 cycle, the World Cup became the main Olympic qualification
route. The best regional teams at the World Cup could qualify directly, while
the remaining places were decided through Olympic Qualifying Tournaments. FIBA
also uses World Cup ranking and regional universality criteria to populate the
OQT field.

The Paris 2024 cycle used four OQTs, with each tournament winner qualifying for
the Olympic Games. The OQT itself is therefore a final qualification event, not
a continental championship and not a friendly tournament.

Sources: [the 2016 OQT format and regional allocation](https://www.fiba.basketball/en/news/new-olympic-qualifying-tournament-format-to-feature-18-teams-playing-across-three-tournaments),
[the 2019 World Cup-to-Olympics explanation](https://www.fiba.basketball/en/news/news-australia-qualify-for-olympics-as-top-oceania-side-at-world-cup),
and [the Paris 2024 OQT results](https://www.fiba.basketball/en/news/greece-brazil-puerto-rico-and-spain-qualify-for-last-spots-at-mens-olympic-basketball-tournament-paris-2024).

## Data and ELO interpretation

The ingestion model should apply these rules consistently:

1. Import official FIBA tournament games, including qualifier, pre-qualifier,
   classification, and final-round games.
2. Exclude friendlies from ELO eligibility even if the provider exposes them.
3. Keep the competition family stable, but retain the edition title, season,
   phase, round, source URL, and source season key.
4. Do not merge continental qualifiers with World Cup or Olympic qualifiers just
   because they occur in the same window.
5. Treat missing FIBA archive pages as provider gaps or inspection cases; never
   create synthetic games from standings or qualification outcomes.
6. When FIBA has no individual historical game date, use the edition date only
   as a documented fallback and keep the provider warning.

The practical result is a single national-team ELO pool with historically
accurate competition metadata. The rating engine can then use official games
from different qualification systems without losing the context that explains
why those games existed.
