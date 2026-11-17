-- Pétitions déposées sur petitions.senat.fr (jeu « Pétitions du Sénat », data.gouv.fr, Licence Ouverte 2.0).
-- Ce sont des soutiens de citoyens (nb_votes), pas des votes d'élus. Le statut est celui publié par la source, jamais recalculé.
CREATE TABLE IF NOT EXISTS petitions (
    id               text PRIMARY KEY,   -- « identifiant » de la source (numéro de l'initiative)
    titre            text NOT NULL,
    description      text,
    date_publication date,
    nb_votes         integer NOT NULL DEFAULT 0,
    statut           text NOT NULL,      -- « Seuil non atteint », « Ouverte », « Seuil atteint » (libellés de la source)
    date_limite_vote date,               -- NULL : non publiée par la source
    type             text NOT NULL,      -- « Texte législatif » ou « Mission de contrôle »
    url              text NOT NULL
);
CREATE INDEX IF NOT EXISTS petitions_date_idx ON petitions (date_publication DESC);
