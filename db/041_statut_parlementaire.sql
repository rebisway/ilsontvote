-- Statut du jour d'un parlementaire quand il siège avec une restriction : « Leave of Absence » (congé), « Suspended », « Disqualified » (Lords, d'après l'annuaire des membres).
-- NULL = aucun statut particulier (ou source qui n'en publie pas). Le statut est celui du JOUR de l'import : la source ne donne pas l'historique, donc un pair en congé hier
-- n'est pas signalé pour les divisions passées. Il explique en partie les pairs « à 0 vote » ; il est affiché comme une donnée incertaine, jamais comme une raison d'absence.
BEGIN;

ALTER TABLE parlementaires ADD COLUMN statut text;

COMMIT;
