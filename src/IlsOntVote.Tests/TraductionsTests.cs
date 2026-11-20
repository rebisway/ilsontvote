using System.Text.RegularExpressions;
using IlsOntVote.Web.Models;

namespace IlsOntVote.Tests;

/// <summary>
/// Garde-fous de l'interface multilingue. Chaque texte s'écrit <c>L.T("cle-stable", "français", "anglais")</c> : la clé est un identifiant qui ne dépend JAMAIS de la phrase (l'ancien
/// système prenait le français comme clé : retoucher une phrase cassait ses traductions sans erreur ni alerte). Les tests veillent à : (1) une clé bien formée, (2) un français et un anglais
/// non vides et, hors mots courts ou noms propres, différents, (3) UNE clé = UN couple (français, anglais) dans tout le code (sinon la même traduction s'appliquerait à deux phrases
/// différentes), (4) aucun appel à l'ancienne forme à deux arguments.
/// </summary>
public class TraductionsTests
{
    // Appel à trois arguments littéraux : clé, français, anglais (L.T, L.TH, L.Tf, L.THf ; récepteurs L, langue, l).
    private static readonly Regex Appel = new(
        @"\b(?:L|langue|l)\.(?:TH?f?)\(\s*""([^""\\]*)""\s*,\s*""((?:[^""\\]|\\.)*)""\s*,\s*""((?:[^""\\]|\\.)*)""", RegexOptions.Compiled);

    // Ancienne forme : deux littéraux seulement (le français puis l'anglais), sans clé.
    private static readonly Regex AncienneForme = new(
        @"\b(?:L|langue|l)\.(?:TH?)\(\s*""(?:[^""\\]|\\.)*""\s*,\s*""(?:[^""\\]|\\.)*""\s*\)", RegexOptions.Compiled);

    private static readonly Regex CleValide = new(@"^[a-z0-9][a-z0-9.-]*$", RegexOptions.Compiled);

    private static string DossierPages()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !Directory.Exists(Path.Combine(d.FullName, "src", "IlsOntVote.Web", "Pages"))) d = d.Parent;
        Assert.NotNull(d);
        return Path.Combine(d!.FullName, "src", "IlsOntVote.Web", "Pages");
    }

    private static IEnumerable<(string Fichier, int Ligne, string Cle, string Fr, string En)> Appels()
    {
        foreach (var fichier in Directory.EnumerateFiles(DossierPages(), "*.cshtml*", SearchOption.AllDirectories))
        {
            var texte = File.ReadAllText(fichier);
            foreach (Match m in Appel.Matches(texte))
                yield return (Path.GetFileName(fichier), texte[..m.Index].Count(c => c == '\n') + 1, m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value);
        }
    }

    private static string Court(string t) => t.Length <= 60 ? t : t[..60] + "…";

    [Fact]
    public void Chaque_appel_a_une_cle_valide_un_francais_et_un_anglais_non_vides_et_differents()
    {
        var erreurs = new List<string>(); var total = 0;
        foreach (var (fichier, ligne, cle, frBrut, enBrut) in Appels())
        {
            total++;
            var fr = frBrut.Trim(); var en = enBrut.Trim();
            if (!CleValide.IsMatch(cle)) erreurs.Add($"{fichier}:{ligne} clé invalide « {cle} » (minuscules, chiffres, points et tirets)");
            if (fr.Length == 0 || en.Length == 0) erreurs.Add($"{fichier}:{ligne} [{cle}] version vide : « {fr} » / « {en} »");
            else if (fr == en && fr.Length > 25) erreurs.Add($"{fichier}:{ligne} [{cle}] même texte dans les deux langues (traduction oubliée ?) : « {Court(fr)} »");
        }
        Assert.True(total > 600, $"Le garde-fou ne trouve que {total} textes : l'expression régulière ne suit plus le code.");
        Assert.True(erreurs.Count == 0, string.Join("\n", erreurs));
    }

    [Fact]
    public void Une_cle_designe_toujours_le_meme_francais_et_le_meme_anglais()
    {
        // Même clé, deux phrases : la traduction enregistrée s'appliquerait aux deux. Même phrase, deux clés : toléré (même français, contexte différent), mais pas l'inverse.
        var vus = new Dictionary<string, (string Fr, string En, string Ou)>();
        var erreurs = new List<string>();
        foreach (var (fichier, ligne, cle, fr, en) in Appels())
        {
            if (vus.TryGetValue(cle, out var v))
            {
                if (v.Fr != fr || v.En != en) erreurs.Add($"clé « {cle} » : {v.Ou} dit « {Court(v.Fr)} » / « {Court(v.En)} » mais {fichier}:{ligne} dit « {Court(fr)} » / « {Court(en)} »");
            }
            else vus[cle] = (fr, en, $"{fichier}:{ligne}");
        }
        Assert.True(erreurs.Count == 0, string.Join("\n", erreurs));
    }

    [Fact]
    public void Aucun_appel_a_l_ancienne_forme_sans_cle()
    {
        var erreurs = new List<string>();
        foreach (var fichier in Directory.EnumerateFiles(DossierPages(), "*.cshtml*", SearchOption.AllDirectories))
        {
            var texte = File.ReadAllText(fichier);
            foreach (Match m in AncienneForme.Matches(texte))
                erreurs.Add($"{Path.GetFileName(fichier)}:{texte[..m.Index].Count(c => c == '\n') + 1} appel sans clé : {Court(m.Value)}");
        }
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
