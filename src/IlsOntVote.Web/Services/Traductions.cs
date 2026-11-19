using System.Collections.Concurrent;
using Dapper;
using IlsOntVote.Web.Data;

namespace IlsOntVote.Web.Services;

/// <summary>Une ligne de la table de traduction : le texte français de référence et sa traduction.</summary>
public sealed class LigneTraduction
{
    public string Cle { get; set; } = "";
    public string Langue { get; set; } = "";
    public string Texte { get; set; } = "";
    public DateTime Maj { get; set; }
}

/// <summary>
/// Traductions de l'interface (table <c>traductions</c>, migration 042). La CLÉ est le texte français écrit dans la page : <c>L.T("Lois", "Laws")</c> cherche d'abord
/// ("en", "Lois") ici, puis retombe sur le texte du code. Pourquoi une table : ajouter l'espagnol ou l'italien ne doit pas obliger à rouvrir chaque page, et corriger une
/// phrase ne doit pas demander un déploiement. Le service garde TOUT en mémoire (quelques milliers de lignes au plus) : <see cref="Get"/> est appelée des centaines de fois par page et
/// doit être synchrone. La copie est relue en arrière-plan toutes les 2 minutes (« stale-while-revalidate ») et à chaque enregistrement de l'administration ; si la table n'existe pas
/// encore (migration non appliquée) ou si la base ne répond pas, le site continue avec les textes du code, jamais d'erreur.
/// </summary>
public sealed class Traductions(DapperContext db, ILogger<Traductions> log)
{
    private static readonly TimeSpan Duree = TimeSpan.FromMinutes(2);
    private IReadOnlyDictionary<(string Langue, string Cle), string> _copie = new Dictionary<(string, string), string>();
    private DateTime _charge = DateTime.MinValue;
    private int _chargementEnCours;
    private readonly ConcurrentDictionary<(string Langue, string Cle), string> _manquants = new();

    /// <summary>Traduction enregistrée pour (langue, texte français), ou null.</summary>
    public string? Get(string langue, string cle)
    {
        Actualiser();
        return _copie.TryGetValue((langue, cle), out var t) ? t : null;
    }

    /// <summary>
    /// Note qu'une page a demandé une traduction absente (langue autre que le français et l'anglais) : l'administration liste ces textes « à traduire », vus depuis le démarrage.
    /// <paramref name="repli"/> = le texte anglais qui sera affiché en attendant.
    /// </summary>
    public void Manque(string langue, string cle, string repli) => _manquants.TryAdd((langue, cle), repli);

    /// <summary>Textes demandés sans traduction pour la langue, depuis le démarrage de l'application.</summary>
    public IReadOnlyList<(string Cle, string Repli)> Manquants(string langue) =>
        _manquants.Where(m => m.Key.Langue == langue && !_copie.ContainsKey(m.Key)).Select(m => (m.Key.Cle, m.Value)).OrderBy(m => m.Cle).ToList();

    /// <summary>Force la relecture au prochain appel (après un enregistrement de l'administration).</summary>
    public void Invalider() => _charge = DateTime.MinValue;

    private void Actualiser()
    {
        if (DateTime.UtcNow - _charge < Duree) return;
        // Premier chargement : synchrone (rien à afficher en attendant) ; ensuite : en arrière-plan, la copie périmée sert pendant ce temps.
        if (_charge == DateTime.MinValue && _copie.Count == 0) { Recharger(); return; }
        if (Interlocked.CompareExchange(ref _chargementEnCours, 1, 0) != 0) return;
        _ = Task.Run(() => { try { Recharger(); } finally { Interlocked.Exchange(ref _chargementEnCours, 0); } });
    }

    private void Recharger()
    {
        try
        {
            using var cx = db.CreateConnection();
            var d = cx.Query<(string Langue, string Cle, string Texte)>("SELECT langue, cle, texte FROM traductions")
                .ToDictionary(r => (r.Langue, r.Cle), r => r.Texte);
            _copie = d;
        }
        catch (Exception ex)
        {
            // Table absente ou base indisponible : on garde la copie précédente et on réessaiera après la durée normale.
            log.LogWarning(ex, "Traductions : lecture impossible, les textes du code sont utilisés.");
        }
        _charge = DateTime.UtcNow;
    }

    // ------------------------------------------------------------------ administration

    /// <summary>Langues qui ont au moins une traduction, plus l'anglais.</summary>
    public async Task<IReadOnlyList<string>> LanguesAsync()
    {
        using var cx = db.CreateConnection();
        var l = (await cx.QueryAsync<string>("SELECT DISTINCT langue FROM traductions ORDER BY 1")).ToList();
        if (!l.Contains("en")) l.Insert(0, "en");
        return l;
    }

    public async Task<IReadOnlyList<LigneTraduction>> ListerAsync(string langue, string? recherche)
    {
        using var cx = db.CreateConnection();
        return (await cx.QueryAsync<LigneTraduction>(
            "SELECT cle, langue, texte, maj FROM traductions WHERE langue = @langue AND (@q IS NULL OR cle ILIKE '%' || @q || '%' OR texte ILIKE '%' || @q || '%') ORDER BY cle",
            new { langue, q = string.IsNullOrWhiteSpace(recherche) ? null : recherche.Trim() })).ToList();
    }

    public async Task EnregistrerAsync(string langue, string cle, string texte)
    {
        using var cx = db.CreateConnection();
        await cx.ExecuteAsync(
            "INSERT INTO traductions (langue, cle, texte) VALUES (@langue, @cle, @texte) ON CONFLICT (langue, cle) DO UPDATE SET texte = EXCLUDED.texte, maj = now()",
            new { langue, cle, texte });
        Invalider();
    }

    public async Task SupprimerAsync(string langue, string cle)
    {
        using var cx = db.CreateConnection();
        await cx.ExecuteAsync("DELETE FROM traductions WHERE langue = @langue AND cle = @cle", new { langue, cle });
        Invalider();
    }
}
