-- 005 : certains scrutins (surtout 15e législature) ont un décompte nominatif
-- plus court que le total officiel (ex. un groupe absent de la ventilation).
-- Les députés manquants apparaîtraient à tort « absents » : on marque ces
-- scrutins pour pouvoir le signaler à l'écran et les exclure de la une.

BEGIN;

ALTER TABLE scrutins ADD COLUMN nominatif_incomplet boolean NOT NULL DEFAULT false;
ALTER TABLE scrutins ADD COLUMN nb_votes_manquants integer NOT NULL DEFAULT 0;

COMMIT;
