using System.Globalization;
using Dapper;
using IlsOntVote.Web.Data;
using IlsOntVote.Web.Repositories;
using IlsOntVote.Web.Services;

namespace IlsOntVote.Web.Mcp;

/// <summary>Votes de la population : votations fédérales suisses (jeu Swissvotes). Résultats agrégés, aucun vote individuel.</summary>
public sealed class VotesPopulaires(IVotationRepository repo) : ITypeDocument
{
    public string Code => "popular_vote";
    public string Nom => "Popular vote";
    public string Description =>
        "Swiss federal popular votes since 2000 (Swissvotes, Année Politique Suisse, University of Bern, CC BY 4.0). " +
        "Federal ballots only (no cantonal or municipal ballots), with results by canton. No individual votes.";
    public string[] Filtres => ["text"];
    public IReadOnlyList<string> Pays(Perimetre p) => p.Publie("CH") ? ["CH"] : [];

    private static object Resume(Votation v, Perimetre p) => new
    {
        Id = v.Anr.ToString(CultureInfo.InvariantCulture), Country = "CH", Date = Reponse.Jour(v.DateVotation), TitleFr = v.TitreFr, TitleDe = v.TitreDe, TitleEn = v.TitreEn,
        Result = v.Accepte is true ? "accepted" : v.Accepte is false ? "rejected" : "upcoming", YesPct = v.OuiPct, TurnoutPct = v.Participation,
        Url = p.Url("CH", "/votation/" + v.Anr.ToString(CultureInfo.InvariantCulture)),
    };

    public async Task<PageDocuments> RechercherAsync(Recherche r, Perimetre p)
    {
        var page = await repo.ListerAsync(r.Texte, null, null, "date", true, r.Page, r.Limite);
        return new(page.Total, [.. page.Lignes.Select(v => Resume(v, p))]);
    }

    public async Task<object?> ConsulterAsync(string id, Perimetre p)
    {
        if (!int.TryParse(id, out var anr) || await repo.GetAsync(anr) is not { } v) return null;
        return new
        {
            Document = Resume(v, p),
            InferredForm = v.Rechtsform switch { 1 => "mandatory referendum", 2 => "optional referendum", 3 => "popular initiative", 4 => "counter-proposal", 5 => "tie-break question", _ => null },
            Accepted = v.Accepte, PeopleAccepted = v.PeupleAccepte, CantonsAccepted = v.CantonsAccepte, Yes = v.Oui, No = v.Non,
            CantonsYes = v.CantonsOui, CantonsNo = v.CantonsNon, YesPctByCanton = v.Cantons().ToDictionary(c => c.Canton, c => c.Pct), OfficialUrl = v.Url,
            Note = "The form of the vote is inferred from titles. A null value means not published.",
        };
    }
}

/// <summary>Lois françaises (dossiers législatifs) et leur parcours.</summary>
public sealed class Lois(ITexteRepository repo) : ITypeDocument
{
    public string Code => "law";
    public string Nom => "Law (France)";
    public string Description => "French legislative files: computed status, stages (including those without a recorded vote), promulgation, Official Journal, Constitutional Council.";
    public string[] Filtres => ["text", "from", "to", "theme"];
    public IReadOnlyList<string> Pays(Perimetre p) => ["FR"];

    private static object Resume(Models.Texte t, Perimetre p) => new
    {
        t.Id, Country = "FR", Title = t.TitreAffiche, TitleEn = t.TitreEn, TitleMachineTranslated = t.TitreEnAuto, Type = t.TypeLibelle,
        ComputedStatus = StatutAnglais(t.Statut.Famille), LastVote = Reponse.Jour(t.DernierVote),
        LawNumber = t.NumeroLoi, Promulgated = Reponse.Jour(t.DatePromulgation), OfficialJournal = Reponse.Jour(t.DateJo),
        Url = p.Url("FR", "/texte/" + t.Id),
    };

    /// <summary>Statut calculé du texte, en anglais (d'après sa famille : même découpage que les étiquettes du site).</summary>
    private static string StatutAnglais(Models.FamilleStatut f) => f switch
    {
        Models.FamilleStatut.Promulgue => "promulgated",
        Models.FamilleStatut.PartiellementCensure => "promulgated after partial censure by the Constitutional Council",
        Models.FamilleStatut.Censure => "struck down by the Constitutional Council",
        Models.FamilleStatut.DeuxAssemblees => "passed by both chambers, not promulgated",
        Models.FamilleStatut.EnDiscussion => "under discussion",
        Models.FamilleStatut.Adopte => "adopted",
        Models.FamilleStatut.Rejete => "rejected",
        _ => "lapsed, withdrawn or merged",
    };

    private static DateTime? Date(string? saisie) =>
        Reponse.DateSaisie(saisie) is { } d ? DateTime.ParseExact(d, "yyyy-MM-dd", CultureInfo.InvariantCulture) : null;

    public async Task<PageDocuments> RechercherAsync(Recherche r, Perimetre p)
    {
        var page = await repo.ListerAsync(new FiltreTextes(r.Texte, null, null, Date(r.Du), Date(r.Au), r.Theme), "date", true, r.Page, r.Limite);
        return new(page.Total, [.. page.Lignes.Select(t => Resume(t, p))]);
    }

    public async Task<object?> ConsulterAsync(string id, Perimetre p)
    {
        if (await repo.GetAsync(id) is not { } t) return null;
        var etapes = await repo.GetEtapesAsync(id);
        var scrutins = await repo.GetScrutinsAsync(id);
        return new
        {
            Document = Resume(t, p), LegifranceUrl = t.UrlLegifrance, NationalAssemblyFileUrl = t.UrlDossierAn, SenateFileUrl = t.UrlDossierSenat,
            ConstitutionalCouncil = new { Referred = Reponse.Jour(t.SaisineCcDate), ReferredBy = t.SaisineCcPar, DecisionNumber = t.DecisionCcNumero, Decided = Reponse.Jour(t.DecisionCcDate), Decision = t.DecisionCcLibelle, DecisionUrl = t.DecisionCcUrl },
            Stages = etapes.Select(e => new { Chamber = e.Chambre, Date = Reponse.Jour(e.DateActe), ActCode = e.CodeActe, Result = Reponse.Resultat(e.Resultat), VoteId = e.ScrutinId, NoRecordedVote = e.SansScrutinPublic, AdoptedWithoutVoteArticle493 = e.Article493 }),
            FinalVotes = scrutins.Select(s => new { s.Id, Chamber = s.Chambre, Date = Reponse.Jour(s.DateScrutin), Result = Reponse.Resultat(s.Sort), For = s.NbPour, Against = s.NbContre, Abstentions = s.NbAbstentions }),
            Note = "A stage with no recorded vote has no individual breakdown. A null value means the data is missing from our sources.",
        };
    }
}

/// <summary>Projets de loi britanniques.</summary>
public sealed class ProjetsDeLoi(IBillRepository repo) : ITypeDocument
{
    public string Code => "bill";
    public string Nom => "Bill (United Kingdom)";
    public string Description => "UK Parliament bills and the divisions linked to them (link computed from the title).";
    public string[] Filtres => ["text", "theme"];
    public IReadOnlyList<string> Pays(Perimetre p) => p.Publie("GB") ? ["GB"] : [];

    private static object Resume(Bill b, Perimetre p) => new
    {
        Id = b.Id.ToString(CultureInfo.InvariantCulture), Country = "GB", Title = b.Titre,
        Status = b.EstLoi ? "act" : b.Rejete ? "rejected" : b.RetireLe is not null ? "withdrawn" : "ongoing", CurrentStage = b.EtapeCourante,
        Introduced = Reponse.Jour(b.DateDepot), RoyalAssent = Reponse.Jour(b.DateLoi),
        Url = p.Url("GB", "/bill/" + b.Id.ToString(CultureInfo.InvariantCulture)), OfficialUrl = b.Url,
    };

    public async Task<PageDocuments> RechercherAsync(Recherche r, Perimetre p)
    {
        var page = await repo.ListerAsync(r.Texte, null, null, "depot", true, r.Page, r.Limite, r.Theme);
        return new(page.Total, [.. page.Lignes.Select(b => Resume(b, p))]);
    }

    public async Task<object?> ConsulterAsync(string id, Perimetre p)
    {
        if (!long.TryParse(id, out var n) || await repo.GetAsync(n) is not { } b) return null;
        var etapes = await repo.GetEtapesAsync(n);
        var divisions = await repo.GetDivisionsAsync(n);
        return new
        {
            Document = Resume(b, p), LongTitle = b.TitreLong, OriginatingHouse = b.ChambreOrigine, CurrentHouse = b.ChambreCourante,
            Stages = etapes.Select(e => new { Name = e.Libelle, Chamber = e.Chambre, Date = Reponse.Jour(e.DateSeance) }),
            DivisionsLinkedByComputation = divisions.Select(d => new { d.Id, Date = Reponse.Jour(d.DateScrutin), Title = d.Titre, Stage = d.BillEtape, Result = Reponse.Resultat(d.Sort), For = d.NbPour, Against = d.NbContre }),
        };
    }
}

/// <summary>Pétitions des plateformes parlementaires (France ; Royaume-Uni s'il est publié).</summary>
public sealed class Petitions(DapperContext db, IPetitionRepository repo) : ITypeDocument
{
    public string Code => "petition";
    public string Nom => "Petition";
    public string Description => "Petitions to the French National Assembly and Senate (and to the UK Parliament). Signature counts are those of the latest snapshot; a petition is not a vote. " +
        "Only the title, status, signature counts, dates and official link are available: not the text of the petition.";
    public string[] Filtres => ["text", "country", "from", "to", "theme"];
    public IReadOnlyList<string> Pays(Perimetre p) => p.Publie("GB") ? ["FR", "GB"] : ["FR"];

    private const string Colonnes =
        "id, titre, date_publication, nb_votes, statut, date_limite_vote, type, url, chambre, commission, decision_commission, reponse_le, debat_le, absente_depuis, themes";

    /// <summary>Plateformes des pays publiés (même garde-fou que le site), restreintes au pays demandé.</summary>
    private string[] Plateformes(Perimetre p, string? pays)
    {
        if (!string.IsNullOrWhiteSpace(pays)) p.Filtrer(pays, null);   // pays non publié : erreur explicite
        return [.. Pays(p).Where(c => string.IsNullOrWhiteSpace(pays) || c.Equals(pays.Trim(), StringComparison.OrdinalIgnoreCase))
            .SelectMany(c => PaysSite.PlateformesPetitions(c == "FR" ? null : c))];
    }

    private static object Resume(Petition x, Perimetre p)
    {
        var pays = x.Chambre == "UK" ? "GB" : "FR";
        return new
        {
            x.Id, Country = pays, Platform = x.Chambre, Title = x.Titre, Published = Reponse.Jour(x.DatePublication), Signatures = x.NbVotes, SourceStatus = x.Statut,
            MissingFromSourceSince = Reponse.Jour(x.AbsenteDepuis), Url = p.Url(pays, "/petition/" + x.Id), OfficialUrl = x.Url,
        };
    }

    public async Task<PageDocuments> RechercherAsync(Recherche r, Perimetre p)
    {
        var mots = RechercheSql.Mots(r.Texte);
        var du = Reponse.DateSaisie(r.Du);
        var au = Reponse.DateSaisie(r.Au);
        var filtre = "WHERE chambre = ANY(@plateformes)"
            + (mots.Length > 0 ? " AND recherche_norm(titre) ILIKE ALL (@mots)" : "")
            + (du is not null ? " AND date_publication >= @du::date" : "")
            + (au is not null ? " AND date_publication <= @au::date" : "")
            + (r.Theme is not null ? " AND @theme = ANY(themes)" : "");
        var param = new { plateformes = Plateformes(p, r.Pays), mots, du, au, theme = r.Theme, limite = r.Limite, decalage = r.Decalage };
        using var cx = db.CreateConnection();
        var total = await cx.ExecuteScalarAsync<int>($"SELECT count(*) FROM petitions {filtre}", param);
        var lignes = await cx.QueryAsync<Petition>(
            $"SELECT {Colonnes} FROM petitions {filtre} ORDER BY date_publication DESC NULLS LAST, id LIMIT @limite OFFSET @decalage", param);
        return new(total, [.. lignes.Select(x => Resume(x, p))]);
    }

    public async Task<object?> ConsulterAsync(string id, Perimetre p)
    {
        using var cx = db.CreateConnection();
        var x = await cx.QuerySingleOrDefaultAsync<Petition>(
            $"SELECT {Colonnes} FROM petitions WHERE id = @id AND chambre = ANY(@plateformes)", new { id, plateformes = Plateformes(p, null) });
        if (x is null) return null;
        var historique = await repo.GetHistoriqueAsync(id);
        return new
        {
            Document = Resume(x, p), x.Type, Committee = x.Commission, CommitteeDecision = x.DecisionCommission, ClosingDate = Reponse.Jour(x.DateLimiteVote),
            GovernmentResponse = Reponse.Jour(x.ReponseLe), Debate = Reponse.Jour(x.DebatLe),
            Snapshots = historique.Select(h => new { Date = Reponse.Jour(h.ReleveLe), Signatures = h.NbVotes, SourceStatus = h.Statut }),
        };
    }
}
