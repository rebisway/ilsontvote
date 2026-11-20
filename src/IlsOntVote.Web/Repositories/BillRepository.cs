using Dapper;
using IlsOntVote.Web.Data;
using IlsOntVote.Web.Models;

namespace IlsOntVote.Web.Repositories;

/// <summary>Un projet de loi du Parlement britannique (table bills), avec le nombre de divisions des Communes qui lui sont rattachées (calculé par le site d'après les titres).</summary>
public sealed class Bill
{
    /// <summary>Thèmes APPROXIMATIFS (mots-clés du titre, migration 057) : codes de la table <c>themes</c> ; vide = aucun thème repéré.</summary>
    public string[] Themes { get; set; } = [];
    public long Id { get; set; }
    public string Titre { get; set; } = "";
    public string? TitreLong { get; set; }
    public string? ChambreOrigine { get; set; }
    public string? ChambreCourante { get; set; }
    public bool EstLoi { get; set; }
    public DateTime? DateLoi { get; set; }
    public DateTime? RetireLe { get; set; }
    public bool Rejete { get; set; }
    public string? EtapeCourante { get; set; }
    public DateTime? DateDepot { get; set; }
    public DateTime? DerniereMaj { get; set; }
    public string Url { get; set; } = "";
    public int NbDivisions { get; set; }

    /// <summary>Statut selon la source : loi (sanction royale), retiré, rejeté, sinon en cours. Jamais un jugement : ce sont les indicateurs publiés (isAct, billWithdrawn, isDefeated).</summary>
    public string StatutCle => EstLoi ? "loi" : Rejete ? "rejete" : RetireLe is not null ? "retire" : "en-cours";
}

/// <summary>Une séance d'une étape d'un projet (NULL = étape sans séance connue).</summary>
public sealed class EtapeBillVue
{
    public int Ordre { get; set; }
    public string Libelle { get; set; } = "";
    public string? Chambre { get; set; }
    public DateTime? DateSeance { get; set; }
}

/// <summary>Une division des Communes rattachée à un projet.</summary>
public sealed class DivisionBill
{
    public string Id { get; set; } = "";
    public DateTime DateScrutin { get; set; }
    public string Titre { get; set; } = "";
    public string? BillEtape { get; set; }
    public string? Sort { get; set; }
    public int? NbPour { get; set; }
    public int? NbContre { get; set; }
}

/// <summary>Projet de loi auquel une division se rattache (page d'une division).</summary>
public sealed record BillResume(long Id, string Titre, string? Etape);

public interface IBillRepository
{
    Task<Resultats<Bill>> ListerAsync(string? recherche, string? statut, string? chambre, string tri, bool decroissant, int page, int parPage, string? theme = null);
    Task<Bill?> GetAsync(long id);
    Task<IReadOnlyList<EtapeBillVue>> GetEtapesAsync(long id);
    Task<IReadOnlyList<DivisionBill>> GetDivisionsAsync(long id);
    Task<BillResume?> GetResumeParDivisionAsync(string scrutinId);
    Task<IReadOnlyList<(string Cle, int Nombre)>> CompterParStatutAsync();
}

/// <summary>Projets de loi britanniques. Le tri vient d'une liste blanche (jamais du SQL construit depuis l'adresse) ; sans la table (migration 036), les pages affichent une liste vide.</summary>
public sealed class BillRepository(DapperContext db) : IBillRepository
{
    private const string Colonnes =
        """
        b.id, b.titre, b.titre_long, b.chambre_origine, b.chambre_courante, b.est_loi, b.date_loi, b.retire_le, b.rejete, b.etape_courante, b.date_depot, b.derniere_maj, b.url, b.themes,
        (SELECT count(*) FROM scrutins s WHERE s.bill_id = b.id)::int AS nb_divisions
        """;

    public static readonly IReadOnlyDictionary<string, string> Tris = new Dictionary<string, string>
    {
        ["depot"] = "b.date_depot",
        ["titre"] = "recherche_norm(b.titre)",
        ["statut"] = "(CASE WHEN b.est_loi THEN 1 WHEN b.rejete THEN 4 WHEN b.retire_le IS NOT NULL THEN 3 ELSE 2 END)",
        ["divisions"] = "(SELECT count(*) FROM scrutins s WHERE s.bill_id = b.id)",
        ["maj"] = "b.derniere_maj",
    };

    private static async Task<bool> TableExisteAsync(System.Data.IDbConnection cx) =>
        await cx.ExecuteScalarAsync<bool>("SELECT to_regclass('public.bills') IS NOT NULL");

    public async Task<Resultats<Bill>> ListerAsync(string? recherche, string? statut, string? chambre, string tri, bool decroissant, int page, int parPage, string? theme = null)
    {
        using var cx = db.CreateConnection();
        if (!await TableExisteAsync(cx)) return new Resultats<Bill>([], 0, 1, parPage);
        var mots = RechercheSql.Mots(recherche);
        const string filtre =
            """
             WHERE (cardinality(@mots) = 0 OR recherche_norm(b.titre) ILIKE ALL (@mots))
               AND (@statut::text IS NULL
                    OR (@statut = 'loi' AND b.est_loi)
                    OR (@statut = 'en-cours' AND NOT b.est_loi AND NOT b.rejete AND b.retire_le IS NULL)
                    OR (@statut = 'retire' AND NOT b.est_loi AND (b.rejete OR b.retire_le IS NOT NULL)))
               AND (@chambre::text IS NULL OR b.chambre_courante = @chambre)
               -- Thème (approximatif, mots-clés du titre : migration 057) ; le code vient de Themes.Valider.
               AND (@theme::text IS NULL OR @theme = ANY(b.themes))
            """;
        var p = new { mots, statut = Vide(statut), chambre = Vide(chambre), theme, limit = parPage, offset = (Math.Max(1, page) - 1) * parPage };
        var total = await cx.ExecuteScalarAsync<int>($"SELECT count(*) FROM bills b {filtre}", p);
        var ordre = Tris.GetValueOrDefault(tri, Tris["depot"]);
        var lignes = (await cx.QueryAsync<Bill>(
            $"""
             SELECT {Colonnes} FROM bills b {filtre}
              ORDER BY {ordre} {(decroissant ? "DESC" : "ASC")} NULLS LAST, b.id DESC
              {(parPage > 0 ? "LIMIT @limit OFFSET @offset" : "")}
             """, p)).ToList();
        return new Resultats<Bill>(lignes, total, page, parPage == 0 ? Math.Max(1, total) : parPage);
    }

    public async Task<Bill?> GetAsync(long id)
    {
        using var cx = db.CreateConnection();
        if (!await TableExisteAsync(cx)) return null;
        return await cx.QuerySingleOrDefaultAsync<Bill>($"SELECT {Colonnes} FROM bills b WHERE b.id = @id", new { id });
    }

    public async Task<IReadOnlyList<EtapeBillVue>> GetEtapesAsync(long id)
    {
        using var cx = db.CreateConnection();
        return (await cx.QueryAsync<EtapeBillVue>(
            "SELECT ordre, libelle, chambre, date_seance FROM bill_etapes WHERE bill_id = @id ORDER BY ordre, date_seance NULLS LAST", new { id })).ToList();
    }

    public async Task<IReadOnlyList<DivisionBill>> GetDivisionsAsync(long id)
    {
        using var cx = db.CreateConnection();
        return (await cx.QueryAsync<DivisionBill>(
            "SELECT id, date_scrutin, titre, bill_etape, sort, nb_pour, nb_contre FROM scrutins WHERE bill_id = @id ORDER BY date_scrutin, numero", new { id })).ToList();
    }

    public async Task<BillResume?> GetResumeParDivisionAsync(string scrutinId)
    {
        using var cx = db.CreateConnection();
        if (!await TableExisteAsync(cx)) return null;
        var r = await cx.QuerySingleOrDefaultAsync<(long Id, string Titre, string? Etape)?>(
            "SELECT b.id, b.titre, s.bill_etape FROM scrutins s JOIN bills b ON b.id = s.bill_id WHERE s.id = @scrutinId", new { scrutinId });
        return r is { } x ? new BillResume(x.Id, x.Titre, x.Etape) : null;
    }

    public async Task<IReadOnlyList<(string Cle, int Nombre)>> CompterParStatutAsync()
    {
        using var cx = db.CreateConnection();
        if (!await TableExisteAsync(cx)) return [];
        var l = await cx.QueryAsync<(string Cle, int Nombre)>(
            """
            SELECT CASE WHEN est_loi THEN 'loi' WHEN rejete OR retire_le IS NOT NULL THEN 'retire' ELSE 'en-cours' END, count(*)::int
              FROM bills GROUP BY 1
            """);
        return l.ToList();
    }

    private static string? Vide(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
