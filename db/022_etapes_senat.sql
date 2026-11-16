-- 022 : décisions de séance publique du SÉNAT dans les étapes d'un texte (avec ou sans scrutin public).
--
-- La table texte_etapes ne contenait que l'Assemblée nationale (Dossiers législatifs de l'AN). On y ajoute les décisions du Sénat tirées de
-- la base DOSLEG (lecture + texte adopté / rejeté), pour montrer aussi les lois adoptées au Sénat SANS scrutin public (main levée).
--   chambre 'AN' (défaut) ou 'SENAT' ; dossier_an est vide pour le Sénat ; code_acte = SEN<type de lecture>-DEC (SEN1-DEC = 1re lecture...).

ALTER TABLE texte_etapes ADD COLUMN chambre text NOT NULL DEFAULT 'AN';
ALTER TABLE texte_etapes ALTER COLUMN dossier_an DROP NOT NULL;
CREATE INDEX ix_texte_etapes_texte_chambre ON texte_etapes (texte_id, chambre);
