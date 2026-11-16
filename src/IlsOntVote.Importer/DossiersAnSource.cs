using System.Text.Json;
using System.Text.RegularExpressions;

namespace IlsOntVote.Importer;

/// <summary>Une décision prise en séance publique à l'Assemblée sur un texte (lecture), avec ou sans scrutin public (table texte_etapes).</summary>
public sealed record EtapeRow(
    string Id, string DossierAn, string CodeActe, DateOnly DateActe, string? Resultat, string? ScrutinId, bool SansScrutinPublic,
    string? SenatChemin = null,
    bool Article493 = false);

/// <summary>
/// Jeu « Dossiers législatifs » de l'Assemblée nationale : pour chaque dossier, ses actes. On ne retient que les actes « Décision »
/// d'une lecture en séance publique (<c>AN1-DEBATS-DEC</c>, <c>AN2-…</c>, <c>ANLUNI-…</c>, <c>ANNLEC-…</c>, <c>ANLDEF-…</c>) :
/// - avec <c>voteRefs</c> : décision prise par scrutin public (le détail « qui a voté quoi » existe, c'est un scrutin déjà importé) ;
/// - sans <c>voteRefs</c> : décision SANS scrutin public (main levée, ou adoption sans vote nominatif) : jamais omise, affichée avec la
///   mention « détail non disponible » (règle du projet).
/// Les décisions de commission (<c>…-COM-…</c>) et celles du Sénat (<c>SN…</c>, dont les scrutins ne sont pas dans ce jeu : on ne pourrait
/// pas dire s'il y a eu scrutin public) sont ignorées.
/// </summary>
public static partial class DossiersAnSource
{
    // Décisions de séance à l'Assemblée : lectures (AN1, AN2, ANLUNI, ANNLEC, ANLDEF) et texte de la CMP lu à l'Assemblée (CMP-DEBATS-AN-DEC).
    [GeneratedRegex(@"^(AN[A-Z0-9]*-DEBATS-DEC|CMP-DEBATS-AN-DEC)$")]
    private static partial Regex CodeDecisionSeance();

    // Motion de censure liée à un texte (article 49, alinéa 3) : l'acte « …-MOTION-VOTE » renvoie vers un SCRUTIN PUBLIC.
    [GeneratedRegex(@"^[A-Z0-9]+-MOTION-VOTE$")]
    private static partial Regex CodeMotion();

    /// <summary>Adresse du zip pour une législature (la 15e a un suffixe « _XV » dans le nom du fichier).</summary>
    public static string Url(int legislature) =>
        $"https://data.assemblee-nationale.fr/static/openData/repository/{legislature}/loi/dossiers_legislatifs/Dossiers_Legislatifs{(legislature == 15 ? "_XV" : "")}.json.zip";

    /// <summary>Lit tous les dossiers d'un dossier de fichiers JSON extraits et renvoie leurs décisions de séance publique.</summary>
    public static IEnumerable<EtapeRow> Lire(string dossier)
    {
        foreach (var fichier in Directory.EnumerateFiles(dossier, "DLR5L*.json", SearchOption.AllDirectories))
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(fichier));
            if (!doc.RootElement.TryGetProperty("dossierParlementaire", out var d)) continue;
            var dossierId = d.TryGetProperty("uid", out var u) ? u.GetString() : null;
            if (string.IsNullOrEmpty(dossierId)) continue;

            // Dossier du Sénat cité par le dossier AN (« …/pjl24-123.html » -> « pjl24-123 ») : égal au signet que porte l'adresse
            // du dossier Sénat de nos textes (56 dossiers sur 168 non rattachés autrement, tous retrouvés).
            string? senat = null;
            if (d.TryGetProperty("titreDossier", out var td) && td.ValueKind == JsonValueKind.Object
                && td.TryGetProperty("senatChemin", out var sc) && sc.ValueKind == JsonValueKind.String)
            {
                var chemin = sc.GetString()!.TrimEnd('/');
                senat = chemin[(chemin.LastIndexOf('/') + 1)..].Replace(".html", "");
                if (senat.Length == 0) senat = null;
            }

            var trouvees = new List<EtapeRow>();
            Parcourir(d, dossierId, trouvees);
            foreach (var e in trouvees) yield return e with { SenatChemin = senat };
        }
    }

    /// <summary>Descend dans tout le dossier (les actes sont imbriqués : un acte « Discussion » contient ses sous-actes dont la « Décision »).</summary>
    private static void Parcourir(JsonElement e, string dossierId, List<EtapeRow> sortie)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                if (e.TryGetProperty("codeActe", out var code) && code.ValueKind == JsonValueKind.String
                    && (CodeDecisionSeance().IsMatch(code.GetString()!) || CodeMotion().IsMatch(code.GetString()!)))
                {
                    var etape = Lire(e, dossierId, code.GetString()!);
                    if (etape is not null) sortie.Add(etape);
                }
                foreach (var p in e.EnumerateObject()) Parcourir(p.Value, dossierId, sortie);
                break;
            case JsonValueKind.Array:
                foreach (var x in e.EnumerateArray()) Parcourir(x, dossierId, sortie);
                break;
        }
    }

    private static EtapeRow? Lire(JsonElement acte, string dossierId, string code)
    {
        var uid = acte.TryGetProperty("uid", out var u) ? u.GetString() : null;
        var date = acte.TryGetProperty("dateActe", out var da) && da.ValueKind == JsonValueKind.String ? da.GetString() : null;
        // « 2026-01-20T00:00:00.000+01:00 » : seul le jour compte (pas de conversion de fuseau, qui pourrait le décaler).
        if (uid is null || date is null || date.Length < 10 || !DateOnly.TryParse(date[..10], out var jour)) return null;

        string? resultat = null;
        if (acte.TryGetProperty("statutConclusion", out var sc) && sc.ValueKind == JsonValueKind.Object
            && sc.TryGetProperty("libelle", out var lib) && lib.ValueKind == JsonValueKind.String)
            resultat = lib.GetString();

        // voteRefs : null (aucun scrutin), { "voteRef": "VT…" } ou { "voteRef": [ "VT…", … ] } (plusieurs votes : on retient le dernier,
        // le plus proche de la décision finale).
        string? scrutin = null;
        var avecScrutin = false;
        if (acte.TryGetProperty("voteRefs", out var vr) && vr.ValueKind == JsonValueKind.Object && vr.TryGetProperty("voteRef", out var r))
        {
            avecScrutin = true;
            scrutin = r.ValueKind switch
            {
                JsonValueKind.String => r.GetString(),
                JsonValueKind.Array => r.EnumerateArray().LastOrDefault().GetString(),
                _ => null,
            };
        }
        // Article 49-3 : « considéré comme adopté (ou rejeté) par l'Assemblée nationale en application de l'article 49, alinéa 3 » -> pas de vote
        // sur le texte (ce n'est pas une main levée). Une motion de censure a toujours un scrutin public : jamais « sans scrutin public ».
        var motion = CodeMotion().IsMatch(code);
        var article493 = resultat is not null && resultat.Contains("article 49", StringComparison.OrdinalIgnoreCase);
        return new EtapeRow(uid, dossierId, code, jour, resultat, scrutin, SansScrutinPublic: !avecScrutin && !motion && !article493, Article493: article493);
    }
}
