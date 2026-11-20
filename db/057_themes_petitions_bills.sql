-- Thèmes (classement approximatif par mots-clés du titre, migrations 048-050) étendus aux pétitions et aux projets de loi britanniques. Mêmes règles : une facilité de filtrage,
-- pas une donnée officielle ; erreurs acceptées. Table `themes` et fonction `themes_de` inchangées (les motifs couvrent déjà le français et l'anglais).
-- Les colonnes sont recalculées par la tâche « themes » (après chaque import).
BEGIN;

ALTER TABLE petitions ADD COLUMN themes text[] NOT NULL DEFAULT '{}';
ALTER TABLE bills     ADD COLUMN themes text[] NOT NULL DEFAULT '{}';
CREATE INDEX petitions_themes_idx ON petitions USING gin (themes);
CREATE INDEX bills_themes_idx     ON bills     USING gin (themes);

UPDATE petitions SET themes = themes_de(titre);
UPDATE bills     SET themes = themes_de(titre);

COMMIT;
