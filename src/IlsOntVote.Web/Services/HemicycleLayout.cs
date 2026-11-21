using IlsOntVote.Web.Models;

namespace IlsOntVote.Web.Services;

/// <summary>Arc de cercle coloré, sous les sièges d'un groupe, avec son étiquette.</summary>
public sealed record ArcGroupe(
    string Sigle, string? Couleur, int Effectif,
    double AngleDebut, double AngleFin, double AngleMilieu)
{
    /// <summary>Un arc trop étroit n'a pas la place de porter son sigle (la légende le remplace).</summary>
    public bool Etiquetable => AngleDebut - AngleFin > 0.11;
}

/// <summary>Un hémicycle prêt à être dessiné : sièges placés + arcs de groupes.</summary>
public sealed record HemicycleVue(IReadOnlyList<Siege> Sieges, double Rayon, IReadOnlyList<ArcGroupe> Arcs,
    string Disposition = "hemicycle", IReadOnlyList<BandeGroupe>? Bandes = null, double LargeurVue = HemicycleLayout.Largeur, double HauteurVue = HemicycleLayout.Hauteur)
{
    public bool FaceAFace => Disposition == "face-a-face";
}

/// <summary>
/// Arc coloré sous un groupe dans la disposition en croissants face à face (équivalent de l'arc d'un hémicycle) : chemin SVG déjà calculé et position de l'étiquette.
/// </summary>
public sealed record BandeGroupe(string Sigle, string? Couleur, int Effectif, string Chemin, double LX, double LY, bool Etiquette = false);

/// <summary>
/// Dispose N points en hémicycle (demi-anneau, rangées concentriques). Les
/// sièges sont attribués de gauche à droite par angle, dans l'ordre reçu :
/// triés par groupe, chaque groupe forme donc un « quartier » radial, comme
/// dans l'hémicycle réel. Repère SVG : viewBox 0 0 <see cref="Largeur"/> × <see cref="Hauteur"/>.
/// </summary>
public static class HemicycleLayout
{
    public const double Largeur = 1000;
    public const double Hauteur = 520;

    // Centre et rayons (unités du viewBox). Les arcs de groupes et les étiquettes sont AU-DELÀ des sièges.
    public const double CentreX = Largeur / 2;
    public const double CentreY = 505;
    public const double RayonMax = 420;
    public const double RayonArc = 438;
    public const double RayonEtiquette = 466;

    public static HemicycleVue Construire(IReadOnlyList<Siege> sieges, string disposition)
        => disposition == "face-a-face" ? ConstruireFaceAFace(sieges) : Construire(sieges);

    // Repère des croissants face à face (viewBox propre à cette disposition, plus haute que large : deux demi-couronnes qui se regardent).
    private const double FaceLargeur = 520, FaceHauteur = 430, FaceCentreY = 215, FaceRayonMax = 168, FaceAllee = 24;

    /// <summary>
    /// Deux croissants face à face (Chambre des Communes) : le gouvernement à gauche, tous les autres à droite, une allée entre les deux et le fauteuil du président en haut.
    /// Chaque côté est une demi-couronne (même principe que l'hémicycle : rangées concentriques, sièges attribués par angle) ; le côté le plus petit a un rayon réduit dans la
    /// proportion de son effectif, pour une densité comparable. Les groupes se suivent du haut vers le bas, le plus gros d'abord (bancs de devant, côté du président) et
    /// forment des quartiers ; une bande colorée en arc, à l'extérieur, marque chaque groupe.
    /// </summary>
    public static HemicycleVue ConstruireFaceAFace(IReadOnlyList<Siege> sieges)
    {
        double centre = FaceLargeur / 2;
        var gauche = sieges.Where(s => s.GroupeGouvernement).ToList();
        var droite = sieges.Where(s => !s.GroupeGouvernement).ToList();
        // Repli : aucun groupe n'est marqué « au gouvernement » (donnée non renseignée) : le plus gros groupe prend le côté gauche, pour ne jamais dessiner un seul croissant.
        // Simple règle de mise en page (le plus gros groupe d'un parlement est en général celui du gouvernement), pas une affirmation : l'admin peut la corriger.
        if (gauche.Count == 0 && droite.Count > 0)
        {
            var plusGros = droite.GroupBy(s => s.GroupeSigle).OrderByDescending(g => g.Count()).First().Key;
            gauche = droite.Where(s => s.GroupeSigle == plusGros).ToList();
            droite = droite.Where(s => s.GroupeSigle != plusGros).ToList();
        }
        int plusGrand = Math.Max(1, Math.Max(gauche.Count, droite.Count));
        double rayonPoint = double.MaxValue;
        var bandes = new List<BandeGroupe>();

        void Cote(List<Siege> cote, bool versGauche)
        {
            int n = cote.Count;
            if (n == 0) return;
            // Rayon proportionnel à la racine de l'effectif : le plus petit côté n'est pas étiré à la taille du plus grand (points trop espacés).
            double rmax = FaceRayonMax * Math.Sqrt((double)n / plusGrand);
            double rmin = rmax * 0.34;
            double cx = versGauche ? centre - FaceAllee : centre + FaceAllee;
            var ordre = cote.GroupBy(s => s.GroupeSigle).OrderByDescending(g => g.Count()).ThenBy(g => g.First().GroupeOrdre).ThenBy(g => g.Key)
                .SelectMany(g => g.OrderBy(s => s.Nom).ThenBy(s => s.Prenom)).ToList();

            int rangees = Math.Max(3, (int)Math.Round(Math.Sqrt(n / 3.4)));
            var rayons = Enumerable.Range(0, rangees).Select(i => rmin + i * (rmax - rmin) / (rangees - 1)).ToArray();
            double somme = rayons.Sum();
            var parRangee = rayons.Select(r => (int)Math.Round(n * r / somme)).ToArray();
            int ecart = n - parRangee.Sum();
            for (int i = rangees - 1; ecart != 0; i = i == 0 ? rangees - 1 : i - 1) { parRangee[i] += Math.Sign(ecart); ecart -= Math.Sign(ecart); }

            // Positions (angle de -90° à +90° : haut vers bas, rayon) triées par angle puis rayon : les groupes, contigus dans « ordre », prennent des quartiers.
            var positions = new List<(double Angle, double Rayon)>(n);
            for (int i = 0; i < rangees; i++)
                for (int j = 0; j < parRangee[i]; j++)
                    positions.Add((parRangee[i] == 1 ? 0 : -Math.PI / 2 + Math.PI * j / (parRangee[i] - 1), rayons[i]));
            positions = positions.OrderBy(p => Math.Round(p.Angle, 6)).ThenBy(p => p.Rayon).ToList();
            double pasRangee = rangees > 1 ? (rmax - rmin) / (rangees - 1) : rmax;
            double pasArc = Math.PI * rmax / Math.Max(1, parRangee[^1] - 1);
            rayonPoint = Math.Min(rayonPoint, Math.Min(7.5, 0.38 * Math.Min(pasRangee, pasArc)));
            var angles = new Dictionary<string, (double Min, double Max, int N, string? Couleur)>();
            for (int i = 0; i < n; i++)
            {
                var (a, r) = positions[i];
                ordre[i].X = Math.Round(versGauche ? cx - r * Math.Cos(a) : cx + r * Math.Cos(a), 1);
                ordre[i].Y = Math.Round(FaceCentreY + r * Math.Sin(a), 1);
                angles[ordre[i].GroupeSigle] = angles.TryGetValue(ordre[i].GroupeSigle, out var g)
                    ? (Math.Min(g.Min, a), Math.Max(g.Max, a), g.N + 1, g.Couleur) : (a, a, 1, ordre[i].GroupeCouleur);
            }

            // Bandes en arc à l'extérieur du croissant, bornes à mi-chemin entre deux groupes voisins ; étiquette seulement si l'arc est assez long et ne recouvre pas la précédente.
            double rBande = rmax + 16, finEtiquette = double.MinValue;
            var blocs = angles.OrderBy(kv => kv.Value.Min).ToList();
            for (int k = 0; k < blocs.Count; k++)
            {
                var b = blocs[k].Value;
                double debut = k == 0 ? -Math.PI / 2 : (blocs[k - 1].Value.Max + b.Min) / 2;
                double fin = k == blocs.Count - 1 ? Math.PI / 2 : (b.Max + blocs[k + 1].Value.Min) / 2;
                debut += 0.012; fin -= 0.012;
                if (fin <= debut) { debut = b.Min; fin = b.Max; }
                double X(double a) => versGauche ? cx - rBande * Math.Cos(a) : cx + rBande * Math.Cos(a);
                double Y(double a) => FaceCentreY + rBande * Math.Sin(a);
                int grand = fin - debut > Math.PI - 1e-6 ? 1 : 0;
                // Sens de tracé : du haut vers le bas ; autour du centre gauche c'est le sens trigonométrique (0), autour du centre droit le sens horaire (1).
                var chemin = FormattableString.Invariant($"M{X(debut):0.#} {Y(debut):0.#}A{rBande:0.#} {rBande:0.#} 0 {grand} {(versGauche ? 0 : 1)} {X(fin):0.#} {Y(fin):0.#}");
                double milieu = (debut + fin) / 2, longueur = (fin - debut) * rBande;
                bool etiquette = longueur > 46 && debut >= finEtiquette + 0.04;
                if (etiquette) finEtiquette = fin;
                double rLabel = rBande + 15;
                bandes.Add(new BandeGroupe(blocs[k].Key, b.Couleur, b.N, chemin,
                    Math.Round(versGauche ? cx - rLabel * Math.Cos(milieu) : cx + rLabel * Math.Cos(milieu), 1), Math.Round(FaceCentreY + rLabel * Math.Sin(milieu), 1), etiquette));
            }
        }
        Cote(gauche, true);
        Cote(droite, false);
        if (rayonPoint == double.MaxValue) rayonPoint = 0;
        // Les arcs servent seulement à associer une couleur à chaque groupe (classes g0, g1… du dessin) : angles nuls, jamais tracés en croissants face à face.
        var arcs = bandes.Select(b => new ArcGroupe(b.Sigle, b.Couleur, b.Effectif, 0, 0, 0)).ToList();
        return new HemicycleVue(sieges, Math.Round(rayonPoint, 1), arcs, "face-a-face", bandes, FaceLargeur, FaceHauteur);
    }

    public static HemicycleVue Construire(IReadOnlyList<Siege> sieges)
    {
        var rayon = Placer(sieges);
        return new HemicycleVue(sieges, rayon, CalculerArcs(sieges));
    }

    /// <summary>Place les sièges (modifie X/Y) et retourne le rayon conseillé d'un point.</summary>
    public static double Placer(IReadOnlyList<Siege> sieges)
    {
        int n = sieges.Count;
        if (n == 0) return 0;

        double rMin = RayonMax * 0.38;

        int rangees = Math.Max(2, (int)Math.Round(Math.Sqrt(n / 4.0)));
        // Petite chambre (< 100 sièges, ex. Conseil des États : 46) : la formule ci-dessus donne trop peu de rangées (3), très espacées entre elles alors que les points d'une même rangée se touchent.
        // On retient le nombre de rangées qui rend les points les PLUS GROS, c'est-à-dire celui où l'écart entre rangées s'approche de l'écart entre points d'une rangée (rangs serrés).
        if (n < 100)
        {
            double meilleur = -1;
            for (int r = 2; r <= 10; r++)
            {
                var ray = Enumerable.Range(0, r).Select(i => rMin + i * (RayonMax - rMin) / (r - 1)).ToArray();
                double sm = ray.Sum();
                int dernier = (int)Math.Round(n * ray[^1] / sm);
                double score = Math.Min((RayonMax - rMin) / (r - 1), Math.PI * RayonMax / Math.Max(1, dernier - 1));
                if (score > meilleur) { meilleur = score; rangees = r; }
            }
        }
        var rayons = Enumerable.Range(0, rangees)
            .Select(i => rMin + i * (RayonMax - rMin) / (rangees - 1))
            .ToArray();

        // Nombre de sièges par rangée, proportionnel au rayon ; l'arrondi est absorbé par les rangées extérieures.
        double somme = rayons.Sum();
        var parRangee = rayons.Select(r => (int)Math.Round(n * r / somme)).ToArray();
        int ecart = n - parRangee.Sum();
        for (int i = rangees - 1; ecart != 0; i = i == 0 ? rangees - 1 : i - 1)
        {
            parRangee[i] += Math.Sign(ecart);
            ecart -= Math.Sign(ecart);
        }

        var positions = new List<(double Angle, double Rayon, double X, double Y)>(n);
        for (int i = 0; i < rangees; i++)
        {
            int k = parRangee[i];
            for (int j = 0; j < k; j++)
            {
                double angle = k == 1 ? Math.PI / 2 : Math.PI * (1 - (double)j / (k - 1));
                positions.Add((angle, rayons[i], CentreX + rayons[i] * Math.Cos(angle), CentreY - rayons[i] * Math.Sin(angle)));
            }
        }

        var ordonnees = positions.OrderByDescending(p => Math.Round(p.Angle, 6)).ThenBy(p => p.Rayon).ToList();
        for (int i = 0; i < n; i++)
        {
            sieges[i].X = Math.Round(ordonnees[i].X, 1);
            sieges[i].Y = Math.Round(ordonnees[i].Y, 1);
        }

        double espaceRangees = (RayonMax - rMin) / Math.Max(1, rangees - 1);
        double espaceArc = Math.PI * RayonMax / Math.Max(1, parRangee[^1] - 1);
        return Math.Round(0.42 * Math.Min(espaceRangees, espaceArc), 1);
    }

    /// <summary>
    /// Un arc par groupe, bornes placées à mi-chemin entre le dernier siège d'un groupe et le premier du suivant.
    /// Les sièges doivent déjà être placés et triés par groupe.
    /// </summary>
    public static IReadOnlyList<ArcGroupe> CalculerArcs(IReadOnlyList<Siege> sieges)
    {
        if (sieges.Count == 0) return [];

        double Angle(Siege s) => Math.Atan2(CentreY - s.Y, s.X - CentreX);

        // Groupes contigus dans l'ordre reçu, avec l'angle extrême de chacun.
        var blocs = new List<(string Sigle, string? Couleur, int Effectif, double Max, double Min)>();
        foreach (var s in sieges)
        {
            var a = Angle(s);
            if (blocs.Count > 0 && blocs[^1].Sigle == s.GroupeSigle)
            {
                var b = blocs[^1];
                blocs[^1] = b with { Effectif = b.Effectif + 1, Max = Math.Max(b.Max, a), Min = Math.Min(b.Min, a) };
            }
            else
            {
                blocs.Add((s.GroupeSigle, s.GroupeCouleur, 1, a, a));
            }
        }

        const double espace = 0.006; // petit blanc entre deux arcs
        var arcs = new List<ArcGroupe>(blocs.Count);
        for (int i = 0; i < blocs.Count; i++)
        {
            double debut = i == 0 ? Math.PI : (blocs[i - 1].Min + blocs[i].Max) / 2;
            double fin = i == blocs.Count - 1 ? 0 : (blocs[i].Min + blocs[i + 1].Max) / 2;
            debut = Math.Min(Math.PI, debut) - espace;
            fin = Math.Max(0, fin) + espace;
            if (fin >= debut) { debut = blocs[i].Max; fin = blocs[i].Min; }
            arcs.Add(new ArcGroupe(blocs[i].Sigle, blocs[i].Couleur, blocs[i].Effectif, debut, fin, (debut + fin) / 2));
        }
        return arcs;
    }

    public static IReadOnlyList<GroupeDecompte> DecompteParGroupe(IEnumerable<Siege> sieges) =>
        sieges
            .GroupBy(s => (s.GroupeSigle, s.GroupeCouleur, s.GroupeOrdre))
            .OrderBy(g => g.Key.GroupeOrdre).ThenBy(g => g.Key.GroupeSigle)
            .Select(g => new GroupeDecompte(
                g.Key.GroupeSigle, g.Key.GroupeCouleur, g.Key.GroupeOrdre,
                g.Count(s => s.Position == Pos.Pour),
                g.Count(s => s.Position == Pos.Contre),
                g.Count(s => s.Position == Pos.Abstention),
                g.Count(s => s.Position == Pos.NonVotant),
                g.Count(s => s.Position == Pos.Absent)))
            .ToList();
}
