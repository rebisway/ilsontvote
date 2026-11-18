-- Pétitions du Parlement britannique (petition.parliament.uk) : nouvelle PLATEFORME « UK » dans la table petitions (colonne chambre, qui désigne ici la plateforme).
-- Ces pétitions ont des étapes que les plateformes françaises n'ont pas : seuil de réponse (10 000 soutiens), réponse du gouvernement, seuil de débat
-- (100 000 soutiens), débat au Parlement. Colonnes NULL pour les pétitions françaises.
BEGIN;

ALTER TABLE petitions
    ADD COLUMN seuil_reponse_le date,     -- date à laquelle le seuil de réponse du gouvernement (10 000 soutiens) a été atteint
    ADD COLUMN reponse_le       date,     -- date de la réponse publiée du gouvernement
    ADD COLUMN reponse_resume   text,     -- résumé de la réponse, tel que publié
    ADD COLUMN seuil_debat_le   date,     -- date à laquelle le seuil de débat (100 000 soutiens) a été atteint
    ADD COLUMN debat_prevu_le   date,     -- date de débat annoncée (avant la tenue du débat)
    ADD COLUMN debat_le         date,     -- date à laquelle la pétition a été débattue
    ADD COLUMN debat_url        text;     -- compte rendu (transcript) du débat

-- Catalogue du planificateur : tâche quotidienne, après l'import des Communes.
INSERT INTO import_taches (code, libelle, description, ordre, ponctuelle, pays) VALUES
 ('petitions_uk', 'Royaume-Uni : pétitions du Parlement',
  'Pétitions ouvertes et clôturées de petition.parliament.uk (JSON public, Open Parliament Licence) : nombre de soutiens, état, réponse du gouvernement, débat. Un relevé par jour ; l''historique et les baisses de soutiens se déduisent des relevés successifs. Les pétitions rejetées avant publication ne sont pas importées.', 62, false, 'GB');

INSERT INTO import_planning (tache, jours, heure, cree_par)
VALUES ('petitions_uk', ARRAY[1,2,3,4,5,6,7]::smallint[], TIME '04:30', 'migration 035');

COMMIT;
