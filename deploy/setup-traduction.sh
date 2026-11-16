#!/bin/bash
# IlsOntVote - installation (UNE fois, en root sur la VM) de la traduction automatique des titres :
# environnement Python + deux modèles OPUS-MT (Helsinki-NLP, licence Apache 2.0, convertis pour CTranslate2) :
#   fr -> en : titres de lois et de votes pour l'interface anglaise ;
#   en -> fr : titres des votes du Parlement européen sans titre français officiel.
#   sudo bash deploy/setup-traduction.sh
# Place nécessaire : ~1,2 Go de disque, ~1 Go de RAM pendant la traduction (processeur seul, aucun service permanent).
# Ensuite, la commande « traductions » de l'importeur (appelée par import-all.sh) traduit chaque nuit les NOUVEAUX titres.
# Sans cette installation, le site fonctionne : les titres restent en français dans l'interface anglaise.
set -e

DIR=/opt/ilsontvote-traduction
# (nom du dossier : dépôt Hugging Face). Le dépôt « gaudi » d'en-fr n'a pas model.bin : on prend une autre conversion CTranslate2 d'OPUS-MT en-fr.
MODELE=$DIR/opus-mt-fr-en
BASE=https://huggingface.co/gaudi/opus-mt-fr-en-ctranslate2/resolve/main
MODELE_EN_FR=$DIR/opus-mt-en-fr
BASE_EN_FR=https://huggingface.co/manancode/opus-mt-en-fr-ctranslate2-android/resolve/main

[ "$(id -u)" -eq 0 ] || { echo "À lancer en root : sudo bash $0"; exit 1; }

# Sur Ubuntu serveur, « python3 -m venv » répond à --help même sans le paquet python3-venv : il crée alors un environnement SANS pip
# (« ensurepip is not available »). On teste donc ensurepip, qui est ce dont on a vraiment besoin.
python3 -c "import ensurepip" >/dev/null 2>&1 || apt-get install -y python3-venv

mkdir -p "$MODELE" "$MODELE_EN_FR"
# Environnement incomplet d'un essai précédent (sans pip) : on le refait.
[ -x "$DIR/venv/bin/pip" ] || rm -rf "$DIR/venv"
[ -x "$DIR/venv/bin/python" ] || python3 -m venv "$DIR/venv"
"$DIR/venv/bin/pip" install --quiet --upgrade pip
"$DIR/venv/bin/pip" install --quiet ctranslate2 sentencepiece

for f in config.json model.bin shared_vocabulary.json source.spm target.spm; do
  if [ ! -s "$MODELE/$f" ]; then
    echo "Téléchargement fr-en : $f…"
    curl -fL --retry 5 -o "$MODELE/$f" "$BASE/$f"
  fi
  if [ ! -s "$MODELE_EN_FR/$f" ]; then
    echo "Téléchargement en-fr : $f…"
    curl -fL --retry 5 -o "$MODELE_EN_FR/$f" "$BASE_EN_FR/$f"
  fi
done

# Lisible par www-data (qui lance l'import).
chown -R root:root "$DIR"; chmod -R a+rX "$DIR"

# Essai : une phrase par sens.
essai() {  # $1 = dossier du modèle, $2 = phrase
"$DIR/venv/bin/python" - "$1" "$2" <<PY
import sys, ctranslate2, sentencepiece as spm
m = sys.argv[1]
src = spm.SentencePieceProcessor(model_file=m + "/source.spm")
tgt = spm.SentencePieceProcessor(model_file=m + "/target.spm")
tr = ctranslate2.Translator(m, device="cpu", compute_type="int8")
r = tr.translate_batch([src.encode(sys.argv[2], out_type=str) + ["</s>"]])
print("Essai :", tgt.decode(r[0].hypotheses[0]))
PY
}
essai "$MODELE" "Projet de loi relatif à la protection des enfants"
essai "$MODELE_EN_FR" "Programme for agile and rapid defence innovation"
echo "Traduction installée dans $DIR. Elle sera utilisée par l'import nocturne."
