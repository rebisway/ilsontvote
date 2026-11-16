#!/bin/bash
# IlsOntVote - applique les migrations SQL (db/NNN_*.sql) qui ne l'ont pas encore été, dans l'ordre. En root, sur la VM.
#   migrate.sh [dossier-des-migrations]      (par défaut : ../db à côté de ce script, sinon /home/ubuntu/ftp/ilsontvote/db)
# Suivi dans la table schema_migrations (nom, empreinte SHA-256, date). Règle du projet : une migration appliquée ne se
# réécrit JAMAIS (toute correction = une nouvelle migration) ; si une migration appliquée a changé, le script le DIT et
# n'y touche pas. Les objets créés appartiennent à l'utilisateur « ilsontvote » (le site n'a donc besoin d'aucun GRANT).
set -e

DB=ilsontvote
ROLE=ilsontvote
HERE=$(cd "$(dirname "$0")" && pwd)
DIR="${1:-$HERE/../db}"
[ -d "$DIR" ] || DIR=/home/ubuntu/ftp/ilsontvote/db
[ -d "$DIR" ] || { echo "ERREUR : dossier des migrations introuvable ($DIR)."; exit 1; }

# psql en tant que postgres mais avec le rôle de session « ilsontvote » (PGOPTIONS) : le propriétaire des tables est le site.
sql() { sudo -u postgres env PGOPTIONS="-c role=$ROLE" psql -X -q -v ON_ERROR_STOP=1 -d "$DB" "$@"; }

sql -c "CREATE TABLE IF NOT EXISTS schema_migrations (nom text PRIMARY KEY, empreinte text NOT NULL, appliquee_le timestamptz NOT NULL DEFAULT now());"

# Base créée à la main avant ce script (suivi vide mais tables présentes) : on enregistre les migrations comme déjà
# appliquées au lieu de les rejouer (« relation already exists »). Seules les migrations NOUVELLES seront ensuite jouées.
if [ "$(sql -tAc "SELECT count(*) FROM schema_migrations")" = "0" ] && [ "$(sql -tAc "SELECT to_regclass('public.groupes') IS NOT NULL")" = "t" ]; then
  echo "Base existante sans suivi : migrations présentes enregistrées comme déjà appliquées."
  for f in $(ls "$DIR"/[0-9][0-9][0-9]_*.sql | sort); do
    sql -c "INSERT INTO schema_migrations (nom, empreinte) VALUES ('$(basename "$f")', '$(tr -d '\015' < "$f" | sha256sum | cut -d' ' -f1)') ON CONFLICT DO NOTHING;"
  done
fi

appliquees=0
for f in $(ls "$DIR"/[0-9][0-9][0-9]_*.sql | sort); do
  nom=$(basename "$f")
  # Empreinte calculée sur le contenu SANS retour chariot (fins de ligne Linux) : identique que le fichier ait été édité sous Windows ou non.
  empreinte=$(tr -d '\015' < "$f" | sha256sum | cut -d' ' -f1)
  # Anciennes empreintes enregistrées avant cette règle : fichier tel quel (CRLF éventuel) : on les accepte aussi.
  empreinte_brute=$(sha256sum "$f" | cut -d' ' -f1)
  empreinte_crlf=$(tr -d '\015' < "$f" | sed 's/$/\r/' | sha256sum | cut -d' ' -f1)
  connue=$(sql -tAc "SELECT empreinte FROM schema_migrations WHERE nom = '$nom'")

  if [ -z "$connue" ]; then
    echo "-> application de $nom"
    # Lu par root puis passé en entrée standard : l'utilisateur « postgres » n'a pas le droit de lire /home/ubuntu (erreur « droit d'accès » avec -f).
    sql < "$f"
    sql -c "INSERT INTO schema_migrations (nom, empreinte) VALUES ('$nom', '$empreinte');"
    appliquees=$((appliquees + 1))
  elif [ "$connue" != "$empreinte" ] && [ "$connue" != "$empreinte_brute" ] && [ "$connue" != "$empreinte_crlf" ]; then
    echo "!! ATTENTION : $nom a été modifiée depuis son application (empreinte différente). Non rejouée : créer une NOUVELLE migration."
  fi
done

echo "Migrations : $appliquees appliquée(s)."
