# Traduction des titres en anglais

L'interface anglaise (`/en`) affiche les **titres de lois en anglais**. Deux origines, toujours distinguées :

| Titre | Origine | Affichage |
|---|---|---|
| Votes du Parlement européen | titre anglais publié par HowTheyVote.eu (donnée source) | tel quel |
| Lois et votes français | **traduction automatique** du titre français (OPUS-MT) | marqué « automatic translation », titre officiel français en infobulle |

Le titre officiel reste le titre français (c'est lui qu'indiquent les données structurées et la recherche en français). Un titre
sans traduction s'affiche en français : le site n'invente rien.

## Comment ça marche

1. `ImportRepository.LireTitresATraduireAsync` liste les titres français affichés sur le site qui n'ont pas encore de traduction
   (table `traductions_titres` = mémoire des traductions déjà faites).
2. La commande `IlsOntVote.Importer traductions` les écrit dans un fichier JSON Lines et lance `tools/traduire_titres.py`
   (modèle **Helsinki-NLP/opus-mt-fr-en**, converti pour CTranslate2, sur processeur, sans réseau ni service permanent).
3. Les résultats sont enregistrés dans `traductions_titres`, puis copiés dans `textes.titre_en` / `scrutins.titre_en`
   avec `titre_en_auto = true`.
4. `import-all.sh` lance cette étape chaque nuit : seuls les **nouveaux** titres sont traduits (quelques secondes).

L'étape est **facultative** : sans le modèle ou sans Python, elle est ignorée et l'import n'échoue pas.

## Sens inverse : titres européens en français (en -> fr)

Les votes du Parlement européen ont un titre français **officiel** (portail du Parlement) quand ils sont rattachés à une procédure. Les
autres (objections, certaines résolutions) n'en ont pas et restaient en anglais dans l'interface française. L'étape `traductions` les traduit
aussi, avec le modèle `opus-mt-en-fr` :

- **Incrémental** : mémoire `traductions_titres_fr` (clé : titre anglais) ; seuls les NOUVEAUX titres passent par le modèle.
- Le ré-import quotidien d'un vote remet le titre anglais de la source : l'étape (placée **après** `ue` dans `import-all.sh`) le retraduit
  aussitôt depuis la mémoire, sans relancer le modèle.
- Signalé : `scrutins.titre_fr_auto`, étiquette « traduction automatique » et titre anglais d'origine (infobulle et note sous le titre).
- Variable : `ILSONVOTE_MODELE_TRADUCTION_EN_FR` (défaut `/opt/ilsontvote-traduction/opus-mt-en-fr` en production).

## Installation sur la VM (une fois)

```bash
sudo bash deploy/setup-traduction.sh      # Python + CTranslate2 + modèle (~1 Go), puis un essai de traduction
sudo /var/www/ilsontvote_importer/import-all.sh    # la première fois : traduit ~4 000 titres (quelques minutes)
```

Variables (valeurs par défaut posées par `import-all.sh`) :

| Variable | Défaut |
|---|---|
| `ILSONVOTE_PYTHON` | `/opt/ilsontvote-traduction/venv/bin/python` |
| `ILSONVOTE_MODELE_TRADUCTION` | `/opt/ilsontvote-traduction/opus-mt-fr-en` |

## En développement

```bash
python -m venv venv && venv/bin/pip install ctranslate2 sentencepiece
# modèle : voir la liste de fichiers dans deploy/setup-traduction.sh (config.json, model.bin, shared_vocabulary.json, source.spm, target.spm)
ILSONVOTE_PYTHON=venv/bin/python ILSONVOTE_MODELE_TRADUCTION=./models/opus-mt-fr-en \
  dotnet run --project src/IlsOntVote.Importer -- traductions --limit 50
```

## Limites

La traduction automatique est correcte sur des titres formulaires (« Projet de loi relatif à… ») mais peut se tromper sur un terme
juridique ou un néologisme (« pédocriminalité » donne « child crime »). C'est la raison du marquage et de l'infobulle. Pour corriger
un titre à la main : modifier `traductions_titres.en` (puis `UPDATE` des tables, ou retirer la ligne pour la retraduire).
