# Ils ont voté

**Who voted what?** See how every French deputy, senator and MEP voted on each law: a hemicycle with one dot per elected official (for, against, abstention, absent), and what became of each text afterwards.

Live site: <https://www.ilsontvote.org> · Version française : [README.fr.md](README.fr.md)

## Features

- **Hemicycle per vote**: one dot per elected official, coloured by vote or by political group.
- **Three assemblies**: National Assembly (since 2017), Senate (since 2006), European Parliament (current term).
- **Law life cycle**: votes in each chamber, promulgation, Official Journal publication, Constitutional Council decision.
- **Per-elected-official pages**: voting history and participation.
- **"Absent" means absent**: no reason is shown, the site does not judge.
- **Doubt and missing data are always visible**: incomplete counts, computed values and third-party sources are flagged (see the [methodology page](https://www.ilsontvote.org/methodologie)).
- Sortable lists, CSV/JSON export, responsive layout, admin area with statistics.

Only **roll-call votes** record each member's vote; show-of-hands votes appear without the detail of who voted what, and the site says so.

## Data sources

| Assembly | Source |
|---|---|
| National Assembly | [data.assemblee-nationale.fr](https://data.assemblee-nationale.fr/) (open data) |
| Senate | [data.senat.fr](https://data.senat.fr/) (open data) |
| European Parliament | [HowTheyVote.eu](https://howtheyvote.eu/), data under the [Open Database License](https://opendatacommons.org/licenses/odbl/1-0/) (ODbL) |

HowTheyVote.eu data stays under the ODbL: attribute the source and share derived databases under the same licence.
Photos and vote summaries are not covered by their licence and are not used.

## Stack

ASP.NET Core 10 Razor Pages · Dapper + Npgsql (all data access, plain SQL) · PostgreSQL · ASP.NET Identity (EF Core, authentication only, no migrations: tables are created by SQL) · a console importer run nightly by a systemd timer.

## Run locally

Requirements: .NET 10 SDK, PostgreSQL.

```bash
# 1. Database (adapt names/port to your setup)
createdb ilsontvote
for f in db/0*.sql; do psql -d ilsontvote -v ON_ERROR_STOP=1 -f "$f"; done

# 2. Settings (never commit them: appsettings.Development.json is git-ignored)
cp src/IlsOntVote.Web/appsettings.Development.example.json src/IlsOntVote.Web/appsettings.Development.json
cp src/IlsOntVote.Importer/appsettings.Development.example.json src/IlsOntVote.Importer/appsettings.Development.json
# edit both: connection string, admin e-mail and password (12+ characters, with a symbol)

# 3. Import data (long the first time)
cd src/IlsOntVote.Importer
DOTNET_ENVIRONMENT=Development dotnet run -- an --legislature all
DOTNET_ENVIRONMENT=Development dotnet run -- senat
DOTNET_ENVIRONMENT=Development dotnet run -- textes
DOTNET_ENVIRONMENT=Development dotnet run -- ue            # incremental; --limit N to test

# 4. Website
cd ../IlsOntVote.Web && dotnet run
```

## Deployment

Ubuntu, nginx reverse proxy, systemd service and import timer. Scripts are in [`deploy/`](deploy/); the procedure (in French) is in [DEPLOYMENT_SITE.fr.md](DEPLOYMENT_SITE.fr.md). Secrets live only in `appsettings.Local.json` on the server, never in the repository.

## Project rules

- Any schema change is a **new numbered migration** in `db/`; an applied migration is never rewritten.
- One colour set for votes everywhere (for = blue, against = red, abstention = orange, non-voting = grey, absent = light grey).
- Public text is in French; code comments explain the *why* (in French).

## Contributing

Issues and pull requests are welcome, especially data-quality reports (with a link to the official source) and new data sources.

## Licence

[GNU GPL v3.0](LICENSE) for the code. Data belongs to its publishers and keeps their licences (see above).
