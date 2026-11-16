-- 004 : ordre d'hémicycle des groupes des législatures 15 et 16 (l'import ne
-- touche jamais à `ordre`, d'où cette migration pour les lignes déjà créées).

BEGIN;

UPDATE groupes SET ordre = CASE sigle
    WHEN 'LFI-NUPES' THEN 10
    WHEN 'SOC-A'     THEN 40
    WHEN 'EDS'       THEN 45
    WHEN 'UDI_I'     THEN 80
    WHEN 'UDI-A-I'   THEN 80
    WHEN 'UDI-AGIR'  THEN 80
    WHEN 'AGIR-E'    THEN 80
    ELSE ordre END
WHERE ordre = 100;

COMMIT;
