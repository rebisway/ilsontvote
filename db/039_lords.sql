-- Royaume-Uni, Chambre des Lords : divisions (votes « Content » / « Not content ») depuis décembre 2019 (58e et 59e législatures des Communes ; les Lords n'ont pas de législature propre).
-- Sources : lordsvotes-api.parliament.uk (divisions, membres ayant voté) et members-api.parliament.uk (dates d'entrée et de sortie des pairs : les mandats, donc les absents déduits).
-- Les Lords restent « inactifs » (données ouvertes) comme les Communes à leur arrivée ; ils s'affichent sur /uk dès que le pays GB est actif.
BEGIN;

INSERT INTO chambres (code, pays, nom_fr, nom_en, nom_local, type, nb_sieges, disposition, terme_vote_fr, terme_vote_en, source_import, ordre, actif) VALUES
 ('LORDS', 'GB', 'Chambre des Lords', 'House of Lords', 'House of Lords', 'haute', 830, 'face-a-face', 'division', 'division', 'lords', 21, false);

-- Parti au gouvernement par période (voir 037) : les conservateurs jusqu'au 4 juillet 2024 ; la période en cours est `groupes.gouvernement` (/Admin/Pays).
-- Le groupe est créé ici s'il n'existe pas encore (l'import le complète sans l'écraser).
INSERT INTO groupes (id, chambre, legislature, sigle, libelle, couleur, ordre)
VALUES ('LORDS-Con', 'LORDS', 58, 'Con', 'Conservative', '#0063ba', 90)
ON CONFLICT (id) DO NOTHING;

INSERT INTO chambre_gouvernements (chambre, groupe_id, date_debut, date_fin) VALUES
 ('LORDS', 'LORDS-Con', '2017-06-08', '2024-07-04');

INSERT INTO import_taches (code, libelle, description, ordre, ponctuelle, pays) VALUES
 ('lords', 'Royaume-Uni, Lords : divisions et votes',
  'Divisions de la Chambre des Lords (Content / Not content) et pairs en exercice (dates d''entrée et de sortie, parti), via lordsvotes-api.parliament.uk et members-api.parliament.uk (Open Parliament Licence). Seuls les pairs qui ont voté sont listés : les autres se déduisent des mandats (absent = absent). Les divisions de moins de 7 jours sont relues.', 55, false, 'GB'),
 ('lords_historique', 'Royaume-Uni, Lords : historique (58e législature, 2019-2024)',
  'Divisions des Lords du 17 décembre 2019 au 30 mai 2024. Tâche ponctuelle (~10 min) : à lancer une fois. Pour la 57e : commande « lords --legislature 57 ».', 54, true, 'GB');

INSERT INTO import_planning (tache, jours, heure, cree_par)
VALUES ('lords', ARRAY[1,2,3,4,5,6,7]::smallint[], TIME '04:35', 'migration 039');

COMMIT;
