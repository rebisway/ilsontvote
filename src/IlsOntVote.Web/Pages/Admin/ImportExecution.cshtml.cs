using Dapper;
using IlsOntVote.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.Json;

namespace IlsOntVote.Web.Pages.Admin;

/// <summary>Compteurs d'une source écrits pendant une exécution (lignes import_runs rattachées par execution_id).</summary>
public sealed class CompteurSource
{
    public string Source { get; set; } = "";
    public bool? Ok { get; set; }
    public int? Scrutins { get; set; }
    public int? Votes { get; set; }
    public string? Message { get; set; }
}

/// <summary>Effectif d'une table avant et après l'exécution (bilan mesuré par le planificateur).</summary>
public sealed record LigneBilan(string Table, long Avant, long Apres)
{
    public long Variation => Apres - Avant;
}

/// <summary>
/// Détail d'une exécution : état, durée, compteurs par source et sortie console complète (la fin si elle dépasse ~400 Ko : c'est là que sont
/// les erreurs). Une exécution en cours se rafraîchit toute seule (script en bas de page) : on suit un import « en direct ».
/// </summary>
public class ImportExecutionModel(DapperContext db) : PageModel
{
    public ExecutionTache? Execution { get; private set; }
    public string Sortie { get; private set; } = "";
    public IReadOnlyList<CompteurSource> Compteurs { get; private set; } = [];
    public IReadOnlyList<LigneBilan> Bilan { get; private set; } = [];
    public string Erreurs { get; private set; } = "";

    /// <summary>Nom de table -> libellé affiché (même liste que Planificateur.TablesSuivies dans l'Importer).</summary>
    private static readonly Dictionary<string, string> Libelles = new()
    {
        ["parlementaires"] = "Parlementaires", ["mandats"] = "Mandats", ["groupes"] = "Groupes", ["groupe_appartenances"] = "Appartenances à un groupe",
        ["scrutins"] = "Scrutins", ["votes"] = "Votes individuels", ["textes"] = "Textes", ["texte_etapes"] = "Étapes des textes",
        ["amendements_dossiers"] = "Amendements : dossiers (effectifs)", ["petitions"] = "Pétitions", ["petitions_historique"] = "Pétitions : relevés (historique)", ["petitions_anomalies"] = "Pétitions : constats",
        ["traductions_titres"] = "Titres traduits (fr vers en)", ["traductions_titres_fr"] = "Titres traduits (en vers fr)",
    };

    public async Task<IActionResult> OnGetAsync(long id)
    {
        using var cx = db.CreateConnection();
        Execution = await cx.QuerySingleOrDefaultAsync<ExecutionTache>(
            """
            SELECT e.id, e.tache, COALESCE(t.libelle, e.tache) AS libelle, e.declencheur, e.demande_par, e.debut, e.fin, e.code_sortie, e.ok, e.ajouts, e.nb_erreurs
              FROM import_executions e LEFT JOIN import_taches t ON t.code = e.tache WHERE e.id = @id
            """, new { id });
        if (Execution is null) return NotFound();
        var (sortie, erreurs, bilanJson) = await cx.QuerySingleAsync<(string Sortie, string Erreurs, string? Bilan)>(
            "SELECT sortie, erreurs, bilan::text AS bilan FROM import_executions WHERE id = @id", new { id });
        Sortie = sortie; Erreurs = erreurs;
        // Bilan : { "table": { "avant": n, "apres": n } } écrit par le planificateur ; libellés de la liste en dur ci-dessous.
        // jsonb ne garde pas l'ordre des clés : on affiche dans l'ordre de la liste de libellés (parlementaires, scrutins, votes, textes, pétitions…).
        if (bilanJson is not null)
        {
            var racine = JsonDocument.Parse(bilanJson).RootElement;
            Bilan = Libelles.Where(l => racine.TryGetProperty(l.Key, out _))
                .Select(l => { var v = racine.GetProperty(l.Key); return new LigneBilan(l.Value, v.GetProperty("avant").GetInt64(), v.GetProperty("apres").GetInt64()); })
                .ToList();
        }
        Compteurs = (await cx.QueryAsync<CompteurSource>(
            "SELECT source, ok, scrutins, votes, message FROM import_runs WHERE execution_id = @id ORDER BY id", new { id })).ToList();
        return Page();
    }
}
