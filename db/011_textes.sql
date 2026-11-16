-- 011 : cycle de vie des textes de loi (source : base DOSLEG du Sénat, table `loi`).
-- Un texte = un dossier législatif ; plusieurs scrutins (AN, Sénat, CMP, nouvelle lecture…)
-- s'y rattachent. Les champs de promulgation, de publication au JO et du Conseil
-- constitutionnel viennent tels quels de la source ; le STATUT est calculé à l'affichage.
BEGIN;

CREATE TABLE textes (
    id                  text PRIMARY KEY,         -- DOSLEG-{loicod}
    source              text NOT NULL DEFAULT 'DOSLEG',
    type_code           text,
    type_libelle        text,                     -- « projet de loi », « proposition de loi organique »…
    titre               text NOT NULL,            -- type + intitulé
    titre_normalise     text NOT NULL,            -- clé de rapprochement avec les scrutins (type + intitulé)
    intitule_normalise  text NOT NULL,            -- idem, intitulé seul
    etat_code           text,                     -- 01 en discussion, 02 fusionné, 03 rejeté, 04 promulgué/adopté, 05 caduc, 06 retiré
    etat_libelle        text,
    numero_loi          text,                     -- ex. 2026-1015
    date_promulgation   date,
    date_jo             date,
    numero_jo           text,
    url_legifrance      text,
    url_dossier_senat   text,
    url_dossier_an      text,
    saisine_cc_date     date,
    saisine_cc_par      text,
    decision_cc_numero  text,
    decision_cc_date    date,
    decision_cc_code    text,                     -- 01 conforme, 02 partiellement conforme, 03 non conforme, 04 incompétent
    decision_cc_libelle text,
    decision_cc_url     text
);
CREATE INDEX ix_textes_titre ON textes (titre_normalise);
CREATE INDEX ix_textes_intitule ON textes (intitule_normalise);
CREATE INDEX ix_textes_promulgation ON textes (date_promulgation DESC);

ALTER TABLE scrutins ADD COLUMN texte_id text REFERENCES textes (id) ON DELETE SET NULL;
CREATE INDEX ix_scrutins_texte ON scrutins (texte_id) WHERE texte_id IS NOT NULL;

COMMIT;
