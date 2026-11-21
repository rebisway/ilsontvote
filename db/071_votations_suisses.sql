-- Votations populaires fédérales suisses (jeu Swissvotes, Année Politique Suisse, Université de Berne ; licence CC BY 4.0) : résultats AGRÉGÉS, aucun vote individuel.
-- Périmètre : depuis l'an 2000 (choix de Thomas). Une ligne par objet soumis au vote ; les objets à venir n'ont pas encore de résultat (colonnes NULL).
CREATE TABLE IF NOT EXISTS votations (
    anr             integer PRIMARY KEY,            -- numéro de la votation dans Swissvotes
    date_votation   date NOT NULL,
    titre_fr        text NOT NULL,
    titre_de        text NOT NULL,
    titre_en        text,
    rechtsform      smallint NOT NULL,              -- 1 référendum obligatoire, 2 référendum facultatif, 3 initiative populaire, 4 contre-projet, 5 question subsidiaire
    accepte         boolean,                        -- NULL = pas de résultat (à venir) ou valeur non interprétable
    peuple_accepte  boolean,                        -- majorité du peuple
    cantons_accepte boolean,                        -- majorité des cantons (sans objet pour un référendum facultatif)
    participation   numeric(5,2),                   -- % des ayants droit
    oui_pct         numeric(5,2),                   -- % de oui, peuple
    oui             integer,
    non             integer,
    cantons_oui     numeric(4,1),                   -- cantons (les demi-cantons comptent 0,5)
    cantons_non     numeric(4,1),
    oui_par_canton  jsonb,                          -- {"zh": 41.2, …} % de oui par canton
    url             text,
    maj             timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_votations_date ON votations (date_votation DESC);

INSERT INTO import_taches (code, libelle, description, ordre, ponctuelle, pays) VALUES
    ('votations', 'Votations populaires suisses (Swissvotes)',
     'Résultats des votations fédérales depuis 2000 : jeu de données Swissvotes (CSV, CC BY 4.0). Mise à jour mensuelle suffisante.', 68, false, 'CH')
ON CONFLICT (code) DO NOTHING;
