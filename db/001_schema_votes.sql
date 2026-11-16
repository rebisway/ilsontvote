-- 001 : socle des données parlementaires (Assemblée nationale d'abord).
-- Base cible : ilsontvote (PostgreSQL). Les tables Identity vivent dans une
-- migration séparée (002) — aucune donnée métier dedans.
--
-- Conventions : snake_case, clés textuelles = identifiants officiels de la
-- source (PA…/PO…/VTANR…), pour que l'import soit idempotent (UPSERT).

BEGIN;

-- Groupes politiques (organes de type GP).
CREATE TABLE groupes (
    id            text PRIMARY KEY,            -- ex. PO845401
    chambre       text NOT NULL DEFAULT 'AN',  -- AN | SENAT | UE
    sigle         text NOT NULL,               -- ex. RN
    libelle       text NOT NULL,
    couleur       text                          -- #RRGGBB, affichage hémicycle
);

CREATE TABLE deputes (
    id            text PRIMARY KEY,            -- ex. PA841621
    chambre       text NOT NULL DEFAULT 'AN',
    civilite      text,
    prenom        text NOT NULL,
    nom           text NOT NULL,
    slug          text NOT NULL UNIQUE,        -- /depute/jean-dupont
    departement   text,
    circonscription text,
    photo_url     text
);

-- Période pendant laquelle la personne siège (un suppléant a ses propres
-- dates). Sert à calculer les absents : « siégeait ce jour-là, n'a pas voté ».
CREATE TABLE depute_mandats (
    id            bigserial PRIMARY KEY,
    depute_id     text NOT NULL REFERENCES deputes(id),
    legislature   smallint NOT NULL,
    date_debut    date NOT NULL,
    date_fin      date                         -- NULL = en cours
);
CREATE INDEX ix_depute_mandats_dates ON depute_mandats (legislature, date_debut, date_fin);

-- Appartenance à un groupe, avec dates (un député peut changer de groupe).
CREATE TABLE depute_groupes (
    id            bigserial PRIMARY KEY,
    depute_id     text NOT NULL REFERENCES deputes(id),
    groupe_id     text NOT NULL REFERENCES groupes(id),
    date_debut    date NOT NULL,
    date_fin      date
);
CREATE INDEX ix_depute_groupes_depute ON depute_groupes (depute_id, date_debut);

CREATE TABLE scrutins (
    id              text PRIMARY KEY,          -- ex. VTANR5L17V100
    chambre         text NOT NULL DEFAULT 'AN',
    legislature     smallint NOT NULL,
    numero          integer NOT NULL,
    date_scrutin    date NOT NULL,
    titre           text NOT NULL,
    type_vote       text,                      -- SPO (ordinaire) / SPS (solennel)
    sort            text,                      -- adopté / rejeté
    dossier_ref     text,                      -- dossier législatif (NULL pour beaucoup d'amendements)
    sur_ensemble    boolean NOT NULL DEFAULT false, -- vote sur l'ensemble d'un texte
    nb_votants      integer,
    nb_pour         integer,
    nb_contre       integer,
    nb_abstentions  integer,
    nb_non_votants  integer,
    mode_publication text,                     -- DecompteNominatif, …
    featured        boolean NOT NULL DEFAULT false,  -- « à la une » (éditorial, via /Admin)
    featured_rank   integer,
    featured_title  text,                      -- titre court lisible (surcharge de `titre`)
    UNIQUE (chambre, legislature, numero)
);
CREATE INDEX ix_scrutins_date ON scrutins (date_scrutin DESC);
CREATE INDEX ix_scrutins_featured ON scrutins (featured_rank) WHERE featured;

-- Un vote enregistré. Les ABSENTS ne sont PAS stockés : ils se déduisent
-- (siégeait à la date du scrutin, absent de cette table). Un vote rectifié
-- (« mise au point ») garde la position d'origine.
CREATE TABLE votes (
    scrutin_id        text NOT NULL REFERENCES scrutins(id) ON DELETE CASCADE,
    depute_id         text NOT NULL REFERENCES deputes(id),
    groupe_id         text REFERENCES groupes(id),
    position          text NOT NULL CHECK (position IN ('pour','contre','abstention','non_votant')),
    par_delegation    boolean NOT NULL DEFAULT false,
    rectifie          boolean NOT NULL DEFAULT false,
    position_origine  text CHECK (position_origine IN ('pour','contre','abstention','non_votant')),
    PRIMARY KEY (scrutin_id, depute_id)
);
CREATE INDEX ix_votes_depute ON votes (depute_id);

-- Journal des imports (affiché dans /Admin).
CREATE TABLE import_runs (
    id            bigserial PRIMARY KEY,
    source        text NOT NULL,               -- AN | SENAT | UE
    started_at    timestamptz NOT NULL DEFAULT now(),
    finished_at   timestamptz,
    ok            boolean,
    scrutins      integer,
    votes         integer,
    message       text
);

COMMIT;
