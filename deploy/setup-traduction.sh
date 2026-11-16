#!/bin/bash
# IlsOntVote - installation (UNE fois, en root sur la VM) de la traduction automatique des titres de lois (fr -> en) :
# environnement Python + modèle OPUS-MT (Helsinki-NLP/opus-mt-fr-en, licence Apache 2.0, converti pour CTranslate2).
#   sudo bash deploy/setup-traduction.sh
# Place nécessaire : ~1 Go de disque, ~1 Go de RAM pendant la traduction (processeur seul, aucun service permanent).
# Ensuite, la commande « traductions » de l'importeur (appelée par import-all.sh) traduit chaque nuit les NOUVEAUX titres.
# Sans cette installation, le site fonctionne : les titres restent en français dans l'interface anglaise.
set -e

DIR=/opt/ilsontvote-traduction
MODELE=$DIR/opus-mt-fr-en
BASE=https://huggingface.co/gaudi/opus-mt-fr-en-ctranslate2/resolve/main

[ "$(id -u)" -eq 0 ] || { echo "À lancer en root : sudo bash $0"; exit 1; }

# Sur Ubuntu serveur, « python3 -m venv » répond à --help même sans le paquet python3-venv : il crée alors un environnement SANS pip
# (« ensurepip is not available »). On teste donc ensurepip, qui est ce dont on a vraiment besoin.
python3 -c "import ensurepip" >/dev/null 2>&1 || apt-get install -y python3-venv

mkdir -p "$MODELE"
# Environnement incomplet d'un essai précédent (sans pip) : on le refait.
[ -x "$DIR/venv/bin/pip" ] || rm -rf "$DIR/venv"
[ -x "$DIR/venv/bin/python" ] || python3 -m venv "$DIR/venv"
"$DIR/venv/bin/pip" install --quiet --upgrade pip
"$DIR/venv/bin/pip" install --quiet ctranslate2 sentencepiece

for f in config.json model.bin shared_vocabulary.json source.spm target.spm; do
  if [ ! -s "$MODELE/$f" ]; then
    echo "Téléchargement de $f…"
    curl -fL --retry 5 -o "$MODELE/$f" "$BASE/$f"
  fi
done

# Lisible par www-data (qui lance l'import).
chown -R root:root "$DIR"; chmod -R a+rX "$DIR"

# Essai : une phrase.
"$DIR/venv/bin/python" - <<PY
import ctranslate2, sentencepiece as spm
src = spm.SentencePieceProcessor(model_file="$MODELE/source.spm")
tgt = spm.SentencePieceProcessor(model_file="$MODELE/target.spm")
tr = ctranslate2.Translator("$MODELE", device="cpu", compute_type="int8")
r = tr.translate_batch([src.encode("Projet de loi relatif à la protection des enfants", out_type=str) + ["</s>"]])
print("Essai :", tgt.decode(r[0].hypotheses[0]))
PY
echo "Traduction installée dans $DIR. Elle sera utilisée par l'import nocturne."
