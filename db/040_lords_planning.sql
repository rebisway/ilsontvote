-- Lords : l'import passe au même créneau que les autres (04:30).
-- Cause du bug constaté : « lords » était planifié à 04:35, APRÈS « stats » (04:30, ordre 80) : la vue des taux de présence (stats_participation_elu) était donc recalculée avant l'arrivée des
-- Lords du jour, et vide pour eux jusqu'au lendemain. Dans un même créneau le planificateur exécute les tâches dans l'ordre des dépendances (lords 55 < stats 80 < export 90).
BEGIN;

DELETE FROM import_planning WHERE tache = 'lords' AND heure = TIME '04:35';
INSERT INTO import_planning (tache, jours, heure, cree_par)
VALUES ('lords', ARRAY[1,2,3,4,5,6,7]::smallint[], TIME '04:30', 'migration 040');

-- Un import lancé À LA MAIN (historique) ne recalcule pas les statistiques : le dire dans la description, là où on le lance.
UPDATE import_taches SET description = description || ' Lancer ensuite la tâche « Statistiques de participation » (sinon les taux de présence n''incluent pas ces divisions avant le prochain passage de nuit).'
 WHERE code IN ('lords_historique', 'communes_historique') AND description NOT LIKE '%Lancer ensuite%';

COMMIT;
