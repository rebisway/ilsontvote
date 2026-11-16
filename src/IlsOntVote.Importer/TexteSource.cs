using System.Globalization;

namespace IlsOntVote.Importer;

public sealed record TexteRow(
    string Id, string? TypeCode, string? TypeLibelle, string Titre, string TitreNormalise, string IntituleNormalise,
    string? EtatCode, string? EtatLibelle, string? NumeroLoi, DateOnly? DatePromulgation, DateOnly? DateJo, string? NumeroJo,
    string? UrlLegifrance, string? UrlDossierSenat, string? UrlDossierAn,
    DateOnly? SaisineCcDate, string? SaisineCcPar,
    string? DecisionCcNumero, DateOnly? DecisionCcDate, string? DecisionCcCode, string? DecisionCcLibelle, string? DecisionCcUrl);

/// <summary>
/// Textes de loi (dossiers législatifs) de la base DOSLEG du Sénat, table <c>loi</c> : intitulé, état,
/// promulgation, publication au Journal officiel, saisine et décision du Conseil constitutionnel.
/// Cette base couvre aussi les textes d'origine Assemblée nationale qui ont été transmis au Sénat.
/// </summary>
/// <summary>Décision du Sénat en séance sur un texte : une lecture, avec le résultat (adopté, rejeté…) et sa date.</summary>
public sealed record EtapeSenatRow(string Id, string TexteId, string CodeActe, DateOnly DateActe, string Resultat);

public sealed class TexteSource
{
    /// <summary>
    /// Résultats d'une décision du Sénat (colonne <c>oritxtcod</c> de la table <c>texte</c>) -> libellé affiché. Les codes viennent de la table
    /// <c>oritxt</c> de DOSLEG : 2 « adopté avec modifications par le Sénat », 6 « sans modification », 9 « définitivement », 11 « adopté par le Sénat »,
    /// 8 « rejeté par le Sénat », 27 « rejeté par question préalable », 31 « rejeté par exception d'irrecevabilité ». Le code 34 (« devenu résolution
    /// du Sénat ») est écarté : une résolution n'est pas une loi. Les libellés d'adoption commencent par « adopt », comme ceux de l'Assemblée
    /// (les agrégats du site reconnaissent « adopt% » comme adopté).
    /// </summary>
    private static readonly Dictionary<string, string> ResultatsSenat = new()
    {
        ["2"] = "adopté avec modifications", ["6"] = "adopté sans modification", ["9"] = "adopté définitivement", ["11"] = "adopté",
        ["8"] = "rejeté", ["27"] = "rejeté (question préalable)", ["31"] = "rejeté (exception d'irrecevabilité)",
    };

    /// <summary>
    /// Décisions de séance du Sénat (tables lecture, lecass, texte de DOSLEG) : un texte est rattaché à une lecture (<c>lecassidt</c>), qui
    /// l'est à une loi ; la lecture du Sénat a le code assemblée « S ». La date est celle de la décision (<c>txtoritxtdat</c>).
    /// </summary>
    public IReadOnlyList<EtapeSenatRow> LireDecisionsSenat(string doslegSql)
    {
        var lectures = new Dictionary<string, (string Loi, string Type)>();
        var lecass = new Dictionary<string, string>();          // lecassidt -> lecidt, pour les lectures du Sénat seulement
        var textes = new List<Dictionary<string, string?>>();

        foreach (var (table, l) in SenatSource.LignesCopy(doslegSql, "lecture", "lecass", "texte"))
        {
            switch (table)
            {
                case "lecture" when T(l["lecidt"]) is { } id && T(l["loicod"]) is { } loi && T(l["typleccod"]) is { } type:
                    lectures[id] = (loi, type);
                    break;
                case "lecass" when T(l["codass"]) == "S" && T(l["lecassidt"]) is { } idt && T(l["lecidt"]) is { } lec:
                    lecass[idt] = lec;
                    break;
                case "texte" when T(l["oritxtcod"]) is { } code && ResultatsSenat.ContainsKey(code):
                    textes.Add(l);
                    break;
            }
        }

        var resultat = new List<EtapeSenatRow>();
        foreach (var t in textes)
        {
            if (T(t["lecassidt"]) is not { } idt || !lecass.TryGetValue(idt, out var lecidt) || !lectures.TryGetValue(lecidt, out var lec)) continue;
            if (T(t["texcod"]) is not { } texcod || Date(t["txtoritxtdat"]) is not { } date) continue;
            resultat.Add(new EtapeSenatRow($"SEN-{texcod}", $"DOSLEG-{lec.Loi}", $"SEN{lec.Type}-DEC", date, ResultatsSenat[T(t["oritxtcod"])!]));
        }
        return resultat;
    }

    public IReadOnlyList<TexteRow> LireTextes(string doslegSql)
    {
        var types = new Dictionary<string, (string Libelle, string Denomination)>();
        var etats = new Dictionary<string, string>();
        var decisions = new Dictionary<string, string>();
        var lois = new List<Dictionary<string, string?>>();

        foreach (var (table, l) in SenatSource.LignesCopy(doslegSql, "typloi", "etaloi", "deccoc", "loi"))
        {
            switch (table)
            {
                case "typloi" when T(l["typloicod"]) is { } code:
                    types[code] = (T(l["typloilib"]) ?? code, T(l["typloiden"]) ?? T(l["typloilib"]) ?? code);
                    break;
                case "etaloi" when T(l["etaloicod"]) is { } code:
                    etats[code] = T(l["etaloilib"]) ?? code;
                    break;
                case "deccoc" when T(l["deccoccod"]) is { } code:
                    decisions[code] = T(l["deccoclib"]) ?? code;
                    break;
                case "loi":
                    lois.Add(l);
                    break;
            }
        }

        var resultat = new List<TexteRow>(lois.Count);
        foreach (var l in lois)
        {
            var cod = T(l["loicod"]);
            var intitule = T(l["loiint"]);
            if (cod is null || intitule is null) continue;

            var typeCode = T(l["typloicod"]);
            (string Libelle, string Denomination) type = typeCode is not null && types.TryGetValue(typeCode, out var tp)
                ? tp : ("", "");
            var etatCode = T(l["etaloicod"]);
            var decCode = T(l["deccoccod"]);
            var signet = T(l["signet"]);

            resultat.Add(new TexteRow(
                $"DOSLEG-{cod}", typeCode, string.IsNullOrEmpty(type.Libelle) ? null : type.Libelle,
                $"{type.Denomination} {intitule}".Trim(),
                TitreTexte.CleTexte(type.Denomination, intitule), TitreTexte.CleIntitule(intitule),
                etatCode, etatCode is not null && etats.TryGetValue(etatCode, out var el) ? el : null,
                T(l["numero"]), Date(l["date_loi"]), Date(l["loidatjo"]), T(l["loinumjo"]),
                Legifrance(T(l["url_jo"])),
                signet is null ? null : $"https://www.senat.fr/dossier-legislatif/{signet}.html",
                Https(T(l["url_an"])),
                Date(l["saisine_date"]), T(l["saisine_par"]),
                T(l["num_decision"]), Date(l["date_decision"]), decCode,
                decCode is not null && decisions.TryGetValue(decCode, out var dl) ? dl : null,
                UrlDecisionCc(T(l["num_decision"]), Https(T(l["deccocurl"])))));
        }
        return resultat;
    }

    // Les anciennes adresses Légifrance « affichTexte.do?cidTexte=JORFTEXT… » ont un équivalent stable ;
    // les adresses par numéro NOR (« UnTexteDeJorf.do?numjo=… ») sont conservées telles quelles.
    private static string? Legifrance(string? url)
    {
        url = Https(url);
        if (url is null) return null;
        var m = System.Text.RegularExpressions.Regex.Match(url, @"cidTexte=(JORFTEXT\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return m.Success ? $"https://www.legifrance.gouv.fr/jorf/id/{m.Groups[1].Value.ToUpperInvariant()}" : url;
    }

    // Les anciennes adresses du Conseil constitutionnel (« …/acces-par-date/… ») sont souvent mortes :
    // l'adresse actuelle se reconstruit depuis le numéro de décision (ex. « 2016-741 DC »).
    private static string? UrlDecisionCc(string? numero, string? ancienne)
    {
        if (numero is null) return ancienne;
        var m = System.Text.RegularExpressions.Regex.Match(numero, @"^(\d{4})-(\d+)\s*([A-Za-z]*)");
        return m.Success
            ? $"https://www.conseil-constitutionnel.fr/decision/{m.Groups[1].Value}/{m.Groups[1].Value}{m.Groups[2].Value}{(m.Groups[3].Length > 0 ? m.Groups[3].Value.ToUpperInvariant() : "DC")}.htm"
            : ancienne;
    }

    private static string? T(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? Https(string? url) =>
        url is not null && url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? "https://" + url[7..] : url;

    private static DateOnly? Date(string? s)
    {
        s = T(s);
        return s is not null && s.Length >= 10
            && DateOnly.TryParseExact(s[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : null;
    }
}
