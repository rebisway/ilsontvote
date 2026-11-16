#!/bin/bash
# IlsOntVote - remise à zéro des statistiques de fréquentation (sur la VM, en utilisateur ubuntu ; sudo pour postgres).
#   reset-stats.sh                  vide toute la table page_views (demande confirmation)
#   reset-stats.sh 2026-10-15       ne supprime que ce qui est AVANT cette date (UTC), garde le reste
#   reset-stats.sh --yes            sans question (utilisable avec une date : reset-stats.sh 2026-10-15 --yes)
# La table et le site ne changent pas : seules les lignes partent. Irréversible : faire un dump avant en cas de doute :
#   sudo -u postgres pg_dump -t page_views ilsontvote > page_views-backup.sql

DB=ilsontvote
AVANT=""
OUI=0
for arg in "$@"; do
  case "$arg" in
    --yes) OUI=1 ;;
    [0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]) AVANT="$arg" ;;
    *) echo "Argument inconnu : $arg"; echo "Usage : $0 [AAAA-MM-JJ] [--yes]"; exit 1 ;;
  esac
done

if [ -n "$AVANT" ]; then
  QUOI="supprimer les visites avant le $AVANT (UTC)"
  SQL="DELETE FROM page_views WHERE visited_at < '$AVANT'::date;"
else
  QUOI="supprimer TOUTES les visites"
  SQL="TRUNCATE page_views RESTART IDENTITY;"
fi

NB=$(sudo -u postgres psql -d "$DB" -tAc "SELECT COUNT(*) FROM page_views;") || { echo "Impossible de lire la base $DB."; exit 1; }
echo "Sur le point de $QUOI de la base $DB ($NB lignes actuellement)."

if [ "$OUI" -ne 1 ]; then
  read -r -p "Taper yes pour continuer : " REPONSE
  [ "$REPONSE" = "yes" ] || { echo "Annulé."; exit 1; }
fi

sudo -u postgres psql -d "$DB" -c "$SQL"
echo "Lignes restantes : $(sudo -u postgres psql -d "$DB" -tAc 'SELECT COUNT(*) FROM page_views;')"
