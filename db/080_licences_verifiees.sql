-- Licences confirmées le 2026-11-27 (complète la migration 078, non réécrite). Seules les lignes encore « unverified » sont modifiées :
-- une correction faite à la main en base est conservée.

BEGIN;

-- Parlement britannique : la page de la licence refuse les accès automatiques (403) ; vérification faite par l'éditeur du site.
UPDATE licences SET statut = 'verified', usage_commercial = 'yes', partage_identique = 'no', verifie_le = '2026-11-27',
       remarque = 'Terms checked by the publisher of this site (the licence page refuses automated access).'
 WHERE code = 'gb-parliament' AND statut = 'unverified';

-- Pétitions britanniques (OGL v3.0) et françaises (Licence Ouverte 2.0, jeux data.gouv.fr) : licences relevées et vérifiées par l'éditeur.
UPDATE licences SET statut = 'verified', usage_commercial = 'yes', partage_identique = 'no', verifie_le = '2026-11-27',
       remarque = 'Terms checked by the publisher of this site.'
 WHERE code = 'gb-petitions' AND statut = 'unverified';

UPDATE licences SET statut = 'verified', usage_commercial = 'yes', partage_identique = 'no', verifie_le = '2026-11-27',
       licence = 'Licence Ouverte / Open Licence 2.0 (Etalab)', remarque = 'Datasets published on data.gouv.fr; terms checked by the publisher of this site.'
 WHERE code = 'fr-petitions' AND statut = 'unverified';

-- Parlement suisse : conditions lues, aucune licence nommée ; l'usage commercial n'est pas traité.
UPDATE licences SET statut = 'no_named_licence', verifie_le = '2026-11-27',
       url_conditions = 'https://www.parlament.ch/fr/%C3%BCber-das-parlament/faits-donnees-chifrees/open-data-web-services',
       attribution = 'Source: Services du Parlement de l''Assemblée fédérale, Berne, with the download date.',
       remarque = 'Data must stay unchanged and not be presented as an official publication; commercial use is not addressed. Totals and results are computed by this site. The general legal notice of parlament.ch asks for prior written permission to reproduce.'
 WHERE code = 'ch-parliament' AND statut = 'unverified';

COMMIT;
