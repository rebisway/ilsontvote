-- Communes : historique (57e et 58e législatures) et gouvernement par PÉRIODE.
-- Un seul indicateur `groupes.gouvernement` ne suffit plus : l'hémicycle d'une division de 2021 doit montrer le parti qui était alors au gouvernement.
-- Périodes CLOSES seulement ; la période en cours reste l'indicateur du groupe (réglé dans /Admin/Pays), donc rien à changer après une élection.
BEGIN;

CREATE TABLE chambre_gouvernements (
    chambre     text NOT NULL REFERENCES chambres (code),
    groupe_id   text NOT NULL REFERENCES groupes (id),
    date_debut  date NOT NULL,
    date_fin    date NOT NULL,
    PRIMARY KEY (chambre, groupe_id, date_debut)
);

-- Les conservateurs ont formé le gouvernement du 8 juin 2017 (57e) au 4 juillet 2024 (travaillistes ensuite). Le groupe peut ne pas exister
-- encore en base (import historique non lancé) : on l'insère à l'identique de l'import, sans écraser un groupe existant.
INSERT INTO groupes (id, chambre, legislature, sigle, libelle, couleur, ordre)
VALUES ('COMMONS-Con', 'COMMONS', 58, 'Con', 'Conservative', '#0087DC', 90)
ON CONFLICT (id) DO NOTHING;

INSERT INTO chambre_gouvernements (chambre, groupe_id, date_debut, date_fin) VALUES
 ('COMMONS', 'COMMONS-Con', '2017-06-08', '2024-07-04');

INSERT INTO import_taches (code, libelle, description, ordre, ponctuelle, pays) VALUES
 ('communes_historique', 'Royaume-Uni, Communes : historique (58e législature, 2019-2024)',
  'Divisions de la 58e législature (17 décembre 2019 → 30 mai 2024), même source que « communes ». Tâche ponctuelle, longue (plusieurs milliers de divisions, ~25 min) : à lancer une fois, reprise possible après interruption. Pour la 57e : commande « communes --legislature 57 ».', 54, true, 'GB');

COMMIT;
