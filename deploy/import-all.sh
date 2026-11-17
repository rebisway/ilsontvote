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

# Étapes de lecture à l'Assemblée, avec ou sans scrutin public (Dossiers législatifs) : APRÈS « an » et « textes » (rattachement aux
# scrutins et aux textes). « historique » : législatures 15, 16 et 17 ; sinon la législature en cours.
echo "=== Dossiers législatifs (étapes sans scrutin public) ==="
if [ "$1" = "historique" ]; then $DOTNET "$IMP" dossiers --legislature all || rc=1; else $DOTNET "$IMP" dossiers || rc=1; fi

# Parlement européen (HowTheyVote) : incrémental, seuls les nouveaux votes sont lus (le tout premier passage est long : un appel par vote).
echo "=== Parlement européen ==="
$DOTNET "$IMP" ue || rc=1

# Pétitions du Sénat et de l'Assemblée (data.gouv.fr, mises à jour chaque lundi à la source). Le fichier est téléchargé chaque nuit (~12 Mo),
# mais un relevé n'est écrit que s'il a changé (empreinte) ; l'historique et les anomalies s'en déduisent. Rattrapage initial : « petitions --archives ».
echo "=== Pétitions (Sénat et Assemblée) ==="
$DOTNET "$IMP" petitions || rc=1

# Traduction automatique des NOUVEAUX titres seulement (mémoire en base) : fr -> en (interface /en) et en -> fr (votes européens sans titre
# français officiel). APRÈS le Parlement européen : le ré-import d'un vote remet son titre anglais d'origine, que la mémoire retraduit aussitôt.
# Facultatif : sans les modèles (voir deploy/setup-traduction.sh), l'étape est ignorée sans erreur.
export ILSONVOTE_PYTHON="${ILSONVOTE_PYTHON:-/opt/ilsontvote-traduction/venv/bin/python}"
export ILSONVOTE_MODELE_TRADUCTION="${ILSONVOTE_MODELE_TRADUCTION:-/opt/ilsontvote-traduction/opus-mt-fr-en}"
export ILSONVOTE_MODELE_TRADUCTION_EN_FR="${ILSONVOTE_MODELE_TRADUCTION_EN_FR:-/opt/ilsontvote-traduction/opus-mt-en-fr}"
echo "=== Traduction des titres ==="
$DOTNET "$IMP" traductions || rc=1

# Statistiques de participation (vue matérialisée) : à recalculer APRÈS les imports, qui en changent les données. Plusieurs minutes.
echo "=== Statistiques ==="
$DOTNET "$IMP" stats || rc=1

# Données ouvertes : fichiers CSV compressés téléchargeables sur /donnees (le site les lit dans ce dossier). Après tous les imports.
export ILSONVOTE_EXPORT_DIR="${ILSONVOTE_EXPORT_DIR:-/var/lib/ilsontvote-export}"
echo "=== Export des données ouvertes ==="
$DOTNET "$IMP" export || rc=1

exit $rc
