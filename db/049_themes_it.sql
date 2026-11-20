-- Traduction italienne du filtre de thème (migration 048). Clé = texte français ; ON CONFLICT DO NOTHING : les corrections de /Admin/Traductions survivent.
BEGIN;
INSERT INTO traductions (langue, cle, texte) VALUES
 ('it', 'Thème', 'Tema'),
 ('it', 'Tous les thèmes', 'Tutti i temi'),
 ('it', 'Thème estimé d''après les mots du titre : peut être inexact', 'Tema stimato dalle parole del titolo: può essere inesatto'),
 ('it', 'Budget et fiscalité', 'Bilancio e fisco'),
 ('it', 'Travail et retraites', 'Lavoro e pensioni'),
 ('it', 'Santé', 'Salute'),
 ('it', 'Éducation et recherche', 'Istruzione e ricerca'),
 ('it', 'Justice et sécurité', 'Giustizia e sicurezza'),
 ('it', 'Immigration et asile', 'Immigrazione e asilo'),
 ('it', 'Environnement et énergie', 'Ambiente ed energia'),
 ('it', 'Transports', 'Trasporti'),
 ('it', 'Agriculture et alimentation', 'Agricoltura e alimentazione'),
 ('it', 'Défense et affaires étrangères', 'Difesa e affari esteri'),
 ('it', 'Institutions et élections', 'Istituzioni ed elezioni'),
 ('it', 'Numérique et médias', 'Digitale e media'),
 ('it', 'Logement et urbanisme', 'Abitazione e urbanistica'),
 ('it', 'Culture et sport', 'Cultura e sport'),
 ('it', 'Famille et solidarité', 'Famiglia e solidarietà'),
 ('it', 'Économie et entreprises', 'Economia e imprese')
ON CONFLICT (langue, cle) DO NOTHING;
COMMIT;
