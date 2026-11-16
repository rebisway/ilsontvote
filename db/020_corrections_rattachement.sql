-- 020 : corrections MANUELLES du rattachement d'un scrutin à un texte de loi.
--
-- Le rattachement automatique (par titre) peut se tromper ou rester muet (titres identiques, formulations atypiques). Une correction
-- saisie dans l'administration PRIME sur le calcul et est rejouée à chaque import : sans elle, le prochain import l'écraserait.
--   texte_id renseigné : le scrutin est rattaché à ce texte (vote sur l'ensemble -> scrutins.texte_id ; autre vote -> scrutins.texte_lie_id) ;
--   texte_id NULL      : le scrutin est volontairement NON rattaché (le calcul automatique se trompait).
-- Le motif est obligatoire : une correction humaine doit rester explicable (niveau « corrigé à la main », distinct du calculé et de l'officiel).

CREATE TABLE corrections_rattachement (
    scrutin_id text PRIMARY KEY REFERENCES scrutins(id) ON DELETE CASCADE,
    texte_id   text REFERENCES textes(id) ON DELETE CASCADE,
    motif      text NOT NULL,
    auteur     text,
    cree_le    timestamptz NOT NULL DEFAULT now()
);
