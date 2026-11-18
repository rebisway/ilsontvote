using Dapper;
using IlsOntVote.Web.Data;
using IlsOntVote.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IlsOntVote.Web.Pages.Admin;

/// <summary>Un pays du référentiel (table pays) avec ce qu'on en sait : préfixe d'adresse, état, volume de données.</summary>
public sealed class PaysAdmin
{
    public string Code { get; set; } = "";
    public string NomFr { get; set; } = "";
    public string? PrefixeUrl { get; set; }
    public bool Actif { get; set; }
    public int NbScrutins { get; set; }
}

/// <summary>Une chambre (table chambres) avec son volume de données.</summary>
public sealed class ChambreAdmin
{
    public string Code { get; set; } = "";
    public string Pays { get; set; } = "";
    public string NomFr { get; set; } = "";
    public int? NbSieges { get; set; }
    public string Disposition { get; set; } = "";
    public bool Actif { get; set; }
    public int NbScrutins { get; set; }
    public DateTime? DernierScrutin { get; set; }
}

/// <summary>Un groupe d'une chambre en bancs face à face, avec son appartenance au gouvernement.</summary>
public sealed class GroupeAdmin
{
    public string Id { get; set; } = "";
    public string Chambre { get; set; } = "";
    public string Sigle { get; set; } = "";
    public string Libelle { get; set; } = "";
    public bool Gouvernement { get; set; }
    public int Membres { get; set; }
}

/// <summary>
/// Activation des pays et des chambres. « Pays actif » ouvre son adresse (/uk) et le sélecteur de pays ; « chambre active » ajoute la chambre aux données
/// ouvertes (/donnees). Garde-fous : on n'active pas un pays sans préfixe d'adresse ni sans données, et la France et l'Union européenne restent toujours actives
/// (la racine du site en dépend). Le changement est immédiat (la liste en mémoire est invalidée).
/// </summary>
public class PaysModel(DapperContext db, PaysSite site) : PageModel
{
    public IReadOnlyList<PaysAdmin> Pays { get; private set; } = [];
    public IReadOnlyList<ChambreAdmin> Chambres { get; private set; } = [];
    public IReadOnlyList<GroupeAdmin> Groupes { get; private set; } = [];
    [TempData] public string? Message { get; set; }

    public async Task OnGetAsync()
    {
        using var cx = db.CreateConnection();
        Pays = (await cx.QueryAsync<PaysAdmin>(
            """
            SELECT p.code, p.nom_fr, p.prefixe_url, p.actif,
                   -- Volume de données du pays : scrutins de toutes ses chambres.
                   (SELECT count(*) FROM scrutins s JOIN chambres c ON c.code = s.chambre WHERE c.pays = p.code)::int AS nb_scrutins
              FROM pays p ORDER BY p.ordre
            """)).ToList();
        Chambres = (await cx.QueryAsync<ChambreAdmin>(
            """
            SELECT c.code, c.pays, c.nom_fr, c.nb_sieges, c.disposition, c.actif,
                   (SELECT count(*) FROM scrutins s WHERE s.chambre = c.code)::int AS nb_scrutins,
                   (SELECT max(s.date_scrutin) FROM scrutins s WHERE s.chambre = c.code) AS dernier_scrutin
              FROM chambres c ORDER BY c.ordre
            """)).ToList();
        // Groupes des chambres en bancs face à face : ceux qui siègent du côté du gouvernement (cochés).
        Groupes = (await cx.QueryAsync<GroupeAdmin>(
            """
            SELECT g.id, g.chambre, g.sigle, g.libelle, g.gouvernement,
                   (SELECT count(*) FROM groupe_appartenances a WHERE a.groupe_id = g.id AND a.date_fin IS NULL)::int AS membres
              FROM groupes g JOIN chambres c ON c.code = g.chambre AND c.disposition = 'face-a-face'
             ORDER BY g.chambre, membres DESC, g.sigle
            """)).ToList();
    }

    /// <summary>Active ou désactive un pays (France et Union européenne : toujours actifs).</summary>
    public async Task<IActionResult> OnPostPaysAsync(string code)
    {
        using var cx = db.CreateConnection();
        var p = await cx.QuerySingleOrDefaultAsync<(string Code, string? Prefixe, bool Actif, int Scrutins)>(
            """
            SELECT p.code, p.prefixe_url, p.actif,
                   (SELECT count(*) FROM scrutins s JOIN chambres c ON c.code = s.chambre WHERE c.pays = p.code)::int
              FROM pays p WHERE p.code = @code
            """, new { code });
        if (p.Code is null) { Message = "Pays inconnu."; return RedirectToPage(); }
        if (p.Code is "FR" or "EU") { Message = "La France et l'Union européenne restent toujours actives."; return RedirectToPage(); }
        if (!p.Actif)
        {
            if (string.IsNullOrEmpty(p.Prefixe)) { Message = "Activation refusée : ce pays n'a pas de préfixe d'adresse (colonne prefixe_url)."; return RedirectToPage(); }
            if (p.Scrutins == 0) { Message = "Activation refusée : aucune donnée importée pour ce pays (lancer d'abord son import dans Imports)."; return RedirectToPage(); }
        }
        await cx.ExecuteAsync("UPDATE pays SET actif = NOT actif WHERE code = @code", new { code });
        site.Invalider();
        Message = p.Actif ? $"Pays {code} désactivé : /{p.Prefixe} n'existe plus." : $"Pays {code} activé : /{p.Prefixe} est ouvert et le sélecteur de pays s'affiche.";
        return RedirectToPage();
    }

    /// <summary>Active ou désactive une chambre : elle entre (ou sort) des données ouvertes au prochain export. Sans effet sur les pages.</summary>
    public async Task<IActionResult> OnPostChambreAsync(string code)
    {
        using var cx = db.CreateConnection();
        var n = await cx.ExecuteAsync("UPDATE chambres SET actif = NOT actif WHERE code = @code", new { code });
        site.Invalider();
        Message = n == 0 ? "Chambre inconnue." : $"Chambre {code} modifiée : elle sera incluse ou exclue des données ouvertes au prochain export.";
        return RedirectToPage();
    }

    /// <summary>Enregistre les groupes du côté du gouvernement d'une chambre en bancs face à face (les autres passent du côté de l'opposition).</summary>
    public async Task<IActionResult> OnPostGouvernementAsync(string chambre, string[]? groupes)
    {
        using var cx = db.CreateConnection();
        var n = await cx.ExecuteAsync(
            "UPDATE groupes SET gouvernement = (id = ANY(@ids)) WHERE chambre = @chambre", new { chambre, ids = groupes ?? [] });
        Message = n == 0 ? "Aucun groupe pour cette chambre." : $"Côté du gouvernement enregistré pour {chambre} (effet immédiat sur les hémicycles).";
        return RedirectToPage();
    }
}
