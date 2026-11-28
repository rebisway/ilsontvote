-- Téléversement manuel d'un fichier d'import (/Admin/Imports) et pétitions de la Chambre des communes du Canada.
-- Pourquoi en base : le site n'exécute rien et n'a pas de dossier commun avec l'Importer. Le contenu est effacé dès que le planificateur l'a pris.

BEGIN;

ALTER TABLE import_taches ADD COLUMN IF NOT EXISTS accepte_fichier boolean NOT NULL DEFAULT false;

CREATE TABLE IF NOT EXISTS import_fichiers (
    id         bigserial PRIMARY KEY,
    nom        text        NOT NULL,
    taille     integer     NOT NULL,
    contenu    bytea,                      -- NULL après transmission à l'import
    ajoute_par text,
    ajoute_le  timestamptz NOT NULL DEFAULT now()
);

ALTER TABLE import_demandes ADD COLUMN IF NOT EXISTS fichier_id bigint REFERENCES import_fichiers (id);

INSERT INTO import_taches (code, libelle, description, ordre, ponctuelle, pays, accepte_fichier) VALUES
('petitions_ca', 'Canada : pétitions (fichier téléversé)',
 'Export XML téléchargé à la main sur noscommunes.ca (la source impose un reCAPTCHA). Export plafonné à 1 000 pétitions : téléverser plusieurs fichiers filtrés. Charger du plus ancien au plus récent.',
 75, true, 'CA', true)
ON CONFLICT (code) DO NOTHING;

INSERT INTO licences (code, producteur, types, chambres, taches, licence, url_conditions, attribution, usage_commercial, partage_identique, statut, verifie_le, remarque, ordre) VALUES
('ca-petitions', 'House of Commons of Canada petitions (ourcommons.ca)', '{petition}', '{CA}', '{petitions_ca}',
 NULL, 'https://www.ourcommons.ca/en/important-notices', 'Source: House of Commons of Canada (ourcommons.ca).', 'unknown', 'unknown', 'unverified', NULL,
 'Files downloaded by hand. Terms for petition data not read separately.', 135)
ON CONFLICT (code) DO NOTHING;

INSERT INTO traductions (langue, cle, texte, source_fr) VALUES
('fr', $t$petca-plateforme$t$, $t$Chambre des communes du Canada$t$, $t$Chambre des communes du Canada$t$),
('en', $t$petca-plateforme$t$, $t$House of Commons of Canada$t$, $t$Chambre des communes du Canada$t$),
('de', $t$petca-plateforme$t$, $t$Unterhaus von Kanada$t$, $t$Chambre des communes du Canada$t$),
('it', $t$petca-plateforme$t$, $t$Camera dei Comuni del Canada$t$, $t$Chambre des communes du Canada$t$),
('fr', $t$petca-chapo$t$, $t$Pétitions à la Chambre des communes du Canada, importées à partir de fichiers téléchargés à la main sur noscommunes.ca : la liste peut être incomplète et en retard sur la source. Les textes restent en français.$t$, $t$Pétitions à la Chambre des communes du Canada, importées à partir de fichiers téléchargés à la main sur noscommunes.ca : la liste peut être incomplète et en retard sur la source. Les textes restent en français.$t$),
('en', $t$petca-chapo$t$, $t$Petitions to the House of Commons of Canada, imported from files downloaded by hand from ourcommons.ca: the list may be incomplete and behind the source. Texts are in French.$t$, $t$Pétitions à la Chambre des communes du Canada, importées à partir de fichiers téléchargés à la main sur noscommunes.ca : la liste peut être incomplète et en retard sur la source. Les textes restent en français.$t$),
('de', $t$petca-chapo$t$, $t$Petitionen an das Unterhaus von Kanada, importiert aus manuell von noscommunes.ca heruntergeladenen Dateien: Die Liste kann unvollständig und veraltet sein. Die Texte sind französisch.$t$, $t$Pétitions à la Chambre des communes du Canada, importées à partir de fichiers téléchargés à la main sur noscommunes.ca : la liste peut être incomplète et en retard sur la source. Les textes restent en français.$t$),
('it', $t$petca-chapo$t$, $t$Petizioni alla Camera dei Comuni del Canada, importate da file scaricati manualmente da noscommunes.ca: l’elenco può essere incompleto e non aggiornato. I testi sono in francese.$t$, $t$Pétitions à la Chambre des communes du Canada, importées à partir de fichiers téléchargés à la main sur noscommunes.ca : la liste peut être incomplète et en retard sur la source. Les textes restent en français.$t$)
ON CONFLICT DO NOTHING;

COMMIT;
