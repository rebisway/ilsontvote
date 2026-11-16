-- 017 : scrutins LIÉS à un texte sans en être le vote sur l'ensemble (amendements, sous-amendements, articles, motions).
--
-- `scrutins.texte_id` reste réservé au vote sur l'ENSEMBLE du texte : toute la fiche d'une loi (statut, « adopté à l'Assemblée »,
-- nombre de votes sur l'ensemble) en dépend. Les autres votes sont rattachés ici, par le titre du texte cité dans leur intitulé
-- (« l'amendement n° 12 … à l'article 7 de la proposition de loi visant à … »). Rattachement CALCULÉ par nous (niveau « déduit »),
-- laissé vide en cas d'ambiguïté (plusieurs textes de même titre) : jamais de choix au hasard.

ALTER TABLE scrutins ADD COLUMN texte_lie_id text REFERENCES textes(id) ON DELETE SET NULL;
CREATE INDEX ix_scrutins_texte_lie ON scrutins (texte_lie_id) WHERE texte_lie_id IS NOT NULL;
