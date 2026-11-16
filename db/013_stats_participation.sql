-- 013 : statistiques de participation (présence) par scrutin et par groupe.
--
-- Pourquoi une vue matérialisée : les absents ne sont PAS stockés (règle « absent = absent », ils se déduisent des mandats).
-- Calculer une participation = joindre, pour chaque scrutin, tous les mandats en cours à sa date (≈ 14 millions de lignes au total) :
-- trop lourd pour une page web. On le calcule donc une fois par import (REFRESH MATERIALIZED VIEW CONCURRENTLY, cf. deploy/import-all.sh)
-- et les pages lisent le résultat.
--
-- Définitions (à garder alignées avec la page « Méthodologie ») :
--   effectif = élus ayant un mandat en cours à la date du scrutin (même règle que les absents déduits : mandat de la législature du
--              scrutin pour l'AN et l'UE ; dates seules pour le Sénat, dont `legislature` est NULL),
--   votants  = parmi eux, ceux dont la position est pour, contre ou abstention,
--   Les élus que l'assemblée DÉCLARE « non votants » pour ce scrutin (ex. président de séance : il ne vote pas par fonction) sont
--   retirés de l'effectif : les compter absents serait faux (fait publié par la source, pas un jugement du site).
--   groupe   = groupe de l'élu à la date du scrutin (NULL = sans groupe : non-inscrit).
-- Les scrutins au décompte nominatif incomplet sont exclus : leurs « absents » ne sont pas fiables (règle du projet).

CREATE MATERIALIZED VIEW stats_participation_groupe AS
WITH sieges AS (
    SELECT s.id AS scrutin_id, s.chambre, s.date_scrutin, p.id AS parlementaire_id, ga.groupe_id
      FROM scrutins s
      JOIN parlementaires p ON p.chambre = s.chambre
      JOIN mandats m ON m.parlementaire_id = p.id
                    AND (m.legislature IS NULL OR m.legislature = s.legislature)
                    AND m.date_debut <= s.date_scrutin
                    AND (m.date_fin IS NULL OR m.date_fin >= s.date_scrutin)
      LEFT JOIN LATERAL (
            SELECT a.groupe_id FROM groupe_appartenances a
             WHERE a.parlementaire_id = p.id AND a.date_debut <= s.date_scrutin
               AND (a.date_fin IS NULL OR a.date_fin >= s.date_scrutin)
             ORDER BY a.date_debut DESC LIMIT 1) ga ON true
     WHERE NOT s.nominatif_incomplet
)
SELECT si.scrutin_id, si.chambre, si.date_scrutin,
       COALESCE(si.groupe_id, '') AS groupe_id,                      -- '' = sans groupe (clé unique : NULL ne s'indexe pas en unique)
       (count(DISTINCT si.parlementaire_id) FILTER (WHERE v.position IS DISTINCT FROM 'non_votant'))::int AS effectif,
       count(v.position) FILTER (WHERE v.position IN ('pour', 'contre', 'abstention'))::int AS votants,
       count(v.position) FILTER (WHERE v.position = 'pour')::int                         AS pour,
       count(v.position) FILTER (WHERE v.position = 'contre')::int                       AS contre,
       count(v.position) FILTER (WHERE v.position = 'abstention')::int                   AS abstention
  FROM sieges si
  LEFT JOIN votes v ON v.scrutin_id = si.scrutin_id AND v.parlementaire_id = si.parlementaire_id
 GROUP BY si.scrutin_id, si.chambre, si.date_scrutin, COALESCE(si.groupe_id, '')
  WITH NO DATA;   -- vide à la création (le calcul prend plusieurs minutes) : rempli par « Importer stats » à la fin de chaque import

-- Index unique : exigé par REFRESH ... CONCURRENTLY (les pages continuent de lire pendant le recalcul).
CREATE UNIQUE INDEX ux_stats_participation_groupe ON stats_participation_groupe (scrutin_id, groupe_id);
CREATE INDEX ix_stats_participation_chambre_date ON stats_participation_groupe (chambre, date_scrutin);

-- Participation PAR ÉLU et PAR JOUR de scrutin : sert au classement des élus les plus / moins présents, sur n'importe quelle période
-- (somme des jours choisis : exact, sans arrondi au mois). Même définition de l'effectif et des votants que ci-dessus ;
-- `*_ensemble` = mêmes comptes limités aux votes sur l'ensemble d'un texte (option « votes sur l'ensemble seulement »).
-- Une ligne par élu et par jour où il avait au moins un scrutin à sa charge : ~ quelques centaines de milliers de lignes.
CREATE MATERIALIZED VIEW stats_participation_elu AS
SELECT p.id AS parlementaire_id, p.chambre, s.date_scrutin,
       (count(*) FILTER (WHERE v.position IS DISTINCT FROM 'non_votant'))::int AS scrutins,   -- hors « non votant » déclaré
       count(v.position) FILTER (WHERE v.position IN ('pour', 'contre', 'abstention'))::int AS votants,
       (count(*) FILTER (WHERE s.sur_ensemble AND v.position IS DISTINCT FROM 'non_votant'))::int AS scrutins_ensemble,
       count(v.position) FILTER (WHERE s.sur_ensemble AND v.position IN ('pour', 'contre', 'abstention'))::int AS votants_ensemble
  FROM scrutins s
  JOIN parlementaires p ON p.chambre = s.chambre
  JOIN mandats m ON m.parlementaire_id = p.id
                AND (m.legislature IS NULL OR m.legislature = s.legislature)
                AND m.date_debut <= s.date_scrutin
                AND (m.date_fin IS NULL OR m.date_fin >= s.date_scrutin)
  LEFT JOIN votes v ON v.scrutin_id = s.id AND v.parlementaire_id = p.id
 WHERE NOT s.nominatif_incomplet
 GROUP BY p.id, p.chambre, s.date_scrutin
  WITH NO DATA;

CREATE UNIQUE INDEX ux_stats_participation_elu ON stats_participation_elu (parlementaire_id, date_scrutin);
CREATE INDEX ix_stats_participation_elu_chambre_date ON stats_participation_elu (chambre, date_scrutin);
