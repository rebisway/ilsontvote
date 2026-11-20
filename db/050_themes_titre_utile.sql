-- Thèmes : ne classer que la partie UTILE du titre. Constaté sur la migration 048 : les titres d'amendements contiennent le nom de l'auteur, sa circonscription et son groupe
-- (« l'amendement n° 255 de Mme Martin (Alpes-Maritimes) à l'article 11 du projet de loi relatif à la protection des enfants… ») : « Alpes-Maritimes » rangeait l'amendement dans
-- « Transports », « Solidarité et Territoires » dans « Famille et solidarité ». On ne garde donc que la fin du titre, à partir du texte de loi cité (« du projet de loi… »,
-- « de la proposition de loi… ») ; sans telle mention, le titre entier. Approximation assumée (facilité de filtrage, pas une donnée officielle).
BEGIN;

CREATE OR REPLACE FUNCTION themes_de(t text) RETURNS text[]
    LANGUAGE sql STABLE
    AS $$
    SELECT COALESCE(array_agg(th.code ORDER BY th.ordre), '{}')
      FROM (SELECT recherche_norm(coalesce(substring(t FROM '(?i)((?:du projet de loi|de la proposition de loi|du projet|de la proposition|de la loi|du texte).*)$'), t, '')) AS utile) u,
           themes th
     WHERE u.utile ~* th.motifs
    $$;

UPDATE scrutins SET themes = themes_de(titre || ' ' || coalesce(titre_en, '')) WHERE themes IS DISTINCT FROM themes_de(titre || ' ' || coalesce(titre_en, ''));
UPDATE textes   SET themes = themes_de(titre || ' ' || coalesce(titre_en, '')) WHERE themes IS DISTINCT FROM themes_de(titre || ' ' || coalesce(titre_en, ''));

COMMIT;
