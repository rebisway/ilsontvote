using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IlsOntVote.Importer;

/// <summary>
/// Source Sénat :
///  - base « Sénateurs » (data.senat.fr/data/senateurs/*.json) : identités, mandats, groupes ;
///  - base DOSLEG (data.senat.fr/data/dosleg/dosleg.zip, dump PostgreSQL) : scrutins publics (table scr)
///    et vote de chaque sénateur (table votsen), reliés par le même matricule.
/// </summary>
public sealed partial class SenatSource
{
    public static readonly string[] FichiersSenateurs = ["ODSEN_GENERAL", "ODSEN_ELUSEN", "ODSEN_HISTOGROUPES"];

    [GeneratedRegex(@"^l['’]ensemble (du|de la|des|de l['’])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SurEnsembleRegex();

    private static readonly Dictionary<string, string> Couleurs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CRCE-K"] = "#830E21", ["CRCE"] = "#830E21", ["CRC-SPG"] = "#830E21", ["CRC"] = "#830E21",
        ["GEST"] = "#77AA79", ["EST"] = "#77AA79",
        ["SER"] = "#E8789F", ["SOC"] = "#E8789F",
        ["RDSE"] = "#D9A441",
        ["RDPI"] = "#7B4591", ["LREM"] = "#7B4591",
        ["UC"] = "#E39B5B", ["UC-UDF"] = "#E39B5B",
        ["INDEP"] = "#5DADE2", ["LIRT"] = "#5DADE2",
        ["UMP"] = "#3B6FB6", ["LR"] = "#3B6FB6",
        ["NI"] = "#8D949A",
    };

    // ------------------------------------------------------------------ annuaire

    public Annuaire LireAnnuaire(string dossier, IReadOnlyDictionary<string, string> slugsExistants)
    {
        var parlementaires = new List<ParlementaireRow>();
        var slugs = new HashSet<string>(slugsExistants.Values);

        foreach (var l in Lignes(Path.Combine(dossier, "ODSEN_GENERAL.json")))
        {
            var id = Texte(l, "Matricule");
            if (id is null) continue;
            var prenom = Texte(l, "Prenom_usuel") ?? "";
            var nom = Texte(l, "Nom_usuel") ?? "";
            parlementaires.Add(new ParlementaireRow(
                id, Chambres.Senat, Texte(l, "Qualite"), prenom, nom,
                slugsExistants.TryGetValue(id, out var connu) ? connu : Slugs.Unique(slugs, prenom, nom, id),
                Texte(l, "Circonscription"), null));
        }
        var connus = parlementaires.Select(p => p.Id).ToHashSet();

        var mandats = new List<MandatRow>();
        foreach (var l in Lignes(Path.Combine(dossier, "ODSEN_ELUSEN.json")))
        {
            var id = Texte(l, "Matricule");
            var debut = Date(Texte(l, "Date_de_debut_de_mandat"));
            if (id is null || debut is null || !connus.Contains(id)) continue;
            mandats.Add(new MandatRow(id, null, debut.Value, Date(Texte(l, "Date_de_fin_de_mandat"))));
        }

        var appartenances = new List<AppartenanceRow>();
        var libelles = new Dictionary<string, (DateOnly Fin, string Libelle)>();
        foreach (var l in Lignes(Path.Combine(dossier, "ODSEN_HISTOGROUPES.json")))
        {
            var id = Texte(l, "Matricule");
            var code = Texte(l, "Code_du_groupe_politique");
            if (id is null || code is null || !connus.Contains(id)) continue;

            var debut = Date(Texte(l, "Date_de_debut_d_appartenance")) ?? new DateOnly(1900, 1, 1);
            var fin = Date(Texte(l, "Date_de_fin_d_appartenance"));
            var groupeId = GroupeId(code);
            appartenances.Add(new AppartenanceRow(id, groupeId, debut, fin));

            // Le libellé le plus récent du groupe fait foi (un groupe peut être renommé).
            var finTri = fin ?? DateOnly.MaxValue;
            var libelle = Texte(l, "Nom_court_du_groupe_politique") ?? code;
            if (!libelles.TryGetValue(code, out var existant) || finTri >= existant.Fin)
                libelles[code] = (finTri, libelle);
        }

        var groupes = libelles
            .Select(kv => new GroupeRow(
                GroupeId(kv.Key), Chambres.Senat, null, kv.Key, kv.Value.Libelle,
                Couleurs.GetValueOrDefault(kv.Key), GroupeOrdre.Pour(kv.Key)))
            .ToList();

        return new Annuaire(groupes, parlementaires, mandats, appartenances);
    }

    private static string GroupeId(string code) => $"SEN-{code}";

    // ------------------------------------------------------------------ scrutins

    /// <summary>Lit dosleg.sql (tables scr et votsen). Les votes sont regroupés par scrutin avant d'être restitués.</summary>
    public IEnumerable<ScrutinAvecVotes> LireScrutins(string dosleg, Annuaire annuaire)
    {
        var appartenances = annuaire.Appartenances
            .GroupBy(a => a.ParlementaireId)
            .ToDictionary(g => g.Key, g => g.OrderBy(a => a.DateDebut).ToList());

        var scrutins = new Dictionary<(int Session, int Numero), Dictionary<string, string?>>();
        var votes = new Dictionary<(int Session, int Numero), List<Dictionary<string, string?>>>();

        foreach (var (table, ligne) in LignesCopy(dosleg, "scr", "votsen"))
        {
            if (!int.TryParse(ligne["sesann"], out var ses) || !int.TryParse(ligne["scrnum"], out var num)) continue;
            if (table == "scr")
                scrutins[(ses, num)] = ligne;
            else
            {
                if (!votes.TryGetValue((ses, num), out var liste)) votes[(ses, num)] = liste = [];
                liste.Add(ligne);
            }
        }

        foreach (var ((ses, num), scr) in scrutins.OrderBy(kv => kv.Key.Session).ThenBy(kv => kv.Key.Numero))
        {
            var date = Date(scr["scrdat"]?[..Math.Min(10, scr["scrdat"]!.Length)]);
            if (date is null) continue;

            var id = $"SENAT-{ses}-{num}";
            var titre = NormaliserTitre(scr["scrint"] ?? "");
            var rows = new List<VoteRow>();
            int nbNonVotants = 0, trouvesPour = 0, trouvesContre = 0, trouvesAbst = 0;

            foreach (var v in votes.GetValueOrDefault((ses, num)) ?? [])
            {
                var senmat = v["senmat"]?.Trim();
                if (string.IsNullOrEmpty(senmat)) continue;

                var position = v["posvotcod"] switch
                {
                    "1" => Positions.Pour,
                    "2" => Positions.Contre,
                    "3" => Positions.Abstention,
                    "4" => Positions.NonVotant,
                    _ => null,
                };
                if (position is null) continue;

                if (position == Positions.NonVotant)
                {
                    nbNonVotants++;
                    // « N'a pas pris part au vote » sans statut particulier = absent (non stocké).
                    // Avec un statut (président de séance, membre du gouvernement…) = non-votant déclaré.
                    if ((v["stavotidt"] ?? "0").Trim() == "0") continue;
                }
                else if (position == Positions.Pour) trouvesPour++;
                else if (position == Positions.Contre) trouvesContre++;
                else trouvesAbst++;

                var rectifie = v["votsenmar"]?.Trim() == "*";
                var delegation = !string.IsNullOrWhiteSpace(v["senmatdel"]);
                rows.Add(new VoteRow(
                    id, senmat, GroupeA(appartenances, senmat, date.Value), position,
                    delegation, rectifie, rectifie ? null : position, OrigineInconnue: rectifie));
            }

            int? pour = Entier(scr["scrpou"]), contre = Entier(scr["scrcon"]);
            int? votants = Entier(scr["scrvot"]), exprimes = Entier(scr["scrsuf"]), majorite = Entier(scr["scrmaj"]);

            // À partir de 2012 la base publie un seuil de 0 : on prend alors celui annoncé en séance,
            // sinon on le calcule (majorité des suffrages exprimés = la moitié, plus un). Sans ce repli,
            // tout scrutin serait « adopté ».
            if (majorite is null or <= 0) majorite = Entier(scr["scrmajsea"]);
            if (majorite is null or <= 0) majorite = (exprimes ?? ((pour ?? 0) + (contre ?? 0))) / 2 + 1;
            int? abstentions = votants is not null && exprimes is not null ? Math.Max(0, votants.Value - exprimes.Value) : null;
            var adopte = majorite is not null ? (pour ?? 0) >= majorite : (pour ?? 0) > (contre ?? 0);

            var manquants =
                Math.Max(0, (pour ?? 0) - trouvesPour) +
                Math.Max(0, (contre ?? 0) - trouvesContre) +
                Math.Max(0, (abstentions ?? 0) - trouvesAbst);

            var scrutin = new ScrutinRow(
                id, Chambres.Senat, ses, num, date.Value, titre,
                null, adopte ? "adopté" : "rejeté", null, SurEnsembleRegex().IsMatch(titre),
                votants, pour, contre, abstentions, nbNonVotants, "senat_votsen",
                manquants, scr["scrbaspag"],
                TypeVoteLibelle: "scrutin public", TypeMajorite: null,
                SuffragesExprimes: exprimes, SuffragesRequis: majorite);

            yield return new ScrutinAvecVotes(scrutin, rows);
        }
    }

    // « sur l'ensemble du projet de loi… » -> « l'ensemble du projet de loi… » (même forme que l'AN).
    private static string NormaliserTitre(string titre)
    {
        titre = titre.Trim();
        return titre.StartsWith("sur ", StringComparison.OrdinalIgnoreCase) ? titre[4..] : titre;
    }

    private static string? GroupeA(Dictionary<string, List<AppartenanceRow>> parSenateur, string senateur, DateOnly date)
    {
        if (!parSenateur.TryGetValue(senateur, out var liste)) return null;
        AppartenanceRow? retenue = null;
        foreach (var a in liste)
            if (a.DateDebut <= date && (a.DateFin is null || a.DateFin >= date)) retenue = a;
        return retenue?.GroupeId;
    }

    // ------------------------------------------------------------------ lecture

    private static IEnumerable<JsonElement> Lignes(string fichier)
    {
        using var stream = File.OpenRead(fichier);
        using var doc = JsonDocument.Parse(stream);
        // Les lignes sont clonées : le document est libéré à la fin de l'énumération.
        foreach (var l in doc.RootElement.GetProperty("results").EnumerateArray().Select(e => e.Clone()).ToList())
            yield return l;
    }

    private static string? Texte(JsonElement l, string nom) =>
        l.TryGetProperty(nom, out var v) && v.ValueKind == JsonValueKind.String
            ? (string.IsNullOrWhiteSpace(v.GetString()) ? null : v.GetString())
            : null;

    private static DateOnly? Date(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        foreach (var format in new[] { "yyyy/MM/dd HH:mm:ss", "yyyy/MM/dd", "yyyy-MM-dd" })
            if (DateTime.TryParseExact(s, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                return DateOnly.FromDateTime(d);
        return null;
    }

    private static int? Entier(string? s) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;

    /// <summary>
    /// Parcourt un dump PostgreSQL (format COPY ... FROM stdin) et restitue, pour les tables demandées,
    /// chaque ligne sous forme colonne -> valeur (null pour \N).
    /// </summary>
    internal static IEnumerable<(string Table, Dictionary<string, string?> Ligne)> LignesCopy(string sql, params string[] tables)
    {
        var voulues = new HashSet<string>(tables);
        string? courante = null;
        string[] colonnes = [];

        foreach (var ligne in File.ReadLines(sql, Encoding.UTF8))
        {
            if (courante is null)
            {
                if (!ligne.StartsWith("COPY ", StringComparison.Ordinal)) continue;
                var m = CopyRegex().Match(ligne);
                if (!m.Success || !voulues.Contains(m.Groups[1].Value)) continue;
                courante = m.Groups[1].Value;
                colonnes = m.Groups[2].Value.Split(',', StringSplitOptions.TrimEntries);
                continue;
            }

            if (ligne == "\\.") { courante = null; continue; }

            var valeurs = ligne.Split('\t');
            var d = new Dictionary<string, string?>(colonnes.Length);
            for (int i = 0; i < colonnes.Length; i++)
                d[colonnes[i]] = i < valeurs.Length ? Decoder(valeurs[i]) : null;
            yield return (courante, d);
        }
    }

    [GeneratedRegex(@"^COPY (?:public\.)?(\w+) \(([^)]*)\) FROM stdin;")]
    private static partial Regex CopyRegex();

    // Format texte de COPY : \N = NULL ; \\ \t \n \r échappés.
    private static string? Decoder(string valeur)
    {
        if (valeur == "\\N") return null;
        if (!valeur.Contains('\\')) return valeur;
        var sb = new StringBuilder(valeur.Length);
        for (int i = 0; i < valeur.Length; i++)
        {
            if (valeur[i] != '\\' || i + 1 >= valeur.Length) { sb.Append(valeur[i]); continue; }
            i++;
            sb.Append(valeur[i] switch { 't' => '\t', 'n' => '\n', 'r' => '\r', _ => valeur[i] });
        }
        return sb.ToString();
    }
}
