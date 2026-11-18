-- Communes : la tâche n'est plus un prototype ponctuel, elle entre au planning.
--  - chaque nuit à 04:30, avec les autres imports (dans l'ordre des dépendances : AVANT « Statistiques de participation » et l'export) ;
--  - du mardi au vendredi à 00:30 (heure de Paris) : les Communes siègent du lundi au jeudi et lèvent la séance entre 22 h et minuit à Londres, soit après
--    minuit à Paris ; ce créneau reprend les divisions du soir même.
-- Planifier l'import ne publie rien : le pays reste invisible tant qu'il n'est pas activé dans /Admin/Pays.
BEGIN;

UPDATE import_taches
   SET ponctuelle = false,
       libelle = 'Royaume-Uni, Communes : divisions et votes',
       description = 'Divisions (votes par appel nominal) de la Chambre des Communes depuis juillet 2024, via l''API publique commonsvotes-api.parliament.uk (Open Parliament Licence). Chaque division liste tous les membres : pour, contre, ou sans vote enregistré (jamais stocké : absent = absent). Les divisions de moins de 7 jours sont relues (l''API corrige après publication).'
 WHERE code = 'communes';

INSERT INTO import_planning (tache, jours, heure, cree_par) VALUES
 ('communes', ARRAY[1,2,3,4,5,6,7]::smallint[], TIME '04:30', 'migration 034'),
 ('communes', ARRAY[2,3,4,5]::smallint[],       TIME '00:30', 'migration 034');

COMMIT;
