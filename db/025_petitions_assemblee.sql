-- Pétitions de l'Assemblée nationale (petitions.assemblee-nationale.fr, jeu data.gouv.fr « Pétitions de l'Assemblée Nationale », Licence Ouverte 2.0)
-- dans les mêmes tables que celles du Sénat. Les identifiants AN sont préfixés « AN- » (les numéros des deux plateformes se recoupent).

ALTER TABLE petitions ADD COLUMN IF NOT EXISTS chambre            text NOT NULL DEFAULT 'SENAT';   -- SENAT | AN
ALTER TABLE petitions ADD COLUMN IF NOT EXISTS commission         text;   -- AN : commission permanente saisie
ALTER TABLE petitions ADD COLUMN IF NOT EXISTS decision_commission text;  -- AN : décision publiée de la commission (texte de la source)
ALTER TABLE petitions ADD COLUMN IF NOT EXISTS legislature        text;   -- AN : « 2017-2022 », « 2022-2024 » ; NULL = non renseignée par la source

-- Donnée manquante ≠ zéro : 22 pétitions de l'Assemblée n'ont pas de nombre de soutiens dans le fichier ; le type n'existe qu'au Sénat.
ALTER TABLE petitions ALTER COLUMN nb_votes DROP NOT NULL;
ALTER TABLE petitions ALTER COLUMN nb_votes DROP DEFAULT;
ALTER TABLE petitions ALTER COLUMN type DROP NOT NULL;
ALTER TABLE petitions_historique ALTER COLUMN nb_votes DROP NOT NULL;
ALTER TABLE petitions_anomalies ALTER COLUMN valeur_avant DROP NOT NULL;

-- Relevés traités, par source. Sert (1) à ne pas écrire un relevé quand le fichier n'a pas changé depuis le précédent (la source se met à jour
-- chaque lundi, l'import tourne chaque nuit) et (2) à connaître le dernier relevé de CHAQUE source (absence d'une pétition).
CREATE TABLE IF NOT EXISTS petitions_releves (
    chambre       text    NOT NULL,
    releve_le     date    NOT NULL,
    sha256        text,              -- empreinte du fichier ; NULL pour les relevés antérieurs à cette table
    nb_petitions  integer NOT NULL,
    PRIMARY KEY (chambre, releve_le)
);
INSERT INTO petitions_releves (chambre, releve_le, nb_petitions)
SELECT 'SENAT', releve_le, count(*) FROM petitions_historique GROUP BY releve_le
ON CONFLICT DO NOTHING;

CREATE INDEX IF NOT EXISTS petitions_chambre_idx ON petitions (chambre);
