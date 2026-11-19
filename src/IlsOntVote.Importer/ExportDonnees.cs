using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;

namespace IlsOntVote.Importer;

/// <summary>Un jeu de données publié en téléchargement : fichier CSV compressé, produit par une requête SQL (colonnes nommées, sans photo).</summary>
public sealed record JeuDonnees(string Nom, string Description, string Sql, string Pays = "FR");

/// <summary>Une ligne du manifeste (manifest.json) lu par la page « Données ouvertes ».</summary>
/// <summary><c>Pays</c> : « FR » (France et Union européenne, fichiers sans préfixe) ou « GB » (fichiers « gb_… ») ; chaque site n'affiche que les fichiers de son pays.</summary>
public sealed record EntreeManifeste(string Fichier, string Description, long Lignes, long Octets, string Sha256, DateTime GenereLe, string Pays = "FR");

/// <summary>
/// Export des données du site en fichiers téléchargeables (CSV UTF-8 virgule, compressés en gzip), régénérés à chaque import.
/// Pourquoi des fichiers précalculés : le jeu des votes dépasse 4 millions de lignes ; le produire à la demande chargerait la base et le
/// serveur web. Règles : fichier écrit sous un nom temporaire puis renommé (jamais de fichier à moitié écrit servi) ; les absents ne sont PAS
/// exportés (ils ne sont pas stockés : ils se déduisent des mandats et des scrutins, cf. LISEZMOI) ; aucune photo (droits).
/// </summary>
public static class ExportDonnees
{
    /// <summary>Jeux exportés. Les colonnes suivent le schéma de la base, sans les champs éditoriaux internes (à la une, titres normalisés).</summary>
    /// <summary>
    /// Filtre commun aux jeux qui portent sur des chambres : seules les chambres PUBLIÉES (<c>chambres.actif</c>) sont exportées. Une chambre en préparation
    /// (Communes avant l'ouverture du site britannique) ne doit pas apparaître dans les données ouvertes publiques.
    /// </summary>
    /// <summary>Plateformes de pétitions publiées : les françaises toujours ; celle du Parlement britannique (UK) seulement quand le pays GB est actif (/Admin/Pays).</summary>
    // Données PAR PAYS : le site français ne propose que les données de la France et de l'Union européenne, le site britannique que celles du Royaume-Uni (fichiers « gb_… ») ;
    // chaque jeu est écrit une fois, pour la France, puis décliné pour le Royaume-Uni en remplaçant ces deux filtres (voir <see cref="PourRoyaumeUni"/>).
    private const string PlateformesPubliees = "chambre IN ('AN', 'SENAT')";
    private const string PlateformesGb = "(chambre = 'UK' AND EXISTS (SELECT 1 FROM pays WHERE code = 'GB' AND actif))";

    private const string ChambresActives = "chambre IN (SELECT code FROM chambres WHERE actif AND pays IN ('FR', 'EU'))";
    private const string ChambresGb = "chambre IN (SELECT code FROM chambres WHERE actif AND pays = 'GB')";

    /// <summary>Jeux communs déclinés pour le Royaume-Uni (mêmes colonnes, autres chambres) ; les autres sont propres à la France.</summary>
    private static readonly string[] JeuxCommuns = ["groupes", "parlementaires", "mandats", "appartenances_groupes", "scrutins", "votes", "petitions", "petitions_historique", "petitions_anomalies"];

    private static readonly Dictionary<string, string> DescriptionsGb = new()
    {
        ["groupes"] = "Partis (groupes) de la Chambre des Communes et de la Chambre des Lords.",
        ["parlementaires"] = "Députés britanniques et pairs (identité, circonscription pour les députés).",
        ["mandats"] = "Périodes de mandat. Pour les Communes : du premier au dernier vote où le député figure (calculé) ; pour les Lords : dates d'entrée et de sortie de l'annuaire des membres.",
        ["appartenances_groupes"] = "Appartenance d'un membre à un parti, avec dates (pour les pairs : le dernier parti connu).",
        ["scrutins"] = "Divisions (votes enregistrés) des Communes et des Lords : décompte, résultat calculé, projet de loi rattaché (bill_id, calculé d'après le titre) ; sur_ensemble = troisième lecture aux Communes (calculé).",
        ["votes"] = "Vote de chaque membre à chaque division (pour = aye / content, contre = no / not content, abstention = présent dans les deux couloirs). Les absents ne figurent pas dans ce fichier.",
        ["petitions"] = "Pétitions de petition.parliament.uk : titre, statut publié, soutiens, réponse du gouvernement et débat.",
        ["petitions_historique"] = "Relevés successifs du nombre de soutiens et du statut de chaque pétition (un relevé par jour de changement).",
        ["petitions_anomalies"] = "Constats entre deux relevés : baisse du nombre de soutiens, disparition ou réapparition d'une pétition. Sans cause connue.",
    };

    /// <summary>Déclinaison britannique d'un jeu commun : mêmes colonnes, chambres et plateformes du Royaume-Uni, fichier « gb_… ».</summary>
    private static JeuDonnees PourRoyaumeUni(JeuDonnees j) => new("gb_" + j.Nom, DescriptionsGb.GetValueOrDefault(j.Nom, j.Description),
        j.Sql.Replace(ChambresActives, ChambresGb).Replace(PlateformesPubliees, PlateformesGb)
            .Replace("nominatif_incomplet, texte_id, texte_lie_id", "nominatif_incomplet, bill_id, bill_etape"), "GB");   // projet de loi britannique rattaché (calculé) au lieu du texte français

    /// <summary>Tous les jeux publiés : France (sans préfixe), puis Royaume-Uni (« gb_… »).</summary>
    public static IReadOnlyList<JeuDonnees> Tous => [.. Jeux.Where(j => j.Pays == "FR" && j.Nom is not ("bills" or "bill_etapes")),
        .. Jeux.Where(j => JeuxCommuns.Contains(j.Nom)).Select(PourRoyaumeUni),
        .. Jeux.Where(j => j.Nom is "bills" or "bill_etapes").Select(j => j with { Nom = "gb_" + j.Nom, Pays = "GB" })];

    public static readonly IReadOnlyList<JeuDonnees> Jeux =
    [
        new("groupes", "Groupes politiques (un identifiant par législature pour l'Assemblée).",
            "SELECT id, chambre, sigle, libelle, legislature FROM groupes WHERE " + ChambresActives + " ORDER BY chambre, legislature, sigle"),
        new("parlementaires", "Députés, sénateurs et eurodéputés (identité, département ou pays, circonscription).",
            "SELECT id, chambre, civilite, prenom, nom, slug, departement, circonscription FROM parlementaires WHERE " + ChambresActives + " ORDER BY chambre, nom, prenom"),
        new("mandats", "Périodes de mandat (début = prise de fonction ; fin vide = en cours).",
            "SELECT parlementaire_id, legislature, date_debut, date_fin FROM mandats WHERE parlementaire_id IN (SELECT id FROM parlementaires WHERE " + ChambresActives + ") ORDER BY parlementaire_id, date_debut"),
        new("appartenances_groupes", "Appartenance d'un élu à un groupe, avec dates.",
            "SELECT parlementaire_id, groupe_id, date_debut, date_fin FROM groupe_appartenances WHERE parlementaire_id IN (SELECT id FROM parlementaires WHERE " + ChambresActives + ") ORDER BY parlementaire_id, date_debut"),
        new("scrutins", "Scrutins publics : décompte, résultat, texte rattaché (texte_id : vote sur l'ensemble ; texte_lie_id : amendement ou article).",
            $"""
            SELECT id, chambre, legislature, numero, date_scrutin, titre, titre_fr_auto, titre_en, titre_en_auto, type_vote_libelle, sort, sur_ensemble,
                   nb_votants, nb_pour, nb_contre, nb_abstentions, nb_non_votants, suffrages_exprimes, suffrages_requis, nominatif_incomplet, texte_id, texte_lie_id
              FROM scrutins WHERE {ChambresActives} ORDER BY date_scrutin, chambre, numero
            """),
        new("votes", "Vote de chaque élu à chaque scrutin public (pour, contre, abstention, non_votant). Les absents ne figurent pas dans ce fichier.",
            $"""
            SELECT scrutin_id, parlementaire_id, groupe_id, "position", par_delegation, rectifie, position_origine, origine_inconnue
              FROM votes WHERE scrutin_id IN (SELECT id FROM scrutins WHERE {ChambresActives}) ORDER BY scrutin_id, parlementaire_id
            """),
        new("textes", "Textes de loi : type, titre, état, promulgation, Journal officiel, décision du Conseil constitutionnel, liens officiels.",
            """
            SELECT id, type_libelle, titre, titre_en, titre_en_auto, etat_libelle, numero_loi, date_promulgation, date_jo, numero_jo, url_legifrance,
                   url_dossier_senat, url_dossier_an, saisine_cc_date, saisine_cc_par, decision_cc_numero, decision_cc_date, decision_cc_libelle, decision_cc_url
              FROM textes ORDER BY id
            """),
        new("petitions", "Pétitions déposées sur petitions.senat.fr, petitions.assemblee-nationale.fr et petition.parliament.uk (plateforme UK, si publiée) : titre, statut publié par la source, commission et décision (Assemblée), étapes du Parlement britannique (réponse du gouvernement, débat), nombre de soutiens de citoyens.",
            "SELECT id, chambre, titre, type, statut, commission, decision_commission, legislature, date_publication, date_limite_vote, nb_votes, seuil_reponse_le, reponse_le, seuil_debat_le, debat_prevu_le, debat_le, debat_url, url FROM petitions WHERE " + PlateformesPubliees + " ORDER BY chambre, date_publication, id"),
        new("petitions_historique", "Relevés successifs du nombre de soutiens et du statut de chaque pétition du Sénat (ajout seul, un relevé par import).",
            "SELECT petition_id, releve_le, nb_votes, statut FROM petitions_historique WHERE petition_id IN (SELECT id FROM petitions WHERE " + PlateformesPubliees + ") ORDER BY petition_id, releve_le"),
        new("petitions_anomalies", "Constats entre deux relevés : baisse du nombre de soutiens, disparition ou réapparition d'une pétition dans le fichier source. Sans cause connue.",
            "SELECT petition_id, type, detecte_le, releve_precedent, valeur_avant, valeur_apres FROM petitions_anomalies WHERE petition_id IN (SELECT id FROM petitions WHERE " + PlateformesPubliees + ") ORDER BY detecte_le, petition_id"),
        new("bills", "Projets de loi du Parlement britannique (sessions 35 à 40, depuis décembre 2019) : titre, chambre d'origine et courante, étape en cours, sanction royale (loi), retrait ou rejet, tels que publiés par bills-api.parliament.uk. Publié seulement quand le pays GB est actif.",
            "SELECT id, titre, chambre_origine, chambre_courante, session_id, est_loi, date_loi, retire_le, rejete, etape_courante, date_depot, derniere_maj, url FROM bills WHERE EXISTS (SELECT 1 FROM pays WHERE code = 'GB' AND actif) ORDER BY id"),
        new("bill_etapes", "Étapes (séances) de chaque projet de loi britannique : chambre, libellé de l'étape, date de séance (vide = sans séance publiée). Publié seulement quand le pays GB est actif.",
            "SELECT bill_id, ordre, libelle, chambre, date_seance FROM bill_etapes WHERE EXISTS (SELECT 1 FROM pays WHERE code = 'GB' AND actif) ORDER BY bill_id, ordre, date_seance"),
        new("amendements_dossiers", "Amendements déposés à l'Assemblée nationale par dossier législatif (référence DLR…) : nombre déposé et sort publié (adopté, rejeté, tombé, non soutenu, retiré, irrecevable). Source : jeu « Amendements » de data.assemblee-nationale.fr (16e et 17e législatures).",
            "SELECT dossier_an, legislature, deposes, adoptes, rejetes, tombes, non_soutenus, retires, irrecevables, releve_le FROM amendements_dossiers ORDER BY dossier_an"),
        new("texte_etapes", "Décisions de séance sur un texte (Assemblée et Sénat), avec ou sans scrutin public.",
            """
            SELECT id, texte_id, chambre, code_acte, date_acte, resultat, scrutin_id, sans_scrutin_public, article_49_3
              FROM texte_etapes WHERE texte_id IS NOT NULL ORDER BY texte_id, date_acte, id
            """),
    ];

    /// <summary>Produit tous les fichiers dans <paramref name="dossier"/> puis le manifeste. Retourne le manifeste écrit.</summary>
    public static async Task<IReadOnlyList<EntreeManifeste>> ExporterAsync(string connectionString, string dossier, Action<string> journal)
    {
        Directory.CreateDirectory(dossier);
        // Pas de délai : le jeu des votes (plus de 6 millions de lignes, trié) met plus de 30 s à produire sa première ligne (constaté : « Timeout during reading attempt »).
        var cs = new NpgsqlConnectionStringBuilder(connectionString) { CommandTimeout = 0, Timeout = 60 };
        await using var conn = new NpgsqlConnection(cs.ConnectionString);
        await conn.OpenAsync();

        var manifeste = new List<EntreeManifeste>();
        foreach (var jeu in Tous)
        {
            var final = Path.Combine(dossier, jeu.Nom + ".csv.gz");
            var temporaire = final + ".tmp";
            long lignes;
            await using (var sortie = File.Create(temporaire))
            await using (var gz = new GZipStream(sortie, CompressionLevel.Optimal))
            await using (var ecrivain = new StreamWriter(gz, new UTF8Encoding(false)))
            // COPY ... CSV : guillemets et séparateurs gérés par PostgreSQL (RFC 4180) ; booléens t/f et dates ISO.
            using (var lecteur = await conn.BeginTextExportAsync($"COPY ({jeu.Sql}) TO STDOUT WITH (FORMAT csv, HEADER true)"))
            {
                string? ligne;
                while ((ligne = await lecteur.ReadLineAsync()) is not null)
                {
                    await ecrivain.WriteAsync(ligne);
                    await ecrivain.WriteAsync('\n');
                }
            }
            // Nombre de LIGNES DE DONNÉES : compté par la base (un champ entre guillemets peut contenir un saut de ligne, ce qui fausserait un décompte du fichier).
            lignes = Convert.ToInt64(await new NpgsqlCommand($"SELECT count(*) FROM ({jeu.Sql}) x", conn).ExecuteScalarAsync());

            var octets = new FileInfo(temporaire).Length;
            string sha;
            await using (var f = File.OpenRead(temporaire)) sha = Convert.ToHexString(await SHA256.HashDataAsync(f)).ToLowerInvariant();
            File.Move(temporaire, final, overwrite: true);   // remplacement atomique : le site ne sert jamais un fichier à moitié écrit
            manifeste.Add(new EntreeManifeste(jeu.Nom + ".csv.gz", jeu.Description, lignes, octets, sha, DateTime.UtcNow, jeu.Pays));
            journal($"  {jeu.Nom}.csv.gz : {lignes:N0} lignes, {octets / 1024.0 / 1024.0:N1} Mo");
        }

        var manifestePropre = Path.Combine(dossier, "manifest.json");
        await File.WriteAllTextAsync(manifestePropre + ".tmp", JsonSerializer.Serialize(manifeste, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(manifestePropre + ".tmp", manifestePropre, overwrite: true);
        return manifeste;
    }
}
