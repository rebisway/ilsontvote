using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;

namespace IlsOntVote.Importer;

/// <summary>Un jeu de données publié en téléchargement : fichier CSV compressé, produit par une requête SQL (colonnes nommées, sans photo).</summary>
public sealed record JeuDonnees(string Nom, string Description, string Sql);

/// <summary>Une ligne du manifeste (manifest.json) lu par la page « Données ouvertes ».</summary>
public sealed record EntreeManifeste(string Fichier, string Description, long Lignes, long Octets, string Sha256, DateTime GenereLe);

/// <summary>
/// Export des données du site en fichiers téléchargeables (CSV UTF-8 virgule, compressés en gzip), régénérés à chaque import.
/// Pourquoi des fichiers précalculés : le jeu des votes dépasse 4 millions de lignes ; le produire à la demande chargerait la base et le
/// serveur web. Règles : fichier écrit sous un nom temporaire puis renommé (jamais de fichier à moitié écrit servi) ; les absents ne sont PAS
/// exportés (ils ne sont pas stockés : ils se déduisent des mandats et des scrutins, cf. LISEZMOI) ; aucune photo (droits).
/// </summary>
public static class ExportDonnees
{
    /// <summary>Jeux exportés. Les colonnes suivent le schéma de la base, sans les champs éditoriaux internes (à la une, titres normalisés).</summary>
    public static readonly IReadOnlyList<JeuDonnees> Jeux =
    [
        new("groupes", "Groupes politiques (un identifiant par législature pour l'Assemblée).",
            "SELECT id, chambre, sigle, libelle, legislature FROM groupes ORDER BY chambre, legislature, sigle"),
        new("parlementaires", "Députés, sénateurs et eurodéputés (identité, département ou pays, circonscription).",
            "SELECT id, chambre, civilite, prenom, nom, slug, departement, circonscription FROM parlementaires ORDER BY chambre, nom, prenom"),
        new("mandats", "Périodes de mandat (début = prise de fonction ; fin vide = en cours).",
            "SELECT parlementaire_id, legislature, date_debut, date_fin FROM mandats ORDER BY parlementaire_id, date_debut"),
        new("appartenances_groupes", "Appartenance d'un élu à un groupe, avec dates.",
            "SELECT parlementaire_id, groupe_id, date_debut, date_fin FROM groupe_appartenances ORDER BY parlementaire_id, date_debut"),
        new("scrutins", "Scrutins publics : décompte, résultat, texte rattaché (texte_id : vote sur l'ensemble ; texte_lie_id : amendement ou article).",
            """
            SELECT id, chambre, legislature, numero, date_scrutin, titre, titre_fr_auto, titre_en, titre_en_auto, type_vote_libelle, sort, sur_ensemble,
                   nb_votants, nb_pour, nb_contre, nb_abstentions, nb_non_votants, suffrages_exprimes, suffrages_requis, nominatif_incomplet, texte_id, texte_lie_id
              FROM scrutins ORDER BY date_scrutin, chambre, numero
            """),
        new("votes", "Vote de chaque élu à chaque scrutin public (pour, contre, abstention, non_votant). Les absents ne figurent pas dans ce fichier.",
            """
            SELECT scrutin_id, parlementaire_id, groupe_id, "position", par_delegation, rectifie, position_origine, origine_inconnue
              FROM votes ORDER BY scrutin_id, parlementaire_id
            """),
        new("textes", "Textes de loi : type, titre, état, promulgation, Journal officiel, décision du Conseil constitutionnel, liens officiels.",
            """
            SELECT id, type_libelle, titre, titre_en, titre_en_auto, etat_libelle, numero_loi, date_promulgation, date_jo, numero_jo, url_legifrance,
                   url_dossier_senat, url_dossier_an, saisine_cc_date, saisine_cc_par, decision_cc_numero, decision_cc_date, decision_cc_libelle, decision_cc_url
              FROM textes ORDER BY id
            """),
        new("petitions", "Pétitions déposées sur petitions.senat.fr et petitions.assemblee-nationale.fr : titre, statut publié par la source, commission et décision (Assemblée), nombre de soutiens de citoyens.",
            "SELECT id, chambre, titre, type, statut, commission, decision_commission, legislature, date_publication, date_limite_vote, nb_votes, url FROM petitions ORDER BY chambre, date_publication, id"),
        new("petitions_historique", "Relevés successifs du nombre de soutiens et du statut de chaque pétition du Sénat (ajout seul, un relevé par import).",
            "SELECT petition_id, releve_le, nb_votes, statut FROM petitions_historique ORDER BY petition_id, releve_le"),
        new("petitions_anomalies", "Constats entre deux relevés : baisse du nombre de soutiens, disparition ou réapparition d'une pétition dans le fichier source. Sans cause connue.",
            "SELECT petition_id, type, detecte_le, releve_precedent, valeur_avant, valeur_apres FROM petitions_anomalies ORDER BY detecte_le, petition_id"),
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
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        var manifeste = new List<EntreeManifeste>();
        foreach (var jeu in Jeux)
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
            manifeste.Add(new EntreeManifeste(jeu.Nom + ".csv.gz", jeu.Description, lignes, octets, sha, DateTime.UtcNow));
            journal($"  {jeu.Nom}.csv.gz : {lignes:N0} lignes, {octets / 1024.0 / 1024.0:N1} Mo");
        }

        var manifestePropre = Path.Combine(dossier, "manifest.json");
        await File.WriteAllTextAsync(manifestePropre + ".tmp", JsonSerializer.Serialize(manifeste, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(manifestePropre + ".tmp", manifestePropre, overwrite: true);
        return manifeste;
    }
}
