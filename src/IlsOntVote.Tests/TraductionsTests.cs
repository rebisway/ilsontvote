using System.Text.RegularExpressions;
using IlsOntVote.Web.Models;

namespace IlsOntVote.Tests;

/// <summary>
/// Garde-fous de l'interface multilingue. TOUS les textes vivent en base (table <c>traductions</c>, une ligne par langue et par clé, français et anglais compris) ; le code ne contient que
/// des clés : <c>L.T("cle-stable")</c>. La clé est un identifiant qui ne dépend JAMAIS de la phrase. Les tests veillent à : (1) une clé bien formée, (2) AUCUN texte dans le code (l'ancienne
/// forme à trois arguments « clé, français, anglais » est interdite), (3) chaque clé du code a sa ligne française ET sa ligne anglaise dans une migration <c>db/*.sql</c> (sans quoi la page
/// afficherait la clé), (4) les libellés issus de données (chambres, positions) ont aussi leurs lignes.
/// </summary>
public class TraductionsTests
{
    // Appel avec un premier argument littéral : la clé (L.T, L.TH, L.Tf, L.THf ; récepteurs L, langue, l). Les clés construites (« statut-" + … ») ne sont pas vues ici.
    private static readonly Regex Appel = new(@"\b(?:L|langue|l)\.(?:TH?f?)\(\s*""([^""\\]*)""(?!\s*\+)", RegexOptions.Compiled);

    // Ancienne forme : la clé suivie d'un texte littéral (le français) : un texte est revenu dans le code.
    private static readonly Regex TexteDansLeCode = new(@"\b(?:L|langue|l)\.(?:TH?f?)\(\s*""[^""\\]*""\s*,\s*""", RegexOptions.Compiled);

    // Ligne de migration : ('fr', $t$cle$t$, …) ou ('en', $t$cle$t$, …).
    private static readonly Regex LigneBase = new(@"\(\s*(?:'|\$t\$)(fr|en)(?:'|\$t\$)\s*,\s*\$t\$(.+?)\$t\$\s*,", RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex CleValide = new(@"^[a-z0-9][a-z0-9.-]*$", RegexOptions.Compiled);

    private static string Racine()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !Directory.Exists(Path.Combine(d.FullName, "src", "IlsOntVote.Web", "Pages"))) d = d.Parent;
        Assert.NotNull(d);
        return d!.FullName;
    }

    private static IEnumerable<string> FichiersWeb() =>
        Directory.EnumerateFiles(Path.Combine(Racine(), "src", "IlsOntVote.Web"), "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".cs") || f.EndsWith(".cshtml")) && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    private static IEnumerable<(string Fichier, int Ligne, string Cle)> Appels()
    {
        foreach (var fichier in FichiersWeb())
        {
            var texte = File.ReadAllText(fichier);
            foreach (Match m in Appel.Matches(texte))
                yield return (Path.GetFileName(fichier), texte[..m.Index].Count(c => c == '\n') + 1, m.Groups[1].Value);
        }
    }

    /// <summary>Clés ayant une ligne pour la langue donnée dans l'ensemble des migrations.</summary>
    private static HashSet<string> ClesEnBase(string langue)
    {
        var cles = new HashSet<string>();
        foreach (var f in Directory.EnumerateFiles(Path.Combine(Racine(), "db"), "*.sql"))
            foreach (Match m in LigneBase.Matches(File.ReadAllText(f)))
                if (m.Groups[1].Value == langue) cles.Add(m.Groups[2].Value);
        return cles;
    }

    [Fact]
    public void Chaque_cle_est_bien_formee()
    {
        var erreurs = new List<string>(); var total = 0;
        foreach (var (fichier, ligne, cle) in Appels())
        {
            total++;
            if (!CleValide.IsMatch(cle)) erreurs.Add($"{fichier}:{ligne} clé invalide « {cle} » (minuscules, chiffres, points et tirets)");
        }
        Assert.True(total > 600, $"Le garde-fou ne trouve que {total} appels : l'expression régulière ne suit plus le code.");
        Assert.True(erreurs.Count == 0, string.Join("\n", erreurs));
    }

    [Fact]
    public void Aucun_texte_dans_le_code()
    {
        var erreurs = new List<string>();
        foreach (var fichier in FichiersWeb())
        {
            var texte = File.ReadAllText(fichier);
            foreach (Match m in TexteDansLeCode.Matches(texte))
                erreurs.Add($"{Path.GetFileName(fichier)}:{texte[..m.Index].Count(c => c == '\n') + 1} texte écrit dans le code (il doit être en base) : {m.Value}");
        }
        Assert.True(erreurs.Count == 0, string.Join("\n", erreurs));
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("en")]
    public void Chaque_cle_du_code_a_son_texte_en_base(string langue)
    {
        var enBase = ClesEnBase(langue);
        var manquantes = Appels().Select(a => a.Cle).Distinct().Where(c => !enBase.Contains(c)).OrderBy(c => c).ToList();
        Assert.True(manquantes.Count == 0, $"Clés sans ligne « {langue} » dans db/*.sql (ajouter une migration) : {string.Join(", ", manquantes)}");
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("en")]
    public void Les_libelles_de_donnees_ont_leurs_lignes(string langue)
    {
        var enBase = ClesEnBase(langue);
        var attendues = new List<string>();
        foreach (var c in typeof(Chambres).GetFields().Where(f => f.IsLiteral && f.FieldType == typeof(string)).Select(f => (string)f.GetRawConstantValue()!))
            foreach (var k in new[] { "nom", "membre", "membres", "court" }) attendues.Add($"chambre.{c}.{k}");
        foreach (var p in new[] { "pour", "contre", "abstention", "non_votant", "absent" }) attendues.Add($"position.{p}");
        foreach (var s in new[] { "tous", "promulgue", "deux-assemblees", "non-promulgue", "discussion", "rejete", "cc", "censure", "49-3" }) attendues.Add($"statut-{s}");
        var manquantes = attendues.Where(c => !enBase.Contains(c)).ToList();
        Assert.True(manquantes.Count == 0, $"Libellés sans ligne « {langue} » : {string.Join(", ", manquantes)}");
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
