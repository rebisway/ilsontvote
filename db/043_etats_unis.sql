-- États-Unis : Chambre des représentants (USHOUSE) et Sénat (USSENATE).
-- Sources : Chambre = fichiers XML OFFICIELS du greffe (clerk.house.gov, un par vote) ; Sénat = Voteview (UCLA / Université de Géorgie, fichiers CSV par Congrès : senate.gov refuse les robots) ;
-- noms des élus = Voteview ; mandats déduits des votes (comme le Parlement européen et les Communes). Interface anglaise, préfixe d'adresse /us.
-- Pays et chambres sont créés INACTIFS (activation dans /Admin/Pays) ; l'import se planifie sans rien publier.
BEGIN;

INSERT INTO pays (code, nom_fr, nom_en, nom_local, langue, fuseau, ordre, actif) VALUES
 ('US', 'États-Unis', 'United States', 'United States', 'en', 'America/New_York', 30, false);
UPDATE pays SET prefixe_url = 'us' WHERE code = 'US';

INSERT INTO chambres (code, pays, nom_fr, nom_en, nom_local, type, nb_sieges, disposition, terme_vote_fr, terme_vote_en, source_import, ordre, actif) VALUES
 ('USHOUSE',  'US', 'Chambre des représentants', 'House of Representatives', 'House of Representatives', 'basse', 435, 'hemicycle', 'vote nominal', 'roll call vote', 'us', 30, false),
 ('USSENATE', 'US', 'Sénat des États-Unis',      'United States Senate',     'United States Senate',     'haute', 100, 'hemicycle', 'vote nominal', 'roll call vote', 'us', 31, false);

INSERT INTO import_taches (code, libelle, description, ordre, ponctuelle, pays) VALUES
 ('us', 'États-Unis : votes nominaux de la Chambre et du Sénat',
  'Votes nominaux du Congrès en cours (119e) : Chambre des représentants d''après les fichiers XML OFFICIELS du greffe (clerk.house.gov, un par vote, lus depuis le dernier vote connu) ; Sénat d''après Voteview (voteview.com, fichiers CSV par Congrès : senate.gov refuse les robots) ; noms des élus : Voteview. Les mandats se déduisent des votes (un élu figure dans la liste de chaque vote tant qu''il siège, y compris « not voting »). Lancer ensuite les statistiques.', 57, false, 'US'),
 ('us_historique', 'États-Unis : historique (107e à 118e Congrès, 2001-2024)',
  'Même source, Congrès 107 à 118 (3 janvier 2001 → 3 janvier 2025). Tâche ponctuelle et longue (une dizaine de millions de votes individuels) : à lancer une fois, reprise possible. Lancer ensuite les statistiques.', 56, true, 'US');

INSERT INTO import_planning (tache, jours, heure, cree_par)
VALUES ('us', ARRAY[1,2,3,4,5,6,7]::smallint[], TIME '04:30', 'migration 043');

COMMIT;
