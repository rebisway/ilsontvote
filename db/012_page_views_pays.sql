-- 012 : pays de chaque page vue (code ISO sur 2 lettres), déduit de l'IP avec la base locale GeoLite2 (aucun appel externe).
-- NULL = inconnu (base absente ou adresse non trouvée). L'IP elle-même n'est jamais stockée.
ALTER TABLE page_views ADD COLUMN IF NOT EXISTS country_code varchar(2);
