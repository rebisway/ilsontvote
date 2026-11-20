-- Bilan de participation d'un élu sur TOUS ses mandats, précalculé : une ligne par élu (la fiche d'un député recalculait ce bilan en parcourant tous les scrutins de sa chambre
-- à chaque affichage : 1,7 s à froid pour l'Assemblée). Même définition que GetBilanAsync : scrutins publics tenus pendant ses mandats, hors décompte nominatif incomplet ;
-- EXISTS (et non JOIN) sur les mandats : un élu aux mandats qui se recouvrent ne compte pas un scrutin deux fois. Rafraîchie avec les statistiques (tâche « stats »).
BEGIN;

CREATE MATERIALIZED VIEW bilan_elu AS
SELECT p.id AS parlementaire_id,
       count(*)::int AS total,
       count(*) FILTER (WHERE v.position = 'pour')::int AS pour,
       count(*) FILTER (WHERE v.position = 'contre')::int AS contre,
       count(*) FILTER (WHERE v.position = 'abstention')::int AS abstention,
       count(*) FILTER (WHERE v.position = 'non_votant')::int AS non_votant,
       -- Absent = siégeait ce jour-là mais aucune ligne de vote : il n'est pas stocké, il se déduit.
       count(*) FILTER (WHERE v.parlementaire_id IS NULL)::int AS absent,
       count(*) FILTER (WHERE v.par_delegation)::int AS delegation,
       count(*) FILTER (WHERE v.rectifie)::int AS mise_au_point
  FROM scrutins s
  JOIN parlementaires p ON p.chambre = s.chambre
  LEFT JOIN votes v ON v.scrutin_id = s.id AND v.parlementaire_id = p.id
 WHERE NOT s.nominatif_incomplet
   AND EXISTS (SELECT 1 FROM mandats m
                WHERE m.parlementaire_id = p.id
                  AND (m.legislature IS NULL OR m.legislature = s.legislature)
                  AND m.date_debut <= s.date_scrutin
                  AND (m.date_fin IS NULL OR m.date_fin >= s.date_scrutin))
 GROUP BY p.id;

-- Index unique : requis pour REFRESH MATERIALIZED VIEW CONCURRENTLY (lecture sans blocage pendant le rafraîchissement) et pour la lecture par élu.
CREATE UNIQUE INDEX bilan_elu_pk ON bilan_elu (parlementaire_id);

COMMIT;
