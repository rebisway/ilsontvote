using System.Text.Json;

namespace IlsOntVote.Importer;

/// <summary>Un membre de la Chambre des Communes vu dans une division (voté pour, contre, ou « no vote recorded »).</summary>
public sealed record MembreCommons(
    int MemberId, string Nom, string Prenom, string Circonscription, string PartiSigle, string PartiLibelle, string? Couleur, string? Procuration);

/// <summary>Une division (scrutin par appel nominal) des Communes, telle que publiée, avec les trois listes de membres.</summary>
public sealed record DivisionCommons(
    int Id, int Numero, DateOnly Date, string Titre, int OfficielPour, int OfficielContre, int NbPourListe, int NbContreListe,
    IReadOnlyList<MembreCommons> Pour, IReadOnlyList<MembreCommons> Contre, IReadOnlyList<MembreCommons> SansVote);

/// <summary>
/// Chambre des Communes (Royaume-Uni) via l'API publique « Commons Votes » (commonsvotes-api.parliament.uk, JSON, sans clé ; Open Parliament Licence).
/// Chaque division liste TOUS les membres en exercice : ceux qui ont voté pour (Ayes), contre (Noes), et ceux « sans vote enregistré » (NoVoteRecorded :
/// absents, président de séance et vice-présidents qui ne votent pas, abstentionnistes de Sinn Féin…). La liste « sans vote » permet de reconstituer
/// les mandats (qui siégeait le jour d'une division) sans annuaire daté : même principe que le Parlement européen.
/// Le Royaume-Uni n'a pas d'abstention formelle en division : s'abstenir, c'est ne pas voter (ou voter dans les deux lobbies, rare).
/// </summary>
public sealed class CommonsSource : IDisposable
{
    private const string Racine = "https://commonsvotes-api.parliament.uk/data";

    /// <summary>Numéro de la législature (« Parliament ») courante : la 59e, élue le 4 juillet 2024, ouverte le 9 juillet 2024.</summary>
    public const int Legislature = 59;

    /// <summary>Début de la 59e législature : date de départ de l'import (les législatures précédentes se rajouteront plus tard).</summary>
    public static readonly DateOnly DebutLegislature = new(2024, 7, 9);

    /// <summary>L'API plafonne à 25 divisions par page, quelle que soit la valeur de « take » demandée (constaté).</summary>
    private const int TaillePage = 25;

    private readonly HttpClient _http = Telechargement.CreerClient(TimeSpan.FromMinutes(2));

    /// <summary>Identifiants et dates des divisions depuis <paramref name="depuis"/> (sans les membres : une requête par page de 25).</summary>
    public async Task<IReadOnlyList<(int Id, DateOnly Date)>> ListerAsync(DateOnly depuis)
    {
        var liste = new List<(int, DateOnly)>();
        for (var skip = 0; ; skip += TaillePage)
        {
            var json = await _http.GetStringAsync($"{Racine}/divisions.json/search?queryParameters.startDate={depuis:yyyy-MM-dd}&queryParameters.skip={skip}&queryParameters.take={TaillePage}");
            using var doc = JsonDocument.Parse(json);
            var n = 0;
            foreach (var d in doc.RootElement.EnumerateArray())
            {
                liste.Add((d.GetProperty("DivisionId").GetInt32(), DateOnly.FromDateTime(d.GetProperty("Date").GetDateTime())));
                n++;
            }
            if (n < TaillePage) break;
            await Task.Delay(200);   // politesse : l'API est publique et partagée
        }
        return liste;
    }

    /// <summary>Lit une division avec ses membres.</summary>
    public async Task<DivisionCommons> LireAsync(int id)
    {
        var json = await _http.GetStringAsync($"{Racine}/division/{id}.json");
        return Analyser(json);
    }

    /// <summary>Analyse le JSON d'une division (séparé de la lecture réseau pour être testé sans connexion).</summary>
    public static DivisionCommons Analyser(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        // Les « tellers » (scrutateurs) sont des membres qui ont voté dans leur camp : ils figurent dans une liste à part, hors de Ayes / Noes.
        // Le décompte officiel (AyeCount / NoCount) correspond à la liste Ayes / Noes SEULE, scrutateurs exclus (constaté sur dix divisions : égalité exacte
        // dans la plupart, écarts de 1 à 10 membres non nommés dans les autres). NbPourListe / NbContreListe servent à cette comparaison.
        var ayes = Membres(r, "Ayes").ToList();
        var noes = Membres(r, "Noes").ToList();
        var pour = ayes.Concat(Membres(r, "AyeTellers")).GroupBy(m => m.MemberId).Select(g => g.First()).ToList();
        var contre = noes.Concat(Membres(r, "NoTellers")).GroupBy(m => m.MemberId).Select(g => g.First()).ToList();
        var sans = Membres(r, "NoVoteRecorded").ToList();
        return new DivisionCommons(
            r.GetProperty("DivisionId").GetInt32(), r.GetProperty("Number").GetInt32(),
            DateOnly.FromDateTime(r.GetProperty("Date").GetDateTime()), (r.GetProperty("Title").GetString() ?? "").Trim(),
            r.GetProperty("AyeCount").GetInt32(), r.GetProperty("NoCount").GetInt32(), ayes.Count, noes.Count, pour, contre, sans);
    }

    private static IEnumerable<MembreCommons> Membres(JsonElement division, string liste)
    {
        if (!division.TryGetProperty(liste, out var tab) || tab.ValueKind != JsonValueKind.Array) yield break;
        foreach (var m in tab.EnumerateArray())
        {
            // « ListAs » : « Nom de famille, Titre Prénom » (ex. « Jenkin, Sir Bernard ») ; à défaut, « Name » (« Bernard Jenkin »).
            var listAs = Texte(m, "ListAs");
            string nom, prenom;
            if (listAs.Contains(','))
            {
                var p = listAs.Split(',', 2);
                nom = p[0].Trim(); prenom = p[1].Trim();
            }
            else
            {
                var p = Texte(m, "Name").Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                prenom = p.Length > 0 ? p[0] : ""; nom = p.Length > 1 ? p[1] : listAs;
            }
            var couleur = Texte(m, "PartyColour");
            yield return new MembreCommons(
                m.GetProperty("MemberId").GetInt32(), nom, prenom, Texte(m, "MemberFrom"),
                // Président et vice-présidents de séance : « Party » renseigné (« Deputy Speaker ») mais sigle vide : le libellé sert de sigle.
                Texte(m, "PartyAbbreviation") is { Length: > 0 } sigle ? sigle : Texte(m, "Party"), Texte(m, "Party"), couleur.Length == 0 ? null : "#" + couleur,
                m.TryGetProperty("ProxyName", out var px) && px.ValueKind == JsonValueKind.String ? px.GetString() : null);
        }
    }

    private static string Texte(JsonElement e, string nom) =>
        e.TryGetProperty(nom, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "").Trim() : "";

    /// <summary>Identifiants préfixés : un MemberId ou un DivisionId britannique ne doit jamais pouvoir coïncider avec ceux d'une autre chambre.</summary>
    public static string IdMembre(int memberId) => $"COMMONS-{memberId}";
    public static string IdGroupe(string sigle) => $"COMMONS-{sigle}";
    public static string IdScrutin(int divisionId) => $"COMMONS-{divisionId}";

    /// <summary>
    /// Convertit une division en scrutin + votes. Règles :
    /// - pour / contre = les listes Ayes + Noes (scrutateurs inclus) ; « sans vote enregistré » n'est PAS stocké (absent = absent, déduit des mandats) ;
    /// - vote par procuration (ProxyName renseigné) = par délégation ;
    /// - résultat « adopté » ou « rejeté » CALCULÉ d'après les décomptes officiels (égalité : indéterminé) et dit comme tel ;
    /// - si les listes ne totalisent pas les décomptes officiels (constaté : l'API est corrigée après publication), le scrutin est marqué incomplet
    ///   et la note donne les deux chiffres, sans choisir.
    /// </summary>
    public static ScrutinAvecVotes VersScrutin(DivisionCommons d)
    {
        var id = IdScrutin(d.Id);
        // Un membre listé à la fois dans « Ayes » et « Noes » a voté dans les DEUX lobbies : pratique britannique reconnue comme abstention.
        // Constaté (ex. divisions des 3 et 16 novembre 2026). Sans ce traitement, la clé (scrutin, membre) serait en double.
        var deuxListes = d.Pour.Select(m => m.MemberId).Intersect(d.Contre.Select(m => m.MemberId)).ToHashSet();
        var votes = d.Pour.Where(m => !deuxListes.Contains(m.MemberId)).Select(m => Vote(id, m, Positions.Pour))
            .Concat(d.Contre.Where(m => !deuxListes.Contains(m.MemberId)).Select(m => Vote(id, m, Positions.Contre)))
            .Concat(d.Pour.Where(m => deuxListes.Contains(m.MemberId)).Select(m => Vote(id, m, Positions.Abstention)))
            .ToList();
        // Comparaison au décompte officiel sur les listes Ayes / Noes hors scrutateurs (voir Analyser) : seul un déficit de noms est un manque.
        var manquants = Math.Max(0, d.OfficielPour - d.NbPourListe) + Math.Max(0, d.OfficielContre - d.NbContreListe);
        var notes = new List<string>();
        // Notes en ANGLAIS : le site des Communes (/uk) n'a que l'interface anglaise.
        if (manquants > 0)
            notes.Add($"Official count: {d.OfficielPour} ayes, {d.OfficielContre} noes; members named in the published lists: {d.NbPourListe} ayes, {d.NbContreListe} noes " +
                      "(tellers are listed separately and added to the votes). The adopted / rejected result is computed from the official count.");
        if (deuxListes.Count > 0)
            notes.Add($"{deuxListes.Count} member(s) appear in both the ayes and the noes lists: recorded as an abstention (voted in both lobbies, which parliamentary practice treats as an abstention).");
        var scrutin = new ScrutinRow(
            // Numéro : l'identifiant de division (unique sur toute la législature). Le numéro de division de la session repart de 1 chaque session et
            // violerait la contrainte unique (chambre, législature, numéro) de scrutins (constaté) : il est repris dans le libellé du type de vote.
            id, Chambres.Commons, Legislature, d.Id, d.Date, d.Titre, "division", Sort(d.OfficielPour, d.OfficielContre), null, false,
            d.OfficielPour + d.OfficielContre, d.OfficielPour, d.OfficielContre, deuxListes.Count, null, null,
            manquants, notes.Count == 0 ? null : string.Join(" ", notes), $"Division no. {d.Numero} of the session (recorded vote; no formal abstention in the Commons)", null, null, null, d.Titre);
        return new ScrutinAvecVotes(scrutin, votes);
    }

    private static VoteRow Vote(string scrutinId, MembreCommons m, string position) =>
        new(scrutinId, IdMembre(m.MemberId), IdGroupe(m.PartiSigle), position, m.Procuration is not null, false, position);

    /// <summary>Place d'un parti dans l'hémicycle (gauche vers droite) : valeur initiale, jamais écrasée par l'import (modifiable en base).</summary>
    public static int OrdreParti(string sigle) => sigle.ToUpperInvariant() switch
    {
        "GREEN" => 30,
        "SF" => 35,          // Sinn Féin : élus abstentionnistes (ne siègent pas)
        "LAB" or "LAB/CO-OP" => 40,
        "SDLP" => 42,
        "SNP" => 45,
        "PC" => 46,
        "LD" => 60,
        "ALL" or "APNI" => 62,
        "CON" => 90,
        "DUP" => 95,
        "UUP" => 96,
        "REFORM" or "REF" => 115,
        "IND" => 130,
        "SPK" or "SPEAKER" or "DEPUTY SPEAKER" => 135,   // président et vice-présidents de séance
        _ => 100,
    };

    /// <summary>Statut de la décision d'après les décomptes officiels : adopté si plus de pour que de contre ; égalité = indéterminé (voix prépondérante du président).</summary>
    public static string? Sort(int pour, int contre) => pour > contre ? "adopté" : pour < contre ? "rejeté" : null;

    public void Dispose() => _http.Dispose();
}
