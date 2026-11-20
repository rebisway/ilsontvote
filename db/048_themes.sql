-- Thèmes : classement APPROXIMATIF des votes et des lois par mots-clés du titre, pour filtrer les listes (?theme=). Pas une donnée officielle : une facilité de recherche,
-- des erreurs sont acceptées (choix de Thomas, 2026-11-20). Un texte peut avoir plusieurs thèmes ou aucun. Les motifs (français, anglais, italien, espagnol) sont des
-- expressions régulières POSIX appliquées au titre SANS accents (recherche_norm) et insensibles à la casse : modifiables en base, sans redéploiement ; relancer la tâche
-- « themes » (ou themes_de) après une modification. \m = début de mot, \M = fin de mot (les mots courts sont bornés des deux côtés pour éviter « port » dans « rapport »).
BEGIN;

CREATE TABLE themes (
    code    text PRIMARY KEY,
    nom_fr  text NOT NULL,
    nom_en  text NOT NULL,
    motifs  text NOT NULL,
    ordre   integer NOT NULL
);

INSERT INTO themes (code, nom_fr, nom_en, ordre, motifs) VALUES
 ('budget', 'Budget et fiscalité', 'Budget and taxation', 10,
  '\m(budget|financ|fiscal|impot|taxe|\mtva\M|tax\M|taxes\M|taxation|revenue|appropriation|spending|bilancio|finanziari|tribut|presupuest|hacienda|loi de finances)'),
 ('travail', 'Travail et retraites', 'Work and pensions', 20,
  '\m(travail|emploi|chomage|retraite|salaire|syndica|apprentissage|licenci|pension|employ|labour|labor\M|worker|wage|unemploy|redundan|lavoro|previdenz|trabajo|empleo|jubilacion)'),
 ('sante', 'Santé', 'Health', 30,
  '\m(sante|hopita|medic|maladie|soins|sanitaire|assurance maladie|fin de vie|aide a mourir|mourir|euthanas|bioethique|pharma|health|hospital|nhs\M|drug|patient|disease|cancer|mental|abortion|salute|sanit|farmac|sanidad|salud|aborto)'),
 ('education', 'Éducation et recherche', 'Education and research', 40,
  '\m(education|ecole|enseignement|universit|etudiant|scolaire|recherche|apprenti|school|student|teacher|college|research|scuola|istruzion|ricerca|educacion|escuela|escolar)'),
 ('justice', 'Justice et sécurité', 'Justice and security', 50,
  '\m(justice|penal|prison|police|securite|crime|terroris|delinquan|tribunal|magistrat|gendarm|violence|criminal|court|offence|offender|sentenc|security|anti-social|giustizia|carcer|sicurezza|polizia|justicia|delito|seguridad)'),
 ('immigration', 'Immigration et asile', 'Immigration and asylum', 60,
  '\m(immigr|etranger|asile|refugi|nationalite|\mvisa\M|migrat|asylum|border|citizenship|deportat|stranier|migrant|extranjer)'),
 ('environnement', 'Environnement et énergie', 'Environment and energy', 70,
  '\m(environnement|climat|ecolog|energie|nucleaire|biodiversite|pollution|carbone|renouvelable|eolien|\meau\M|environment|energy|climate|emission|waste|nuclear|renewable|net zero|water\M|ambient|energia|clima|ambiente|energetic)'),
 ('transports', 'Transports', 'Transport', 80,
  '\m(transport|ferroviaire|\msncf\M|aerien|autoroute|maritime|\mport\M|\mrail\M|railway|airport|aviation|highway|vehicle|trasport|ferrovi|autostrad|ferrocarril|carretera|\mtren\M)'),
 ('agriculture', 'Agriculture et alimentation', 'Agriculture and food', 90,
  '\m(agricultur|agricole|\mpeche\M|paysan|alimentation|elevage|viticol|farm|fisher|\mfood\M|rural|agricoltur|\mpesca\M|agricola|ganader)'),
 ('international', 'Défense et affaires étrangères', 'Defence and foreign affairs', 100,
  '\m(defense|armement|militaire|armee|guerre|\motan\M|traite\M|convention|accord entre|accord-cadre|ratification|cooperation|defence|military|armed forces|treaty|foreign|\mnato\M|\mwar\M|ukrain|sanction|difesa|trattato|accordo|convenio|tratado|acuerdo|\mpalestin)'),
 ('institutions', 'Institutions et élections', 'Institutions and elections', 110,
  '\m(constitution|institution|election|referendum|reglement|collectivite|decentralis|motion de censure|motion de rejet|commission d.enquete|statut|elect|standing order|parliament|local government|devolution|elezion|costituzion|regolament|constitucion|reglamento|legislat)'),
 ('numerique', 'Numérique et médias', 'Digital and media', 120,
  '\m(numerique|internet|donnees|cyber|intelligence artificielle|telecom|audiovisuel|\mmedias?\M|presse|digital|\mdata\M|online|artificial intelligence|broadband|\mmedia\M|broadcast|digitale|\mdati\M|telecomunic)'),
 ('logement', 'Logement et urbanisme', 'Housing and planning', 130,
  '\m(logement|loyer|immobilier|urbanisme|habitat|housing|\mrent\M|tenant|planning|propert|alloggi|abitazion|vivienda|alquiler)'),
 ('culture', 'Culture et sport', 'Culture and sport', 140,
  '\m(culture|patrimoine|\msport|cinema|musee|langue|heritage|\marts?\M|museum|language|cultura|patrimonio|deporte)'),
 ('social', 'Famille et solidarité', 'Family and welfare', 150,
  '\m(famille|handicap|enfant|egalite|solidarite|pauvrete|aide sociale|protection sociale|social|famil|disab|child|welfare|benefit|poverty|equality|gender|sociale\M|disabil|discriminazion|igualdad)'),
 ('economie', 'Économie et entreprises', 'Economy and business', 160,
  '\m(entreprise|commerce|industrie|concurrence|consommat|bancaire|assurance|economi|business|trade\M|industry|competition|consumer|bank|insurance|company|companies|impresa|commercio|industria|concorrenza|banca|empresa|comercio)');

-- Thèmes d'un texte : tous les thèmes dont un motif correspond au titre (tableau vide = aucun).
CREATE FUNCTION themes_de(t text) RETURNS text[]
    LANGUAGE sql STABLE
    AS $$ SELECT COALESCE(array_agg(code ORDER BY ordre), '{}') FROM themes WHERE recherche_norm(coalesce(t, '')) ~* motifs $$;

ALTER TABLE scrutins ADD COLUMN themes text[] NOT NULL DEFAULT '{}';
ALTER TABLE textes   ADD COLUMN themes text[] NOT NULL DEFAULT '{}';
CREATE INDEX scrutins_themes_idx ON scrutins USING gin (themes);
CREATE INDEX textes_themes_idx   ON textes   USING gin (themes);

UPDATE scrutins SET themes = themes_de(titre || ' ' || coalesce(titre_en, ''));
UPDATE textes   SET themes = themes_de(titre || ' ' || coalesce(titre_en, ''));

-- Tâche d'import : recalcule les thèmes (après les imports, avant les statistiques).
INSERT INTO import_taches (code, libelle, description, ordre, ponctuelle, pays) VALUES
 ('themes', 'Thèmes : classement des votes et des lois',
  'Recalcule le thème (approximatif, par mots-clés du titre) des scrutins et des lois. À lancer après les imports.', 80, false, NULL);
INSERT INTO import_planning (tache, jours, heure, cree_par) VALUES
 ('themes', ARRAY[1,2,3,4,5,6,7]::smallint[], TIME '04:30', 'migration 048');

COMMIT;
