# ilsontvote.org MCP server

Version française : [MCP.fr.md](MCP.fr.md).

Lets an AI assistant query the site's data (Model Context Protocol).

- URL: `https://www.ilsontvote.org/mcp` (local: `http://localhost:5310/mcp`)
- Transport: stateless streamable HTTP. Read-only, no key.
- Rate limit: 60 requests per minute per IP address (429 beyond that).
- Scope: France, the European Union and the **active** countries, as on the website.

## Connecting a client

Add `https://www.ilsontvote.org/mcp` as a remote HTTP MCP server in any compatible client, without a key.

## Tools

The API (tool names, parameters, fields, messages) is in English. Titles, group names and notes stay in the language of
their source.

| Tool | Purpose |
|---|---|
| `list_types` | Document types, filters, countries, chambers, themes |
| `search` | Paginated search within one type (50 results at most) |
| `get_document` | Full record of one document |
| `vote_positions` | Breakdown by group and each member's position (200 per page at most) |
| `member_history` | A member's votes, absences included |
| `import_status` | Last run and last success of each data import (no error details) |
| `data_quality` | Known gaps per chamber (latest vote, incomplete roll calls, terms, groups) |

Vote positions are `for`, `against`, `abstention`, `non_voting` and `absent`. Theme codes are French identifiers
(`sante`…); `list_types` gives their English names.

## Sources and licences

Every `search`, `get_document`, `vote_positions` and `member_history` answer carries a `sources` block for the data it
returns (`list_types` lists them all): producer, licence, terms URL, attribution, `commercial_use` and `share_alike`
(`yes`, `no`, `unknown`), `status` (`verified`, `no_named_licence`, `unverified`), licence verification date (`licence_verified_on`, not the date of the data) and last successful
import. They live in the `licences` table (migration 078): update the row when terms are checked.

## Document types

| Type | Content | Countries |
|---|---|---|
| `parliamentary_vote` | Recorded parliamentary votes | all active countries |
| `popular_vote` | Federal popular votes (aggregate results) | Switzerland |
| `law` | Bills and laws with their progress | France |
| `bill` | Bills | United Kingdom |
| `petition` | Parliamentary petitions | France, United Kingdom |
| `member` | Members of the assemblies | all active countries |

## Deployment

The server is part of the web application (`src/IlsOntVote.Web/Mcp/`): deploying the site deploys it. No extra
service, port, nginx rule or database migration.

## Adding a document type

1. Write a class implementing `ITypeDocument` (`src/IlsOntVote.Web/Mcp/Documents.cs`): code, name, description
   **including the limits of the data**, filters, countries, search and record.
2. Register it in `McpServeur.AjouterMcp`.

Rules: stay within the published scope (`Perimetre`); SQL parameters only; missing data stays `null`; anything
computed or inferred is named or annotated as such; every document carries its `url`.

## Operations

Tool calls are counted in `/Admin/Stats` (table `mcp_appels`, migration 077; hashed IP only, kept 400 days).

Each tool call writes a `MCP : outil …` line to the log (no parameters, no IP address):

```
journalctl -u ilsontvote | grep "MCP : outil"
```

## Publishing to the official registry

`server.json` (repository root) describes the server. Install `mcp-publisher` (see the registry quickstart), then, in Git Bash at the repository root, create the key and the DNS record:

```bash
openssl genpkey -algorithm Ed25519 -out key.pem
echo "ilsontvote.org. IN TXT \"v=MCPv1; k=ed25519; p=$(openssl pkey -in key.pem -pubout -outform DER | tail -c 32 | base64)\""
```

Add that TXT record at the apex of `ilsontvote.org`, wait for propagation, then publish:

```bash
PRIVATE_KEY="$(openssl pkey -in key.pem -noout -text | grep -A3 "priv:" | tail -n +2 | tr -d ' :\n')"
./mcp-publisher.exe login dns --domain ilsontvote.org --private-key "$PRIVATE_KEY"
./mcp-publisher.exe publish
```

Keep `key.pem` out of the repository. Raise `version` in `server.json` before publishing again. The registry is in preview: check its documentation (modelcontextprotocol.io/registry).
