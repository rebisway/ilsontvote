using System.Security.Cryptography;
using System.Text;

namespace IlsOntVote.Web.Services;

/// <summary>
/// Empreinte d'IP pour compter des visiteurs uniques SANS pouvoir remonter à une personne.
/// HMAC-SHA256 avec une clé secrète propre au serveur (<c>App_Data/stats.key</c>, générée au
/// premier démarrage, jamais dans le dépôt) : un simple SHA-256 d'IP se « casse » en énumérant
/// les 4 milliards d'IPv4, pas un HMAC dont on ignore la clé.
/// Supprimer le fichier de clé rend définitivement irréversible l'historique des empreintes.
/// </summary>
public interface IIpHasher
{
    string Hash(System.Net.IPAddress? ip);
}

public sealed class IpHasher : IIpHasher
{
    private readonly byte[] _key;

    public IpHasher(IHostEnvironment env)
    {
        var dossier = Path.Combine(env.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dossier);
        var fichier = Path.Combine(dossier, "stats.key");

        if (File.Exists(fichier))
        {
            _key = File.ReadAllBytes(fichier);
        }
        else
        {
            _key = RandomNumberGenerator.GetBytes(32);
            File.WriteAllBytes(fichier, _key);
        }
    }

    public string Hash(System.Net.IPAddress? ip) =>
        Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(ip?.ToString() ?? "unknown")));
}
