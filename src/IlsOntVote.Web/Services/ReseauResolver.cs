using System.Net;
using System.Text.RegularExpressions;
using MaxMind.GeoIP2;
using MaxMind.GeoIP2.Exceptions;

namespace IlsOntVote.Web.Services;

/// <summary>
/// Adresse IP -> « vient d'un hébergeur cloud ? », avec la base locale GeoLite2 ASN (<c>App_Data/GeoLite2-ASN.mmdb</c>, même compte MaxMind
/// gratuit que la base pays, jamais versionnée : à déposer sur la VM). Sert aux STATISTIQUES seulement : une visite venant d'un hébergeur
/// est comptée à part (« Cloud »), jamais bloquée. Pourquoi : les collecteurs de pages (Alibaba, Tencent, AWS…) se présentent avec un navigateur
/// ordinaire, le filtre sur le User-Agent ne les voit pas ; ils apparaissaient comme des centaines de « lecteurs » à Singapour ou aux États-Unis.
/// Limite assumée : un vrai lecteur derrière un VPN hébergé est classé « Cloud » (acceptable pour des chiffres d'audience).
/// Base absente ou adresse inconnue : faux, le site fonctionne.
/// </summary>
public sealed class ReseauResolver : IDisposable
{
    /// <summary>
    /// Fragments du nom de l'organisation propriétaire du réseau (champ « AutonomousSystemOrganization ») : grands clouds et hébergeurs
    /// bon marché fréquemment utilisés par les collecteurs. Volontairement par NOM et non par numéro d'ASN : un fournisseur possède
    /// des dizaines de numéros. Un fournisseur d'accès grand public n'y figure jamais.
    /// </summary>
    private static readonly Regex Hebergeurs = new(
        "alibaba|aliyun|tencent|huawei|amazon|aws|google|microsoft|azure|digitalocean|linode|akamai|vultr|choopa|ovh|hetzner|" +
        "scaleway|contabo|oracle|cloudflare|leaseweb|m247|datacamp|hosting|datacenter|data center|server|cloud",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly DatabaseReader? _lecteur;

    public ReseauResolver(IWebHostEnvironment env, ILogger<ReseauResolver> log)
    {
        var chemin = Path.Combine(env.ContentRootPath, "App_Data", "GeoLite2-ASN.mmdb");
        if (File.Exists(chemin)) _lecteur = new DatabaseReader(chemin);
        else log.LogWarning("GeoLite2-ASN.mmdb introuvable ({Chemin}) : les robots hébergés dans le cloud ne sont pas détectés.", chemin);
    }

    /// <summary>Vrai si l'adresse appartient à un réseau d'hébergeur cloud connu.</summary>
    public bool EstHebergeur(IPAddress? ip)
    {
        if (_lecteur is null || ip is null) return false;
        try { return _lecteur.Asn(ip).AutonomousSystemOrganization is { } nom && Hebergeurs.IsMatch(nom); }
        catch (Exception ex) when (ex is AddressNotFoundException or GeoIP2Exception) { return false; }
    }

    public void Dispose() => _lecteur?.Dispose();
}
