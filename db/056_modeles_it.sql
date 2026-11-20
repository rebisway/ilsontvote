-- Traductions italiennes des textes À VALEURS (modèles {0}, {1}…) devenus des clés propres avec la migration 055. Avant, ces phrases étaient construites en interpolation dans le code :
-- elles n'avaient aucune clé, donc aucune traduction possible. ON CONFLICT DO NOTHING : les corrections de /Admin/Traductions survivent.
BEGIN;
INSERT INTO traductions (langue, cle, texte, source_fr) VALUES
 ('it', 'elu-description', '{0}, {1}{2}: partecipazione alle votazioni per appello nominale e storico dei voti.', '{0}, {1}{2} : participation aux scrutins publics et historique de vote.'),
 ('it', 'elus-description', 'I {0} {1} in carica: gruppo, circoscrizione e storico dei voti.', 'Les {0} {1} en exercice : groupe, circonscription et historique de vote.'),
 ('it', 'jauge-majorite-requise', ', maggioranza richiesta {0}', ', majorité requise {0}'),
 ('it', 'lignes-plafond-export', 'Visualizzazione limitata alle prime {0:N0} righe su {1:N0}: l''esportazione (CSV, JSON) fornisce tutto.', 'Affichage limité aux {0:N0} premières lignes sur {1:N0} : l''export (CSV, JSON) donne tout.')
ON CONFLICT (langue, cle) DO NOTHING;
COMMIT;
