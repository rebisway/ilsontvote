-- Planificateur d'imports : catalogue des tâches, planning (plusieurs créneaux par tâche), demandes manuelles depuis /Admin,
-- exécutions journalisées avec leur sortie console.
--
-- Principe : le site (/Admin) n'exécute JAMAIS rien lui-même. Il écrit une demande ici ; le planificateur (commande « planificateur »
-- de l'Importer, lancée chaque minute par ilsontvote-planificateur.timer) lit les demandes et le planning, lance les tâches et journalise.
-- Le code exécuté pour chaque tâche est dans l'Importer (liste blanche par code) : cette base ne contient aucune ligne de commande.
BEGIN;

-- Catalogue : ce qui peut être importé, et à quoi ça correspond (affiché dans /Admin/Imports).
CREATE TABLE import_taches (
    code        text PRIMARY KEY,
    libelle     text NOT NULL,
    description text NOT NULL,
    -- Ordre d'exécution quand plusieurs tâches sont dues en même temps : l'ordre des dépendances (scrutins avant textes, etc.).
    ordre       integer NOT NULL,
    -- Tâche lourde, à ne lancer qu'une fois (premier remplissage, rattrapage) : pas de planning récurrent proposé par défaut.
    ponctuelle  boolean NOT NULL DEFAULT false
);

-- Planning : un créneau = des jours de la semaine (ISO : 1 = lundi … 7 = dimanche) + une heure, heure de Paris.
CREATE TABLE import_planning (
    id                serial PRIMARY KEY,
    tache             text NOT NULL REFERENCES import_taches (code) ON DELETE CASCADE,
    jours             smallint[] NOT NULL CHECK (cardinality(jours) BETWEEN 1 AND 7),
    heure             time NOT NULL,
    actif             boolean NOT NULL DEFAULT true,
    -- Dernier créneau déclenché : évite de relancer deux fois le même créneau, et de « rattraper » un créneau antérieur à la création de la ligne.
    dernier_lancement timestamptz NOT NULL DEFAULT now(),
    cree_par          text,
    cree_le           timestamptz NOT NULL DEFAULT now()
);

-- Demandes manuelles (bouton « Lancer maintenant ») : prises en charge au passage suivant du planificateur.
CREATE TABLE import_demandes (
    id          bigserial PRIMARY KEY,
    tache       text NOT NULL REFERENCES import_taches (code) ON DELETE CASCADE,
    demandee_le timestamptz NOT NULL DEFAULT now(),
    demandee_par text,
    prise_le    timestamptz
);
CREATE INDEX ix_import_demandes_attente ON import_demandes (id) WHERE prise_le IS NULL;

-- Une exécution d'une tâche (planifiée ou manuelle), avec sa sortie console. ok NULL + fin NULL = en cours ; fin renseignée sans ok = interrompue.
CREATE TABLE import_executions (
    id           bigserial PRIMARY KEY,
    tache        text NOT NULL,                       -- sans clé étrangère : le journal survit à un retrait de tâche du catalogue
    declencheur  text NOT NULL CHECK (declencheur IN ('planning', 'admin')),
    demande_par  text,
    planning_id  integer,
    debut        timestamptz NOT NULL DEFAULT now(),
    fin          timestamptz,
    code_sortie  integer,
    ok           boolean,
    sortie       text NOT NULL DEFAULT ''             -- fin de la sortie standard + erreur (tronquée à ~400 Ko, début supprimé)
);
CREATE INDEX ix_import_executions_debut ON import_executions (debut DESC);
CREATE INDEX ix_import_executions_tache ON import_executions (tache, debut DESC);

-- Les compteurs par source (import_runs) se rattachent à l'exécution qui les a produits.
ALTER TABLE import_runs ADD COLUMN execution_id bigint;
CREATE INDEX ix_import_runs_execution ON import_runs (execution_id) WHERE execution_id IS NOT NULL;

-- Battement du planificateur : dernier passage, pour avertir dans /Admin s'il ne tourne plus.
CREATE TABLE import_etat (
    cle    text PRIMARY KEY,
    valeur timestamptz NOT NULL
);

INSERT INTO import_taches (code, libelle, description, ordre, ponctuelle) VALUES
 ('an',                  'Assemblée nationale : scrutins et votes',
  'Scrutins publics et vote de chaque député de la législature en cours (17e), depuis data.assemblee-nationale.fr. Met aussi à jour l''annuaire des députés et leurs groupes. À rejouer souvent : l''Assemblée publie le soir des séances.', 10, false),
 ('an_historique',       'Assemblée nationale : historique complet (15e à 17e)',
  'Mêmes données que ci-dessus pour les législatures 15, 16 et 17 (2017 à aujourd''hui). Long (dizaines de minutes) : premier remplissage ou correction après un changement de format de la source.', 11, true),
 ('senat',               'Sénat : scrutins, votes et sénateurs',
  'Scrutins publics du Sénat, vote de chaque sénateur et annuaire (base DOSLEG + base Sénateurs, data.senat.fr). Télécharge le dump DOSLEG (~16 Mo) réutilisé par « Textes de loi ».', 20, false),
 ('textes',              'Textes de loi : cycle de vie et rattachement des scrutins',
  'Dossiers législatifs : promulgation, Journal officiel, Conseil constitutionnel, et rattachement de chaque scrutin à son texte. À lancer APRÈS « Assemblée nationale » et « Sénat ».', 30, false),
 ('dossiers',            'Dossiers législatifs de l''Assemblée : étapes sans scrutin public',
  'Étapes de lecture à l''Assemblée avec ou sans scrutin public (votes à main levée compris), législature en cours. APRÈS « Assemblée nationale » et « Textes de loi ».', 40, false),
 ('dossiers_historique', 'Dossiers législatifs : législatures 15 à 17',
  'Comme ci-dessus pour les trois législatures. Premier remplissage ou rattrapage.', 41, true),
 ('ue',                  'Parlement européen : votes des eurodéputés',
  'Votes en séance plénière via HowTheyVote.eu. Incrémental : seuls les nouveaux votes sont lus (le tout premier passage est long : un appel par vote).', 50, false),
 ('petitions',           'Pétitions du Sénat et de l''Assemblée',
  'Fichiers data.gouv.fr (mis à jour chaque lundi à la source). Un relevé n''est écrit que si le fichier a changé ; l''historique et les anomalies s''en déduisent.', 60, false),
 ('petitions_archives',  'Pétitions : rattrapage des archives hebdomadaires',
  'Rejoue toutes les archives hebdomadaires de data.gouv.fr (Sénat depuis mars 2026, Assemblée depuis juillet 2025) pour reconstituer l''historique. Une seule fois.', 61, true),
 ('traductions',         'Traduction automatique des titres',
  'Traduit les NOUVEAUX titres seulement (fr vers en pour l''interface anglaise, en vers fr pour les votes européens). Ignoré sans erreur si les modèles de traduction ne sont pas installés. APRÈS le Parlement européen.', 70, false),
 ('stats',               'Statistiques de participation',
  'Recalcule la vue matérialisée de participation des élus. Plusieurs minutes. À lancer APRÈS les imports de votes.', 80, false),
 ('export',              'Export des données ouvertes',
  'Régénère les fichiers CSV compressés téléchargeables sur /donnees. À lancer en dernier.', 90, false);

-- Planning initial = l'ancien import nocturne (04:30 tous les jours), dans l'ordre des dépendances (colonne « ordre »).
INSERT INTO import_planning (tache, jours, heure, cree_par)
SELECT code, ARRAY[1,2,3,4,5,6,7]::smallint[], TIME '04:30', 'migration 026'
  FROM import_taches WHERE NOT ponctuelle;

COMMIT;
