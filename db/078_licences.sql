-- Licences par lot de données, renvoyées par l'API MCP (bloc « sources »). Un lot = une source, pour des types de documents et des chambres
-- (ou plateformes de pétitions) donnés.
-- Règle du doute : `statut` = 'verified' (licence nommée, lue), 'no_named_licence' (conditions lues, aucune licence nommée) ou
-- 'unverified' (conditions NON lues : jamais supposées libres). `usage_commercial` / `partage_identique` : 'yes', 'no' ou 'unknown'.
-- Textes en anglais : ils sont destinés à l'API, pas aux pages (table `traductions`).
-- Les corrections faites en base survivent à une nouvelle exécution (ON CONFLICT DO NOTHING).

BEGIN;

CREATE TABLE IF NOT EXISTS licences (
    code              text PRIMARY KEY,
    producteur        text NOT NULL,
    types             text[] NOT NULL,          -- types de documents du MCP
    chambres          text[] NOT NULL,          -- chambres, plateformes de pétitions, ou « CH » pour les votations
    taches            text[] NOT NULL,          -- tâches d'import : date de la dernière mise à jour
    licence           text,                     -- NULL = aucune licence nommée ou non lue
    url_conditions    text,
    attribution       text NOT NULL,
    usage_commercial  text NOT NULL CHECK (usage_commercial IN ('yes', 'no', 'unknown')),
    partage_identique text NOT NULL CHECK (partage_identique IN ('yes', 'no', 'unknown')),
    statut            text NOT NULL CHECK (statut IN ('verified', 'no_named_licence', 'unverified')),
    verifie_le        date,
    remarque          text,
    ordre             integer NOT NULL DEFAULT 100
);

INSERT INTO licences (code, producteur, types, chambres, taches, licence, url_conditions, attribution, usage_commercial, partage_identique, statut, verifie_le, remarque, ordre) VALUES
('fr-an', 'Assemblée nationale (data.assemblee-nationale.fr)', '{parliamentary_vote,member,law}', '{AN}', '{an,dossiers,amendements}',
 'Licence Ouverte / Open Licence (Etalab)', 'https://data.assemblee-nationale.fr/licence-ouverte-open-licence',
 'Source: Assemblée nationale (data.assemblee-nationale.fr), with the date of the last update.', 'yes', 'no', 'verified', '2026-11-27', NULL, 10),
('fr-senat', 'Sénat (data.senat.fr)', '{parliamentary_vote,member,law}', '{SENAT}', '{senat,textes}',
 'Licence Ouverte / Open Licence 2.0 (Etalab)', 'https://data.senat.fr/licence/',
 'Source: Sénat (data.senat.fr), with the date of the last update.', 'yes', 'no', 'verified', '2026-11-27',
 'Reuse must not imply official endorsement nor mislead about content, source or update date.', 20),
('fr-petitions', 'Assemblée nationale and Sénat petition platforms', '{petition}', '{AN,SENAT}', '{petitions}',
 NULL, 'https://www.data.gouv.fr/', 'Source: petitions.assemblee-nationale.fr / petitions.senat.fr.', 'unknown', 'unknown', 'unverified', NULL,
 'The datasets are reported to be under Licence Ouverte 2.0 on data.gouv.fr; not checked. The Senate platform terms of use state no reuse licence.', 30),
('eu-howtheyvote', 'HowTheyVote.eu (independent project, from European Parliament data)', '{parliamentary_vote,member}', '{UE}', '{ue}',
 'Open Database License (ODbL)', 'https://howtheyvote.eu/about',
 'Data source: HowTheyVote.eu (Open Database License), https://howtheyvote.eu', 'yes', 'yes', 'verified', '2026-11-27',
 'Derived databases must remain under the ODbL.', 40),
('gb-parliament', 'UK Parliament (Votes, Members and Bills APIs)', '{parliamentary_vote,member,bill}', '{COMMONS,LORDS}', '{communes,lords,bills_uk}',
 'Open Parliament Licence v3.0', 'https://www.parliament.uk/site-information/copyright-parliament/open-parliament-licence/',
 'Contains Parliamentary information licensed under the Open Parliament Licence v3.0.', 'unknown', 'unknown', 'unverified', NULL,
 'Licence name as recorded by this site; the licence page could not be read.', 50),
('gb-petitions', 'UK Parliament petitions (petition.parliament.uk)', '{petition}', '{UK}', '{petitions_uk}',
 'Open Government Licence v3.0', 'https://petition.parliament.uk/help',
 'Source: petition.parliament.uk, Open Government Licence v3.0.', 'unknown', 'unknown', 'unverified', NULL,
 'Licence named in the site footer; the licence text was not read.', 60),
('us-clerk', 'Office of the Clerk, U.S. House of Representatives', '{parliamentary_vote,member}', '{USHOUSE}', '{us}',
 NULL, 'https://clerk.house.gov/PrivacyPolicy', 'Source: Office of the Clerk, U.S. House of Representatives (clerk.house.gov).', 'unknown', 'unknown', 'no_named_licence', '2026-11-27',
 'Copying allowed unless otherwise specified, with citation. Proceedings may not be used for political or commercial advertising.', 70),
('us-voteview', 'Voteview', '{parliamentary_vote,member}', '{USSENATE,USHOUSE}', '{us}',
 NULL, 'https://voteview.com/data',
 'Lewis, Jeffrey B., Keith Poole, Howard Rosenthal, Adam Boche, Aaron Rudkin, and Luke Sonnet (2026). Voteview: Congressional Roll-Call Votes Database. https://voteview.com/',
 'unknown', 'unknown', 'no_named_licence', '2026-11-27', 'Senate votes and member names. Citation requested.', 80),
('it-camera', 'Camera dei deputati (dati.camera.it)', '{parliamentary_vote,member}', '{ITCAMERA}', '{italie}',
 'CC BY-SA 4.0', 'https://dati.camera.it/', 'Source: Camera dei deputati (dati.camera.it), CC BY-SA 4.0.', 'yes', 'yes', 'verified', '2026-11-27',
 'Derived data must be shared under the same licence.', 90),
('it-senato', 'Senato della Repubblica (dati.senato.it)', '{parliamentary_vote,member}', '{ITSENATE}', '{italie_senat}',
 'CC BY 3.0', 'https://dati.senato.it/', 'Source: Senato della Repubblica (dati.senato.it), CC BY 3.0.', 'yes', 'no', 'verified', '2026-11-20', NULL, 100),
('ie-oireachtas', 'Houses of the Oireachtas (api.oireachtas.ie)', '{parliamentary_vote,member}', '{IEDAIL,IESEANAD}', '{irlande,irlande_seanad}',
 'Oireachtas (Open Data) PSI Licence, incorporating CC BY 4.0', 'https://www.oireachtas.ie/en/open-data/license/',
 'Source: Houses of the Oireachtas (api.oireachtas.ie), CC BY 4.0.', 'yes', 'no', 'verified', '2026-11-20', NULL, 110),
('ch-parliament', 'Parlamentsdienste der Bundesversammlung, Bern', '{parliamentary_vote,member}', '{CHNR,CHSR}', '{suisse}',
 NULL, 'https://www.parlament.ch/', 'Source: Parlamentsdienste der Bundesversammlung, Bern, with the download date.', 'unknown', 'unknown', 'unverified', NULL,
 'Data to be reproduced without modification and never presented as official when computed; totals and results are computed by this site.', 120),
('ch-swissvotes', 'Swissvotes, Année Politique Suisse, University of Bern', '{popular_vote}', '{CH}', '{votations}',
 'CC BY 4.0', 'https://swissvotes.ch/page/dataset', 'Source: Swissvotes (Année Politique Suisse, University of Bern), CC BY 4.0.', 'yes', 'no', 'verified', '2026-11-27', NULL, 130),
('ca-commons', 'House of Commons of Canada (ourcommons.ca)', '{parliamentary_vote,member}', '{CACOMMONS}', '{canada}',
 NULL, 'https://www.ourcommons.ca/en/important-notices', 'Source: House of Commons of Canada (ourcommons.ca). Not an official version.', 'no', 'unknown', 'no_named_licence', '2026-11-27',
 'Proceedings: accurate reproduction, not presented as official, no commercial use. The open data page announces free reuse without naming a licence: treated as non-commercial.', 140),
('ca-senate', 'Senate of Canada (sencanada.ca)', '{parliamentary_vote,member}', '{CASENATE}', '{canada_senat}',
 NULL, 'https://sencanada.ca/en/intellectual-property', 'Source: Senate of Canada (sencanada.ca). Not an official version.', 'no', 'unknown', 'no_named_licence', '2026-11-27',
 'Accurate reproduction, not presented as official, no use for financial gain.', 150)
ON CONFLICT (code) DO NOTHING;

COMMIT;
