-- Journal du site consultable dans /Admin/Journal : appels d'outils MCP, avertissements et erreurs de l'application (pas le journal du serveur).
-- Dates en UTC sans fuseau ; `niveau` = niveau .NET (2 information, 3 avertissement, 4 erreur, 5 critique). Lignes effacées après 14 jours.

BEGIN;

CREATE TABLE IF NOT EXISTS journal_site (
    id      bigserial PRIMARY KEY,
    le      timestamp without time zone NOT NULL,
    niveau  smallint NOT NULL,
    source  varchar(200) NOT NULL,
    message text NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_journal_site_le ON journal_site (le);

COMMIT;
