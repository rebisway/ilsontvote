-- Activation des pays hors France, À LANCER À LA MAIN (jamais par migration : c'est une décision de publication).
-- Prérequis : migrations appliquées (update-ilsontvote.sh), puis imports faits (voir DEPLOY_ITALIE.md) : sans données, les pages seraient vides.
-- Usage : sudo -u postgres psql ilsontvote -f deploy/activer-pays.sql   (décommenter les pays voulus ; le cache de la liste des pays dure 5 minutes)
-- Retour arrière : mettre actif = false (rien n'est supprimé).

-- Italie (Chambre + Sénat). Licences : Chambre CC BY-SA 4.0, Sénat CC BY 3.0 (dites dans mentions, méthodologie, données).
UPDATE pays     SET actif = true WHERE code = 'IT';
UPDATE chambres SET actif = true WHERE pays = 'IT';

-- Irlande (Dáil + Seanad ; licence CC BY 4.0 « Oireachtas (Open Data) PSI Licence »). Prérequis : migration 047, tâches `irlande` et `irlande_seanad`, puis `stats` et `export`.
-- UPDATE pays     SET actif = true WHERE code = 'IE';
-- UPDATE chambres SET actif = true WHERE pays = 'IE';

-- Royaume-Uni et États-Unis (déjà livrés en local, jamais activés en production) :
-- UPDATE pays     SET actif = true WHERE code IN ('GB', 'US');
-- UPDATE chambres SET actif = true WHERE pays IN ('GB', 'US');

SELECT p.code, p.prefixe_url, p.actif, count(c.*) FILTER (WHERE c.actif) AS chambres_actives FROM pays p LEFT JOIN chambres c ON c.pays = p.code GROUP BY 1, 2, 3 ORDER BY p.ordre;

-- Suisse (le Conseil des États exige la clé d'API Parlament:ApiKey dans appsettings.Local.json) :
-- UPDATE pays SET actif = true WHERE code = 'CH';
-- UPDATE chambres SET actif = true WHERE pays = 'CH';

-- Canada (Chambre des communes + Sénat ; permission de la Présidence de chaque chambre : reproduction exacte, non officielle, non commerciale). Prérequis : migrations 073 et 074, tâches `canada` et `canada_senat`
-- (+ `canada_historique` et `canada_senat_historique`), puis `stats` et `export`. La seconde ligne active les DEUX chambres du pays.
-- UPDATE pays SET actif = true WHERE code = 'CA';
-- UPDATE chambres SET actif = true WHERE pays = 'CA';
