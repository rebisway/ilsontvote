using System.Globalization;
using System.Text;

namespace IlsOntVote.Importer;

/// <summary>
/// Une pétition telle que publiée (Sénat : petitions.senat.fr ; Assemblée : petitions.assemblee-nationale.fr), via les jeux data.gouv.fr.
/// <c>NbVotes</c> est NULL quand la source ne le renseigne pas (manquant ≠ zéro). Les champs propres à une plateforme sont NULL sur l'autre.
/// </summary>
public sealed record PetitionRow(string Id, string Chambre, string Titre, string? Description, DateTime? DatePublication, int? NbVotes, string Statut,
    DateTime? DateLimiteVote, string? Type, string Url, string? Commission, string? DecisionCommission, string? Legislature)
{
    // Étapes propres à la plateforme du Parlement britannique (NULL sur les plateformes françaises).
    public DateTime? SeuilReponseLe { get; init; }
    public DateTime? ReponseLe { get; init; }
    public string? ReponseResume { get; init; }
    public DateTime? SeuilDebatLe { get; init; }
    public DateTime? DebatPrevuLe { get; init; }
    public DateTime? DebatLe { get; init; }
    public string? DebatUrl { get; init; }
}

/// <summary>Une plateforme de pétitions : fichier courant, dossier d'archives hebdomadaires et préfixe d'identifiant (les numéros des plateformes se recoupent).</summary>
public sealed record SourcePetitions(string Chambre, string Prefixe, string Url, string UrlArchives, string Nom);

/// <summary>
/// Sources « pétitions » : un CSV par plateforme (séparateur « ; », UTF-8, champs entre guillemets pouvant contenir des sauts de ligne),
/// régénéré chaque lundi par data.gouv.fr (Licence Ouverte 2.0), avec un dossier d'archives hebdomadaires.
/// </summary>
public static class PetitionsSource
{
    /// <summary>Sénat : ressource « Pétitions au JJ-MM-AAAA » du jeu data.gouv.fr/datasets/petitions-du-senat ; archives depuis mars 2026.</summary>
    public static readonly SourcePetitions Senat = new("SENAT", "", "https://data-pipeline-open.s3.sbg.io.cloud.ovh.net/senat_petitions/petitions.csv",
        "https://files.data.gouv.fr/data-pipeline-open/senat_petitions/", "Sénat");

    /// <summary>Assemblée nationale : jeu data.gouv.fr/datasets/petitions-de-lassemblee-nationale ; archives depuis juillet 2025. Identifiants préfixés « AN- ».</summary>
    public static readonly SourcePetitions Assemblee = new("AN", "AN-", "https://data-pipeline-open.s3.sbg.io.cloud.ovh.net/an_petitions/petitions.csv",
        "https://files.data.gouv.fr/data-pipeline-open/an_petitions/", "Assemblée nationale");

    /// <summary>Compatibilité : adresse du CSV du Sénat.</summary>
    public const string Url = "https://data-pipeline-open.s3.sbg.io.cloud.ovh.net/senat_petitions/petitions.csv";

    /// <summary>
    /// Lit un CSV de pétitions. Colonnes obligatoires : identifiant, titre, statut, url ; les autres sont facultatives (les archives anciennes ou l'autre
    /// plateforme n'ont pas les mêmes). Lignes sans identifiant ou sans titre ignorées (rien n'est inventé). Le nombre de soutiens peut s'écrire
    /// « 425.0 » (observé à l'Assemblée) ; une valeur vide ou illisible donne NULL, jamais 0.
    /// </summary>
    public static IReadOnlyList<PetitionRow> Lire(string chemin, SourcePetitions source)
    {
        var lignes = AnalyserCsv(File.ReadAllText(chemin, new UTF8Encoding(false)).TrimStart('\uFEFF'));
        if (lignes.Count == 0) return [];
        var entete = lignes[0].Select(c => c.Trim()).ToList();
        int Obligatoire(string nom) => entete.IndexOf(nom) is var i and >= 0 ? i : throw new InvalidDataException($"Colonne « {nom} » absente du CSV des pétitions (format modifié ?).");
        int Facultative(string nom) => entete.IndexOf(nom);
        int iId = Obligatoire("identifiant"), iTitre = Obligatoire("titre"), iStatut = Obligatoire("statut"), iUrl = Obligatoire("url");
        int iDesc = Facultative("description"), iPub = Facultative("date_publication"), iVotes = Facultative("nb_votes"), iLimite = Facultative("date_limite_vote"),
            iType = Facultative("type"), iCommission = Facultative("commission"), iDecision = Facultative("decision_commission"), iLeg = Facultative("legislature");
        var dernier = new[] { iId, iTitre, iStatut, iUrl }.Max();

        string? Texte(List<string> l, int i) => i >= 0 && i < l.Count && !string.IsNullOrWhiteSpace(l[i]) ? l[i].Trim() : null;
        DateTime? Date(string? s) => DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.Date : null;
        int? Entier(string? s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d >= 0 && d < int.MaxValue ? (int)d : null;

        var res = new List<PetitionRow>();
        foreach (var l in lignes.Skip(1))
        {
            if (l.Count <= dernier) continue;   // ligne tronquée : ignorée
            if (string.IsNullOrWhiteSpace(l[iId]) || string.IsNullOrWhiteSpace(l[iTitre])) continue;
            res.Add(new PetitionRow(source.Prefixe + l[iId].Trim(), source.Chambre, l[iTitre].Trim(), Texte(l, iDesc), Date(Texte(l, iPub)), Entier(Texte(l, iVotes)),
                l[iStatut].Trim(), Date(Texte(l, iLimite)), Texte(l, iType), l[iUrl].Trim(), Texte(l, iCommission), Texte(l, iDecision), Texte(l, iLeg)));
        }
        return res;
    }

    /// <summary>Analyse CSV minimale (RFC 4180, séparateur « ; ») : guillemets doublés et sauts de ligne dans un champ gérés.</summary>
    public static List<List<string>> AnalyserCsv(string texte)
    {
        var lignes = new List<List<string>>();
        var ligne = new List<string>();
        var champ = new StringBuilder();
        var entreGuillemets = false;
        for (var i = 0; i < texte.Length; i++)
        {
            var c = texte[i];
            if (entreGuillemets)
            {
                if (c == '"' && i + 1 < texte.Length && texte[i + 1] == '"') { champ.Append('"'); i++; }
                else if (c == '"') entreGuillemets = false;
                else champ.Append(c);
            }
            else if (c == '"') entreGuillemets = true;
            else if (c == ';') { ligne.Add(champ.ToString()); champ.Clear(); }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < texte.Length && texte[i + 1] == '\n') i++;
                ligne.Add(champ.ToString()); champ.Clear();
                if (ligne.Any(x => x.Length > 0)) lignes.Add(ligne);
                ligne = [];
            }
            else champ.Append(c);
        }
        if (champ.Length > 0 || ligne.Count > 0) { ligne.Add(champ.ToString()); if (ligne.Any(x => x.Length > 0)) lignes.Add(ligne); }
        return lignes;
    }
}
