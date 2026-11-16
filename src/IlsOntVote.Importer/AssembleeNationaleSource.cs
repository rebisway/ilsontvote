using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IlsOntVote.Importer;

/// <summary>
/// Lit les jeux open data de l'Assemblée nationale (data.assemblee-nationale.fr),
/// déjà extraits sur disque : « Scrutins » (un JSON par scrutin) et
/// « AMO30 » (acteurs, mandats, organes).
/// </summary>
public sealed partial class AssembleeNationaleSource
{
    // « l'ensemble du projet de loi… », « l’ensemble de la proposition de loi… »
    [GeneratedRegex(@"^l['’]ensemble (du|de la|des|de l['’])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SurEnsembleRegex();

    private static readonly (string Cle, string Position)[] Categories =
    [
        ("pours", Positions.Pour),
        ("contres", Positions.Contre),
        ("abstentions", Positions.Abstention),
        ("nonVotants", Positions.NonVotant),
        ("nonVotantsVolontaires", Positions.NonVotant),
    ];

    // ------------------------------------------------------------------ annuaire

    /// <param name="slugsExistants">id -> slug déjà en base : un slug ne change jamais (il fait l'URL).</param>
    public Annuaire LireAnnuaire(string amoDir, int legislature, IReadOnlyDictionary<string, string> slugsExistants)
    {
        var acteursDir = Path.Combine(amoDir, "acteur");
        var organesDir = Path.Combine(amoDir, "organe");
        var leg = legislature.ToString(CultureInfo.InvariantCulture);

        var deputes = new List<ParlementaireRow>();
        var mandats = new List<MandatRow>();
        var appartenances = new List<AppartenanceRow>();
        var groupes = new Dictionary<string, GroupeRow>();
        var slugs = new HashSet<string>(slugsExistants.Values);

        foreach (var file in Directory.EnumerateFiles(acteursDir, "PA*.json"))
        {
            using var doc = Parse(file);
            var acteur = doc.RootElement.Get("acteur");
            if (acteur is null) continue;

            var mandatsAn = acteur.Value.Dig("mandats", "mandat").Items()
                .Where(m => m.Str("typeOrgane") == "ASSEMBLEE" && m.Str("legislature") == leg)
                .ToList();
            if (mandatsAn.Count == 0) continue;

            var id = acteur.Value.Get("uid")?.Get("#text").Str();
            var ident = acteur.Value.Dig("etatCivil", "ident");
            if (id is null || ident is null) continue;

            var prenom = ident.Value.Str("prenom") ?? "";
            var nom = ident.Value.Str("nom") ?? "";
            var election = mandatsAn[0].Dig("election", "lieu");

            deputes.Add(new ParlementaireRow(
                id, Chambres.AN, ident.Value.Str("civ"), prenom, nom,
                slugsExistants.TryGetValue(id, out var slugConnu) ? slugConnu : Slugs.Unique(slugs, prenom, nom, id),
                election?.Str("departement"), election?.Str("numCirco")));

            foreach (var m in mandatsAn)
            {
                // `dateDebut` est la date de l'ÉLECTION, même pour un suppléant qui ne siège que plus tard (après un décès,
                // une nomination au gouvernement…) : la date de PRISE DE FONCTION dit quand l'élu siège réellement.
                // Sans elle, titulaire et suppléant comptent tous deux comme présents sur la même période.
                var debut = m.Dig("mandature", "datePriseFonction").Date() ?? m.Get("dateDebut").Date();
                if (debut is null) continue;
                mandats.Add(new MandatRow(id, legislature, debut.Value, m.Get("dateFin").Date()));
            }

            foreach (var m in acteur.Value.Dig("mandats", "mandat").Items()
                         .Where(m => m.Str("typeOrgane") == "GP" && m.Str("legislature") == leg))
            {
                var organeRef = m.Dig("organes", "organeRef").Str();
                var debut = m.Get("dateDebut").Date();
                if (organeRef is null || debut is null) continue;

                if (!groupes.ContainsKey(organeRef) && LireGroupe(organesDir, organeRef, legislature) is { } g)
                    groupes[organeRef] = g;

                appartenances.Add(new AppartenanceRow(id, organeRef, debut.Value, m.Get("dateFin").Date()));
            }
        }

        return new Annuaire([.. groupes.Values], deputes, mandats, appartenances);
    }

    private static GroupeRow? LireGroupe(string organesDir, string organeRef, int legislature)
    {
        var file = Path.Combine(organesDir, organeRef + ".json");
        if (!File.Exists(file)) return null;
        using var doc = Parse(file);
        var o = doc.RootElement.Get("organe");
        if (o is null) return null;
        var sigle = o.Value.Str("libelleAbrev") ?? organeRef;
        return new GroupeRow(
            organeRef, Chambres.AN, legislature, sigle,
            o.Value.Str("libelle") ?? organeRef,
            o.Value.Str("couleurAssociee"),
            GroupeOrdre.Pour(sigle));
    }

    // ------------------------------------------------------------------ scrutins

    public IEnumerable<ScrutinAvecVotes> LireScrutins(string scrutinsDir)
    {
        foreach (var file in Directory.EnumerateFiles(scrutinsDir, "VTANR*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            using var doc = Parse(file);
            var s = doc.RootElement.Get("scrutin");
            if (s is null) continue;
            if (LireScrutin(s.Value) is { } result) yield return result;
        }
    }

    private static ScrutinAvecVotes? LireScrutin(JsonElement s)
    {
        var id = s.Str("uid");
        var numero = s.Get("numero").Int();
        var legislature = s.Get("legislature").Int();
        var date = s.Get("dateScrutin").Date();
        if (id is null || numero is null || legislature is null || date is null) return null;

        var titre = s.Str("titre") ?? "";
        var decompte = s.Dig("syntheseVote", "decompte");

        var scrutin = new ScrutinRow(
            id, Chambres.AN, legislature.Value, numero.Value, date.Value, titre,
            s.Dig("typeVote").Str("codeTypeVote"),
            s.Dig("sort").Str("code"),
            s.Dig("objet").Str("dossierLegislatif"),
            SurEnsembleRegex().IsMatch(titre.Trim()),
            s.Dig("syntheseVote", "nombreVotants").Int(),
            decompte?.Get("pour").Int(), decompte?.Get("contre").Int(),
            decompte?.Get("abstentions").Int(), decompte?.Get("nonVotants").Int(),
            s.Str("modePublicationDesVotes"));

        // 1. Votes tels que publiés (décompte nominatif, par groupe).
        var votes = new Dictionary<string, VoteRow>();
        foreach (var groupe in s.Dig("ventilationVotes", "organe", "groupes", "groupe").Items())
        {
            var groupeId = groupe.Str("organeRef");
            var nominatif = groupe.Dig("vote", "decompteNominatif");
            if (nominatif is null) continue;

            foreach (var (cle, position) in Categories)
            foreach (var votant in nominatif.Value.Get(cle).Items())
            {
                var acteur = votant.Str("acteurRef");
                if (acteur is null) continue;
                var delegation = votant.Str("parDelegation") == "true";
                votes[acteur] = new VoteRow(id, acteur, groupeId, position, delegation, false, position);
            }
        }

        // 2. Mises au point : le député déclare après coup avoir voulu voter
        //    autrement. On garde les deux positions.
        var misesAuPoint = s.Get("miseAuPoint");
        if (misesAuPoint is not null)
        {
            foreach (var (cle, position) in Categories)
            foreach (var votant in misesAuPoint.Value.Get(cle).Items())
            {
                var acteur = votant.Str("acteurRef");
                if (acteur is null) continue;

                if (votes.TryGetValue(acteur, out var existant))
                {
                    if (existant.Position != position)
                        votes[acteur] = existant with { Position = position, Rectifie = true };
                }
                else
                {
                    // Absent à l'origine, mise au point seule.
                    votes[acteur] = new VoteRow(id, acteur, null, position, false, true, null);
                }
            }
        }

        // Contrôle : le décompte nominatif doit retrouver les totaux officiels
        // (positions d'origine, avant mises au point). Sinon la source est incomplète.
        int Trouves(string pos) => votes.Values.Count(v => v.PositionOrigine == pos);
        var manquants =
            Math.Max(0, (scrutin.NbPour ?? 0) - Trouves(Positions.Pour)) +
            Math.Max(0, (scrutin.NbContre ?? 0) - Trouves(Positions.Contre)) +
            Math.Max(0, (scrutin.NbAbstentions ?? 0) - Trouves(Positions.Abstention));

        return new ScrutinAvecVotes(scrutin with
        {
            NbVotesManquants = manquants,
            TypeVoteLibelle = s.Dig("typeVote").Str("libelleTypeVote"),
            TypeMajorite = s.Dig("typeVote").Str("typeMajorite"),
            SuffragesExprimes = s.Dig("syntheseVote", "suffragesExprimes").Int(),
            SuffragesRequis = s.Dig("syntheseVote", "nbrSuffragesRequis").Int(),
        }, [.. votes.Values]);
    }

    // ------------------------------------------------------------------ utilitaires

    private static JsonDocument Parse(string file)
    {
        using var stream = File.OpenRead(file);
        return JsonDocument.Parse(stream);
    }

}
