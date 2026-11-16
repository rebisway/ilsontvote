#!/usr/bin/env python3
"""
Traduction automatique fr -> en des titres de lois (OPUS-MT, modèle Helsinki-NLP/opus-mt-fr-en converti pour CTranslate2).

Pourquoi un script séparé : l'importeur (C#) lui passe un fichier JSON Lines et récupère un autre fichier JSON Lines ; aucun accès à la
base ici, aucun appel réseau pendant la traduction (tout tourne sur le processeur de la machine). Le site marque ces titres
« traduction automatique » : ils ne remplacent jamais le titre officiel français.

Usage : traduire_titres.py MODELE_DIR ENTREE.jsonl SORTIE.jsonl
  ENTREE : une ligne par titre : {"id": "...", "fr": "Projet de loi relatif à ..."}
  SORTIE : {"id": "...", "en": "Bill on ..."}

Installation (une fois) : pip install ctranslate2 sentencepiece ; modèle : voir docs/TRADUCTION.md
"""
import json
import os
import re
import sys

import ctranslate2
import sentencepiece as spm

TAILLE_LOT = 32          # titres traduits ensemble (compromis vitesse / mémoire)
LONGUEUR_MAX = 400       # un titre plus long est tronqué par le modèle : on l'envoie en entier mais on borne la sortie
FAISCEAU = 4             # recherche en faisceau : meilleure qualité qu'un choix glouton, pour un coût raisonnable


# Le modèle traduit « projet de loi » / « proposition de loi » de façons variées (Draft law, Proposal for a law, Bill…). On uniformise
# le DÉBUT du titre pour que la liste soit cohérente ; le reste de la phrase n'est pas touché.
NORMALISATIONS = [
    (re.compile(r"^(Draft|Proposed) organic (law|bill)(?=\W|$)", re.I), "Organic bill"),
    (re.compile(r"^Proposal for an organic law(?=\W|$)", re.I), "Organic bill"),
    (re.compile(r"^(Draft law|Proposal for a law|Proposed law|Law proposal)(?=\W|$)", re.I), "Bill"),
]


def normaliser(texte: str) -> str:
    for motif, remplacement in NORMALISATIONS:
        nouveau = motif.sub(remplacement, texte, count=1)
        if nouveau != texte:
            return nouveau
    return texte


def main(modele_dir: str, entree: str, sortie: str) -> int:
    source = spm.SentencePieceProcessor(model_file=os.path.join(modele_dir, "source.spm"))
    cible = spm.SentencePieceProcessor(model_file=os.path.join(modele_dir, "target.spm"))
    traducteur = ctranslate2.Translator(modele_dir, device="cpu", compute_type="int8", inter_threads=1,
                                        intra_threads=max(1, (os.cpu_count() or 2) - 1))

    with open(entree, encoding="utf-8") as f:
        lignes = [json.loads(l) for l in f if l.strip()]

    with open(sortie, "w", encoding="utf-8") as out:
        for debut in range(0, len(lignes), TAILLE_LOT):
            lot = lignes[debut:debut + TAILLE_LOT]
            jetons = [source.encode(x["fr"], out_type=str) + ["</s>"] for x in lot]
            resultats = traducteur.translate_batch(jetons, beam_size=FAISCEAU, max_decoding_length=LONGUEUR_MAX)
            for x, r in zip(lot, resultats):
                texte = normaliser(cible.decode(r.hypotheses[0]).strip())
                out.write(json.dumps({"id": x["id"], "en": texte}, ensure_ascii=False) + "\n")
            print(f"  {min(debut + TAILLE_LOT, len(lignes))}/{len(lignes)} titres traduits", flush=True)
    return 0


if __name__ == "__main__":
    if len(sys.argv) != 4:
        print(__doc__)
        sys.exit(2)
    sys.exit(main(*sys.argv[1:]))
