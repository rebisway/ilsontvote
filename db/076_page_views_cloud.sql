-- Statistiques : troisième catégorie « cloud » (visite venant d'un hébergeur, détectée par la base GeoLite2 ASN) distincte des robots
-- déclarés (is_bot = User-Agent). Un lecteur = ni l'un ni l'autre. Les visites déjà enregistrées restent à false (réseau inconnu :
-- l'adresse n'est pas conservée).
ALTER TABLE page_views ADD COLUMN IF NOT EXISTS est_cloud boolean NOT NULL DEFAULT false;
