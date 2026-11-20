using System.Text.Json;

namespace IlsOntVote.Web.Services;

/// <summary>Un fichier de données ouvertes tel que décrit par le manifeste écrit par l'importeur (commande « export »).</summary>
/// <summary>
/// Descriptions ANGLAISES des fichiers de données ouvertes, par nom de fichier. Le manifeste (écrit par l'importeur) ne porte que le français : cette table évite d'attendre
/// un nouvel export pour traduire la page. Un fichier absent de la table garde sa description française (jamais de description inventée).
/// </summary>
public static class DescriptionsDonnees
{
    private static readonly Dictionary<string, string> En = new()
    {
        ["groupes"] = "Political groups (one identifier per legislature for the National Assembly).",
        ["parlementaires"] = "Members of the French Parliament and the European Parliament (identity, department or country, constituency).",
        ["mandats"] = "Terms of office (start = taking office; empty end = ongoing).",
        ["appartenances_groupes"] = "Membership of a member in a group, with dates.",
        ["scrutins"] = "Recorded votes: count, result, linked text (texte_id: vote on the whole text; texte_lie_id: amendment or article). UK divisions: bill_id and bill_etape when linked.",
        ["votes"] = "Each member's vote in each recorded vote (for, against, abstention, non-voting). Absent members are not in this file.",
        ["textes"] = "French laws: type, title, status, promulgation, Official Journal, Constitutional Council decision, official links.",
        ["petitions"] = "Petitions from petitions.senat.fr, petitions.assemblee-nationale.fr and petition.parliament.uk (UK platform, when published): title, status as published by the source, committee and decision (Assembly), UK Parliament stages (government response, debate), number of citizen supporters.",
        ["petitions_historique"] = "Successive readings of the number of supporters and the status of each Senate petition (append-only, one reading per import).",
        ["petitions_anomalies"] = "Findings between two readings: drop in supporters, petition disappearing or reappearing from the source file. No cause is stated.",
        ["bills"] = "UK Parliament bills (sessions 35 to 40, since December 2019): title, originating and current house, current stage, Royal Assent (Act), withdrawal or defeat, as published by bills-api.parliament.uk. Published only when the UK is active.",
        ["bill_etapes"] = "Stages (sittings) of each UK bill: house, stage label, sitting date (empty = no published sitting). Published only when the UK is active.",
        ["amendements_dossiers"] = "Amendments tabled in the National Assembly per legislative file (DLR reference): number tabled and published outcome (adopted, rejected, lapsed, not moved, withdrawn, inadmissible). Source: the \"Amendements\" dataset of data.assemblee-nationale.fr (16th and 17th legislatures).",
        ["texte_etapes"] = "Floor decisions on a text (Assembly and Senate), with or without a recorded vote.",
    };

    private static readonly Dictionary<string, string> EnGb = new()
    {
        ["groupes"] = "Parties (groups) of the House of Commons and the House of Lords.",
        ["parlementaires"] = "MPs and peers (identity, constituency for MPs).",
        ["mandats"] = "Terms of office. Commons: from the first to the last vote where the MP appears (calculated); Lords: entry and exit dates from the members directory.",
        ["appartenances_groupes"] = "Membership of a member in a party, with dates (peers: the latest known party).",
        ["scrutins"] = "Divisions (recorded votes) of the Commons and the Lords: count, calculated result, linked bill (bill_id, calculated from the title); sur_ensemble = third reading in the Commons (calculated).",
        ["votes"] = "Each member's vote in each division (pour = aye / content, contre = no / not content, abstention = listed in both lobbies). Absent members are not in this file.",
        ["petitions"] = "Petitions from petition.parliament.uk: title, published status, supporters, government response and debate.",
        ["petitions_historique"] = "Successive readings of the number of supporters and the status of each petition (one reading per day of change).",
        ["petitions_anomalies"] = "Findings between two readings: drop in supporters, petition disappearing or reappearing. No cause is stated.",
        ["bills"] = "UK Parliament bills (sessions 35 to 40, since December 2019): title, originating and current house, current stage, Royal Assent (Act), withdrawal or defeat, as published by bills-api.parliament.uk.",
        ["bill_etapes"] = "Stages (sittings) of each UK bill: house, stage label, sitting date (empty = no published sitting).",
    };

    private static readonly Dictionary<string, string> EnUs = new()
    {
        ["groupes"] = "Parties (groups) of the House of Representatives and the Senate of the United States.",
        ["parlementaires"] = "Representatives and senators (identity, state, district for the House).",
        ["mandats"] = "Terms of office, deduced from the votes: from the first to the last roll call where the member appears in the list (calculated). One row per Congress (column legislature = Congress number).",
        ["appartenances_groupes"] = "Membership of a member in a party, with dates (the party in the Congress concerned).",
        ["scrutins"] = "Roll call votes of the House and the Senate: official counts, result, question put, required majority (calculated), url_officielle = official record; sur_ensemble = final passage, nomination or “suspend the rules and pass” (calculated from the question).",
        ["votes"] = "Each member's vote in each roll call (pour = yea, contre = nay, abstention = present). Not voting members and merely announced or paired positions are not in this file.",
    };

    private static readonly Dictionary<string, string> EnIt = new()
    {
        ["groupes"] = "Parliamentary groups of the Italian Chamber of Deputies and Senate (one identifier per legislature).",
        ["parlementaires"] = "Deputies and senators (identity).",
        ["mandats"] = "Terms of office, deduced from the votes: from the first to the last imported vote where the deputy appears in the list (calculated). One row per legislature.",
        ["appartenances_groupes"] = "Membership of a deputy in a group, with dates (the group at the time of the votes).",
        ["scrutins"] = "Votes of the Chamber and the Senate: official counts, result (official field), majority threshold, url_officielle = official page of the vote; sur_ensemble = final vote as flagged by the Chamber. By default only final votes (and the Chamber's confidence votes) are imported; secret ballots have totals only (nominatif_incomplet).",
        ["votes"] = "Each deputy's vote in each vote (pour = in favour, contre = against, abstention). Deputies who did not vote are not in this file; in secret ballots the Chamber does not publish positions.",
    };

    /// <summary>Description anglaise du fichier (« votes.csv.gz », « gb_votes.csv.gz », « us_votes.csv.gz »), ou null si la table ne la connaît pas.</summary>
    public static string? Anglais(string fichier)
    {
        var nom = fichier.Split('.')[0];
        return nom.StartsWith("gb_") ? EnGb.GetValueOrDefault(nom[3..]) : nom.StartsWith("us_") ? EnUs.GetValueOrDefault(nom[3..]) : nom.StartsWith("it_") ? EnIt.GetValueOrDefault(nom[3..]) : En.GetValueOrDefault(nom);
    }
}

public sealed class FichierDonnees
{
    public string Fichier { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>« FR » (France et Union européenne) ou « GB » ; absent des anciens manifestes = « FR ».</summary>
    public string Pays { get; set; } = "FR";
    public long Lignes { get; set; }
    public long Octets { get; set; }
    public string Sha256 { get; set; } = "";
    public DateTime GenereLe { get; set; }
}

/// <summary>
/// Données ouvertes : fichiers CSV compressés produits chaque nuit par l'importeur dans un dossier (« Export:Dir » ou ILSONVOTE_EXPORT_DIR).
/// Le site ne génère rien : il liste le manifeste et sert les fichiers, avec une liste blanche de noms (jamais un chemin venant de l'adresse).
/// </summary>
public sealed class DonneesOuvertes(string dossier)
{
    private static readonly System.Text.RegularExpressions.Regex NomValide = new(@"^[a-z_]+\.csv\.gz$", System.Text.RegularExpressions.RegexOptions.Compiled);

    public string Dossier { get; } = dossier;

    /// <summary>Fichiers disponibles d'après le manifeste ; liste vide si l'export n'a pas encore tourné.</summary>
    public IReadOnlyList<FichierDonnees> Lire()
    {
        var chemin = Path.Combine(Dossier, "manifest.json");
        if (!File.Exists(chemin)) return [];
        try
        {
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<List<FichierDonnees>>(File.ReadAllText(chemin), opts) ?? [];
        }
        catch (JsonException) { return []; }
    }

    /// <summary>Chemin d'un fichier à servir, ou null : le nom doit être de la forme « nom.csv.gz » ET figurer au manifeste.</summary>
    public string? Chemin(string nom)
    {
        if (!NomValide.IsMatch(nom) || !Lire().Any(f => f.Fichier == nom)) return null;
        var chemin = Path.Combine(Dossier, nom);
        return File.Exists(chemin) ? chemin : null;
    }
}
