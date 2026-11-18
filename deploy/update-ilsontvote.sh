#!/bin/bash
# IlsOntVote - mise à jour sur la VM : à lancer avec sudo après avoir copié publish/, deploy/ et db/ dans $FTP/ilsontvote/
# (publish.ps1 -Vm ou publish.sh --vm le fait). appsettings.Local.json et App_Data restent où ils sont (jamais écrasés).

# Dépôt : « ftp » dans le dossier personnel de l'utilisateur qui lance sudo (celui qui a copié les fichiers).
FTP="$(getent passwd "${SUDO_USER:-ubuntu}" | cut -d: -f6)/ftp"
SRC="$FTP/ilsontvote"
APP=/var/www/ilsontvote_app
IMP=/var/www/ilsontvote_importer

# S'arrêter AVANT de toucher au service si le programme publié n'est pas là où il faut.
[ -f "$SRC/publish/web/IlsOntVote.Web.dll" ] || { echo "ERREUR : $SRC/publish/web/IlsOntVote.Web.dll introuvable. Copier publish/ (la dll directement dans publish/web) et relancer avec sudo."; exit 1; }
[ -f "$SRC/publish/importer/IlsOntVote.Importer.dll" ] || { echo "ERREUR : $SRC/publish/importer/IlsOntVote.Importer.dll introuvable."; exit 1; }
[ -d "$SRC/db" ] || { echo "ERREUR : $SRC/db introuvable (les migrations SQL doivent être copiées avec le reste)."; exit 1; }
[ -f "$SRC/publish/VERSION.txt" ] && echo "Version déployée : $(cat "$SRC/publish/VERSION.txt")"

systemctl stop ilsontvote.service 2>/dev/null

# Les fichiers de service déposés dans $SRC/deploy remplacent ceux installés ; le planificateur d'imports (chaque minute) est activé.
for u in ilsontvote.service ilsontvote-import.service ilsontvote-import.timer ilsontvote-planificateur.service ilsontvote-planificateur.timer; do
  [ -f "$SRC/deploy/$u" ] && cp "$SRC/deploy/$u" "/etc/systemd/system/$u"
done
systemctl daemon-reload
# L'ancien timer nocturne est remplacé par le planning (migration 026 : mêmes tâches à 04:30) : le laisser actif doublerait les imports.
systemctl disable --now ilsontvote-import.timer >/dev/null 2>&1
systemctl enable ilsontvote.service ilsontvote-planificateur.timer >/dev/null 2>&1

# Copie des programmes. rsync --delete : un fichier supprimé d'une version à l'autre (page, css) disparaît aussi du serveur.
# Exclus (donc jamais touchés) : les secrets et ce que le site crée lui-même (clés des cookies, clé des statistiques).
mkdir -p "$APP" "$IMP"
rsync -a --delete --exclude 'appsettings.Local.json' --exclude 'App_Data/' "$SRC/publish/web/" "$APP/"
rsync -a --delete "$SRC/publish/importer/" "$IMP/"
cp "$SRC/deploy/import-all.sh" "$IMP/import-all.sh"
mkdir -p "$APP/App_Data" /var/lib/ilsontvote-import

chown -R www-data:www-data "$APP" "$IMP" /var/lib/ilsontvote-import
chmod -R 755 "$APP" "$IMP"
chmod +x "$IMP/import-all.sh"
chmod 700 "$APP/App_Data"
[ -f "$APP/appsettings.Local.json" ] && chmod 600 "$APP/appsettings.Local.json"
[ -f "$APP/App_Data/stats.key" ] && chmod 600 "$APP/App_Data/stats.key"

# Migrations AVANT le démarrage : le site, au premier démarrage, a besoin des tables Identity pour créer l'administrateur.
bash "$SRC/deploy/migrate.sh" "$SRC/db" || { echo "!!!! MIGRATIONS EN ÉCHEC : le site n'est pas redémarré. Corriger puis relancer. !!!!"; exit 1; }

systemctl start ilsontvote.service
systemctl start ilsontvote-planificateur.timer

sleep 3
journalctl -u ilsontvote.service -n 30 --no-pager

# L'alarme : dire clairement ce qui manque encore (secrets, base vide, administrateur, import ancien). Les secrets vivent dans
# $APP/appsettings.Local.json ; le check lit tout avec l'environnement du service.
echo
( cd "$APP" && env $(systemctl show ilsontvote.service -p Environment --value) runuser -u www-data -- /usr/bin/dotnet IlsOntVote.Web.dll check ) \
  || echo "!!!! NON CONFIGURÉ : voir les lignes ci-dessus. Le site tourne mais refuse ce qui dépend des éléments manquants. !!!!"
