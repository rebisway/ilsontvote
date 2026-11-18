-- Bilan d'une exécution d'import : effectif de chaque table AVANT et APRÈS (donc ce qui a été ajouté ou retiré), et les erreurs relevées.
-- Mesure faite par le planificateur autour du processus d'import : elle ne dépend d'aucune source et s'applique à toutes les tâches.
BEGIN;

ALTER TABLE import_executions
    ADD COLUMN bilan      jsonb,                      -- { "table": { "avant": n, "apres": n } } ; NULL tant que l'exécution n'est pas terminée
    ADD COLUMN ajouts     bigint,                     -- somme des hausses d'effectif (enregistrements ajoutés), pour la liste
    ADD COLUMN nb_erreurs integer NOT NULL DEFAULT 0, -- lignes d'erreur relevées dans la sortie
    ADD COLUMN erreurs    text NOT NULL DEFAULT '';   -- ces lignes, à part (sortie d'erreur + lignes signalant une erreur ou une exception)

COMMIT;
