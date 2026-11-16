-- 018 : titres FRANÇAIS traduits automatiquement (anglais -> français) pour les votes du Parlement européen.
--
-- Les titres français OFFICIELS (portail du Parlement européen) restent la règle ; ceux qui n'en ont pas (objections, résolutions
-- sans procédure...) étaient affichés en anglais en français. On les traduit à l'import (OPUS-MT en-fr) et on le SIGNALE :
-- `titre_fr_auto` = le `titre` affiché est une traduction automatique, l'original anglais reste dans `titre_en`.
-- `traductions_titres_fr` : mémoire des traductions (clé = titre anglais) -> chaque titre n'est traduit qu'une seule fois, et le
-- ré-import quotidien du vote (qui remet le titre anglais de la source) le retraduit instantanément depuis cette mémoire.

ALTER TABLE scrutins ADD COLUMN titre_fr_auto boolean NOT NULL DEFAULT false;

CREATE TABLE traductions_titres_fr (
    en         text PRIMARY KEY,
    fr         text NOT NULL,
    modele     text NOT NULL,
    traduit_le timestamptz NOT NULL DEFAULT now()
);
