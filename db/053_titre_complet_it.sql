-- Traduction italienne de « Titre complet » (bloc repliable des titres très longs). ON CONFLICT DO NOTHING : les corrections de /Admin/Traductions survivent.
BEGIN;
INSERT INTO traductions (langue, cle, texte) VALUES ('it', 'Titre complet', 'Titolo completo') ON CONFLICT (langue, cle) DO NOTHING;
COMMIT;
