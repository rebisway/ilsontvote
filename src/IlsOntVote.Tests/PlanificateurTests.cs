using IlsOntVote.Importer;

namespace IlsOntVote.Tests;

/// <summary>Calcul des créneaux du planning d'imports (heure de Paris, jours ISO, rattrapage borné).</summary>
public class PlanificateurTests
{
    private static readonly short[] TousLesJours = [1, 2, 3, 4, 5, 6, 7];
    private static readonly short[] Mercredi = [3];

    private static DateTime Utc(int a, int m, int j, int h, int mi = 0) => new(a, m, j, h, mi, 0, DateTimeKind.Utc);

    [Fact]
    public void Creneau_passe_et_non_lance_est_du()
    {
        // 18 novembre 2026 (mercredi), 04:30 à Paris = 03:30 UTC (heure d'hiver) ; il est 03:31 UTC.
        var du = Planificateur.CreneauDu(TousLesJours, new TimeSpan(4, 30, 0), Utc(2026, 11, 17, 3, 31), Utc(2026, 11, 18, 3, 31));
        Assert.Equal(Utc(2026, 11, 18, 3, 30), du);
    }

    [Fact]
    public void Creneau_pas_encore_arrive_n_est_pas_du()
    {
        Assert.Null(Planificateur.CreneauDu(TousLesJours, new TimeSpan(4, 30, 0), Utc(2026, 11, 17, 3, 31), Utc(2026, 11, 18, 3, 29)));
    }

    [Fact]
    public void Creneau_deja_lance_n_est_pas_rejoue()
    {
        Assert.Null(Planificateur.CreneauDu(TousLesJours, new TimeSpan(4, 30, 0), Utc(2026, 11, 18, 3, 31), Utc(2026, 11, 18, 3, 45)));
    }

    [Fact]
    public void Jour_de_semaine_respecte_et_dimanche_vaut_7()
    {
        // Mercredi 18 novembre 2026 : un créneau « mercredi 15:00 » est dû à 14:01 Paris (= 13:01 UTC) ; pas un jeudi.
        Assert.NotNull(Planificateur.CreneauDu(Mercredi, new TimeSpan(15, 0, 0), Utc(2026, 11, 18, 0, 0), Utc(2026, 11, 18, 14, 1)));
        Assert.Null(Planificateur.CreneauDu(Mercredi, new TimeSpan(15, 0, 0), Utc(2026, 11, 18, 0, 0), Utc(2026, 11, 19, 14, 1)));
        // Dimanche 22 novembre 2026 : jour ISO 7.
        Assert.NotNull(Planificateur.CreneauDu([7], new TimeSpan(10, 0, 0), Utc(2026, 11, 21, 0, 0), Utc(2026, 11, 22, 10, 0)));
    }

    [Fact]
    public void Creneau_trop_ancien_est_abandonne()
    {
        // Créneau de 04:30 non lancé, constaté à 20:00 le même jour (plus de 12 h) : abandonné.
        Assert.Null(Planificateur.CreneauDu(TousLesJours, new TimeSpan(4, 30, 0), Utc(2026, 11, 17, 3, 31), Utc(2026, 11, 18, 19, 0)));
    }

    [Fact]
    public void Creneau_de_la_veille_au_soir_est_rattrape_apres_minuit()
    {
        // « Tous les jours 23:30 » : à 00:10 Paris le lendemain (23:10 UTC), le créneau de la veille (22:30 UTC) reste dû s'il n'a pas été lancé.
        var du = Planificateur.CreneauDu(TousLesJours, new TimeSpan(23, 30, 0), Utc(2026, 11, 17, 12, 0), Utc(2026, 11, 17, 23, 10));
        Assert.Equal(Utc(2026, 11, 17, 22, 30), du);
    }
}
