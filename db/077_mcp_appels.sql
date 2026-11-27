-- Usage du serveur MCP (/Admin/Stats) : une ligne par appel d'outil. Vie privée, comme page_views : ni paramètres, ni IP, ni User-Agent ;
-- `ip_hash` = empreinte HMAC mensuelle (clients distincts). Purgée avec les visites (400 jours).

BEGIN;

CREATE TABLE IF NOT EXISTS mcp_appels (
    id        bigserial PRIMARY KEY,
    outil     varchar(40) NOT NULL,
    ip_hash   varchar(64) NOT NULL,
    appele_le timestamp without time zone NOT NULL DEFAULT (now() AT TIME ZONE 'utc')
);

CREATE INDEX IF NOT EXISTS idx_mcp_appels_date ON mcp_appels (appele_le);

COMMIT;
