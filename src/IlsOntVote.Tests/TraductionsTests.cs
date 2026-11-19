using System.Text.RegularExpressions;
using IlsOntVote.Web.Models;

namespace IlsOntVote.Tests;

/// <summary>
/// Garde-fous de l'interface multilingue : chaque texte écrit <c>L.T("français", "anglais")</c> dans les pages doit avoir ses DEUX versions (sinon la traduction oubliée ne se voit
/// que dans une langue) et, hors mots courts ou noms propres, deux textes différents (un copier-coller du français passé pour de l'anglais). Les textes qui doivent rester identiques dans
/// les deux langues (noms, sigles, nombres) sont courts.
/// </summary>
public class TraductionsTests
{
    private static readonly Regex Paire = new(@"L\.TH?\(\s*""((?:[^""\\]|\\.)*)""\s*,\s*""((?:[^""\\]|\\.)*)""", RegexOptions.Compiled);

    private static string DossierPages()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !Directory.Exists(Path.Combine(d.FullName, "src", "IlsOntVote.Web", "Pages"))) d = d.Parent;
        Assert.NotNull(d);
        return Path.Combine(d!.FullName, "src", "IlsOntVote.Web", "Pages");
    }

    [Fact]
    public void Chaque_L_T_a_un_francais_et_un_anglais_non_vides_et_differents()
    {
        var erreurs = new List<string>(); var total = 0;
        foreach (var fichier in Directory.EnumerateFiles(DossierPages(), "*.cshtml*", SearchOption.AllDirectories))
        {
            var texte = File.ReadAllText(fichier);
            foreach (Match m in Paire.Matches(texte))
            {
                total++;
                var fr = m.Groups[1].Value.Trim(); var en = m.Groups[2].Value.Trim();
                var ligne = texte[..m.Index].Count(c => c == '\n') + 1;
                if (fr.Length == 0 || en.Length == 0) erreurs.Add($"{Path.GetFileName(fichier)}:{ligne} version vide : « {fr} » / « {en} »");
                else if (fr == en && fr.Length > 25) erreurs.Add($"{Path.GetFileName(fichier)}:{ligne} même texte dans les deux langues (traduction oubliée ?) : « {fr[..Math.Min(60, fr.Length)]} »");
            }
        }
        Assert.True(total > 300, $"Le garde-fou ne trouve que {total} textes : l expression régulière ne suit plus le code.");
        Assert.True(erreurs.Count == 0, string.Join("\n", erreurs));
    }

    [Fact]
    public void Statuts_a_restriction_traduits()
    {
        Assert.Equal("on leave of absence", StatutTexte.Libelle("Leave of Absence", true));
        Assert.Equal("en congé", StatutTexte.Libelle("Leave of Absence", false));
        Assert.Equal("suspendu", StatutTexte.Libelle("Suspended", false));
        Assert.Null(StatutTexte.Libelle(null, true));
    }
}
