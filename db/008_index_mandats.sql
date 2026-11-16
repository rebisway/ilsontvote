-- 008 : la requête « qui siégeait à la date du scrutin » filtrait `mandats` sans index
-- sur le parlementaire (28 s sur un scrutin du Sénat). Index dédié.
BEGIN;
CREATE INDEX ix_mandats_parlementaire ON mandats (parlementaire_id, date_debut);
ANALYZE mandats;
COMMIT;
