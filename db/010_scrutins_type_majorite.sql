-- 010 : informations de vote affichées sur la page d'un scrutin.
--   type_vote_libelle   : « scrutin public ordinaire », « scrutin public solennel »…
--   type_majorite       : règle de majorité (AN : « Majorité absolue des suffrages exprimés »,
--                         « Majorité absolue des membres composant l'Assemblée »…). Le Sénat ne
--                         publie que le seuil chiffré.
--   suffrages_exprimes  : pour + contre (les abstentions et blancs ne comptent pas)
--   suffrages_requis    : nombre de voix nécessaires à l'adoption (le seuil de majorité)
BEGIN;

ALTER TABLE scrutins
    ADD COLUMN type_vote_libelle  text,
    ADD COLUMN type_majorite      text,
    ADD COLUMN suffrages_exprimes integer,
    ADD COLUMN suffrages_requis   integer;

COMMIT;
