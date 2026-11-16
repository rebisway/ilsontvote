-- 002 : ordre d'affichage des groupes dans l'hémicycle (gauche -> droite).
-- Éditorial / géométrique, jamais écrasé par l'import. Les groupes inconnus
-- (nouveaux, ou autres législatures) tombent en fin avec la valeur par défaut.

BEGIN;

ALTER TABLE groupes ADD COLUMN ordre smallint NOT NULL DEFAULT 100;

UPDATE groupes SET ordre = CASE sigle
    WHEN 'LFI-NFP' THEN 10
    WHEN 'GDR'     THEN 20
    WHEN 'ECOS'    THEN 30
    WHEN 'SOC'     THEN 40
    WHEN 'LIOT'    THEN 50
    WHEN 'DEM'     THEN 60
    WHEN 'EPR'     THEN 70
    WHEN 'HOR'     THEN 80
    WHEN 'DR'      THEN 90
    WHEN 'UDR'     THEN 110
    WHEN 'AD'      THEN 115
    WHEN 'RN'      THEN 120
    WHEN 'NI'      THEN 130
    ELSE ordre END;

COMMIT;
