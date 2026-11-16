-- 003 : rattache chaque groupe à sa législature (les identifiants PO… sont
-- propres à une législature). Permet de rejouer l'import d'une législature
-- sans effacer les appartenances des autres.

BEGIN;

ALTER TABLE groupes ADD COLUMN legislature smallint;
UPDATE groupes SET legislature = 17 WHERE legislature IS NULL;
ALTER TABLE groupes ALTER COLUMN legislature SET NOT NULL;

CREATE INDEX ix_groupes_legislature ON groupes (legislature);

COMMIT;
