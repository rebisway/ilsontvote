using Dapper;
using IlsOntVote.Web.Data;

namespace IlsOntVote.Web.Services;

/// <summary>Un thème de la liste de filtrage (table <c>themes</c>, migration 048) : code stable, noms français et anglais (les autres langues passent par la table de traduction).</summary>
public sealed record Theme(string Code, string NomFr, string NomEn);

/// <summary>
/// Liste des thèmes proposés dans les filtres (<c>?theme=</c>). Les thèmes eux-mêmes sont APPROXIMATIFS (mots-clés du titre, voir la migration 048) : une facilité de recherche,
/// pas une donnée officielle. Copie en mémoire, relue toutes les 10 minutes : la liste ne change presque jamais.
/// </summary>
public sealed class Themes(DapperContext db)
{
    private IReadOnlyList<Theme> _liste = [];
    private DateTime _charge = DateTime.MinValue;

    /// <summary>Thèmes par ordre d'affichage ; liste vide (jamais d'erreur) si la table n'existe pas encore.</summary>
    public IReadOnlyList<Theme> Lister()
    {
        if (DateTime.UtcNow - _charge < TimeSpan.FromMinutes(10)) return _liste;
        try
        {
            using var cx = db.CreateConnection();
            _liste = cx.Query<Theme>("SELECT code AS Code, nom_fr AS NomFr, nom_en AS NomEn FROM themes ORDER BY ordre").ToList();
        }
        catch (Exception) { /* migration 048 non appliquée : pas de filtre de thème */ }
        _charge = DateTime.UtcNow;
        return _liste;
    }

    /// <summary>Code valide (présent dans la liste) ou null : l'adresse ne peut jamais injecter autre chose qu'un code connu.</summary>
    public string? Valider(string? code) => string.IsNullOrEmpty(code) ? null : Lister().FirstOrDefault(t => t.Code == code)?.Code;
}
