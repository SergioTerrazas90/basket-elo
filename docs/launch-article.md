# We set out to build a basketball rating site. We ended up building a history machine.

## Introducing Basket ELO, an AI-native basketball intelligence workspace

The original idea sounded almost suspiciously manageable:

> Take basketball results, run them through ELO, and show us who is good.

That was the beginning of Basket ELO.

The first version had the familiar ingredients of a sensible MVP: a .NET API, a Blazor web app, PostgreSQL, a background worker, and a small backfill pipeline. The plan was to import games, calculate ratings, put the rankings on a page, and move on to the interesting part.

Then the data arrived.

Not “the data” in the singular. The data from official competition archives, public sports databases, historical schedule pages, APIs, old league websites, archived pages, score matrices, and sources that were technically public but operationally determined to make you work for every row. Some sources called a tournament by one name and its qualifying cycle by another. Some published scores but not dates. Some listed a champion and runner-up but not the games that produced them. Some had the same team under three names in three decades.

The interesting part was the data.

Basket ELO is now a basketball intelligence workspace: public rankings, team profiles, game browsing, ELO history, explanations for individual results, upcoming games, current-result refreshes, historical backfill operations, identity review, and a Model Lab for testing alternate rating models. It is designed to let someone move from “who is ranked first?” to “why?” and then to “what if we changed the model?”

This is the story of how that happened.

## The fast part was building an application

The first few days moved quickly. We established the solution structure, created the initial database schema, exposed health and status endpoints, enabled Swagger, and added provider-abstracted backfill jobs processed by a worker.

That early architecture turned out to be one of the best decisions in the project. A request to import a season did not have to hold open a web request while a remote archive was crawled. The API could create a job, the worker could claim it, and the result could come back with counts, warnings, failures, and provenance.

The system grew around that seam:

- providers resolve competitions and parse source-specific records;
- jobs make long-running imports observable and retryable;
- canonical games preserve their source identity and provenance;
- ELO rebuilds are queued separately from ingestion;
- the web app can show the state of the data instead of pretending everything is complete.

The first surprise was that the basic ELO calculation was not the difficult part. A rating can start at 1500, use a K-factor of 20, account for home advantage, and adjust for margin. The harder question was how to change that formula later without rewriting history invisibly. So ratings became ruleset-versioned from the beginning: `basic-elo-v1`, `point-margin-elo-v1`, and the public `adjusted-v1` ruleset can coexist and be rebuilt independently.

The formula was quick. Making it explainable, reproducible, and safe to evolve took longer.

## The slow part was discovering what a “game” means

A historical sports database is full of things that look like simple records until you try to reconcile them.

Is BAA the same competition as NBA? For Basket ELO, the answer is yes for the canonical NBA rating pool, while the original source labels remain as provenance.

Are the Minneapolis Lakers and Los Angeles Lakers different teams? Not for franchise continuity. They are one rating chain, with dated aliases and a visible relocation history.

Is a 2022 qualification game necessarily found on a page labelled 2022? Not always. Historical FIBA pages can describe the source year while the game belongs to the cycle for the later championship.

Should standings be converted into games when the archive does not expose the scorecards? Absolutely not.

Those decisions became part of the product, not hidden implementation details. Identity health checks detect unresolved or ambiguous teams. Review screens show canonical targets for aliases. Competition stages remain separate even when they belong to the same tournament cycle. Manual corrections survive routine re-imports. A source warning is not quietly turned into a confident-looking number.

The guiding rule became simple: if the system cannot establish what a record is, it should make the uncertainty visible.

That rule sounds modest. It is not. It changes the database model, the import pipeline, the admin UI, the ELO rebuild gates, the tests, and the language used in coverage reports.

## Current results are an identity problem too

The live-results pipeline introduced a different version of the same problem. It is not enough to read today’s scores. The system has to decide whether a source competition is one we understand, whether its teams are canonical, whether a finished result belongs to an already scheduled fixture, and whether the result is allowed to affect ELO.

The default LiveScore window is nine calendar days: yesterday for reconciliation, today, and the next seven days for schedule visibility. The worker reads one source page per date. A run can therefore find scheduled games, live games, and final scores in the same pass, while repeated runs update the same source identity instead of creating duplicates.

Competition identity is resolved before team identity. Canonical competitions can carry source-specific aliases, so an observed name such as `WNBA` can be mapped to the canonical competition and remembered for future runs. The source’s presentation hierarchy matters here. LiveScore displays a parent competition such as `Asia U18 Championship` and a child heading such as `Group A`; treating the child heading as the competition created four false competitions. The parser now preserves the parent as the competition and the group as the stage.

Unmatched competitions are surfaced before their games are allowed into the normal pipeline. An administrator can merge one into an existing supported competition, which saves the alias, or choose “Don’t handle.” That decision marks the competition as both unsupported and inactive, ignores its existing reviews, and retains the alias so future ingestion recognizes it and skips it without generating the same review again.

The distinction between those two flags is deliberate. “Unsupported” describes current-results policy: do not ingest this competition. “Inactive” describes catalog status: do not present this canonical competition as an active selectable identity. An inactive unsupported competition can still be recognized through its alias; otherwise rejecting a competition would paradoxically make it unmatched on the next run.

This workflow also exposed several launch caveats. Source competition IDs are not always available, so name and country normalization remain important. A competition can be known while its teams are unresolved, and a candidate can be parsed correctly without being safe to insert as a canonical game. A source result may match more than one planned fixture, in which case the system asks for a decision instead of guessing. Provider availability is also an operational policy: the source must be permitted, enabled consistently across the API and worker services, and monitored for layout changes.

## The NBA became a small project inside the project

The NBA looked like the obvious historical starting point: a famous competition, a relatively well-known schedule archive, and a clean target range from 1946–47 to the present.

It quickly became a lesson in source policy.

The system now treats BAA and NBA as one canonical competition, routes early seasons through a pinned FiveThirtyEight archive, uses API-Sports for later coverage and current-season work, supports authorized offline archives, and keeps Basketball-Reference network access disabled unless permission is explicitly recorded. Imported records retain source IDs, season keys, URLs or archive identifiers, parser versions, and import timestamps.

The provider also has to know that a source abbreviation is not a team identity. A franchise can relocate, rename, disappear, or be confused with a similarly named historical club. The NBA catalog therefore carries continuity decisions for teams such as the Lakers, Pistons, Warriors, Kings, Thunder, and Hornets, while defunct teams remain distinct instead of being guessed into modern franchises.

What we thought would be “import the NBA history” became an audit tool, a source-permission policy, a franchise identity catalog, retry and rate-limit controls, range-based queueing, current-season refresh scheduling, and competition-scoped ELO rebuilds.

That last part matters. Refreshing NBA data should not accidentally rebuild national-team or European club ratings. The system learned to make the boundary explicit.

## The archive was the opponent

The European and international expansion was where the project became genuinely entertaining—not because parsing a website is funny, but because every archive carries a different theory of what history is.

We recovered league and cup history from Italy, France, Greece, Germany, Turkey, Serbia and the former Yugoslav space, the ABA League, Israel, Lithuania, the Czech Republic, Poland, and the Baltic competitions. We added FIBA national-team families across Europe, Africa, Asia, the Americas, Oceania, the World Cup, and the Olympics.

Some examples capture the character of the work:

- The European club archive produced 4,060 FIBA-source game rows from 1958–59 through 1999–2000, with zero duplicate source IDs after reconciliation.
- The Israeli historical import produced 7,741 official-source games, while smaller early seasons were flagged for review instead of being declared broken.
- The Americas regional pass added 910 FIBA-canonical rows across Centrobasket, COCABA, South American, and Caribbean competitions.
- The archived Baltic Challenge Cup yielded 28 complete games; incomplete archived pages became warnings rather than invented results.
- A South American archive exposed usable game cards but no match dates for some historic editions, so the system used the documented edition-start-date fallback and retained the warning.

The details are not edge cases. They are the work.

The ingestion catalog now contains more than thirty provider adapters. Each one has its own boundaries, source policy, parser behavior, and tests. The system knows that a “completed with warnings” import can be a success with a known source gap, while a zero-game result may indicate a parser failure, a genuinely empty archive, or an edition that was never played.

That distinction is the difference between a database that is full and a database that can be trusted.

## What took longer than expected—and what did not

The fastest work was the work with clear technical edges: scaffolding the services, creating the first backfill job, wiring health checks, adding basic rankings, and standing up the initial ELO calculation.

The slowest work was the work that looked like a naming problem:

- deciding whether two source teams are the same team;
- deciding whether two competitions are the same competition;
- deciding which source wins when both contain the same game;
- deciding when a missing date can be safely approximated;
- deciding whether a result is complete enough to affect ratings;
- deciding how to rerun a historical import without creating duplicates or erasing a correction.

We also underestimated the user interface work. A chart is easy to draw. A chart that remains useful when showing decades of ELO history is a different problem. We refined sampling, paging, zoom ranges, tooltips, inactive dates, labels, caching, and the relationship between a chart point and the game that caused it. The interface had to support public discovery and internal operations without turning either into a generic dashboard.

Conversely, some ambitious features arrived quickly once the underlying boundaries were solid. The Model Lab grew from a backtesting idea into saved models, scoped competitions, run history, metric breakdowns, quotas, entitlements, authentication, and run details. Once the system could describe a game, a competition, a team, a ruleset, and a result, experimentation had somewhere reliable to live.

## AI-native, in the literal sense

Basket ELO is an AI-native project.

The code was produced, changed, debugged, tested, documented, and iterated through conversations with AI. No line of code has been reviewed by a human.

That is not a claim that review is unnecessary. It is a statement about how this project was made—and a useful constraint on what the product must do. The system needs tests, explicit policies, source provenance, dry runs, health checks, audit reports, backups before reconciliation, and visible warnings because the development process itself does not provide the comfort of traditional line-by-line review.

In practice, the conversation became part of the engineering loop. A vague request became a schema. A failing import became a provider rule. A source mismatch became a reconciliation constraint. A UI annoyance became a chart interaction requirement. A dangerous rebuild became a scoped operation with a gate.

The AI was very good at producing the next working shape. The hard part was asking the next precise question.

## The launch version

From the initial scaffold on April 9 to the current launch snapshot, the repository records 174 commits across a six-project .NET solution. It contains public product surfaces for rankings, games, teams, charts, upcoming results, explanations, and model experiments, alongside the operational machinery required to keep the data alive.

At launch, Basket ELO includes:

- rankings with historical evolution and movers;
- team profiles with rating history and franchise continuity;
- game-level ELO explanations;
- separate rating pools for NBA, European clubs, and national teams;
- ruleset-versioned rebuilds;
- asynchronous historical backfills with retries, rate limits, dry runs, and failure isolation;
- current-result ingestion with review queues for ambiguous matches;
- source-aware reconciliation that only removes verified one-to-one duplicates;
- identity and coverage review tools;
- a public Model Lab with saved models and backtest runs;
- Docker and VPS deployment with service health checks.

The coverage is deliberately described as a snapshot, not a promise that every historical archive is complete. That may be the most important launch feature of all: the system can tell you what it knows, what it does not know, and why.

## A beginning, not a finished archive

Basketball history is too large, inconsistent, and alive to be “done.” Archives will change. Sources will disappear. New seasons will be played. Old identities will need better evidence. Models will need calibration.

That is why Basket ELO is built as a system for continued investigation rather than a one-time import. New sources can be added behind provider boundaries. Historical gaps can remain explicit until evidence improves. Ratings can be rebuilt under a new ruleset without silently replacing the old one. A refresh can update a game without creating a second copy of it.

The original promise was to show who is good.

The launch version tries to answer the harder questions too: good according to which games, which identities, which source, which rules, and which assumptions?

That is Basket ELO.
