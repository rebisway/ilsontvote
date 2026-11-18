-- Groupe « au gouvernement » : sert aux chambres dont l'hémicycle est en bancs face à face (Chambre des Communes : le gouvernement siège d'un côté de l'allée,
-- tous les autres de l'autre). Faux partout par défaut. À mettre à jour après chaque changement de majorité (nouvelle législature).
BEGIN;

ALTER TABLE groupes ADD COLUMN gouvernement boolean NOT NULL DEFAULT false;

-- Communes, 59e législature (depuis juillet 2024) : le Parti travailliste forme le gouvernement.
UPDATE groupes SET gouvernement = true WHERE id = 'COMMONS-Lab';

COMMIT;
