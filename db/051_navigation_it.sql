-- Traduction italienne de la navigation précédent / suivant d'un scrutin. ON CONFLICT DO NOTHING : les corrections de /Admin/Traductions survivent.
BEGIN;
INSERT INTO traductions (langue, cle, texte) VALUES
 ('it', 'Précédent', 'Precedente'),
 ('it', 'Suivant', 'Successivo'),
 ('it', 'Scrutin précédent et suivant', 'Votazione precedente e successiva')
ON CONFLICT (langue, cle) DO NOTHING;
COMMIT;
