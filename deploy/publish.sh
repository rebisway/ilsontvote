#!/bin/bash
# IlsOntVote - publication depuis un poste Linux/macOS/Git Bash (équivalent de publish.ps1).
#   deploy/publish.sh                    publie dans publish/ (web + importer)
#   deploy/publish.sh --vm ubuntu@ma-vm  publie PUIS copie publish/, deploy/ et db/ dans /home/ubuntu/ftp/ilsontvote/
# Ensuite, sur la VM : sudo bash /home/ubuntu/ftp/ilsontvote/deploy/update-ilsontvote.sh
set -e
cd "$(dirname "$0")/.."

VM=""
DISTANT=/home/ubuntu/ftp/ilsontvote
[ "$1" = "--vm" ] && VM="$2"

rm -rf publish
dotnet publish src/IlsOntVote.Web/IlsOntVote.Web.csproj -c Release -o publish/web
dotnet publish src/IlsOntVote.Importer/IlsOntVote.Importer.csproj -c Release -o publish/importer

# Garde-fou : aucun secret, modèle ou clé dans la publication.
INTERDITS=$(find publish -type f \( -name 'appsettings.Development.json' -o -name 'appsettings.Local.json' -o -name 'appsettings.Production.json' \
  -o -name '*.example.json' -o -name 'stats.key' -o -path '*/App_Data/*' \))
if [ -n "$INTERDITS" ]; then
  echo "INTERDIT dans la publication :"; echo "$INTERDITS"; exit 1
fi

echo "$(date '+%Y-%m-%d %H:%M') ($(git rev-parse --short HEAD 2>/dev/null || echo sans-commit))" > publish/VERSION.txt
echo "Publication prête : $(pwd)/publish (web + importer)."

if [ -n "$VM" ]; then
  ssh "$VM" "mkdir -p $DISTANT && rm -rf $DISTANT/publish $DISTANT/deploy $DISTANT/db"
  scp -r publish deploy db "$VM:$DISTANT/"
  echo "Copié sur $VM. Maintenant, sur la VM : sudo bash $DISTANT/deploy/update-ilsontvote.sh"
else
  echo "Copier publish/, deploy/ et db/ dans $DISTANT/ sur la VM, puis : sudo bash $DISTANT/deploy/update-ilsontvote.sh"
fi
