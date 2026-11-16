using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace IlsOntVote.Importer;

/// <summary>
/// Clés de rapprochement entre le titre d'un scrutin (« l'ensemble du projet de loi X (première lecture) »)
/// et l'intitulé d'un texte du dossier législatif (« projet de loi » + « X »). Volontairement simple et
/// déterministe : égalité de deux chaînes normalisées, jamais de « presque pareil » (un faux rattachement
/// attribuerait le vote d'un texte à un autre). En cas de doute, le scrutin reste sans texte.
/// </summary>
public static partial class TitreTexte
{
    private const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    [GeneratedRegex(@"^sur\s+", Opt)] private static partial Regex Sur();
    [GeneratedRegex(@"^l['’]ensemble\s+(du|de la|des|de l['’])\s*", Opt)] private static partial Regex Ensemble();
    [GeneratedRegex(@"^texte\s+(?:[ée]labor[ée]\s+par\s+la|de\s+la)\s+commission\s+mixte\s+paritaire\s+(sur\s+)?(le\s+|la\s+|les\s+)?", Opt)] private static partial Regex Cmp();
    [GeneratedRegex(@"^(premi[èe]re|deuxi[èe]me|seconde|troisi[èe]me)\s+partie\s+(du|de la)\s+", Opt)] private static partial Regex Partie();
    [GeneratedRegex(@"^(le|la|les)\s+(?=(projet|proposition)\s)", Opt)] private static partial Regex Article();
    [GeneratedRegex(
        @",?\s*(adopt[ée]e?|modifi[ée]e?)\s+(avec modifications\s+)?(par\s+l['’]assembl[ée]e\s+nationale|par\s+le\s+s[ée]nat)" +
        @"(\s+(en\s+nouvelle\s+lecture|apr[èe]s\s+engagement\s+de\s+la\s+proc[ée]dure\s+acc[ée]l[ée]r[ée]e|apr[èe]s\s+d[ée]claration\s+d['’]urgence|en\s+premi[èe]re\s+lecture|en\s+lecture\s+d[ée]finitive))*\s*,?", Opt)]
    private static partial Regex Transmission();
    [GeneratedRegex(@"\s+dans\s+la\s+r[ée]daction\s+(du\s+texte\s+)?(propos[ée]e?\s+)?(par\s+la\s+commission.*|modifi[ée].*)$", Opt)] private static partial Regex Redaction();
    [GeneratedRegex(@"\s+modifi[ée]\s+par\s+les\s+amendements.*$", Opt)] private static partial Regex Amendements();
    [GeneratedRegex(@"\bde\s+organique\b", Opt)] private static partial Regex DeOrganique();
    [GeneratedRegex(@"\s*\([^)]*\)\s*\.?\s*$", Opt)] private static partial Regex ParentheseFinale();
    [GeneratedRegex(@"[^a-z0-9]+")] private static partial Regex NonAlnum();

    /// <summary>Clé d'un titre de scrutin.</summary>
    public static string CleScrutin(string titre)
    {
        var t = Sur().Replace(titre.Trim(), "");
        t = Ensemble().Replace(t, "");
        t = Cmp().Replace(t, "");
        t = Partie().Replace(t, "");
        t = Article().Replace(t, "");
        t = Transmission().Replace(t, " ");
        t = Redaction().Replace(t, "");
        t = Amendements().Replace(t, "");
        t = DeOrganique().Replace(t.Replace("œ", "oe").Replace("Œ", "oe").Replace("\u009c", "oe"), "organique");
        t = ParentheseFinale().Replace(t, "");
        t = ParentheseFinale().Replace(t, "");
        return Normaliser(t.TrimEnd('.'));
    }

    [GeneratedRegex(@"\b(projet|proposition) de loi\b.*$", Opt)] private static partial Regex TexteCite();

    /// <summary>
    /// Clé du texte cité à la fin de l'intitulé d'un scrutin qui n'est pas un vote sur l'ensemble (« l'amendement n° 12 … à l'article 7 de la
    /// proposition de loi visant à … (première lecture). ») ; null si l'intitulé ne cite aucun projet ni proposition de loi.
    /// </summary>
    public static string? CleTexteCite(string titre)
    {
        var m = TexteCite().Match(titre);
        return m.Success ? CleScrutin(m.Value) : null;
    }

    /// <summary>Clé d'un texte : type (« projet de loi ») + intitulé (« relatif à… »).</summary>
    public static string CleTexte(string typeLibelle, string intitule) => Normaliser($"{typeLibelle} {intitule}");

    public static string CleIntitule(string intitule) => Normaliser(intitule);

    private static string Normaliser(string s)
    {
        s = s.ToLowerInvariant().Replace("œ", "oe").Replace("\u009c", "oe").Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return NonAlnum().Replace(sb.ToString(), " ").Trim();
    }
}
