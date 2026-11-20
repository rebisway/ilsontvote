-- Irlande : Dáil Éireann (IEDAIL) et Seanad Éireann (IESEANAD). Source OFFICIELLE : API de l'Oireachtas (api.oireachtas.ie/v1, JSON), licence CC BY 4.0
-- (« Oireachtas (Open Data) PSI Licence », lue le 2026-11-20 sur oireachtas.ie/en/open-data/license/ : attribution seule, pas de partage à l'identique).
-- Chaque division porte ses listes nominatives Tá / Níl / Staon ; mandats et partis DATÉS fournis par l'annuaire des membres (absents déduits des mandats).
-- Pays et chambres créés INACTIFS (activation dans /Admin/Pays ou deploy/activer-pays.sql). Interface anglaise, préfixe d'adresse /ie.
BEGIN;

INSERT INTO pays (code, nom_fr, nom_en, nom_local, langue, fuseau, ordre, actif) VALUES
 ('IE', 'Irlande', 'Ireland', 'Éire', 'en', 'Europe/Dublin', 50, false);
UPDATE pays SET prefixe_url = 'ie' WHERE code = 'IE';

INSERT INTO chambres (code, pays, nom_fr, nom_en, nom_local, type, nb_sieges, disposition, terme_vote_fr, terme_vote_en, source_import, ordre, actif) VALUES
 ('IEDAIL',   'IE', 'Dáil Éireann',   'Dáil Éireann',   'Dáil Éireann',   'basse', 174, 'hemicycle', 'division', 'division', 'irlande',        50, false),
 ('IESEANAD', 'IE', 'Seanad Éireann', 'Seanad Éireann', 'Seanad Éireann', 'haute',  60, 'hemicycle', 'division', 'division', 'irlande_seanad', 51, false);

INSERT INTO import_taches (code, libelle, description, ordre, ponctuelle, pays) VALUES
 ('irlande', 'Irlande : divisions du Dáil',
  'Divisions (votes nominaux) du Dáil Éireann, mandature en cours, d''après l''API officielle de l''Oireachtas : totaux et résultat officiels, vote de chaque député (Tá, Níl, Staon), mandats et partis datés. Lancer ensuite les statistiques.', 62, false, 'IE'),
 ('irlande_seanad', 'Irlande : divisions du Seanad',
  'Divisions du Seanad Éireann, mandature en cours, même source. Lancer ensuite les statistiques.', 63, false, 'IE'),
 ('irlande_historique', 'Irlande : Dáil, historique (31e à 33e mandatures, 2011-2024)',
  'Même source, mandatures 31 à 33 du Dáil. Tâche ponctuelle : à lancer une fois. Lancer ensuite les statistiques.', 64, true, 'IE'),
 ('irlande_seanad_historique', 'Irlande : Seanad, historique (24e à 26e mandatures, 2011-2025)',
  'Même source, mandatures 24 à 26 du Seanad. Tâche ponctuelle : à lancer une fois. Lancer ensuite les statistiques.', 65, true, 'IE');

INSERT INTO import_planning (tache, jours, heure, cree_par) VALUES
 ('irlande',        ARRAY[1,2,3,4,5,6,7]::smallint[], TIME '04:30', 'migration 047'),
 ('irlande_seanad', ARRAY[1,2,3,4,5,6,7]::smallint[], TIME '04:30', 'migration 047');

COMMIT;
