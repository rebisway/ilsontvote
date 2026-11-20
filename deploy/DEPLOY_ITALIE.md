# Mise en ligne de l'Italie (Chambre + Sénat, interface italienne)

Prérequis : tout est commité et poussé. Procédure générale : `DEPLOYMENT_SITE.fr.md`.

1. **Publier et migrer** : `deploy\publish.ps1 -Vm ubuntu@<vm>` puis, sur la VM, `sudo bash .../deploy/update-ilsontvote.sh`.
   Applique les migrations manquantes. Vérifier que **044, 045, 046** (et 033-043 si la production ne les a pas encore) sont passées.
   Les pays restent **inactifs** : le site public ne change pas.
2. **Importer** (Admin → Imports, ou en ligne de commande sur la VM, dans cet ordre) :
   `italie` (~ quelques minutes, XIXe législature, votes finaux + confiance), `italie_senat`, puis `stats`, puis `export`.
   Historique facultatif : `italie_historique`, `italie_senat_historique` (législatures 17-18). `--tout` (amendements, ~7 M de votes) : à ne pas lancer sans décision.
3. **Contrôler avant d'activer** (Admin ou psql) : `SELECT chambre, count(*) FROM scrutins WHERE chambre LIKE 'IT%' GROUP BY 1;` (attendu : environ 479 Chambre, 263 Sénat pour la XIXe) ;
   page `/it` visible seulement après activation, donc tester d'abord en local ou via `?pays=it` n'est pas possible : activer, puis vérifier, retour arrière possible.
4. **Activer** : `sudo -u postgres psql ilsontvote -f deploy/activer-pays.sql` (cache 5 min). Vérifier `/it`, `/it/textes`, `/it/senatori`, `/it/donnees`, `/sitemap.xml` (liste `sitemap-it.xml`), le sélecteur de langue FR | EN | IT.
5. **Planning** : les tâches `italie` et `italie_senat` sont déjà au planning (chaque nuit 04:30, migrations 044-045) ; `stats` et `export` passent après dans l'ordre des dépendances.
6. **Retour arrière** : remettre `actif = false` (`pays` et `chambres`) ; aucune donnée n'est supprimée.

Points à garder en tête : licence **CC BY-SA 4.0** (Chambre : partage à l'identique, déclaré sur la page des données et dans le manifeste), **CC BY 3.0** (Sénat) ;
accès **GET seulement** au SPARQL du Sénat ; votes finaux seulement (limite affichée sur le site) ; traductions italiennes à relire (`/Admin/Traductions`, langue `it`).
