using System.Text.Json;
using Dapper;
using IlsOntVote.Web.Data;
using IlsOntVote.Web.Models;

namespace IlsOntVote.Web.Repositories;

/// <summary>
/// Une votation populaire fédérale suisse (table votations, migration 071) : jeu Swissvotes, résultats AGRÉGÉS depuis 2000, CC BY 4.0. Aucun vote individuel.
/// <c>Rechtsform</c> : 1 référendum obligatoire, 2 facultatif, 3 initiative populaire, 4 contre-projet, 5 question subsidiaire (sens DÉDUIT des titres : le codebook n'a pas pu être lu).
/// </summary>
public sealed class Votation
{
    public int Anr { get; set; }
    public DateTime DateVotation { get; set; }
    public string TitreFr { get; set; } = "";
    public string TitreDe { get; set; } = "";
    public string? TitreEn { get; set; }
    public short Rechtsform { get; set; }
    public bool? Accepte { get; set; }
    public bool? PeupleAccepte { get; set; }
    public bool? CantonsAccepte { get; set; }
    public decimal? Participation { get; set; }
    public decimal? OuiPct { get; set; }
    public int? Oui { get; set; }
    public int? Non { get; set; }
    public decimal? CantonsOui { get; set; }
    public decimal? CantonsNon { get; set; }
    public string? OuiParCanton { get; set; }
    public string? Url { get; set; }

    /// <summary>Titre dans la langue de la page : allemand en allemand, anglais si la source en a un (langues autres que fr et de), sinon français.</summary>
    public string Titre(string langue) => langue == "de" ? TitreDe : langue != "fr" && !string.IsNullOrWhiteSpace(TitreEn) ? TitreEn! : TitreFr;

    /// <summary>Pourcentage de oui par canton (code du canton en majuscules), dans l'ordre de la source ; vide si le détail n'est pas publié.</summary>
    public IReadOnlyList<(string Canton, decimal Pct)> Cantons() =>
        string.IsNullOrEmpty(OuiParCanton) ? [] : JsonSerializer.Deserialize<Dictionary<string, decimal>>(OuiParCanton)!.Select(kv => (kv.Key.ToUpperInvariant(), kv.Value)).ToList();

    /// <summary>acceptee | rejetee | avenir : « avenir » = pas encore de résultat ; une valeur non interprétable reste « avenir » (jamais devinée).</summary>
    public string ResultatCle => Accepte is true ? "acceptee" : Accepte is false ? "rejetee" : "avenir";
}

public interface IVotationRepository
{
    Task<Resultats<Votation>> ListerAsync(string? recherche, string? resultat, short? forme, string tri, bool decroissant, int page, int parPage);
    Task<Votation?> GetAsync(int anr);
}

/// <summary>Votations suisses. Tri par liste blanche (jamais du SQL construit depuis l'adresse) ; sans la table (migration 071), la liste est vide.</summary>
public sealed class VotationRepository(DapperContext db) : IVotationRepository
{
    private const string Colonnes = "anr, date_votation, titre_fr, titre_de, titre_en, rechtsform, accepte, peuple_accepte, cantons_accepte, participation, oui_pct, oui, non, cantons_oui, cantons_non, oui_par_canton::text AS oui_par_canton, url";

    public static readonly IReadOnlyDictionary<string, string> Tris = new Dictionary<string, string>
    {
        ["date"] = "v.date_votation",
        ["titre"] = "recherche_norm(v.titre_fr)",
        ["forme"] = "v.rechtsform",
        ["oui"] = "v.oui_pct",
        ["participation"] = "v.participation",
    };

    public async Task<Resultats<Votation>> ListerAsync(string? recherche, string? resultat, short? forme, string tri, bool decroissant, int page, int parPage)
    {
        using var cx = db.CreateConnection();
        if (!await cx.ExecuteScalarAsync<bool>("SELECT to_regclass('public.votations') IS NOT NULL")) return new Resultats<Votation>([], 0, 1, parPage);
        var mots = RechercheSql.Mots(recherche);
        const string filtre =
            """
             WHERE (cardinality(@mots) = 0 OR recherche_norm(v.titre_fr || ' ' || v.titre_de) ILIKE ALL (@mots))
               AND (@resultat::text IS NULL OR (@resultat = 'acceptee' AND v.accepte IS TRUE) OR (@resultat = 'rejetee' AND v.accepte IS FALSE) OR (@resultat = 'avenir' AND v.accepte IS NULL))
               AND (@forme::smallint IS NULL OR v.rechtsform = @forme)
            """;
        var p = new { mots, resultat = string.IsNullOrEmpty(resultat) ? null : resultat, forme, limit = parPage, offset = (Math.Max(1, page) - 1) * parPage };
        var total = await cx.ExecuteScalarAsync<int>($"SELECT count(*) FROM votations v {filtre}", p);
        var ordre = Tris.GetValueOrDefault(tri, Tris["date"]);
        var lignes = (await cx.QueryAsync<Votation>(
            $"""
             SELECT {Colonnes} FROM votations v {filtre}
              ORDER BY {ordre} {(decroissant ? "DESC" : "ASC")} NULLS LAST, v.anr DESC
              {(parPage > 0 ? "LIMIT @limit OFFSET @offset" : "")}
             """, p)).ToList();
        return new Resultats<Votation>(lignes, total, page, parPage == 0 ? Math.Max(1, total) : parPage);
    }

    public async Task<Votation?> GetAsync(int anr)
    {
        using var cx = db.CreateConnection();
        if (!await cx.ExecuteScalarAsync<bool>("SELECT to_regclass('public.votations') IS NOT NULL")) return null;
        return await cx.QuerySingleOrDefaultAsync<Votation>($"SELECT {Colonnes} FROM votations v WHERE anr = @anr", new { anr });
    }
}
