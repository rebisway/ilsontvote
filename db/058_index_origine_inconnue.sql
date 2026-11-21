-- Tableau de bord qualité : « mises au point du Sénat sans position d'origine » comptait les votes avec origine_inconnue par un parcours complet de la table votes (11 M de lignes, 1,7 s).
-- Index PARTIEL : ne contient que ces quelques lignes (le drapeau est vrai pour une infime minorité), donc quasi gratuit en écriture et en place.
CREATE INDEX IF NOT EXISTS ix_votes_origine_inconnue ON votes (scrutin_id) WHERE origine_inconnue;
