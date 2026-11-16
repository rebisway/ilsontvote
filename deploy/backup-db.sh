#!/bin/bash
# IlsOntVote - sauvegarde de la base (à lancer en root ; exemple de crontab : 15 3 * * * /home/ubuntu/ftp/ilsontvote/deploy/backup-db.sh).
# Format « custom » de pg_dump (compressé, restaurable table par table). Garde 14 jours. Les données publiques se retélécharge ;
# ce qu'il faut vraiment protéger : les comptes (AspNetUsers), les textes à la une (scrutins.featured*) et les statistiques.
#   Restauration :  sudo -u postgres pg_restore -d ilsontvote --clean --if-exists <fichier.dump>
set -e
DEST=/var/backups/ilsontvote
mkdir -p "$DEST"
FICHIER="$DEST/ilsontvote-$(date +%Y%m%d-%H%M).dump"
sudo -u postgres pg_dump -Fc ilsontvote > "$FICHIER"
chmod 600 "$FICHIER"
find "$DEST" -name 'ilsontvote-*.dump' -mtime +14 -delete
echo "Sauvegarde : $FICHIER ($(du -h "$FICHIER" | cut -f1))"
