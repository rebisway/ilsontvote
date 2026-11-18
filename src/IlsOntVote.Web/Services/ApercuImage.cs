using System.Globalization;
using IlsOntVote.Web.Models;
using SkiaSharp;

namespace IlsOntVote.Web.Services;

/// <summary>
/// Image d'aperçu de partage (1200 × 630, format attendu par les réseaux sociaux : balise <c>og:image</c>) d'un scrutin :
/// titre, résultat, décomptes et l'hémicycle réel (mêmes points, mêmes couleurs que le site). Dessinée côté serveur avec SkiaSharp,
/// police Roboto fournie dans <c>Fonts/</c> (jamais celle du système : un serveur Ubuntu minimal n'en a pas).
/// Les couleurs reprennent celles du thème sombre de <c>site.css</c> : une seule palette de vote, partout.
/// </summary>
public sealed class ApercuImage
{
    private const int L = 1200, H = 630, Marge = 60;

    // Palette de vote (thème sombre de site.css) : Pour bleu, Contre rouge, Abstention orange, Non-votant gris, Absent gris foncé.
    private static readonly SKColor Fond = SKColor.Parse("#14171a");
    private static readonly SKColor Encre = SKColor.Parse("#eceef0");
    private static readonly SKColor EncreDouce = SKColor.Parse("#9aa3ad");
    private static readonly SKColor Pour = SKColor.Parse("#5b9bf5");
    private static readonly SKColor Contre = SKColor.Parse("#f0625a");
    private static readonly SKColor Abstention = SKColor.Parse("#f5a43a");
    private static readonly SKColor NonVotant = SKColor.Parse("#7d8794");
    private static readonly SKColor Absent = SKColor.Parse("#3a424a");
    private static readonly SKColor Adopte = SKColor.Parse("#4cc38a");

    private readonly SKTypeface _gras;
    private readonly SKTypeface _normal;

    public ApercuImage(IWebHostEnvironment env)
    {
        var dossier = Path.Combine(env.ContentRootPath, "Fonts");
        // Repli sur la police par défaut si les fichiers manquent : l'image reste produite (texte moins soigné) au lieu d'une erreur 500.
        _gras = Charger(Path.Combine(dossier, "Roboto-Bold.ttf"), SKFontStyle.Bold);
        _normal = Charger(Path.Combine(dossier, "Roboto-Regular.ttf"), SKFontStyle.Normal);
    }

    private static SKTypeface Charger(string chemin, SKFontStyle repli) =>
        (File.Exists(chemin) ? SKTypeface.FromFile(chemin) : null) ?? SKTypeface.FromFamilyName(null, repli);

    /// <summary>Image d'un scrutin : titre, résultat, décomptes, hémicycle.</summary>
    public byte[] Scrutin(Scrutin s, HemicycleVue vue, bool en = false, bool royaumeUni = false)
    {
        using var surface = SKSurface.Create(new SKImageInfo(L, H));
        var c = surface.Canvas;
        c.Clear(Fond);
        Marque(c, 36, royaumeUni);

        var fr = CultureInfo.GetCultureInfo(en ? "en-GB" : "fr-FR");
        Texte(c, $"{Chambres.Nom(s.Chambre, en)} · {s.DateScrutin.ToString("d MMMM yyyy", fr)}", Marge, 132, 26, EncreDouce, _normal);

        // Titre : 3 lignes au plus, « … » si trop long.
        var y = 196f;
        // Croissants face à face (colonne de droite plus haute) : le titre ne dépasse pas la colonne de gauche, sinon il se superpose au dessin.
        var largeurTitre = vue.FaceAFace ? 590f : L - 2 * Marge;
        foreach (var ligne in Couper(s.TitreLangue(en), 46, largeurTitre, 3, _gras))
        {
            Texte(c, ligne, Marge, y, 46, Encre, _gras);
            y += 56;
        }

        // Colonne gauche : résultat + décomptes.
        var adopte = s.Adopte;
        Texte(c, adopte ? (en ? "ADOPTED" : "ADOPTÉ") : (en ? "REJECTED" : "REJETÉ"), Marge, 420, 40, adopte ? Adopte : Contre, _gras);
        Compteur(c, s.NbPour, en ? "for" : "pour", Pour, Marge, 520);
        Compteur(c, s.NbContre, en ? "against" : "contre", Contre, Marge + 190, 520);
        Compteur(c, s.NbAbstentions, "abst.", Abstention, Marge + 380, 520);

        // Colonne droite : l'hémicycle, mêmes coordonnées que le SVG du site (1000 × 520) réduites de moitié.
        // Demi-cercle : repère 1000 x 520 réduit de moitié ; croissants face à face (Communes) : repère propre à la vue, mis à l'échelle pour tenir dans la colonne.
        float echelle = 0.5f, ox = 640, oy = 345;
        if (vue.FaceAFace)
        {
            echelle = Math.Min(540f / (float)vue.LargeurVue, 470f / (float)vue.HauteurVue);
            ox = 620 + (560f - (float)vue.LargeurVue * echelle) / 2;
            oy = 110;
        }
        using var pinceau = new SKPaint { IsAntialias = true };
        var rayon = Math.Max(1.4f, (float)vue.Rayon * echelle);
        foreach (var siege in vue.Sieges)
        {
            pinceau.Color = siege.Position switch
            {
                Pos.Pour => Pour, Pos.Contre => Contre, Pos.Abstention => Abstention, Pos.NonVotant => NonVotant, _ => Absent,
            };
            c.DrawCircle(ox + (float)siege.X * echelle, oy + (float)siege.Y * echelle, rayon, pinceau);
        }
        return Encoder(surface);
    }

    /// <summary>Image générale du site (pages sans aperçu propre) : marque et question.</summary>
    public byte[] Defaut(bool en = false, bool royaumeUni = false)
    {
        using var surface = SKSurface.Create(new SKImageInfo(L, H));
        var c = surface.Canvas;
        c.Clear(Fond);
        Marque(c, 72, royaumeUni);
        Texte(c, en ? "Who voted what?" : "Qui a voté quoi ?", Marge, 330, 84, Encre, _gras);
        if (royaumeUni)
        {
            Texte(c, "How every MP voted in the House of Commons,", Marge, 410, 34, EncreDouce, _normal);
            Texte(c, "division by division. Official data, free software.", Marge, 456, 34, EncreDouce, _normal);
            return Encoder(surface);
        }
        Texte(c, en ? "How every French deputy, senator and MEP" : "Le vote de chaque député, sénateur et eurodéputé,", Marge, 410, 34, EncreDouce, _normal);
        Texte(c, en ? "voted, law by law. Official data, free software." : "texte par texte. Données officielles, code libre.", Marge, 456, 34, EncreDouce, _normal);
        return Encoder(surface);
    }

    /// <summary>Largeur d'un texte en pixels (SkiaSharp 2.88 mesure des glyphes, pas des chaînes).</summary>
    private static float Mesure(SKFont police, string texte)
    {
        using var p = new SKPaint(police);
        return p.MeasureText(texte);
    }

    private static byte[] Encoder(SKSurface surface)
    {
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }

    /// <summary>« Ils ont voté » avec « voté » en rouge, comme dans l'en-tête du site.</summary>
    private void Marque(SKCanvas c, float taille = 36, bool royaumeUni = false)
    {
        using var police = new SKFont(_gras, taille);
        using var p = new SKPaint { IsAntialias = true, Color = Encre };
        // Site britannique : « They voted » (même mise en page, « voted » en rouge).
        var debut = royaumeUni ? "They " : "Ils ont ";
        c.DrawText(debut, Marge, 84, police, p);
        p.Color = Contre;
        c.DrawText(royaumeUni ? "voted" : "voté", Marge + Mesure(police, debut), 84, police, p);
    }

    private void Compteur(SKCanvas c, int? valeur, string libelle, SKColor couleur, float x, float y)
    {
        Texte(c, valeur?.ToString("N0", CultureInfo.GetCultureInfo("fr-FR")) ?? "—", x, y, 60, couleur, _gras);
        Texte(c, libelle, x, y + 34, 26, EncreDouce, _normal);
    }

    private static void Texte(SKCanvas c, string texte, float x, float y, float taille, SKColor couleur, SKTypeface police)
    {
        using var f = new SKFont(police, taille);
        using var p = new SKPaint { IsAntialias = true, Color = couleur };
        c.DrawText(texte, x, y, f, p);
    }

    /// <summary>Coupe un texte en lignes de largeur donnée (au plus <paramref name="maxLignes"/>, la dernière finie par « … »).</summary>
    private static List<string> Couper(string texte, float taille, float largeur, int maxLignes, SKTypeface police)
    {
        using var f = new SKFont(police, taille);
        var lignes = new List<string>();
        var courante = "";
        var mots = texte.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < mots.Length; i++)
        {
            var essai = courante.Length == 0 ? mots[i] : courante + " " + mots[i];
            if (Mesure(f, essai) <= largeur) { courante = essai; continue; }

            if (lignes.Count == maxLignes - 1)
            {
                // Dernière ligne autorisée : on y met le reste, raccourci jusqu'à tenir avec « … ».
                var reste = string.Join(' ', mots.Skip(i - (courante.Length == 0 ? 0 : courante.Split(' ').Length)));
                while (reste.Length > 1 && Mesure(f, reste + "…") > largeur) reste = reste[..^1];
                lignes.Add(reste.TrimEnd() + "…");
                return lignes;
            }
            if (courante.Length > 0) lignes.Add(courante);
            courante = mots[i];
        }
        if (courante.Length > 0) lignes.Add(courante);
        return lignes;
    }
}
