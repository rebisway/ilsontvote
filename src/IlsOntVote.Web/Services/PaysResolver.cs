using System.Net;
using MaxMind.GeoIP2;
using MaxMind.GeoIP2.Exceptions;

namespace IlsOntVote.Web.Services;

/// <summary>
/// Adresse IP -> code pays ISO, avec la base locale GeoLite2 (<c>App_Data/GeoLite2-Country.mmdb</c>, gratuite avec un compte MaxMind,
/// jamais versionnée ni publiée : à déposer sur la VM). Aucun appel à un service extérieur, l'IP n'est pas stockée.
/// Base absente ou adresse inconnue : null, le site fonctionne et les statistiques affichent « ? ».
/// </summary>
public sealed class PaysResolver : IDisposable
{
    private readonly DatabaseReader? _lecteur;

    public PaysResolver(IWebHostEnvironment env, ILogger<PaysResolver> log)
    {
        var chemin = Path.Combine(env.ContentRootPath, "App_Data", "GeoLite2-Country.mmdb");
        if (File.Exists(chemin)) _lecteur = new DatabaseReader(chemin);
        else log.LogWarning("GeoLite2-Country.mmdb introuvable ({Chemin}) : les visites sont comptées sans pays.", chemin);
    }

    /// <summary>Code ISO du pays de l'adresse, ou null si inconnu.</summary>
    public string? Resolve(IPAddress? ip)
    {
        if (_lecteur is null || ip is null) return null;
        try { return _lecteur.Country(ip).Country.IsoCode; }
        catch (Exception ex) when (ex is AddressNotFoundException or GeoIP2Exception) { return null; }
    }

    /// <summary>
    /// « 🇫🇷 FR - France » : le drapeau est la paire d'indicateurs régionaux Unicode du code ISO. Les navigateurs Windows affichent
    /// les deux lettres au lieu de l'image, d'où le code et le nom à côté. « ? » = pays inconnu.
    /// </summary>
    public static string Libelle(string code)
    {
        if (code.Length != 2 || !char.IsAsciiLetter(code[0]) || !char.IsAsciiLetter(code[1])) return code == "?" ? "? Inconnu" : code;
        var u = code.ToUpperInvariant();
        var drapeau = char.ConvertFromUtf32(0x1F1E6 + (u[0] - 'A')) + char.ConvertFromUtf32(0x1F1E6 + (u[1] - 'A'));
        try { return $"{drapeau} {u} - {new System.Globalization.RegionInfo(u).DisplayName}"; }
        catch (ArgumentException) { return $"{drapeau} {u}"; } // code que .NET ne connaît pas : le code seul
    }

    public void Dispose() => _lecteur?.Dispose();
}
