-- Italie : Chambre des députés (ITCAMERA). Source OFFICIELLE : point SPARQL de la Chambre (dati.camera.it/sparql, licence CC BY-SA 4.0).
-- Par défaut seulement les votes FINAUX et les questions de CONFIANCE (~480 votations pour la XIXe législature, sur ~20 800 : amendements, ordres du jour…) ; l'option --tout importe le reste.
-- Mandats déduits des votes (comme les États-Unis et les Communes). Interface anglaise (pas encore de traduction italienne), préfixe d'adresse /it.
-- Pays et chambre sont créés INACTIFS (activation dans /Admin/Pays) ; l'import se planifie sans rien publier. Sénat italien : source non vérifiée, pas encore décrit.
BEGIN;

INSERT INTO pays (code, nom_fr, nom_en, nom_local, langue, fuseau, ordre, actif) VALUES
 ('IT', 'Italie', 'Italy', 'Italia', 'en', 'Europe/Rome', 40, false);
UPDATE pays SET prefixe_url = 'it' WHERE code = 'IT';

INSERT INTO chambres (code, pays, nom_fr, nom_en, nom_local, type, nb_sieges, disposition, terme_vote_fr, terme_vote_en, source_import, ordre, actif) VALUES
 ('ITCAMERA', 'IT', 'Chambre des députés', 'Chamber of Deputies', 'Camera dei deputati', 'basse', 400, 'hemicycle', 'vote nominal', 'roll call vote', 'italie', 40, false);

INSERT INTO import_taches (code, libelle, description, ordre, ponctuelle, pays) VALUES
 ('italie', 'Italie : votes de la Chambre des députés',
  'Votes nominaux de la Chambre des députés d''Italie (XIXe législature) d''après le point SPARQL officiel de la Chambre (dati.camera.it) : votes FINAUX et questions de CONFIANCE, avec le vote de chaque député. Au scrutin secret, la source ne publie que les totaux. Les mandats se déduisent des votes. Lancer ensuite les statistiques.', 58, false, 'IT'),
 ('italie_historique', 'Italie : historique (XVIIe et XVIIIe législatures, 2013-2022)',
  'Même source, législatures 17 et 18 (15 mars 2013 → 12 octobre 2022). Tâche ponctuelle : à lancer une fois. Lancer ensuite les statistiques.', 59, true, 'IT');

INSERT INTO import_planning (tache, jours, heure, cree_par)
VALUES ('italie', ARRAY[1,2,3,4,5,6,7]::smallint[], TIME '04:30', 'migration 044');

COMMIT;
