-- 015 : titres en anglais (interface /en).
--
-- `titre_en` : titre affiché dans l'interface anglaise. Deux origines, jamais mélangées sans le dire :
--   - Parlement européen : le titre anglais de HowTheyVote (donnée source, `scrutins.titre_en` rempli à l'import UE) ;
--   - lois et scrutins français : TRADUCTION AUTOMATIQUE du titre français (OPUS-MT, voir docs/TRADUCTION.md), repérée par
--     `titre_en_auto = true` : le site l'étiquette « traduction automatique » et montre le titre officiel en infobulle.
-- `traductions_titres` : une ligne par titre français distinct déjà traduit (pas de retraduction à chaque import ; le même titre
-- sert à plusieurs scrutins).

BEGIN;

ALTER TABLE textes   ADD COLUMN titre_en text, ADD COLUMN titre_en_auto boolean NOT NULL DEFAULT false;
ALTER TABLE scrutins ADD COLUMN titre_en text, ADD COLUMN titre_en_auto boolean NOT NULL DEFAULT false;

CREATE TABLE traductions_titres (
    fr          text PRIMARY KEY,                       -- titre français tel que stocké (textes.titre / scrutins.titre)
    en          text NOT NULL,
    modele      text NOT NULL,                          -- ex. opus-mt-fr-en
    traduit_le  timestamptz NOT NULL DEFAULT now()
);

COMMIT;
