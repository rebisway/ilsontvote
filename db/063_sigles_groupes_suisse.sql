-- Suisse : sigles usuels des groupes (Verts, PS, PVL, Centre, PLR, UDC) à la place des lettres de la source (G, S, GL, M-E, RL, V).
-- L'import les réécrit à chaque passage ; cette migration corrige les lignes déjà en base. Identifiants de groupes inchangés.
UPDATE groupes g SET sigle = CASE split_part(g.id, '-', 3) || CASE WHEN split_part(g.id, '-', 4) <> '' THEN '-' || split_part(g.id, '-', 4) ELSE '' END
    WHEN 'G' THEN 'Verts' WHEN 'S' THEN 'PS' WHEN 'GL' THEN 'PVL' WHEN 'M-E' THEN 'Centre' WHEN 'RL' THEN 'PLR' WHEN 'V' THEN 'UDC'
    WHEN 'C' THEN 'PDC' WHEN 'BD' THEN 'PBD' WHEN 'CE' THEN 'Centre' WHEN 'R' THEN 'PLR' WHEN 'FRAKTIONSLOS' THEN 'SG' ELSE g.sigle END
WHERE g.chambre IN ('CHNR', 'CHSR');
