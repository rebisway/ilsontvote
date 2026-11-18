-- Pays et chambres : préparation de l'intégration d'autres parlements (Royaume-Uni, États-Unis, Espagne, Italie…).
-- Jusqu'ici « chambre » est un simple texte (AN | SENAT | UE) répété dans plusieurs tables. Ces deux tables en font un référentiel : noms en plusieurs
-- langues, rattachement à un pays, nombre de sièges, disposition de l'hémicycle, mode de vote. Aucun affichage ne change avec cette migration :
-- elle décrit les trois chambres existantes et verrouille les valeurs de « chambre » par des clés étrangères.
--
-- Les libellés d'INTERFACE restent dans le code (L.T fr/en) : seules les données (noms de pays et de chambres) portent leurs traductions ici.
-- petitions.chambre et petitions_releves.chambre désignent une PLATEFORME de pétitions (AN | SENAT), pas une chambre de vote : ils ne sont pas liés.
BEGIN;

CREATE TABLE pays (
    code        text PRIMARY KEY,                 -- ISO 3166-1 alpha-2 (FR, GB, US, ES, IT…) ; « EU » pour l'Union européenne (supranational, pas un pays)
    nom_fr      text NOT NULL,
    nom_en      text NOT NULL,
    nom_local   text NOT NULL,                    -- nom dans la langue du pays
    langue      text NOT NULL,                    -- langue des titres de la source (fr, en, es, it…) : choisit le sens de la traduction automatique
    fuseau      text NOT NULL,                    -- fuseau IANA des heures de séance (Europe/Paris, Europe/London, America/New_York…)
    ordre       integer NOT NULL DEFAULT 100,     -- ordre d'affichage
    actif       boolean NOT NULL DEFAULT true     -- faux = préparé mais pas encore publié
);

CREATE TABLE chambres (
    code            text PRIMARY KEY,             -- code stocké dans scrutins, parlementaires, groupes, texte_etapes (AN, SENAT, UE, puis COMMONS, LORDS…)
    pays            text NOT NULL REFERENCES pays (code),
    nom_fr          text NOT NULL,
    nom_en          text NOT NULL,
    nom_local       text NOT NULL,
    type            text NOT NULL CHECK (type IN ('basse', 'haute', 'supranationale')),
    nb_sieges       integer,                      -- NULL = variable ou inconnu (Lords : nombre de pairs non fixe)
    disposition     text NOT NULL DEFAULT 'hemicycle' CHECK (disposition IN ('hemicycle', 'face-a-face')),   -- schéma de l'hémicycle SVG
    mode_vote       text NOT NULL DEFAULT 'individuel' CHECK (mode_vote IN ('individuel', 'groupe')),       -- groupe : vote enregistré par groupe, pas par élu (Pays-Bas)
    terme_vote_fr   text NOT NULL DEFAULT 'scrutin',   -- « scrutin », « division », « vote par appel nominal »…
    terme_vote_en   text NOT NULL DEFAULT 'vote',
    source_import   text,                         -- code de la tâche d'import de cette chambre (planificateur)
    ordre           integer NOT NULL DEFAULT 100,
    actif           boolean NOT NULL DEFAULT true
);

INSERT INTO pays (code, nom_fr, nom_en, nom_local, langue, fuseau, ordre) VALUES
 ('FR', 'France',           'France',          'France',          'fr', 'Europe/Paris',    10),
 ('EU', 'Union européenne', 'European Union',  'European Union',  'en', 'Europe/Brussels', 90);

INSERT INTO chambres (code, pays, nom_fr, nom_en, nom_local, type, nb_sieges, terme_vote_fr, terme_vote_en, source_import, ordre) VALUES
 ('AN',    'FR', 'Assemblée nationale', 'National Assembly',   'Assemblée nationale', 'basse',          577, 'scrutin', 'vote', 'an',     10),
 ('SENAT', 'FR', 'Sénat',               'Senate',              'Sénat',               'haute',          348, 'scrutin', 'vote', 'senat',  20),
 ('UE',    'EU', 'Parlement européen',  'European Parliament', 'European Parliament', 'supranationale', 720, 'vote',    'vote', 'ue',     90);

-- Les valeurs existantes (AN, SENAT, UE) sont toutes présentes ci-dessus : les clés étrangères s'appliquent sans modifier de données.
-- Une nouvelle chambre doit d'abord être décrite dans « chambres » (sinon l'import est refusé : c'est voulu, pas de code de chambre inventé).
ALTER TABLE scrutins      ADD CONSTRAINT fk_scrutins_chambre      FOREIGN KEY (chambre) REFERENCES chambres (code);
ALTER TABLE parlementaires ADD CONSTRAINT fk_parlementaires_chambre FOREIGN KEY (chambre) REFERENCES chambres (code);
ALTER TABLE groupes       ADD CONSTRAINT fk_groupes_chambre       FOREIGN KEY (chambre) REFERENCES chambres (code);
ALTER TABLE texte_etapes  ADD CONSTRAINT fk_texte_etapes_chambre  FOREIGN KEY (chambre) REFERENCES chambres (code);

COMMIT;
