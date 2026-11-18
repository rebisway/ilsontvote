-- Amendements déposés à l'Assemblée nationale, par dossier législatif : effectifs OFFICIELS (jeu « Amendements » de data.assemblee-nationale.fr,
-- 148 760 fichiers pour la 17e législature). On ne stocke que l'agrégat par dossier : la page /lois affiche des totaux, pas chaque amendement.
-- Rattachement à un texte : dossier_an = texte_etapes.dossier_an (même référence DLR5L…N…), cf. migration 014.
BEGIN;

CREATE TABLE amendements_dossiers (
    dossier_an     text PRIMARY KEY,              -- référence du dossier législatif AN, ex. DLR5L17N53886
    legislature    smallint NOT NULL,
    deposes        integer NOT NULL,              -- tous les amendements du dossier (quel que soit leur sort)
    adoptes        integer NOT NULL,              -- sort publié « Adopté »
    rejetes        integer NOT NULL,              -- « Rejeté »
    tombes         integer NOT NULL,              -- « Tombé » (devenu sans objet après un vote précédent)
    non_soutenus   integer NOT NULL,              -- « Non soutenu » (auteur absent à l'appel de l'amendement)
    retires        integer NOT NULL,              -- « Retiré » (sort ou état)
    irrecevables   integer NOT NULL,              -- état « Irrecevable… » (article 40, cavalier, etc.), jamais discutés
    releve_le      timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_amendements_dossiers_leg ON amendements_dossiers (legislature);

-- Catalogue du planificateur : l'import officiel (législature en cours) et son rattrapage (16e + 17e, lourd : 2 x ~360 Mo).
INSERT INTO import_taches (code, libelle, description, ordre, ponctuelle) VALUES
 ('amendements', 'Amendements de l''Assemblée : effectifs par dossier',
  'Jeu officiel « Amendements » (17e législature, ~365 Mo) : nombre d''amendements par dossier législatif et leur sort (adoptés, rejetés, tombés, non soutenus, retirés, irrecevables). Ne retélécharge que si le fichier a changé. APRÈS « Dossiers législatifs » (rattachement aux textes).', 45, false),
 ('amendements_historique', 'Amendements : 16e et 17e législatures',
  'Comme ci-dessus pour les deux législatures disponibles (~730 Mo à télécharger). Premier remplissage ou rattrapage.', 46, true);

-- Planning initial : chaque nuit à 04:30 comme les autres imports (ne retélécharge que si le fichier a changé).
INSERT INTO import_planning (tache, jours, heure, cree_par)
VALUES ('amendements', ARRAY[1,2,3,4,5,6,7]::smallint[], TIME '04:30', 'migration 028');

COMMIT;
