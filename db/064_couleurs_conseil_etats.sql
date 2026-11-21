-- Suisse, Conseil des États : l'API ne donne pas de couleur ; les groupes avaient reçu des couleurs de palette sans rapport avec les partis (PS vert, Verts rouge).
-- On reprend les couleurs du Conseil national pour le même groupe ; groupe inconnu = gris neutre.
-- Code du groupe = ce qui suit « CHSR-{législature}- » dans l'identifiant (le code « M-E » contient un tiret).
UPDATE groupes SET couleur = CASE substr(id, length('CHSR-') + position('-' in substr(id, length('CHSR-') + 1)) + 1)
    WHEN 'G' THEN '#90EE90' WHEN 'S' THEN '#FF0000' WHEN 'GL' THEN '#EE82EE' WHEN 'M-E' THEN '#FFA500' WHEN 'RL' THEN '#00BFFF' WHEN 'V' THEN '#006400' ELSE '#8a8f98' END
WHERE chambre = 'CHSR';
