using IlsOntVote.Importer;

namespace IlsOntVote.Tests;

/// <summary>Lecture des CSV de pétitions (Sénat et Assemblée) : formats différents, soutiens manquants, champs multilignes.</summary>
public class PetitionsTests
{
    private static string Ecrire(string contenu)
    {
        var chemin = Path.Combine(Path.GetTempPath(), $"petitions-{Guid.NewGuid():N}.csv");
        File.WriteAllText(chemin, contenu);
        return chemin;
    }

    [Fact]
    public void Assemblee_soutiens_decimaux_ou_vides_et_identifiant_prefixe()
    {
        // « 425.0 » observé dans le fichier de l'Assemblée ; un nombre vide est un manque (NULL), jamais 0.
        var f = Ecrire("titre;description;date_publication;nb_votes;statut;date_limite_vote;commission;legislature;decision_commission;url;identifiant\n" +
                       "A;d;2024-01-02;425.0;ouverte;2024-06-01;Commission des lois;;;https://x/i-9;9\n" +
                       "B;d;2024-01-03;;classee;;;2022-2024;Décision;https://x/i-10;10\n");
        var l = PetitionsSource.Lire(f, PetitionsSource.Assemblee);
        Assert.Equal(2, l.Count);
        Assert.Equal("AN-9", l[0].Id);
        Assert.Equal(425, l[0].NbVotes);
        Assert.Null(l[1].NbVotes);
        Assert.Equal("AN", l[1].Chambre);
        Assert.Equal("Décision", l[1].DecisionCommission);
        Assert.Null(l[1].Type);
    }

    [Fact]
    public void Senat_identifiant_sans_prefixe_et_type()
    {
        var f = Ecrire("titre;description;date_publication;nb_votes;statut;date_limite_vote;type;url;identifiant\n" +
                       "T;\"ligne 1\nligne 2; avec point-virgule\";2021-09-10;122484;Seuil atteint;2021-11-23;Texte législatif;https://x/i-742;742\n");
        var l = PetitionsSource.Lire(f, PetitionsSource.Senat);
        Assert.Single(l);
        Assert.Equal("742", l[0].Id);
        Assert.Equal(122484, l[0].NbVotes);
        Assert.Contains("point-virgule", l[0].Description);
        Assert.Equal("Texte législatif", l[0].Type);
    }

    [Fact]
    public void Colonne_obligatoire_absente_leve_une_erreur_claire()
    {
        var f = Ecrire("titre;statut\nA;ouverte\n");
        Assert.Throws<InvalidDataException>(() => PetitionsSource.Lire(f, PetitionsSource.Senat));
    }
}
