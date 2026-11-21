namespace IlsOntVote.Web.Services;

/// <summary>
/// Drapeaux des pays de l'UE à partir du nom français stocké pour un eurodéputé (colonne <c>departement</c>).
/// Le drapeau est la paire d'indicateurs régionaux Unicode du code ISO. Les navigateurs Windows affichent les deux lettres au lieu
/// de l'image : le nom du pays reste donc toujours écrit à côté.
/// </summary>
public static class Drapeaux
{
    private static readonly Dictionary<string, string> Iso = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Autriche"] = "AT", ["Belgique"] = "BE", ["Bulgarie"] = "BG", ["Croatie"] = "HR", ["Chypre"] = "CY", ["Tchéquie"] = "CZ",
        ["Danemark"] = "DK", ["Estonie"] = "EE", ["Finlande"] = "FI", ["France"] = "FR", ["Allemagne"] = "DE", ["Grèce"] = "GR",
        ["Hongrie"] = "HU", ["Irlande"] = "IE", ["Italie"] = "IT", ["Lettonie"] = "LV", ["Lituanie"] = "LT", ["Luxembourg"] = "LU",
        ["Malte"] = "MT", ["Pays-Bas"] = "NL", ["Pologne"] = "PL", ["Portugal"] = "PT", ["Roumanie"] = "RO", ["Slovaquie"] = "SK",
        ["Slovénie"] = "SI", ["Espagne"] = "ES", ["Suède"] = "SE",
    };

    /// <summary>« Finlande » -> « 🇫🇮 Finlande » pour un eurodéputé ; le texte inchangé pour un député ou un sénateur (département).</summary>
    public static string Avec(string chambre, string? lieu, Langue l)
    {
        if (string.IsNullOrEmpty(lieu)) return "";
        if (chambre != Models.Chambres.UE || !Iso.TryGetValue(lieu, out var code)) return lieu;
        return char.ConvertFromUtf32(0x1F1E6 + (code[0] - 'A')) + char.ConvertFromUtf32(0x1F1E6 + (code[1] - 'A')) + " " + (l.Code != "fr" ? l.TexteOuNull("pays-ue-" + Cles.Slug(lieu)) ?? lieu : lieu);
    }
}
