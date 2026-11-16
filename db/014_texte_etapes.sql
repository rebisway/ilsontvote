-- 014 : étapes de lecture d'un texte à l'Assemblée nationale, y compris celles SANS scrutin public (vote à main levée, adoption sans
-- votes individuels). Source : jeu « Dossiers législatifs » de l'AN (acte « Décision » d'une lecture en séance publique).
--
-- Un acte « Décision » qui porte un `voteRef` renvoie à un scrutin public (détail « qui a voté quoi » disponible) ; sans `voteRef`, la
-- décision a été prise sans scrutin public : le site l'affiche quand même, avec la mention « détail non disponible » (règle du projet :
-- les votes à main levée ne sont jamais omis faute de nominatif).
--
-- `texte_id` est NULL tant que le dossier n'est pas rattaché à un texte (rattachement : adresse du dossier AN du texte, ou scrutin
-- public cité par un acte du même dossier, ou dossier du Sénat cité par le dossier AN). Rempli à l'import.

BEGIN;

CREATE TABLE texte_etapes (
    id                  text PRIMARY KEY,                 -- identifiant de l'acte AN, ex. L17-VD229940DEC
    dossier_an          text NOT NULL,                    -- identifiant du dossier AN, ex. DLR5L17N50450
    senat_chemin        text,                             -- identifiant du dossier au Sénat cité par le dossier AN (ex. pjl24-123) : clé de rattachement
    texte_id            text REFERENCES textes (id) ON DELETE SET NULL,
    code_acte           text NOT NULL,                    -- AN1-DEBATS-DEC (1re lecture), AN2-…, ANLUNI-… (lecture unique), ANNLEC-…, ANLDEF-…
    date_acte           date NOT NULL,
    resultat            text,                             -- libellé de la source : adopté, rejetée, modifiée, adopté sans modification…
    scrutin_id          text REFERENCES scrutins (id) ON DELETE SET NULL,
    sans_scrutin_public boolean NOT NULL                  -- true = aucun scrutin public lié : main levée ou adoption sans vote nominatif
);
CREATE INDEX ix_texte_etapes_texte ON texte_etapes (texte_id, date_acte);
CREATE INDEX ix_texte_etapes_dossier ON texte_etapes (dossier_an);

COMMIT;
