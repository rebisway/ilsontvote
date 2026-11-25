# Site `https://www.ilsontvote.org/` : publication et exploitation

Adresse officielle : `www.ilsontvote.org` ; `ilsontvote.org`, `ilsontvote.com` et `www.ilsontvote.com` (HTTP et HTTPS) y
redirigent en 301. nginx de référence après certbot : `deploy/nginx-ilsontvote.tls`.

Application **ASP.NET Core** (`src/IlsOntVote.Web`) avec **sa propre base PostgreSQL `ilsontvote`**, plus un **importeur**
(`src/IlsOntVote.Importer`) lancé chaque nuit. nginx n'est que la porte d'entrée (HTTPS, proxy vers `127.0.0.1:5295`).
Même VM que rebisway, FreeSpeech et openpostpro : vhost, service, port (5295) et base propres.

## Mise en place (une seule fois)

```bash
# DNS : enregistrements A/AAAA de ilsontvote.org et www.ilsontvote.org vers la VM
# PC : copier publish/, deploy/ et db/ dans /home/ubuntu/ftp/ilsontvote/ (voir « À chaque version »)
# VM : base + utilisateur (mot de passe demandé), dossiers, nginx, certificat
cd /home/ubuntu/ftp/ilsontvote/deploy && sudo bash setup-site.sh
# VM : secrets (modèle : deploy/appsettings.Local.example.json), remplir les <...>
sudo cp appsettings.Local.example.json /var/www/ilsontvote_app/appsettings.Local.json
sudo nano /var/www/ilsontvote_app/appsettings.Local.json
sudo chown www-data:www-data /var/www/ilsontvote_app/appsettings.Local.json && sudo chmod 600 /var/www/ilsontvote_app/appsettings.Local.json
# VM : migrations, programmes, services, démarrage, vérification
sudo bash /home/ubuntu/ftp/ilsontvote/deploy/update-ilsontvote.sh
# VM : premier remplissage de la base (long : plusieurs dizaines de minutes)
sudo -u www-data env DOTNET_ENVIRONMENT=Production ILSONVOTE_LOCAL_CONFIG=/var/www/ilsontvote_app/appsettings.Local.json \
  bash -c 'cd /var/lib/ilsontvote-import && /var/www/ilsontvote_importer/import-all.sh historique'
```

Pays des visites (facultatif, pour `/Admin/Stats`) : déposer `GeoLite2-Country.mmdb` (compte MaxMind gratuit, mis à jour chaque mois)
dans `/var/www/ilsontvote_app/App_Data/` (propriétaire `www-data`), puis `sudo systemctl restart ilsontvote`. Sans ce fichier le site fonctionne, le pays reste « ? ».

Détection des robots hébergés (facultatif, statistiques seulement) : déposer aussi `GeoLite2-ASN.mmdb` (même compte MaxMind) dans `App_Data/`. Les visites dont
le réseau appartient à un hébergeur cloud (Alibaba, Tencent, Amazon, OVH…) sont comptées comme robots ; rien n'est bloqué. Sans ce fichier, seul le User-Agent est utilisé.

Puis retirer `AdminBootstrapEmail` / `AdminBootstrapPassword` du fichier de secrets (l'administrateur existe) ; les autres
comptes se gèrent dans `/Admin/Utilisateurs`. Ajouter `ilsontvote.com` : modifier `ALIASES` dans `setup-site.sh` quand le DNS est prêt.

## À chaque version

```powershell
# PC (PowerShell) : publie le site et l'importeur, vérifie qu'aucun secret n'est dedans, copie sur la VM
.\deploy\publish.ps1 -Vm ubuntu@<vm>
# (sans -Vm : publication seule dans publish\ ; à copier ensuite avec deploy\ et db\ dans /home/ubuntu/ftp/ilsontvote/)
```
```bash
# VM
sudo bash /home/ubuntu/ftp/ilsontvote/deploy/update-ilsontvote.sh
```

Le script d'update : arrête le site, installe les services, copie les programmes (`rsync --delete`, secrets et `App_Data` intacts),
**applique les migrations `db/NNN_*.sql` manquantes**, redémarre, puis affiche `dotnet IlsOntVote.Web.dll check` : tables absentes, base
vide, aucun administrateur, import ancien… et `NON CONFIGURÉ` si quelque chose manque.

## Imports (chaque nuit à 4 h 30)

`ilsontvote-planificateur.timer` réveille chaque minute `IlsOntVote.Importer planificateur`, qui exécute les demandes faites dans
`/Admin/Imports` (bouton « Lancer maintenant ») et les créneaux du planning (jours + heure de Paris, plusieurs créneaux par import). Les imports
disponibles, leur description, le planning et le journal (avec la sortie console de chaque exécution) sont dans `/Admin/Imports` et
`/Admin/ImportsJournal`. Le planning initial (migration 026) reproduit l'ancien import nocturne : tout à 04:30, dans l'ordre des dépendances.
État : `systemctl list-timers ilsontvote-planificateur.timer` · journal système : `journalctl -u ilsontvote-planificateur.service`.
L'ancien `ilsontvote-import.timer` est désactivé par `update-ilsontvote.sh` (le laisser actif doublerait les imports). `import-all.sh` reste
utilisable à la main pour le premier remplissage (`historique`).
Équivalent crontab : `30 4 * * * www-data ...import-all.sh` (le timer évite d'avoir à le faire).

## Exploitation

| Besoin | Commande |
|---|---|
| Sauvegarde (14 jours gardés, dans `/home/ubuntu/backups/ilsontvote`) | `sudo bash deploy/backup-db.sh` (à mettre en crontab : `15 3 * * *`) |
| Remise à zéro des statistiques | `deploy/reset-stats.sh [AAAA-MM-JJ] [--yes]` |
| Migrations seules | `sudo bash deploy/migrate.sh` |
| État | `systemctl status ilsontvote ilsontvote-planificateur.timer` · `journalctl -u ilsontvote -n 50` |

## Règles

- Une migration appliquée ne se **réécrit jamais** (nouvelle migration) ; `migrate.sh` signale toute modification.
- Secrets uniquement dans `/var/www/ilsontvote_app/appsettings.Local.json` (jamais publié, jamais dans le dépôt).
- La base se restaure avec `pg_restore` (voir `backup-db.sh`) ; les données publiques se retéléchargent.
- Statistiques : jamais d'IP (empreinte HMAC avec clé `App_Data/stats.key`) ; si on change ce qui est compté, mettre à jour la page de confidentialité.
