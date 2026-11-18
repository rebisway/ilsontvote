-- Préfixe d'adresse des pays : /uk/… pour le Royaume-Uni (le préfixe est le nom ANGLAIS usuel, pas le code ISO « GB »).
-- NULL = pays servi à la racine du site (la France). Un pays n'a une adresse propre que s'il est actif ET a un préfixe.
BEGIN;

ALTER TABLE pays ADD COLUMN prefixe_url text UNIQUE
    CHECK (prefixe_url IS NULL OR prefixe_url ~ '^[a-z]{2,10}$');   -- minuscules, un seul segment ; jamais « en », « fr », « admin »… (voir ci-dessous)

-- Mots réservés : langue (en), administration, comptes, fichiers techniques : un préfixe de pays ne doit pas les masquer.
ALTER TABLE pays ADD CONSTRAINT ck_pays_prefixe_reserve
    CHECK (prefixe_url IS NULL OR prefixe_url NOT IN ('en', 'fr', 'admin', 'identity', 'apercu', 'lois', 'textes', 'scrutin', 'deputes', 'senateurs', 'eurodeputes', 'petitions'));

UPDATE pays SET prefixe_url = 'uk' WHERE code = 'GB';

COMMIT;
