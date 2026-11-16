-- 021 : recherche insensible aux accents, aux tirets et aux apostrophes (listes de lois, de scrutins, d'élus).
--
-- « etat d'urgence » doit trouver « état d'urgence » et « lot et garonne » « Lot-et-Garonne ». L'extension `unaccent` est livrée avec PostgreSQL
-- (« trusted » : le propriétaire de la base peut la créer sans être superutilisateur). La fonction centralise la normalisation : le site
-- l'applique à la colonne ET, côté C#, au texte saisi (RechercheSql.Mots) ; changer la règle = changer cette fonction seulement.

CREATE EXTENSION IF NOT EXISTS unaccent;

-- STABLE (et non IMMUTABLE) : unaccent() dépend du dictionnaire courant ; sans incidence ici (pas d'index sur ce calcul).
CREATE OR REPLACE FUNCTION recherche_norm(t text) RETURNS text
    LANGUAGE sql STABLE
    AS $$ SELECT unaccent(translate(t, '-''’', '   ')) $$;
