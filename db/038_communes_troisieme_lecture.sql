-- Communes : « vote sur l'ensemble » britannique = division de « Third Reading » (calculé par le site d'après le titre, comme `CommonsSource.EstTroisiemeLecture`).
-- Rattrape les divisions déjà importées ; l'import marque les nouvelles. Sert à l'accueil, au filtre par défaut des listes et aux statistiques du site britannique.
BEGIN;

UPDATE scrutins SET sur_ensemble = (titre ~* '\mthird reading\s*$') WHERE chambre = 'COMMONS';

COMMIT;
