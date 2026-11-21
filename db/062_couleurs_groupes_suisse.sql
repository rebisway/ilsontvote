-- Suisse : la source donne les couleurs de groupe en ARGB (#AARRGGBB). Lues comme RGBA par CSS, plusieurs étaient transparentes (arcs de groupe invisibles).
-- On retire l'octet d'opacité des couleurs à 9 caractères des chambres suisses.
UPDATE groupes SET couleur = '#' || substr(couleur, 4)
WHERE chambre IN ('CHNR', 'CHSR') AND length(couleur) = 9 AND left(couleur, 1) = '#';
