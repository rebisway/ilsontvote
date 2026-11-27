using Dapper;
using IlsOntVote.Web.Data;
using IlsOntVote.Web.Models;
using IlsOntVote.Web.Repositories;

namespace IlsOntVote.Web.Mcp;

/// <summary>Votes parlementaires (table scrutins), toutes chambres publiées.</summary>
public sealed class VotesParlementaires(DapperContext db) : ITypeDocument
{
    public string Code => "parliamentary_vote";
    public string Nom => "Parliamentary vote";
    public string Description =>
        "Recorded votes of parliamentary chambers. Votes by show of hands have no individual record and are not listed here. " +
        "Use the vote_positions tool to see who voted how. Titles are in the language of the source.";
    public string[] Filtres => ["text", "country", "chamber", "from", "to", "theme", "final_votes_only"];
    public IReadOnlyList<string> Pays(Perimetre p) => [.. p.Chambres.Values.Distinct()];

    private const string Colonnes =
        """
        id, chambre, legislature, numero, date_scrutin, titre, titre_en, titre_en_auto, titre_fr_auto, sort, type_vote, sur_ensemble,
        nb_votants, nb_pour, nb_contre, nb_abstentions, nb_non_votants, featured_title, nominatif_incomplet, nb_votes_manquants, note,
        type_vote_libelle, type_majorite, suffrages_exprimes, suffrages_requis, dossier_ref, themes, confiance
        """;

    private static object Resume(Scrutin s, Perimetre p) => new
    {
        s.Id, Chamber = s.Chambre, Country = p.Chambres[s.Chambre], Date = Reponse.Jour(s.DateScrutin), Title = s.TitreAffiche, TitleEn = s.TitreEn,
        TitleMachineTranslated = s.TitreEnAuto || s.TitreFrAuto,
        Result = Reponse.Resultat(s.Sort), For = s.NbPour, Against = s.NbContre, Abstentions = s.NbAbstentions,
        FinalVoteComputed = s.SurEnsemble, IncompleteRollCall = s.NominatifIncomplet,
        Url = p.Url(p.Chambres[s.Chambre], "/scrutin/" + s.Id),
    };

    public async Task<PageDocuments> RechercherAsync(Recherche r, Perimetre p)
    {
        var chambres = p.Filtrer(r.Pays, r.Chambre);
        var mots = RechercheSql.Mots(r.Texte);
        var du = Reponse.DateSaisie(r.Du);
        var au = Reponse.DateSaisie(r.Au);
        // Bornes de période ajoutées SEULEMENT si elles existent : « @du IS NULL OR … » empêcherait l'index (chambre, date) de borner la lecture.
        var filtre = "WHERE chambre = ANY(@chambres)"
            + (mots.Length > 0 ? " AND (recherche_norm(titre) ILIKE ALL (@mots) OR recherche_norm(titre_en) ILIKE ALL (@mots))" : "")
            + (du is not null ? " AND date_scrutin >= @du::date" : "")
            + (au is not null ? " AND date_scrutin <= @au::date" : "")
            + (r.SurEnsemble ? " AND sur_ensemble" : "")
            + (r.Theme is not null ? " AND @theme = ANY(themes)" : "");
        var param = new { chambres, mots, du, au, theme = r.Theme, limite = r.Limite, decalage = r.Decalage };
        using var cx = db.CreateConnection();
        var total = await cx.ExecuteScalarAsync<int>($"SELECT count(*) FROM scrutins {filtre}", param);
        var lignes = await cx.QueryAsync<Scrutin>(
            $"SELECT {Colonnes} FROM scrutins {filtre} ORDER BY date_scrutin DESC, numero DESC, id LIMIT @limite OFFSET @decalage", param);
        return new(total, [.. lignes.Select(s => Resume(s, p))]);
    }

    /// <summary>Scrutin publié (chambre visible) ou null.</summary>
    public async Task<Scrutin?> LireAsync(string id, Perimetre p)
    {
        using var cx = db.CreateConnection();
        return await cx.QuerySingleOrDefaultAsync<Scrutin>(
            $"SELECT {Colonnes} FROM scrutins WHERE id = @id AND chambre = ANY(@chambres)", new { id, chambres = p.Filtrer(null, null) });
    }

    public async Task<object?> ConsulterAsync(string id, Perimetre p)
    {
        if (await LireAsync(id, p) is not { } s) return null;
        return new
        {
            Document = Resume(s, p), s.Legislature, Number = s.Numero, VoteType = s.TypeVoteLibelle ?? s.TypeVote, MajorityType = s.TypeMajorite,
            VotesCast = s.SuffragesExprimes, VotesRequired = s.SuffragesRequis, Voters = s.NbVotants, NonVoting = s.NbNonVotants, ConfidenceVote = s.Confiance,
            MissingIndividualVotes = s.NbVotesManquants, s.Note, OfficialUrl = s.UrlOfficielle,
            ApproximateThemes = s.Themes,
        };
    }
}

/// <summary>Élus des chambres publiées.</summary>
public sealed class Elus(DapperContext db, IParlementaireRepository repo) : ITypeDocument
{
    public string Code => "member";
    public string Nom => "Member of parliament";
    public string Description => "Deputies, senators and other members of parliamentary chambers. Use the member_history tool for their votes.";
    public string[] Filtres => ["text", "country", "chamber"];
    public IReadOnlyList<string> Pays(Perimetre p) => [.. p.Chambres.Values.Distinct()];

    private static object Resume(Parlementaire e, Perimetre p) => new
    {
        e.Id, Chamber = e.Chambre, Country = p.Chambres[e.Chambre], FirstName = e.Prenom, LastName = e.Nom, Area = e.Departement, Constituency = e.Circonscription,
        Url = p.Url(p.Chambres[e.Chambre], e.Lien),
    };

    public async Task<PageDocuments> RechercherAsync(Recherche r, Perimetre p)
    {
        var chambres = p.Filtrer(r.Pays, r.Chambre);
        var mots = RechercheSql.Mots(r.Texte);
        var filtre = "WHERE chambre = ANY(@chambres)" + (mots.Length > 0 ? " AND recherche_norm(prenom || ' ' || nom) ILIKE ALL (@mots)" : "");
        var param = new { chambres, mots, limite = r.Limite, decalage = r.Decalage };
        using var cx = db.CreateConnection();
        var total = await cx.ExecuteScalarAsync<int>($"SELECT count(*) FROM parlementaires {filtre}", param);
        var lignes = await cx.QueryAsync<Parlementaire>(
            $"SELECT id, chambre, prenom, nom, slug, departement, circonscription FROM parlementaires {filtre} ORDER BY nom, prenom, id LIMIT @limite OFFSET @decalage", param);
        return new(total, [.. lignes.Select(e => Resume(e, p))]);
    }

    /// <summary>Élu publié (chambre visible), avec son groupe courant, ou null.</summary>
    public async Task<Parlementaire?> LireAsync(string id, Perimetre p)
    {
        using var cx = db.CreateConnection();
        var cle = await cx.QuerySingleOrDefaultAsync<(string Chambre, string Slug)?>(
            "SELECT chambre, slug FROM parlementaires WHERE id = @id AND chambre = ANY(@chambres)", new { id, chambres = p.Filtrer(null, null) });
        return cle is { } c ? await repo.GetParSlugAsync(c.Chambre, c.Slug) : null;
    }

    public async Task<object?> ConsulterAsync(string id, Perimetre p)
    {
        if (await LireAsync(id, p) is not { } e) return null;
        var bilan = await repo.GetBilanAsync(e.Id, e.Chambre);
        var mandats = await repo.GetMandatsAsync(e.Id);
        return new
        {
            Document = Resume(e, p), InOffice = e.EnExercice, CurrentGroup = e.GroupeLibelle, CurrentGroupShort = e.GroupeSigle, DirectoryStatus = e.Statut,
            Terms = mandats.Select(m => new { Start = Reponse.Jour(m.DateDebut), End = Reponse.Jour(m.DateFin) }),
            ComputedRecord = new
            {
                Votes = bilan.Total, For = bilan.Pour, Against = bilan.Contre, bilan.Abstention, NonVoting = bilan.NonVotant, bilan.Absent,
                IncludingDelegated = bilan.Delegation, IncludingCorrected = bilan.MiseAuPoint,
                Note = "Computed over the recorded votes held during their terms, incomplete roll calls excluded. 'Absent' is inferred; no reason is known.",
            },
        };
    }
}
