# Serveur MCP d'ilsontvote.org
Version anglaise : [MCP.md](MCP.md).

Permet à un assistant IA d'interroger les données du site (Model Context Protocol).

- Adresse : `https://www.ilsontvote.org/mcp` (local : `http://localhost:5310/mcp`)
- Transport : HTTP « streamable », sans état. Lecture seule, sans clé.
- Limite : 60 requêtes par minute et par adresse IP (réponse 429 au-delà).
- Périmètre : France, Union européenne et pays **actifs**, comme le site.

## Brancher un client

Ajouter `https://www.ilsontvote.org/mcp` comme serveur MCP HTTP distant dans tout client compatible, sans clé.

## Outils

| Outil | Rôle |
|---|---|
| `list_types` | Types de documents, filtres, pays, chambres, thèmes |
| `search` | Recherche paginée dans un type (50 résultats au plus) ; pétitions : `sort=date` ou `sort=signatures` |
| `get_document` | Fiche d'un document |
| `vote_positions` | Décompte par groupe et position de chaque élu (200 au plus par page) |
| `member_history` | Votes d'un élu, absences comprises |
| `import_status` | Dernière exécution et dernier succès de chaque import (sans détail d'erreur) |
| `data_quality` | Lacunes connues par chambre (dernier vote, décomptes incomplets, mandats, groupes) |

## Sources et licences

Les réponses de `search`, `get_document`, `vote_positions` et `member_history` portent un bloc `sources` pour les données
renvoyées (`list_types` les liste toutes) : producteur, licence, lien des conditions, attribution, `commercial_use` et
`share_alike` (`yes`, `no`, `unknown`), `status` (`verified`, `no_named_licence`, `unverified`), date de vérification de la licence (`licence_verified_on`, pas celle des données) et
dernier import réussi. Elles sont dans la table `licences` (migration 078) : mettre la ligne à jour après vérification.

## Types de documents

| Type | Contenu | Pays |
|---|---|---|
| `parliamentary_vote` | Scrutins publics | tous les pays actifs |
| `popular_vote` | Votations fédérales (résultats agrégés) | Suisse |
| `law` | Dossiers législatifs et leur parcours | France |
| `bill` | Bills | Royaume-Uni |
| `petition` | Pétitions parlementaires | France, Royaume-Uni |
| `member` | Membres des assemblées | tous les pays actifs |

## Ajouter un type de document

1. Écrire une classe qui implémente `ITypeDocument` (`src/IlsOntVote.Web/Mcp/Documents.cs`) : code, nom,
   description **avec les limites des données**, filtres, pays, recherche et fiche.
2. L'enregistrer dans `McpServeur.AjouterMcp`.

Règles : se limiter au périmètre publié (`Perimetre`) ; paramètres SQL uniquement ; une donnée manquante reste
`null` ; ce qui est calculé ou déduit est nommé ou annoté comme tel ; chaque document porte son `url`.

## Exploitation

Les appels d'outils sont comptés dans `/Admin/Stats` (table `mcp_appels`, migration 077 ; IP hachée seulement, 400 jours).

Chaque appel d'outil écrit une ligne `MCP : outil …` dans le journal (ni paramètres ni adresse IP) :

```
journalctl -u ilsontvote | grep "MCP : outil"
```

L'API (noms d'outils, paramètres, champs, messages) est en anglais, écrite dans le code : elle s'adresse aux assistants,
pas aux visiteurs, et ne passe pas par la table `traductions`.

## Déploiement

Le serveur fait partie de l'application web (`src/IlsOntVote.Web/Mcp/`) : déployer le site le déploie. Aucun service,
port, règle nginx ni migration en plus.

## Publier au registre officiel

`server.json` (racine du dépôt) décrit le serveur. Installer `mcp-publisher` (voir le guide de démarrage du registre), puis, dans Git Bash à la racine du dépôt, créer la clé et l'enregistrement DNS :

```bash
openssl genpkey -algorithm Ed25519 -out key.pem
echo "ilsontvote.org. IN TXT \"v=MCPv1; k=ed25519; p=$(openssl pkey -in key.pem -pubout -outform DER | tail -c 32 | base64)\""
```

Ajouter cet enregistrement TXT à la racine (apex) de `ilsontvote.org`, attendre la propagation, puis publier :

```bash
PRIVATE_KEY="$(openssl pkey -in key.pem -noout -text | grep -A3 "priv:" | tail -n +2 | tr -d ' :\n')"
./mcp-publisher.exe login dns --domain ilsontvote.org --private-key "$PRIVATE_KEY"
./mcp-publisher.exe publish
```

`key.pem` ne va jamais dans le dépôt. Augmenter `version` dans `server.json` avant de republier. Le registre est en préversion : vérifier sa documentation (modelcontextprotocol.io/registry).
