-- Royaume-Uni, Chambre des Communes : PROTOTYPE d'import (API publique des divisions, 59e législature depuis le 9 juillet 2024).
-- Pays et chambre sont créés INACTIFS : le site n'a pas encore de pages pour eux (hémicycle en bancs face à face, vocabulaire « division », titres en anglais).
-- Tant qu'ils sont inactifs, la tâche « communes » reste hors planning (ponctuelle) : l'importer en production ferait apparaître des divisions sur /textes.
BEGIN;

INSERT INTO pays (code, nom_fr, nom_en, nom_local, langue, fuseau, ordre, actif) VALUES
 ('GB', 'Royaume-Uni', 'United Kingdom', 'United Kingdom', 'en', 'Europe/London', 20, false);

INSERT INTO chambres (code, pays, nom_fr, nom_en, nom_local, type, nb_sieges, disposition, terme_vote_fr, terme_vote_en, source_import, ordre, actif) VALUES
 ('COMMONS', 'GB', 'Chambre des Communes', 'House of Commons', 'House of Commons', 'basse', 650, 'face-a-face', 'division', 'division', 'communes', 20, false);

INSERT INTO import_taches (code, libelle, description, ordre, ponctuelle) VALUES
 ('communes', 'Royaume-Uni, Communes : divisions et votes (prototype)',
  'Divisions (votes par appel nominal) de la Chambre des Communes depuis juillet 2024, via l''API publique commonsvotes-api.parliament.uk (Open Parliament Licence). Chaque division liste tous les membres : pour, contre, ou sans vote enregistré (jamais stocké : absent = absent). Les divisions de moins de 7 jours sont relues (l''API corrige après publication). PROTOTYPE : ne pas lancer en production tant que le site n''affiche pas cette chambre.', 55, true);

COMMIT;
