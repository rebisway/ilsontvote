using Dapper;
using IlsOntVote.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IlsOntVote.Web.Pages.Admin;

/// <summary>Une correction manuelle du rattachement d'un scrutin à un texte (classe à propriétés : cf. la note sur Dapper dans Qualite).</summary>
public sealed class CorrectionRattachement
{
    public string ScrutinId { get; set; } = "";
    public string? ScrutinTitre { get; set; }
    public DateTime? DateScrutin { get; set; }
    public string? TexteId { get; set; }
    public string? TexteTitre { get; set; }
    public string Motif { get; set; } = "";
    public string? Auteur { get; set; }
    public DateTime CreeLe { get; set; }
}

/// <summary>
/// Corrections manuelles du rattachement scrutin -> texte. Une correction prime sur le calcul automatique et est rejouée à chaque import
/// (cf. migration 020). Elle s'applique tout de suite au scrutin concerné ; sa suppression rend la main au calcul au prochain import.
/// </summary>
public class CorrectionsModel(DapperContext db) : PageModel
{
    public IReadOnlyList<CorrectionRattachement> Corrections { get; private set; } = [];
    [TempData] public string? Message { get; set; }

    public async Task OnGetAsync()
    {
        using var cx = db.CreateConnection();
        Corrections = (await cx.QueryAsync<CorrectionRattachement>(
            """
            SELECT c.scrutin_id, s.titre AS scrutin_titre, s.date_scrutin, c.texte_id, t.titre AS texte_titre, c.motif, c.auteur, c.cree_le
              FROM corrections_rattachement c
              LEFT JOIN scrutins s ON s.id = c.scrutin_id
              LEFT JOIN textes t ON t.id = c.texte_id
             ORDER BY c.cree_le DESC
            """)).ToList();
    }

    /// <summary>Enregistre (ou remplace) la correction d'un scrutin et l'applique immédiatement. Texte vide = « ne pas rattacher ».</summary>
    public async Task<IActionResult> OnPostAjouterAsync(string? scrutinId, string? texteId, string? motif)
    {
        scrutinId = scrutinId?.Trim();
        texteId = string.IsNullOrWhiteSpace(texteId) ? null : texteId.Trim();
        motif = motif?.Trim();
        if (string.IsNullOrEmpty(scrutinId) || string.IsNullOrEmpty(motif)) { Message = "Le scrutin et le motif sont obligatoires."; return RedirectToPage(); }

        using var cx = db.CreateConnection();
        var scrutinExiste = await cx.ExecuteScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM scrutins WHERE id = @scrutinId)", new { scrutinId });
        if (!scrutinExiste) { Message = $"Scrutin introuvable : {scrutinId}."; return RedirectToPage(); }
        if (texteId is not null && !await cx.ExecuteScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM textes WHERE id = @texteId)", new { texteId }))
        { Message = $"Texte introuvable : {texteId}."; return RedirectToPage(); }

        await cx.ExecuteAsync(
            """
            INSERT INTO corrections_rattachement (scrutin_id, texte_id, motif, auteur) VALUES (@scrutinId, @texteId, @motif, @auteur)
            ON CONFLICT (scrutin_id) DO UPDATE SET texte_id = EXCLUDED.texte_id, motif = EXCLUDED.motif, auteur = EXCLUDED.auteur, cree_le = now();
            -- Effet immédiat (l'import rejoue la correction ensuite) : vote sur l'ensemble -> texte_id ; autre vote -> texte_lie_id.
            UPDATE scrutins SET texte_id = @texteId WHERE id = @scrutinId AND sur_ensemble;
            UPDATE scrutins SET texte_lie_id = @texteId WHERE id = @scrutinId AND NOT sur_ensemble;
            """, new { scrutinId, texteId, motif, auteur = User.Identity?.Name });
        Message = texteId is null ? "Correction enregistrée : scrutin non rattaché." : "Correction enregistrée.";
        return RedirectToPage();
    }

    /// <summary>Supprime la correction : le calcul automatique redevient seul juge à l'import suivant (le rattachement actuel n'est pas modifié ici).</summary>
    public async Task<IActionResult> OnPostSupprimerAsync(string scrutinId)
    {
        using var cx = db.CreateConnection();
        await cx.ExecuteAsync("DELETE FROM corrections_rattachement WHERE scrutin_id = @scrutinId", new { scrutinId });
        Message = "Correction supprimée : le rattachement sera recalculé au prochain import.";
        return RedirectToPage();
    }
}
