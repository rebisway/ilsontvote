using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace IlsOntVote.Importer;

/// <summary>Un élu du Congrès américain d'après Voteview : identifiant ICPSR (clé stable, présent même pour les élus anciens), nom, État, circonscription, parti.</summary>
public sealed record MembreUs(string Chambre, string Icpsr, string Bioguide, string Nom, string Prenom, string Etat, string? Circonscription, int CodeParti);

/// <summary>Un vote nominal (roll call) : numéro dans le Congrès, date, question, résultat texte, décomptes officiels.</summary>
public sealed record AppelNominalUs(
    string Chambre, int Congres, int Numero, DateOnly Date, int Session, int NumeroGreffe, string Question, string Resultat, string Projet,
    string Description, string DescriptionLongue, int Pour, int Contre, string Majorite);

/// <summary>Le code de vote d'un élu à un appel nominal (codes de Voteview : 1 yea … 9 not voting).</summary>
public sealed record CodeVoteUs(string Chambre, int Numero, string Icpsr, int Code);

/// <summary>
/// Congrès des États-Unis (Chambre des représentants et Sénat) d'après <c>voteview.com</c> : trois fichiers CSV par Congrès (élus, appels nominaux, votes de chaque élu),
/// à jour au jour près. Pourquoi Voteview : le Sénat (<c>senate.gov</c>) refuse les clients automatiques (403) et la Chambre n'a qu'un XML par vote ; Voteview donne les deux
/// chambres dans un format unique. C'est un traitement universitaire (UCLA, Université de Géorgie) des actes officiels : la source est signalée « tierce » sur le site, avec un lien vers
/// l'enregistrement officiel de chaque vote. Les scores idéologiques (NOMINATE) du fichier sont des calculs de chercheurs : jamais importés.
/// </summary>
public sealed class UsSource : IDisposable
{
    private const string Racine = "https://voteview.com/static/data/out";

    /// <summary>Premier Congrès de l'historique importé (107e : 3 janvier 2001) et Congrès en cours (119e : 3 janvier 2025).</summary>
    public const int PremierHistorique = 107, Courant = 119;

    public static string IdChambre(string chambreVoteview) => chambreVoteview == "Senate" ? Chambres.UsSenate : Chambres.UsHouse;
    public static string IdMembre(string chambre, string icpsr) => $"{chambre}-{icpsr}";
    public static string IdGroupe(string chambre, string sigle) => $"{chambre}-{sigle}";
    public static string IdScrutin(string chambre, int congres, int numero) => $"{chambre}-{congres}-{numero}";

    private readonly HttpClient _http = Telechargement.CreerClient(TimeSpan.FromMinutes(5));

    /// <summary>Les trois fichiers d'un Congrès, lus et analysés (élus, appels nominaux, votes).</summary>
    public async Task<(IReadOnlyList<MembreUs> Membres, IReadOnlyList<AppelNominalUs> Appels, IReadOnlyList<CodeVoteUs> Votes)> LireCongresAsync(int congres)
    {
        var m = await _http.GetStringAsync($"{Racine}/members/HS{congres}_members.csv");
        var r = await _http.GetStringAsync($"{Racine}/rollcalls/HS{congres}_rollcalls.csv");
        var v = await _http.GetStringAsync($"{Racine}/votes/HS{congres}_votes.csv");
        return (AnalyserMembres(m), AnalyserAppels(r), AnalyserVotes(v));
    }

    // ------------------------------------------------------------------ analyse des CSV (séparée du réseau : testée sans connexion)

    /// <summary>Lecteur CSV RFC 4180 (champs entre guillemets, virgules et guillemets doublés dans les champs) : les descriptions de votes contiennent des virgules.</summary>
    public static IEnumerable<string[]> LireCsv(string texte)
    {
        var champ = new StringBuilder();
        var ligne = new List<string>();
        var guillemets = false;
        for (var i = 0; i < texte.Length; i++)
        {
            var c = texte[i];
            if (guillemets)
            {
                if (c == '"') { if (i + 1 < texte.Length && texte[i + 1] == '"') { champ.Append('"'); i++; } else guillemets = false; }
                else champ.Append(c);
            }
            else if (c == '"') guillemets = true;
            else if (c == ',') { ligne.Add(champ.ToString()); champ.Clear(); }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < texte.Length && texte[i + 1] == '\n') i++;
                ligne.Add(champ.ToString()); champ.Clear();
                if (ligne.Count > 1 || ligne[0].Length > 0) yield return [.. ligne];
                ligne.Clear();
            }
            else champ.Append(c);
        }
        if (champ.Length > 0 || ligne.Count > 0) { ligne.Add(champ.ToString()); yield return [.. ligne]; }
    }

    private static (Dictionary<string, int> Index, IEnumerable<string[]> Lignes) Entete(string csv)
    {
        var lignes = LireCsv(csv);
        var e = lignes.First();
        return (e.Select((n, i) => (n, i)).ToDictionary(x => x.n, x => x.i), lignes.Skip(1));
    }

    private static string Col(string[] l, Dictionary<string, int> idx, string nom) => idx.TryGetValue(nom, out var i) && i < l.Length ? l[i].Trim() : "";

    /// <summary>Élus d'un Congrès (la ligne « President » du fichier et les lignes sans identifiant sont écartées).</summary>
    public static IReadOnlyList<MembreUs> AnalyserMembres(string csv)
    {
        var (idx, lignes) = Entete(csv);
        var res = new List<MembreUs>();
        foreach (var l in lignes)
        {
            var chambre = Col(l, idx, "chambre") is { Length: > 0 } c ? c : Col(l, idx, "chamber");
            if (chambre is not ("House" or "Senate")) continue;
            var icpsr = Col(l, idx, "icpsr");
            if (icpsr.Length == 0) continue;
            var (prenom, nom) = DecouperNom(Col(l, idx, "bioname"));
            var district = Col(l, idx, "district_code");
            var etat = Col(l, idx, "state_abbrev");
            // Chambre : « AL-5 » ; sénateur : pas de circonscription (l'État suffit). Les districts 0 / 98 / 99 (élu unique de l'État, délégué) : « AL-AL ».
            var circ = chambre == "House" ? (district is "" or "0" or "98" or "99" ? $"{etat} (at large)" : $"{etat}-{district}") : null;
            int.TryParse(Col(l, idx, "party_code"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parti);
            res.Add(new MembreUs(IdChambre(chambre), icpsr, Col(l, idx, "bioguide_id"), nom, prenom, etat, circ, parti));
        }
        return res;
    }

    /// <summary>« STRONG, Dale » → (« Dale », « Strong ») ; « ROGERS, Harold Dallas (Hal) » → (« Hal », « Rogers »). Nom tout en capitales : mis en casse de titre ; « McCARTHY » garde sa forme.</summary>
    public static (string Prenom, string Nom) DecouperNom(string bioname)
    {
        var morceaux = bioname.Split(',', 2, StringSplitOptions.TrimEntries);
        var nom = morceaux[0];
        if (nom.Length > 1 && nom == nom.ToUpperInvariant())
            nom = string.Join("", Regex.Split(nom.ToLowerInvariant(), "([ '-])").Select(p => p.Length > 0 && char.IsLetter(p[0]) ? char.ToUpperInvariant(p[0]) + p[1..] : p));
        var reste = morceaux.Length > 1 ? morceaux[1] : "";
        var surnom = Regex.Match(reste, @"\(([^)]+)\)");
        var prenom = surnom.Success ? surnom.Groups[1].Value.Trim() : reste.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return (prenom, nom);
    }

    /// <summary>Appels nominaux d'un Congrès (les deux chambres).</summary>
    public static IReadOnlyList<AppelNominalUs> AnalyserAppels(string csv)
    {
        var (idx, lignes) = Entete(csv);
        var res = new List<AppelNominalUs>();
        foreach (var l in lignes)
        {
            var chambre = Col(l, idx, "chamber");
            if (chambre is not ("House" or "Senate")) continue;
            if (!DateOnly.TryParse(Col(l, idx, "date"), CultureInfo.InvariantCulture, out var date)) continue;
            int E(string n) => int.TryParse(Col(l, idx, n), NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) ? x : 0;
            res.Add(new AppelNominalUs(IdChambre(chambre), E("congress"), E("rollnumber"), date, E("session"), E("clerk_rollnumber"),
                Col(l, idx, "vote_question"), Col(l, idx, "vote_result"), Col(l, idx, "bill_number"), Col(l, idx, "vote_desc"), Col(l, idx, "dtl_desc"),
                E("yea_count"), E("nay_count"), Col(l, idx, "majority_requirement")));
        }
        return res;
    }

    /// <summary>Votes de chaque élu à chaque appel (fichier volumineux : une ligne par élu et par vote).</summary>
    public static IReadOnlyList<CodeVoteUs> AnalyserVotes(string csv)
    {
        var (idx, lignes) = Entete(csv);
        var res = new List<CodeVoteUs>();
        foreach (var l in lignes)
        {
            var chambre = Col(l, idx, "chamber");
            if (chambre is not ("House" or "Senate")) continue;
            if (!int.TryParse(Col(l, idx, "rollnumber"), out var num) || !int.TryParse(Col(l, idx, "cast_code"), out var code)) continue;
            res.Add(new CodeVoteUs(IdChambre(chambre), num, Col(l, idx, "icpsr"), code));
        }
        return res;
    }

    // ------------------------------------------------------------------ conversion

    /// <summary>Sigle du parti d'après le code Voteview (100 démocrate, 200 républicain, 328 indépendant) ; les autres gardent leur code (« P714 »).</summary>
    public static (string Id, string Sigle, string Libelle, string? Couleur) Parti(int code) => code switch
    {
        // Id = clé stable du groupe (ne change jamais) ; Sigle = ce qu'on affiche : le NOM du parti, pas une lettre (« D » ne dit rien à un lecteur non américain).
        100 => ("D", "Democratic", "Democratic Party", "#2a62c9"),
        200 => ("R", "Republican", "Republican Party", "#d6332d"),
        328 => ("I", "Independent", "Independent", "#8a8f98"),
        0 => ("NA", "No party", "No party recorded", null),
        _ => ($"P{code}", "Other", $"Other party (Voteview code {code})", null),
    };

    /// <summary>Place du parti dans l'hémicycle (gauche vers droite) : démocrates, indépendants, républicains.</summary>
    public static int OrdreParti(string sigle) => sigle switch { "D" => 40, "I" => 60, "R" => 90, _ => 100 };

    /// <summary>
    /// Position d'après le code Voteview : 1 yea → pour ; 6 nay → contre ; 7 et 8 « present » → abstention ; tout le reste = pas de vote exprimé (not voting, ou position
    /// ANNONCÉE / APPARIÉE, codes 2 à 5 : une annonce ou un appariement n'est pas un vote et ne compte pas dans le décompte officiel). Null = absent, non stocké (absent = absent).
    /// </summary>
    public static string? Position(int code) => code switch { 1 => Positions.Pour, 6 => Positions.Contre, 7 or 8 => Positions.Abstention, _ => null };

    /// <summary>
    /// Résultat d'après le texte de la source (« Passed », « Agreed to », « Nomination Confirmed » → adopté ; « Failed », « Rejected » → rejeté). Texte inconnu (élection du Président de
    /// la Chambre : le nom de l'élu) : aucun résultat, jamais deviné.
    /// </summary>
    public static string? Sort(string resultat)
    {
        var r = resultat.ToLowerInvariant();
        if (Regex.IsMatch(r, @"fail|reject|not agreed|not sustained|not confirmed|defeat|lost")) return "rejeté";
        if (Regex.IsMatch(r, @"pass|agreed to|confirmed|sustained|adopted|ratified|approved")) return "adopté";
        return null;
    }

    /// <summary>
    /// Vote sur l'ensemble d'une mesure ou d'une nomination (CALCULÉ d'après la question du vote, la source ne le distingue pas) : adoption finale d'un projet ou d'une résolution,
    /// motion « suspend the rules and pass » (la Chambre y adopte la plupart de ses textes), confirmation d'une nomination. Les amendements, motions de procédure et cloture n'en sont pas.
    /// </summary>
    public static bool EstVoteSurEnsemble(string question) =>
        Regex.IsMatch(question, @"^On (Passage|the Joint Resolution|the Concurrent Resolution|the Bill|the Nomination|Agreeing to the (Joint |Concurrent )?Resolution|Agreeing to the Conference Report|the Conference Report|Motion to Suspend the Rules and (Pass|Agree))", RegexOptions.IgnoreCase);

    /// <summary>Majorité requise : voix nécessaires d'après la règle de la source (« 1/2 » simple, « 3/5 » cloture, « 2/3 »), sur les suffrages exprimés. Calculé par le site.</summary>
    public static (string? Libelle, int? Requis) Majorite(string regle, int exprimes)
    {
        return regle switch
        {
            "1/2" => ("Simple majority of those voting (calculated)", exprimes / 2 + 1),
            "3/5" => ("Three-fifths (calculated)", (int)Math.Ceiling(3.0 * exprimes / 5)),
            "2/3" => ("Two-thirds of those voting (calculated)", (int)Math.Ceiling(2.0 * exprimes / 3)),
            _ => (null, null),
        };
    }

    /// <summary>Adresse de l'enregistrement officiel du vote (greffe de la Chambre ; Sénat) : stockée dans <c>dossier_ref</c> et affichée comme lien « official record ».</summary>
    public static string UrlOfficielle(AppelNominalUs a) => a.Chambre == Chambres.UsHouse
        ? $"https://clerk.house.gov/Votes/{a.Date.Year}{a.NumeroGreffe:000}"
        : $"https://www.senate.gov/legislative/LIS/roll_call_votes/vote{a.Congres}{a.Session}/vote_{a.Congres}_{a.Session}_{a.NumeroGreffe:00000}.htm";

    /// <summary>Titre : « projet : description » (« HR29: Laken Riley Act »), à défaut la question du vote.</summary>
    public static string Titre(AppelNominalUs a)
    {
        var d = a.Description.Length > 0 ? a.Description : a.DescriptionLongue;
        var t = a.Projet.Length > 0 && d.Length > 0 ? $"{a.Projet}: {d}" : a.Projet.Length > 0 ? a.Projet : d.Length > 0 ? d : a.Question;
        return a.Projet.Length > 0 && d.Length == 0 ? $"{a.Projet} — {a.Question}" : t;
    }

    /// <summary>Convertit un appel nominal et les codes de vote de ses élus en scrutin + votes. Décomptes OFFICIELS repris tels quels ; écart avec la liste des élus dit dans la note.</summary>
    public static ScrutinAvecVotes VersScrutin(AppelNominalUs a, IReadOnlyList<CodeVoteUs> codes, IReadOnlyDictionary<string, string> groupeDe)
    {
        var id = IdScrutin(a.Chambre, a.Congres, a.Numero);
        var votes = new List<VoteRow>();
        var annonces = 0;
        foreach (var c in codes)
        {
            if (Position(c.Code) is { } pos) votes.Add(new VoteRow(id, IdMembre(a.Chambre, c.Icpsr), groupeDe.GetValueOrDefault(c.Icpsr), pos, false, false, pos));
            else if (c.Code is >= 2 and <= 5) annonces++;
        }
        var y = votes.Count(v => v.Position == Positions.Pour);
        var n = votes.Count(v => v.Position == Positions.Contre);
        var present = votes.Count(v => v.Position == Positions.Abstention);
        var notes = new List<string>();
        if (y != a.Pour || n != a.Contre)
            notes.Add($"Official count: {a.Pour} yea, {a.Contre} nay; member list used by this site (Voteview): {y} yea, {n} nay. The official count is shown; the dots follow the member list.");
        if (annonces > 0)
            notes.Add($"{annonces} member(s) announced or paired a position: an announcement is not a vote and is not counted (shown as not voting).");
        var exprimes = a.Pour + a.Contre;
        var (libMaj, requis) = Majorite(a.Majorite, exprimes);
        var scrutin = new ScrutinRow(
            id, a.Chambre, a.Congres, a.Numero, a.Date, Titre(a), "roll call", Sort(a.Resultat), UrlOfficielle(a), EstVoteSurEnsemble(a.Question),
            exprimes + present, a.Pour, a.Contre, present, null, null, 0, notes.Count == 0 ? null : string.Join(" ", notes),
            a.Question, libMaj, exprimes, requis, Titre(a));
        return new ScrutinAvecVotes(scrutin, votes);
    }

    public void Dispose() => _http.Dispose();
}
