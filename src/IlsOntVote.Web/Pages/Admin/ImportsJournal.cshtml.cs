using Dapper;
using IlsOntVote.Web.Data;
using IlsOntVote.Web.Models;
using IlsOntVote.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace IlsOntVote.Web.Pages.Admin;

/// <summary>
/// Journal complet des exécutions d'imports : filtres (import, état, origine), tri par colonne (liste blanche) et export, comme toute liste du site.
/// Le détail et la sortie console d'une exécution sont dans /Admin/ImportExecution.
/// </summary>
public class ImportsJournalModel(DapperContext db) : ListePageModel
{
    [BindProperty(SupportsGet = true)] public string? Tache { get; set; }
    /// <summary>ok, echec, en cours ou interrompue (même vocabulaire que <see cref="ImportAffichage.Etat"/>).</summary>
    [BindProperty(SupportsGet = true)] public string? Etat { get; set; }
    /// <summary>planning ou admin.</summary>
    [BindProperty(SupportsGet = true)] public string? Origine { get; set; }
    /// <summary>Pays de la tâche : FR, GB, EU, ou « commun » (traductions, statistiques, export) ; vide = tous.</summary>
    [BindProperty(SupportsGet = true)] public string? Pays { get; set; }
    public IReadOnlyList<(string Code, string Nom)> PaysChoix { get; private set; } = [];

    public static readonly string[] Etats = ["ok", "echec", "en cours", "interrompue"];
    public IReadOnlyList<(string Code, string Libelle)> Taches { get; private set; } = [];
    public Resultats<ExecutionTache> Resultats { get; private set; } = new([], 0, 1, 30);

    /// <summary>Clés de tri autorisées -> expression SQL : jamais du SQL construit depuis l'adresse.</summary>
    private static readonly IReadOnlyDictionary<string, string> Tris = new Dictionary<string, string>
    {
        ["debut"] = "e.debut",
        ["tache"] = "libelle",
        ["duree"] = "(e.fin - e.debut)",
        ["origine"] = "e.declencheur",
        ["ajouts"] = "e.ajouts",
        ["erreurs"] = "e.nb_erreurs",
        ["etat"] = "CASE WHEN e.fin IS NULL THEN 0 WHEN e.ok THEN 1 WHEN e.ok IS FALSE THEN 2 ELSE 3 END",
    };

    protected override string TriDefaut => "debut";
    protected override IReadOnlyCollection<string> ClesTri { get; } = [.. Tris.Keys];
    protected override bool DecroissantInitial(string cle) => cle is "debut" or "duree" or "ajouts" or "erreurs";
    protected override IEnumerable<(string Nom, string? Valeur)> Filtres => [("tache", Tache), ("etat", Etat), ("origine", Origine), ("pays", Pays)];

    private static IReadOnlyList<Colonne<ExecutionTache>> ColonnesExport() =>
    [
        new("Numéro", e => e.Id),
        new("Import", e => e.Libelle),
        new("Code", e => e.Tache),
        // Texte et non DateTime : l'export de dates ne garde que le jour, or l'heure compte ici.
        new("Début (UTC)", e => e.Debut.ToString("yyyy-MM-dd HH:mm:ss")),
        new("Fin (UTC)", e => e.Fin?.ToString("yyyy-MM-dd HH:mm:ss")),
        new("Durée (secondes)", e => e.Duree is { } d ? (int)d.TotalSeconds : null),
        new("Origine", e => e.Declencheur == "admin" ? "manuel" : "planning"),
        new("Demandé par", e => e.DemandePar),
        new("État", e => e.Etat),
        new("Enregistrements ajoutés", e => e.Ajouts),
        new("Lignes d'erreur", e => e.NbErreurs),
        new("Code de sortie", e => e.CodeSortie),
    ];

    public async Task<IActionResult> OnGetAsync()
    {
        using var cx = db.CreateConnection();
        Taches = (await cx.QueryAsync<(string Code, string Libelle)>("SELECT code, libelle FROM import_taches ORDER BY ordre")).ToList();
        if (Etat is not null && !Etats.Contains(Etat)) Etat = null;
        if (Origine is not ("planning" or "admin")) Origine = null;
        PaysChoix = (await cx.QueryAsync<(string Code, string Nom)>(
            "SELECT DISTINCT p.code, p.nom_fr FROM import_taches t JOIN pays p ON p.code = t.pays ORDER BY p.nom_fr")).ToList();
        if (Pays is not null && Pays != "commun" && !PaysChoix.Any(x => x.Code == Pays)) Pays = null;
        var pays = string.IsNullOrWhiteSpace(Pays) ? null : Pays;
        var tache = string.IsNullOrWhiteSpace(Tache) ? null : Tache;

        // État : mêmes règles que ImportAffichage.Etat (en cours = pas de fin ; interrompue = fin sans résultat).
        const string filtre =
            """
             WHERE (@tache IS NULL OR e.tache = @tache)
               -- Pays de la tâche (catalogue) ; « commun » = tâche sans pays ou tâche retirée du catalogue.
               AND (@pays IS NULL OR (@pays = 'commun' AND t.pays IS NULL) OR t.pays = @pays)
               AND (@origine IS NULL OR e.declencheur = @origine)
               AND (@etat IS NULL
                    OR (@etat = 'en cours' AND e.fin IS NULL)
                    OR (@etat = 'ok' AND e.ok IS TRUE)
                    OR (@etat = 'echec' AND e.ok IS FALSE)
                    OR (@etat = 'interrompue' AND e.fin IS NOT NULL AND e.ok IS NULL))
            """;
        var p = new { tache, pays, etat = Etat, origine = Origine, limit = ParPage, offset = (Math.Max(1, NumeroPage) - 1) * ParPage };
        var total = await cx.ExecuteScalarAsync<int>($"SELECT count(*) FROM import_executions e LEFT JOIN import_taches t ON t.code = e.tache {filtre}", p);
        var ordre = Tris[TriEffectif];
        var sens = Decroissant ? "DESC" : "ASC";
        var export = ExportDemande;
        var lignes = (await cx.QueryAsync<ExecutionTache>(
            $"""
             SELECT e.id, e.tache, COALESCE(t.libelle, e.tache) AS libelle, e.declencheur, e.demande_par, e.debut, e.fin, e.code_sortie, e.ok, e.ajouts, e.nb_erreurs
               FROM import_executions e LEFT JOIN import_taches t ON t.code = e.tache
               {filtre}
              ORDER BY {ordre} {sens} NULLS LAST, e.id DESC
              {(export ? "" : "LIMIT @limit OFFSET @offset")}
             """, p)).ToList();

        if (export) return Exporteur.Ecrire(Format!, "imports-journal", ColonnesExport(), lignes);
        Resultats = new Resultats<ExecutionTache>(lignes, total, Math.Max(1, NumeroPage), ParPage);
        return Page();
    }
}
