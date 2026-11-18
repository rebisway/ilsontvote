-- Projets de loi du Parlement britannique (Bills) : l'équivalent britannique de « textes » (/lois). Source : bills-api.parliament.uk (JSON public, Open Parliament Licence).
-- Parlement courant seulement (sessions 39 et 40, depuis juillet 2024). Une division des Communes est rattachée à son projet de loi par le titre (calcul du site).
BEGIN;

CREATE TABLE bills (
    id               bigint PRIMARY KEY,            -- billId de la source
    titre            text NOT NULL,                 -- shortTitle
    titre_long       text,                          -- longTitle (objet du projet, tel que publié)
    chambre_origine  text,                          -- Commons | Lords (chambre où le projet a été déposé)
    chambre_courante text,                          -- chambre où il se trouve
    session_id       integer,                       -- session d'introduction (identifiant de la source)
    est_loi          boolean NOT NULL DEFAULT false,-- isAct : le projet est devenu une loi (Royal Assent)
    date_loi         date,                          -- date de la sanction royale (étape « Royal Assent »), si connue
    retire_le        date,                          -- billWithdrawn : projet retiré
    rejete           boolean NOT NULL DEFAULT false,-- isDefeated
    etape_courante   text,                          -- libellé de l'étape en cours, tel que publié (« Committee stage »…)
    date_depot       date,                          -- première séance connue (1re lecture)
    derniere_maj     timestamptz,                   -- lastUpdate de la source : sert à ne relire les étapes que des projets modifiés
    url              text NOT NULL
);
CREATE INDEX ix_bills_session ON bills (session_id);

-- Étapes (une ligne par séance d'une étape) : 1re lecture, 2e lecture, commission, rapport, 3e lecture, étapes de l'autre chambre, sanction royale…
CREATE TABLE bill_etapes (
    id            bigserial PRIMARY KEY,
    bill_id       bigint NOT NULL REFERENCES bills (id) ON DELETE CASCADE,
    ordre         integer NOT NULL,                 -- sortOrder de la source
    libelle       text NOT NULL,
    chambre       text,
    date_seance   date                              -- NULL = étape sans séance connue
);
CREATE INDEX ix_bill_etapes_bill ON bill_etapes (bill_id, ordre, date_seance);

-- Rattachement d'une division à un projet (calculé par le site d'après le titre : voir CLAUDE.md) et partie du titre qui suit le nom du projet (« Third Reading »…).
ALTER TABLE scrutins ADD COLUMN bill_id bigint REFERENCES bills (id) ON DELETE SET NULL;
ALTER TABLE scrutins ADD COLUMN bill_etape text;
CREATE INDEX ix_scrutins_bill ON scrutins (bill_id) WHERE bill_id IS NOT NULL;

INSERT INTO import_taches (code, libelle, description, ordre, ponctuelle, pays) VALUES
 ('bills_uk', 'Royaume-Uni : projets de loi (Bills)',
  'Projets de loi du Parlement actuel (sessions 39 et 40) depuis bills-api.parliament.uk : étape en cours, étapes de chaque chambre, sanction royale (loi), retrait ou rejet. Rattache ensuite les divisions des Communes à leur projet d''après le titre (calculé : « Finance Bill: Third Reading »). APRÈS l''import des Communes.', 56, false, 'GB');

INSERT INTO import_planning (tache, jours, heure, cree_par)
VALUES ('bills_uk', ARRAY[1,2,3,4,5,6,7]::smallint[], TIME '04:30', 'migration 036');

COMMIT;
