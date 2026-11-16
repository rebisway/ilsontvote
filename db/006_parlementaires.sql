-- 006 : le schéma accueille désormais les deux chambres (AN + Sénat).
--   deputes          -> parlementaires   (colonne chambre : AN | SENAT)
--   depute_mandats   -> mandats
--   depute_groupes   -> groupe_appartenances
--   *.depute_id      -> *.parlementaire_id
-- `legislature` devient facultative pour les groupes et mandats (le Sénat n'a
-- pas de législature). Pour `scrutins`, `legislature` = législature (AN) ou
-- ANNÉE DE SESSION (Sénat), car elle fait partie de la clé (chambre, legislature, numero).

BEGIN;

ALTER TABLE deputes RENAME TO parlementaires;
ALTER TABLE depute_mandats RENAME TO mandats;
ALTER TABLE depute_groupes RENAME TO groupe_appartenances;

ALTER TABLE mandats RENAME COLUMN depute_id TO parlementaire_id;
ALTER TABLE groupe_appartenances RENAME COLUMN depute_id TO parlementaire_id;
ALTER TABLE votes RENAME COLUMN depute_id TO parlementaire_id;

ALTER INDEX ix_depute_mandats_dates RENAME TO ix_mandats_dates;
ALTER INDEX ix_depute_groupes_depute RENAME TO ix_groupe_appartenances_parlementaire;
ALTER INDEX ix_votes_depute RENAME TO ix_votes_parlementaire;

ALTER TABLE groupes ALTER COLUMN legislature DROP NOT NULL;
ALTER TABLE mandats ALTER COLUMN legislature DROP NOT NULL;

-- Sénat : une « mise au point » est signalée par un astérisque, sans position d'origine.
ALTER TABLE votes ADD COLUMN origine_inconnue boolean NOT NULL DEFAULT false;

-- Note officielle de bas de page d'un scrutin (Sénat).
ALTER TABLE scrutins ADD COLUMN note text;

CREATE INDEX ix_parlementaires_chambre ON parlementaires (chambre);
CREATE INDEX ix_scrutins_chambre_date ON scrutins (chambre, date_scrutin DESC);

COMMIT;
