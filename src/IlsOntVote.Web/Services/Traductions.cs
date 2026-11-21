using System.Collections.Concurrent;
using Dapper;
using IlsOntVote.Web.Data;

namespace IlsOntVote.Web.Services;

/// <summary>
/// Une ligne de la table de traduction : clé STABLE, langue, texte traduit, et le texte français d'origine (<c>SourceFr</c>) tel qu'il était quand la traduction a été écrite.
/// <c>SourceFr</c> permet de repérer une traduction PÉRIMÉE : si la phrase française de la page a changé depuis, les deux ne coïncident plus.
/// </summary>
public sealed class LigneTraduction
{
    public string Cle { get; set; } = "";
    public string Langue { get; set; } = "";
    public string Texte { get; set; } = "";
    public string? SourceFr { get; set; }
    public DateTime Maj { get; set; }
}

/// <summary>Une traduction dont le texte français de la page n'est plus celui qui a été traduit (à relire).</summary>
public sealed record TraductionPerimee(string Cle, string FrActuel, string FrTraduit, string Texte);

/// <summary>Un texte demandé par une page sans traduction pour la langue (« à traduire »).</summary>
public sealed record TexteATraduire(string Cle, string Fr, string En);

/// <summary>
/// Traductions de l'interface (table <c>traductions</c>, migrations 042 et 055). La CLÉ est un identifiant STABLE écrit dans la page (<c>L.T("accueil-accroche")</c>) :
/// retoucher la phrase française ou anglaise ne casse jamais ses traductions (l'ancien système prenait le texte français comme clé : une virgule changée et toutes les traductions
/// retombaient silencieusement sur l'anglais). Le service cherche (langue, clé) ici, puis retombe sur le texte du code. Pourquoi une table : ajouter l'espagnol ou l'italien ne doit pas
/// obliger à rouvrir chaque page, et corriger une phrase ne doit pas demander un déploiement. Tout est gardé en mémoire (quelques milliers de lignes au plus) : <see cref="Get"/> est appelée
/// des centaines de fois par page et doit être synchrone. La copie est relue en arrière-plan toutes les 2 minutes (« stale-while-revalidate ») et à chaque enregistrement de
/// l'administration ; si la table n'existe pas encore (migration non appliquée) ou si la base ne répond pas, le site continue avec les textes du code, jamais d'erreur.
/// </summary>
public sealed class Traductions(DapperContext db, ILogger<Traductions> log)
{
    private sealed record Entree(string Texte, string? SourceFr);

    private static readonly TimeSpan Duree = TimeSpan.FromMinutes(2);
    private IReadOnlyDictionary<(string Langue, string Cle), Entree> _copie = new Dictionary<(string, string), Entree>();
    private DateTime _charge = DateTime.MinValue;
    private int _chargementEnCours;
    private readonly ConcurrentDictionary<(string Langue, string Cle), (string Fr, string En)> _manquants = new();
    private readonly ConcurrentDictionary<(string Langue, string Cle), (string FrActuel, string FrTraduit, string Texte)> _perimees = new();

    /// <summary>
    /// Texte enregistré pour (langue, clé), ou null. Pour une langue autre que le français et l'anglais, on compare la source française gardée avec la ligne française ACTUELLE : si la phrase
    /// française a changé depuis la traduction, celle-ci reste utilisée (mieux qu'un repli) mais est signalée « périmée » dans l'administration.
    /// </summary>
    public string? Lire(string langue, string cle)
    {
        Actualiser();
        if (!_copie.TryGetValue((langue, cle), out var e)) return null;
        if (langue is not ("fr" or "en") && e.SourceFr is not null && _copie.TryGetValue(("fr", cle), out var fr) && fr.Texte != e.SourceFr)
            _perimees.TryAdd((langue, cle), (fr.Texte, e.SourceFr, e.Texte));
        return e.Texte;
    }

    /// <summary>
    /// Note qu'une page a demandé un texte sans traduction pour cette langue : l'administration liste ces textes « à traduire », vus depuis le démarrage, avec le français et l'anglais sous les yeux.
    /// </summary>
    public void Manque(string langue, string cle) =>
        _manquants.TryAdd((langue, cle), (_copie.TryGetValue(("fr", cle), out var f) ? f.Texte : "", _copie.TryGetValue(("en", cle), out var en) ? en.Texte : ""));

    /// <summary>Textes demandés sans traduction pour la langue, depuis le démarrage de l'application.</summary>
    public IReadOnlyList<TexteATraduire> Manquants(string langue) =>
        _manquants.Where(m => m.Key.Langue == langue && !_copie.ContainsKey(m.Key)).Select(m => new TexteATraduire(m.Key.Cle, m.Value.Fr, m.Value.En)).OrderBy(m => m.Cle).ToList();

    /// <summary>Traductions utilisées depuis le démarrage dont le texte français a changé depuis qu'elles ont été écrites (à relire).</summary>
    public IReadOnlyList<TraductionPerimee> Perimees(string langue) =>
        // Encore périmée : la ligne enregistrée garde un texte source différent du français actuel (une retraduction enregistrée depuis met la source à jour).
        _perimees.Where(p => p.Key.Langue == langue && _copie.TryGetValue(p.Key, out var e) && e.SourceFr != p.Value.FrActuel)
            .Select(p => new TraductionPerimee(p.Key.Cle, p.Value.FrActuel, p.Value.FrTraduit, p.Value.Texte)).OrderBy(p => p.Cle).ToList();

    /// <summary>Remplace la copie en mémoire par ces lignes, sans lire la base (tests unitaires) ; la copie est considérée à jour pour la durée normale.</summary>
    public void Remplacer(IEnumerable<LigneTraduction> lignes)
    {
        _copie = lignes.ToDictionary(l => (l.Langue, l.Cle), l => new Entree(l.Texte, l.SourceFr));
        _charge = DateTime.UtcNow;
    }

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
            var d = cx.Query<(string Langue, string Cle, string Texte, string? SourceFr)>("SELECT langue, cle, texte, source_fr FROM traductions")
                .ToDictionary(r => (r.Langue, r.Cle), r => new Entree(r.Texte, r.SourceFr));
            _copie = d;
        }
        catch (Exception ex)
        {
            // Table absente ou base indisponible : on garde la copie précédente et on réessaiera après la durée normale.
            log.LogWarning(ex, "Traductions : lecture impossible, la copie précédente est conservée (sans copie, les pages affichent les clés).");
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
            "SELECT cle, langue, texte, source_fr AS SourceFr, maj FROM traductions WHERE langue = @langue AND (@q IS NULL OR cle ILIKE '%' || @q || '%' OR texte ILIKE '%' || @q || '%' OR source_fr ILIKE '%' || @q || '%') ORDER BY cle",
            new { langue, q = string.IsNullOrWhiteSpace(recherche) ? null : recherche.Trim() })).ToList();
    }

    /// <summary>Enregistre une traduction ; <paramref name="sourceFr"/> = le texte français traduit (garde la trace pour repérer plus tard une traduction périmée).</summary>
    public async Task EnregistrerAsync(string langue, string cle, string texte, string? sourceFr)
    {
        using var cx = db.CreateConnection();
        await cx.ExecuteAsync(
            "INSERT INTO traductions (langue, cle, texte, source_fr) VALUES (@langue, @cle, @texte, @sourceFr) ON CONFLICT (langue, cle) DO UPDATE SET texte = EXCLUDED.texte, source_fr = COALESCE(EXCLUDED.source_fr, traductions.source_fr), maj = now()",
            new { langue, cle, texte, sourceFr });
        Invalider();
    }

    public async Task SupprimerAsync(string langue, string cle)
    {
        using var cx = db.CreateConnection();
        await cx.ExecuteAsync("DELETE FROM traductions WHERE langue = @langue AND cle = @cle", new { langue, cle });
        Invalider();
    }
}
