using System.Net;
using IlsOntVote.Web.Services;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IlsOntVote.Web.Pages;

/// <summary>
/// Socle des pages de liste : tri par colonne (<c>?tri=&amp;sens=</c>), pagination et liens d'export qui
/// conservent les filtres actifs. Les clés de tri valides sont une liste blanche fournie par la page.
/// </summary>
public abstract class ListePageModel : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Tri { get; set; }
    [BindProperty(SupportsGet = true)] public string? Sens { get; set; }
    [BindProperty(SupportsGet = true, Name = "page")] public int NumeroPage { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public string? Format { get; set; }

    protected abstract string TriDefaut { get; }
    protected virtual bool DecroissantDefaut => true;
    protected abstract IReadOnlyCollection<string> ClesTri { get; }

    /// <summary>Filtres actifs (nom, valeur) : repris dans tous les liens de tri, de pagination et d'export.</summary>
    protected abstract IEnumerable<(string Nom, string? Valeur)> Filtres { get; }

    /// <summary>Colonnes « numériques ou dates » : le premier clic trie du plus grand au plus petit.</summary>
    protected virtual bool DecroissantInitial(string cle) => false;

    public string TriEffectif => Tri is not null && ClesTri.Contains(Tri) ? Tri : TriDefaut;

    public bool Decroissant => Sens switch { "asc" => false, "desc" => true, _ => DecroissantDefaut };

    public bool ExportDemande => Exporteur.EstFormat(Format);

    public string Lien(string? tri = null, bool? decroissant = null, int? page = null, string? format = null)
    {
        var q = Filtres.Where(f => !string.IsNullOrWhiteSpace(f.Valeur))
            .Select(f => $"{f.Nom}={WebUtility.UrlEncode(f.Valeur)}").ToList();

        var t = tri ?? (Tri is not null && ClesTri.Contains(Tri) ? Tri : null);
        if (t is not null)
        {
            q.Add($"tri={t}");
            q.Add($"sens={(decroissant ?? Decroissant ? "desc" : "asc")}");
        }
        if (page is > 1) q.Add($"page={page}");
        if (format is not null) q.Add($"format={format}");

        var chemin = Request.Path.Value ?? "/";
        return q.Count == 0 ? chemin : chemin + "?" + string.Join("&", q);
    }

    /// <summary>En-tête de colonne triable : lien, flèche et <c>aria-sort</c> pour les lecteurs d'écran.</summary>
    public HtmlString Th(string cle, string libelle, string? classe = null)
    {
        var actif = TriEffectif == cle;
        var inverse = actif ? !Decroissant : DecroissantInitial(cle);
        var aria = actif ? (Decroissant ? "descending" : "ascending") : "none";
        var fleche = actif ? (Decroissant ? " ▼" : " ▲") : "";
        var cls = string.IsNullOrEmpty(classe) ? "" : $" class=\"{WebUtility.HtmlEncode(classe)}\"";
        return new HtmlString(
            $"<th scope=\"col\"{cls} aria-sort=\"{aria}\"><a class=\"tri{(actif ? " actif" : "")}\" href=\"{WebUtility.HtmlEncode(Lien(cle, inverse))}\">" +
            $"{WebUtility.HtmlEncode(libelle)}{fleche}</a></th>");
    }
}
