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
public sealed record HemicycleVue(IReadOnlyList<Siege> Sieges, double Rayon, IReadOnlyList<ArcGroupe> Arcs);

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
