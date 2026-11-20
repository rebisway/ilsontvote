-- Italie : Sénat de la République (ITSENATE). Source OFFICIELLE : point SPARQL du Sénat (dati.senato.it/sparql). Conditions de réutilisation : à confirmer avant publication.
-- Contrairement à la Chambre, la source donne les mandats (début, fin) et les groupes DATÉS : rien n'est déduit des votes. Par défaut seulement les votes FINAUX (« Votazione finale »).
-- Chambre créée INACTIVE (activation avec le pays dans /Admin/Pays).
BEGIN;

INSERT INTO chambres (code, pays, nom_fr, nom_en, nom_local, type, nb_sieges, disposition, terme_vote_fr, terme_vote_en, source_import, ordre, actif) VALUES
 ('ITSENATE', 'IT', 'Sénat de la République', 'Senate of the Republic', 'Senato della Repubblica', 'haute', 206, 'hemicycle', 'vote nominal', 'roll call vote', 'italie_senat', 41, false);

INSERT INTO import_taches (code, libelle, description, ordre, ponctuelle, pays) VALUES
 ('italie_senat', 'Italie : votes finaux du Sénat',
  'Votes finaux du Sénat d''Italie (XIXe législature) d''après le point SPARQL officiel du Sénat (dati.senato.it) : totaux et résultat officiels, vote de chaque sénateur, mandats et groupes datés fournis par la source. Lancer ensuite les statistiques.', 60, false, 'IT'),
 ('italie_senat_historique', 'Italie : Sénat, historique (XVIIe et XVIIIe législatures, 2013-2022)',
  'Même source, législatures 17 et 18. Tâche ponctuelle : à lancer une fois. Lancer ensuite les statistiques.', 61, true, 'IT');

INSERT INTO import_planning (tache, jours, heure, cree_par)
VALUES ('italie_senat', ARRAY[1,2,3,4,5,6,7]::smallint[], TIME '04:30', 'migration 045');

COMMIT;
