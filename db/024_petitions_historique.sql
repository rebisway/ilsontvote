-- Historique des pétitions du Sénat : un relevé (date, soutiens, statut) par pétition et par import, en AJOUT SEUL (jamais écrasé ni supprimé).
-- Pourquoi : la table « petitions » garde seulement le dernier état. Sans historique, une baisse du nombre de soutiens ou la disparition d'une
-- pétition du fichier source passerait inaperçue. Les anomalies sont des constats entre deux relevés, sans cause connue.
CREATE TABLE IF NOT EXISTS petitions_historique (
    petition_id text    NOT NULL REFERENCES petitions(id),
    releve_le   date    NOT NULL,   -- date du fichier source (archives data.gouv.fr : date dans le nom du fichier ; import courant : jour de l'import)
    nb_votes    integer NOT NULL,
    statut      text    NOT NULL,
    PRIMARY KEY (petition_id, releve_le)
);

-- Constats entre deux relevés consécutifs :
--   baisse       : le nombre de soutiens est inférieur à celui du relevé précédent ;
--   disparition  : la pétition figurait au relevé précédent et n'est plus dans le fichier ;
--   reapparition : la pétition absente du relevé précédent est de nouveau dans le fichier.
CREATE TABLE IF NOT EXISTS petitions_anomalies (
    id               bigserial PRIMARY KEY,
    petition_id      text    NOT NULL REFERENCES petitions(id),
    type             text    NOT NULL CHECK (type IN ('baisse', 'disparition', 'reapparition')),
    detecte_le       date    NOT NULL,   -- relevé où le constat est fait
    releve_precedent date    NOT NULL,
    valeur_avant     integer NOT NULL,   -- soutiens au relevé précédent (reapparition : dernier nombre connu avant l'absence)
    valeur_apres     integer,            -- soutiens au relevé de détection ; NULL pour une disparition
    UNIQUE (petition_id, type, detecte_le)   -- rejouer un import ne duplique rien
);
CREATE INDEX IF NOT EXISTS petitions_anomalies_date_idx ON petitions_anomalies (detecte_le DESC);

-- Date du premier relevé où la pétition manque au fichier (NULL = présente au dernier relevé).
ALTER TABLE petitions ADD COLUMN IF NOT EXISTS absente_depuis date;
