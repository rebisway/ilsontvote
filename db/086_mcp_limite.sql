-- Limite de débit du serveur MCP réglable dans /Admin/Mcp (dérogation temporaire, par exemple pendant l'examen d'un annuaire).
-- Une seule ligne. Sans dérogation en cours (fin_le NULL ou passée), la limite normale écrite dans le code s'applique.
-- `limite` NULL pendant la dérogation = aucune limite. `fin_le` en UTC sans fuseau, toujours renseignée : pas de dérogation sans échéance.

BEGIN;

CREATE TABLE IF NOT EXISTS mcp_reglage (
    id     smallint PRIMARY KEY DEFAULT 1 CHECK (id = 1),
    limite integer CHECK (limite BETWEEN 1 AND 10000),
    fin_le timestamp without time zone
);

INSERT INTO mcp_reglage (id) VALUES (1) ON CONFLICT DO NOTHING;

COMMIT;
