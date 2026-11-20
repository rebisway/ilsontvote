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

    /// <summary>
    /// Image d'un scrutin : marque, titre, hémicycle et décomptes, TOUT CENTRÉ. Pourquoi : WhatsApp, Telegram et d'autres messageries rognent l'image en carré autour de son centre
    /// (constaté : « They voted » et « Who voted what? » coupés sur les côtés avec une mise en page à gauche). Tout ce qui compte tient donc dans la bande centrale de 630 px
    /// (x de 285 à 915) ; les côtés de l'image 1200 × 630 ne portent rien d'essentiel.
    /// </summary>
    public byte[] Scrutin(Scrutin s, HemicycleVue vue, bool en = false, string? pays = null)
    {
        using var surface = SKSurface.Create(new SKImageInfo(L, H));
        var c = surface.Canvas;
        c.Clear(Fond);
        MarqueCentree(c, 40, 60, pays is not null);

        var fr = CultureInfo.GetCultureInfo(en ? "en-GB" : "fr-FR");
        TexteCentre(c, $"{Chambres.Nom(s.Chambre, en)} · {s.DateScrutin.ToString("d MMMM yyyy", fr)}", 104, 24, EncreDouce, _normal);

        // Titre : 3 lignes au plus (2 pour les croissants, plus hauts), centré, dans la bande centrale.
        var y = 156f;
        foreach (var ligne in Couper(s.TitreLangue(en), 38, Bande, vue.FaceAFace ? 2 : 3, _gras))
        {
            TexteCentre(c, ligne, y, 38, Encre, _gras);
            y += 46;
        }

        // Décomptes en bas (une ligne centrée) : résultat, pour, contre, abstentions.
        const float yBas = 596;
        var adopte = s.Adopte;
        var segments = new List<(string Texte, SKColor Couleur, SKTypeface Police)>
        {
            (adopte ? (en ? "ADOPTED" : "ADOPTÉ") : (en ? "REJECTED" : "REJETÉ"), adopte ? Adopte : Contre, _gras),
            ("   ", Encre, _normal),
            ($"{s.NbPour?.ToString("N0", fr) ?? "—"} {(en ? "for" : "pour")}", Pour, _gras),
            ("  ", Encre, _normal),
            ($"{s.NbContre?.ToString("N0", fr) ?? "—"} {(en ? "against" : "contre")}", Contre, _gras),
            ("  ", Encre, _normal),
            ($"{s.NbAbstentions?.ToString("N0", fr) ?? "—"} abst.", Abstention, _gras),
        };
        var total = segments.Sum(g => { using var f = new SKFont(g.Police, 28); return Mesure(f, g.Texte); });
        var x = (L - total) / 2;
        foreach (var g in segments)
        {
            Texte(c, g.Texte, x, yBas, 28, g.Couleur, g.Police);
            using var f = new SKFont(g.Police, 28);
            x += Mesure(f, g.Texte);
        }

        // Hémicycle : mêmes coordonnées que le SVG du site, mis à l'échelle pour tenir entre le titre et les décomptes, centré.
        var haut = y + 16;
        var dispo = 548 - haut;
        var echelle = Math.Min(Bande / (float)vue.LargeurVue, dispo / (float)vue.HauteurVue);
        var ox = (L - (float)vue.LargeurVue * echelle) / 2;
        using var pinceau = new SKPaint { IsAntialias = true };
        var rayon = Math.Max(1.4f, (float)vue.Rayon * echelle);
        foreach (var siege in vue.Sieges)
        {
            pinceau.Color = siege.Position switch
            {
                Pos.Pour => Pour, Pos.Contre => Contre, Pos.Abstention => Abstention, Pos.NonVotant => NonVotant, _ => Absent,
            };
            c.DrawCircle(ox + (float)siege.X * echelle, haut + (float)siege.Y * echelle, rayon, pinceau);
        }
        return Encoder(surface);
    }

    /// <summary>Largeur de la bande centrale qui survit au rognage carré des messageries.</summary>
    private const float Bande = 600;

    /// <summary>
    /// Image générale du site (pages sans aperçu propre) : marque, question et un petit hémicycle de points, centrés (voir <see cref="Scrutin"/> : rognage carré des messageries).
    /// Le dessin est un exemple fixe (pas un vrai vote) : il montre à quoi ressemble le site.
    /// </summary>
    public byte[] Defaut(bool en = false, string? pays = null)
    {
        using var surface = SKSurface.Create(new SKImageInfo(L, H));
        var c = surface.Canvas;
        c.Clear(Fond);
        MarqueCentree(c, 76, 112, pays is not null);
        TexteCentre(c, en ? "Who voted what?" : "Qui a voté quoi ?", 196, 60, Encre, _gras);
        var phrase = pays == "GB"
            ? "How every MP and peer voted in Parliament, division by division. Official data, free software."
            : pays == "US"
            ? "How every Representative and Senator voted in Congress, roll call by roll call. Official data, free software."
            : pays == "IT"
            ? "How every Italian deputy voted, final vote by final vote. Official data, free software."
            : en ? "How every French deputy, senator and MEP voted, law by law. Official data, free software."
                 : "Le vote de chaque député, sénateur et eurodéputé, texte par texte. Données officielles, code libre.";
        var y = 250f;
        foreach (var ligne in Couper(phrase, 28, Bande, 3, _normal)) { TexteCentre(c, ligne, y, 28, EncreDouce, _normal); y += 36; }

        // Demi-cercle d'exemple : rangées de points colorées par secteurs (pour, contre, abstention, absents), centre en bas de l'image.
        using var pinceau = new SKPaint { IsAntialias = true };
        const float cx = L / 2f, cy = 618;
        for (var rang = 0; rang < 6; rang++)
        {
            var r = 150f + rang * 24f;
            var n = (int)(Math.PI * r / 16.5f);
            for (var i = 0; i < n; i++)
            {
                var angle = Math.PI - Math.PI * (i + 0.5) / n;
                var part = (double)i / n;
                // Secteurs fixes + quelques points « absents » répartis : lisible dès la miniature.
                pinceau.Color = (i * 7 + rang * 3) % 11 == 0 ? Absent : part < 0.46 ? Pour : part < 0.58 ? Abstention : part < 0.92 ? Contre : Pour;
                c.DrawCircle(cx + (float)(r * Math.Cos(angle)), cy - (float)(r * Math.Sin(angle)), 6f, pinceau);
            }
        }
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

    /// <summary>« Ils ont voté » (ou « They voted ») centré, « voté » en rouge comme dans l'en-tête du site.</summary>
    private void MarqueCentree(SKCanvas c, float taille, float y, bool royaumeUni)
    {
        using var police = new SKFont(_gras, taille);
        using var p = new SKPaint { IsAntialias = true, Color = Encre };
        var debut = royaumeUni ? "They " : "Ils ont ";
        var fin = royaumeUni ? "voted" : "voté";
        var x = (L - Mesure(police, debut + fin)) / 2;
        c.DrawText(debut, x, y, police, p);
        p.Color = Contre;
        c.DrawText(fin, x + Mesure(police, debut), y, police, p);
    }

    private void TexteCentre(SKCanvas c, string texte, float y, float taille, SKColor couleur, SKTypeface police)
    {
        using var f = new SKFont(police, taille);
        Texte(c, texte, (L - Mesure(f, texte)) / 2, y, taille, couleur, police);
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
