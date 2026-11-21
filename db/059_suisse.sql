-- Suisse : Conseil national (CHNR, 200 sièges) et Conseil des États (CHSR, 46). Source OFFICIELLE : service OData des Services du Parlement (ws.parlament.ch/odata.svc).
-- Conditions d'utilisation (page « open data » du Parlement) : données non modifiées, source citée « Parlamentsdienste der Bundesversammlung, Bern » avec la date de téléchargement, pas de
-- présentation comme publication officielle ; décision de Thomas du 2026-11-21 : accès au téléchargement sans modification des données = conforme. Pays et chambres créés INACTIFS.
-- Langues de l'interface : fr (par défaut), de, it ; l'anglais n'est pas une langue suisse (choix de Thomas) : repli sur le français pour de/it tant que le texte n'est pas traduit.
BEGIN;

INSERT INTO pays (code, nom_fr, nom_en, nom_local, langue, fuseau, ordre, actif) VALUES
 ('CH', 'Suisse', 'Switzerland', 'Schweiz / Suisse / Svizzera', 'fr', 'Europe/Zurich', 60, false);
UPDATE pays SET prefixe_url = 'ch' WHERE code = 'CH';

INSERT INTO chambres (code, pays, nom_fr, nom_en, nom_local, type, nb_sieges, disposition, terme_vote_fr, terme_vote_en, source_import, ordre, actif) VALUES
 ('CHNR', 'CH', 'Conseil national', 'National Council', 'Nationalrat', 'basse', 200, 'hemicycle', 'vote', 'vote', 'suisse', 60, false),
 ('CHSR', 'CH', 'Conseil des États', 'Council of States', 'Ständerat', 'haute',  46, 'hemicycle', 'vote', 'vote', 'suisse', 61, false);

INSERT INTO import_taches (code, libelle, description, ordre, ponctuelle, pays) VALUES
 ('suisse', 'Suisse : votes du Conseil national et du Conseil des États',
  'Votes de l''Assemblée fédérale (législature en cours) d''après le service OData officiel du Parlement : bulletin de chaque conseiller, mandats datés ; totaux et résultat calculés, groupes déduits des bulletins. Lancer ensuite les statistiques.', 66, false, 'CH'),
 ('suisse_historique', 'Suisse : historique (49e à 51e législatures, 2011-2023)',
  'Même source, législatures 49 à 51. Tâche ponctuelle : à lancer une fois. Lancer ensuite les statistiques.', 67, true, 'CH');

INSERT INTO import_planning (tache, jours, heure, cree_par) VALUES
 ('suisse', ARRAY[1,2,3,4,5,6,7]::smallint[], TIME '04:30', 'migration 059');

COMMIT;
