#!/bin/bash
# IlsOntVote - mise en place UNIQUE du site (à lancer en root, depuis le dossier contenant nginx-ilsontvote :
# /home/ubuntu/ftp/ilsontvote/deploy).
#  1. crée l'utilisateur et la base PostgreSQL « ilsontvote » (rien de partagé avec un autre site ; ignoré s'ils existent),
#  2. crée les dossiers du site, de l'import et des sauvegardes,
#  3. installe le site nginx et son certificat (les enregistrements DNS doivent déjà pointer vers ce serveur).
# Les tables sont créées par update-ilsontvote.sh (migrations db/*.sql). Ensuite : appsettings.Local.json, puis update-ilsontvote.sh.
set -e

# Domaine officiel et alias (redirigés vers lui). Ajouter ilsontvote.com / www.ilsontvote.com une fois achetés ET le DNS en place :
# certbot échoue si un nom ne pointe pas encore vers ce serveur.
CANONICAL=www.ilsontvote.org
ALIASES="ilsontvote.org ilsontvote.com www.ilsontvote.com"
HERE=$(cd "$(dirname "$0")" && pwd)

# 1. Base. Le mot de passe est demandé ici et jamais conservé par ce script : le mettre aussi dans appsettings.Local.json.
if sudo -u postgres psql -tAc "SELECT 1 FROM pg_roles WHERE rolname='ilsontvote'" | grep -q 1; then
  echo "L'utilisateur PostgreSQL ilsontvote existe déjà : laissé tel quel."
else
  sudo -u postgres createuser ilsontvote --pwprompt
fi
if sudo -u postgres psql -tAc "SELECT 1 FROM pg_database WHERE datname='ilsontvote'" | grep -q 1; then
  echo "La base ilsontvote existe déjà : laissée telle quelle."
else
  sudo -u postgres createdb -O ilsontvote ilsontvote
fi

# 2. Dossiers : le site, l'importeur, l'état de l'importeur (jeux téléchargés), les sauvegardes.
mkdir -p /var/www/ilsontvote_app /var/www/ilsontvote_importer /var/lib/ilsontvote-import /home/ubuntu/backups/ilsontvote
chown www-data:www-data /var/www/ilsontvote_app /var/www/ilsontvote_importer /var/lib/ilsontvote-import
chmod 700 /home/ubuntu/backups/ilsontvote
# Dossier des données ouvertes : écrit par l'import nocturne, lu par le site (tous deux sous www-data).
mkdir -p /var/lib/ilsontvote-export && chown www-data:www-data /var/lib/ilsontvote-export && chmod 755 /var/lib/ilsontvote-export

# 3. nginx + certificat. Les noms de domaine sont injectés dans le modèle.
sed -e "s/__CANONICAL__/$CANONICAL/g" -e "s/__ALIASES__/$ALIASES/g" "$HERE/nginx-ilsontvote" > /etc/nginx/sites-available/ilsontvote
ln -sf /etc/nginx/sites-available/ilsontvote /etc/nginx/sites-enabled/ilsontvote
nginx -t
systemctl reload nginx

DOMAINS="-d $CANONICAL"
for a in $ALIASES; do DOMAINS="$DOMAINS -d $a"; done
# --keep-until-expiring : un certificat encore valide est conservé sans poser de question (le script peut être relancé).
certbot --nginx --keep-until-expiring $DOMAINS

echo "Terminé. Maintenant : appsettings.Local.json (même mot de passe de base), puis update-ilsontvote.sh (voir DEPLOYMENT_SITE.fr.md)."
