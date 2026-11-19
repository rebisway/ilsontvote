-- Table de traduction de l'interface.
-- Clé = le texte FRANÇAIS tel qu'il est écrit dans la page (L.T("Lois", "Laws")) : le code reste lisible et le français sert de référence. Une ligne (cle, langue) remplace le texte
-- écrit dans le code pour cette langue ; sans ligne, le code s'applique (français, anglais), et pour une langue sans traduction (espagnol, italien…) l'anglais sert de repli.
-- Ajouter une langue = ajouter des lignes (page /Admin/Traductions), sans toucher au code ni redéployer. Les DONNÉES (titres de lois, noms de groupes) ne passent pas par là.
BEGIN;

CREATE TABLE traductions (
    cle     text NOT NULL,
    langue  text NOT NULL CHECK (langue ~ '^[a-z]{2}$'),
    texte   text NOT NULL,
    maj     timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (langue, cle)
);

COMMIT;
