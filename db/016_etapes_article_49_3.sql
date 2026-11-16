-- 016 : lois adoptées SANS VOTE (article 49, alinéa 3 de la Constitution) et motions de censure liées à un texte.
--
-- Quand le Gouvernement engage sa responsabilité sur un texte, celui-ci est « considéré comme adopté » sauf si une motion de censure est
-- votée : il n'y a AUCUN vote sur le texte lui-même (ce n'est pas une main levée : il n'y a pas de vote du tout). Les motions de censure,
-- elles, sont votées par scrutin public (le détail « qui a voté quoi » existe) : on les rattache au texte.
-- `article_49_3` est calculé à l'import d'après le libellé de la décision (« … en application de l'article 49, alinéa 3 »).

ALTER TABLE texte_etapes ADD COLUMN article_49_3 boolean NOT NULL DEFAULT false;
