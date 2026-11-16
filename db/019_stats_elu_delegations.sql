-- 019 : la vue « par élu et par jour » compte aussi les votes PAR DÉLÉGATION et les MISES AU POINT.
--
-- Sert la page « Taux de présence des élus » (/elus) : participation, présence effective (vote exprimé sans délégation), délégations,
-- mises au point, absences. Une vue matérialisée ne se modifie pas : on la recrée (même nom, mêmes colonnes + 4 nouvelles), VIDE ;
-- elle est remplie par « Importer stats » à la fin de chaque import (cf. deploy/import-all.sh). Jusque-là, les pages qui la lisent sont vides.
-- L'ancien classement « plus / moins présents » (seuil arbitraire de 20 scrutins) est abandonné : la liste complète la remplace.

DROP MATERIALIZED VIEW IF EXISTS stats_participation_elu;

CREATE MATERIALIZED VIEW stats_participation_elu AS
SELECT p.id AS parlementaire_id, p.chambre, s.date_scrutin,
       -- Scrutins « à la charge » de l'élu ce jour-là : mandat en cours, hors « non votant » déclaré par l'assemblée (ex. président de séance).
       (count(*) FILTER (WHERE v.position IS DISTINCT FROM 'non_votant'))::int AS scrutins,
       count(v.position) FILTER (WHERE v.position IN ('pour', 'contre', 'abstention'))::int AS votants,
       -- Votes exprimés PAR DÉLÉGATION (un collègue a voté pour lui) : comptés dans « votants », retirés de la « présence effective ».
       count(v.position) FILTER (WHERE v.par_delegation AND v.position IN ('pour', 'contre', 'abstention'))::int AS delegations,
       -- Mises au point : position rectifiée après le scrutin (position finale = celle comptée ci-dessus).
       count(v.position) FILTER (WHERE v.rectifie)::int AS mises_au_point,
       -- Mêmes comptes limités aux votes sur l'ensemble d'un texte (option « votes sur l'ensemble seulement »).
       (count(*) FILTER (WHERE s.sur_ensemble AND v.position IS DISTINCT FROM 'non_votant'))::int AS scrutins_ensemble,
       count(v.position) FILTER (WHERE s.sur_ensemble AND v.position IN ('pour', 'contre', 'abstention'))::int AS votants_ensemble,
       count(v.position) FILTER (WHERE s.sur_ensemble AND v.par_delegation AND v.position IN ('pour', 'contre', 'abstention'))::int AS delegations_ensemble,
       count(v.position) FILTER (WHERE s.sur_ensemble AND v.rectifie)::int AS mises_au_point_ensemble
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
