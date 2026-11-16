#!/bin/bash
# IlsOntVote - import des données : Assemblée nationale, Sénat, textes de loi. Lancé chaque nuit par ilsontvote-import.timer
# (utilisateur www-data) ; à la main :
#   sudo systemctl start ilsontvote-import.service     import normal (législature en cours + Sénat + textes)
#   premier remplissage (historique complet), à la main :
#   sudo -u www-data env DOTNET_ENVIRONMENT=Production ILSONVOTE_LOCAL_CONFIG=/var/www/ilsontvote_app/appsettings.Local.json \
#        bash -c 'cd /var/lib/ilsontvote-import && /var/www/ilsontvote_importer/import-all.sh historique'
# « historique » : recharge aussi les législatures 15 et 16 (premier remplissage de la base ; à refaire seulement si la source change).
# Code de sortie non nul si UNE étape a échoué (les suivantes sont quand même tentées) : systemd le signale.

# Lancé à la main en root ou ubuntu : on se relance en www-data (propriétaire des données téléchargées) avec le bon dossier de
# travail et le fichier de secrets. Sous systemd (déjà www-data, variables posées par le service), rien de tout cela ne change.
if [ "$(id -un)" != "www-data" ]; then
  exec sudo -u www-data env DOTNET_ENVIRONMENT=Production \
    ILSONVOTE_LOCAL_CONFIG="${ILSONVOTE_LOCAL_CONFIG:-/var/www/ilsontvote_app/appsettings.Local.json}" \
    bash -c 'cd /var/lib/ilsontvote-import && exec /var/www/ilsontvote_importer/import-all.sh "$@"' _ "$@"
fi

IMP=/var/www/ilsontvote_importer/IlsOntVote.Importer.dll
DOTNET=/usr/bin/dotnet
rc=0

if [ "$1" = "historique" ]; then AN_ARGS="an --legislature all"; else AN_ARGS="an"; fi

echo "=== Assemblée nationale ($AN_ARGS) ==="
$DOTNET "$IMP" $AN_ARGS || rc=1

echo "=== Sénat ==="
$DOTNET "$IMP" senat || rc=1

# « textes » réutilise le dump déjà téléchargé par « senat » (16 Mo compressés, 130 Mo décompressés) au lieu de le reprendre.
DOSLEG=/var/lib/ilsontvote-import/data/senat/dosleg/dosleg.sql
echo "=== Textes de loi ==="
if [ -f "$DOSLEG" ]; then $DOTNET "$IMP" textes --dosleg-sql "$DOSLEG" || rc=1; else $DOTNET "$IMP" textes || rc=1; fi

# Parlement européen (HowTheyVote) : incrémental, seuls les nouveaux votes sont lus (le tout premier passage est long : un appel par vote).
echo "=== Parlement européen ==="
$DOTNET "$IMP" ue || rc=1

exit $rc
