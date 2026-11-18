-- Pays d'une tâche d'import : sert à trier la page /Admin/Imports par onglets (France, Royaume-Uni, Union européenne, Commun).
-- NULL = tâche commune à tous les pays (traductions, statistiques, export), exécutée après les imports.
BEGIN;

ALTER TABLE import_taches ADD COLUMN pays text REFERENCES pays (code);

UPDATE import_taches SET pays = 'FR'
 WHERE code IN ('an', 'an_historique', 'senat', 'textes', 'dossiers', 'dossiers_historique', 'amendements', 'amendements_historique', 'petitions', 'petitions_archives');
UPDATE import_taches SET pays = 'EU' WHERE code = 'ue';
UPDATE import_taches SET pays = 'GB' WHERE code = 'communes';

COMMIT;
