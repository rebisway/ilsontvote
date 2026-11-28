using Dapper;
using IlsOntVote.Web.Data;
using IlsOntVote.Web.Models;
using IlsOntVote.Web.Repositories;
using IlsOntVote.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace IlsOntVote.Web.Pages.Admin;

/// <summary>Journal du site (table <c>journal_site</c>) : filtres période, niveau et texte, tri par liste blanche, export.</summary>
public class JournalModel(DapperContext db) : ListePageModel
{
    /// <summary>« heure », « semaine », « tout » ; vide = 24 dernières heures.</summary>
    [BindProperty(SupportsGet = true)] public string? Periode { get; set; }
    /// <summary>« info » (appels MCP), « avertissement » ou « erreur » (erreurs et critiques) ; vide = tous.</summary>
    [BindProperty(SupportsGet = true)] public string? Niveau { get; set; }
    /// <summary>Mots cherchés dans le message ou la source.</summary>
    [BindProperty(SupportsGet = true, Name = "q")] public string? Recherche { get; set; }

    public Resultats<LigneJournal> Resultats { get; private set; } = new([], 0, 1, 100);
    public override int TotalLignes => Resultats.Total;

    private static readonly IReadOnlyDictionary<string, string> Tris = new Dictionary<string, string> { ["date"] = "le", ["niveau"] = "niveau", ["source"] = "source" };

    protected override int ParPageDefaut => 100;
    protected override string TriDefaut => "date";
    protected override IReadOnlyCollection<string> ClesTri { get; } = [.. Tris.Keys];
    protected override bool DecroissantInitial(string cle) => cle is "date" or "niveau";
    protected override IEnumerable<(string Nom, string? Valeur)> Filtres => [("periode", Periode), ("niveau", Niveau), ("q", Recherche)];

    /// <summary>Libellé d'un niveau .NET.</summary>
    public static string Libelle(short niveau) => niveau >= (short)LogLevel.Error ? "erreur" : niveau == (short)LogLevel.Warning ? "avertissement" : "information";

    private static IReadOnlyList<Colonne<LigneJournal>> ColonnesExport() =>
    [
        // Texte et non DateTime : l'export de dates ne garde que le jour, or l'heure compte ici.
        new("Date (UTC)", l => l.Le.ToString("yyyy-MM-dd HH:mm:ss")),
        new("Niveau", l => Libelle(l.Niveau)),
        new("Source", l => l.Source),
        new("Message", l => l.Message),
    ];

    public async Task<IActionResult> OnGetAsync()
    {
        if (Periode is not ("heure" or "semaine" or "tout")) Periode = null;
        if (Niveau is not ("info" or "avertissement" or "erreur")) Niveau = null;
        int? heures = Periode switch { "heure" => 1, "semaine" => 24 * 7, "tout" => null, _ => 24 };
        var (min, max) = Niveau switch { "info" => (0, 2), "avertissement" => (3, 3), "erreur" => (4, 6), _ => (0, 6) };
        var mots = RechercheSql.Mots(Recherche);
        using var cx = db.CreateConnection();
        // Borne de date ajoutée seulement si elle existe (index sur `le`) ; recherche sans accents, comme les autres listes.
        var filtre = "WHERE niveau BETWEEN @min AND @max"
            + (heures is null ? "" : " AND le >= (now() AT TIME ZONE 'utc') - make_interval(hours => @heures)")
            + (mots.Length == 0 ? "" : " AND recherche_norm(source || ' ' || message) ILIKE ALL (@mots)");
        var p = new { min, max, heures, mots, limit = ParPage, offset = (Math.Max(1, NumeroPage) - 1) * ParPage };
        var total = await cx.ExecuteScalarAsync<int>($"SELECT count(*) FROM journal_site {filtre}", p);
        var export = ExportDemande;
        var lignes = (await cx.QueryAsync<LigneJournal>(
            $"""
             SELECT id, le, niveau, source, message FROM journal_site {filtre}
              ORDER BY {Tris[TriEffectif]} {(Decroissant ? "DESC" : "ASC")}, id DESC
              {(export ? "" : "LIMIT @limit OFFSET @offset")}
             """, p)).ToList();

        if (export) return Exporteur.Ecrire(Format!, "journal-site", ColonnesExport(), lignes);
        Resultats = new Resultats<LigneJournal>(lignes, total, Math.Max(1, NumeroPage), ParPage);
        return Page();
    }
}
