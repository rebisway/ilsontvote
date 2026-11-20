-- Activation des pays hors France, À LANCER À LA MAIN (jamais par migration : c'est une décision de publication).
-- Prérequis : migrations appliquées (update-ilsontvote.sh), puis imports faits (voir DEPLOY_ITALIE.md) : sans données, les pages seraient vides.
-- Usage : sudo -u postgres psql ilsontvote -f deploy/activer-pays.sql   (décommenter les pays voulus ; le cache de la liste des pays dure 5 minutes)
-- Retour arrière : mettre actif = false (rien n'est supprimé).

-- Italie (Chambre + Sénat). Licences : Chambre CC BY-SA 4.0, Sénat CC BY 3.0 (dites dans mentions, méthodologie, données).
UPDATE pays     SET actif = true WHERE code = 'IT';
UPDATE chambres SET actif = true WHERE pays = 'IT';

-- Royaume-Uni et États-Unis (déjà livrés en local, jamais activés en production) :
-- UPDATE pays     SET actif = true WHERE code IN ('GB', 'US');
-- UPDATE chambres SET actif = true WHERE pays IN ('GB', 'US');

SELECT p.code, p.prefixe_url, p.actif, count(c.*) FILTER (WHERE c.actif) AS chambres_actives FROM pays p LEFT JOIN chambres c ON c.pays = p.code GROUP BY 1, 2, 3 ORDER BY p.ordre;
