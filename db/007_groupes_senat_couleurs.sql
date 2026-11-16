-- 007 : ordre et couleurs des anciens groupes du Sénat (importés avec des valeurs par défaut).
BEGIN;

UPDATE groupes g SET couleur = v.couleur, ordre = v.ordre
  FROM (VALUES
      ('ECO',   '#77AA79',  30),
      ('RTLI',  '#5DADE2',  65),
      ('RDE',   '#D9A441',  55),
      ('GD',    '#D9A441',  55),
      ('C',     '#830E21',  10),
      ('UCDP',  '#E39B5B',  75),
      ('AUCUN', '#8D949A', 130)
  ) AS v(sigle, couleur, ordre)
 WHERE g.chambre = 'SENAT' AND g.sigle = v.sigle;

COMMIT;
