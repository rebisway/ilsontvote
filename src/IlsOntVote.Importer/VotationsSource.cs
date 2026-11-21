using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Dapper;
using Npgsql;

namespace IlsOntVote.Importer;

/// <summary>Une votation populaire fédérale (une ligne du jeu Swissvotes). Résultats AGRÉGÉS : jamais de vote individuel.</summary>
public sealed record VotationCh(
    int Anr, DateOnly Date, string TitreFr, string TitreDe, string? TitreEn, int Rechtsform, bool? Accepte, bool? PeupleAccepte, bool? CantonsAccepte,
    decimal? Participation, decimal? OuiPct, int? Oui, int? Non, decimal? CantonsOui, decimal? CantonsNon, string? OuiParCanton, string? Url);

/// <summary>
/// Votations populaires fédérales suisses : jeu de données Swissvotes (Année Politique Suisse, Université de Berne), licence CC BY 4.0 (attribution obligatoire, affichée sur le site).
/// Fichier CSV unique (séparateur « ; », UTF-8 avec BOM, ~2,7 Mo, 874 colonnes dont les 26 « xx-japroz » = % de oui par canton) téléchargé en entier à chaque passage : une mise à jour mensuelle suffit.
/// Périmètre : depuis l'an 2000 (choix de Thomas). Les objets à venir (sans résultat) sont importés avec des colonnes vides, jamais complétés.
/// Piège constaté : certaines cases valent « . » ou sont vides (résultat non publié) : elles deviennent NULL, jamais zéro. Le codebook (PDF) n'a pas pu être lu automatiquement : le sens de
/// <c>rechtsform</c> (1 à 5) est déduit des titres des lignes (1 obligatoire, 2 facultatif, 3 initiative, 4 contre-projet, 5 question subsidiaire) et dit comme tel sur le site.
/// </summary>
public static class VotationsSource
{
    private const string Adresse = "https://swissvotes.ch/page/dataset/swissvotes_dataset.csv";
    private static readonly DateOnly Depuis = new(2000, 1, 1);
    private static readonly string[] Cantons = ["zh", "be", "lu", "ur", "sz", "ow", "nw", "gl", "zg", "fr", "so", "bs", "bl", "sh", "ar", "ai", "sg", "gr", "ag", "tg", "ti", "vd", "vs", "ne", "ge", "ju"];

    /// <summary>Télécharge, convertit et enregistre les votations depuis 2000. Retourne le nombre de lignes écrites.</summary>
    public static async Task<int> ExecuterAsync(string connectionString)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        // Agent qui s'identifie honnêtement (même règle que les autres sources du site).
        http.DefaultRequestHeaders.UserAgent.ParseAdd("IlsOntVote-importer (+https://www.ilsontvote.org)");
        var octets = await http.GetByteArrayAsync(Adresse);
        var votations = Convertir(Encoding.UTF8.GetString(octets).TrimStart('﻿'));
        await using var cx = new NpgsqlConnection(connectionString);
        await cx.OpenAsync();
        foreach (var v in votations)
        {
            await cx.ExecuteAsync(
                """
                INSERT INTO votations (anr, date_votation, titre_fr, titre_de, titre_en, rechtsform, accepte, peuple_accepte, cantons_accepte, participation, oui_pct, oui, non,
                                       cantons_oui, cantons_non, oui_par_canton, url, maj)
                VALUES (@Anr, @Date, @TitreFr, @TitreDe, @TitreEn, @Rechtsform, @Accepte, @PeupleAccepte, @CantonsAccepte, @Participation, @OuiPct, @Oui, @Non,
                        @CantonsOui, @CantonsNon, @OuiParCanton::jsonb, @Url, now())
                ON CONFLICT (anr) DO UPDATE SET date_votation = EXCLUDED.date_votation, titre_fr = EXCLUDED.titre_fr, titre_de = EXCLUDED.titre_de, titre_en = EXCLUDED.titre_en,
                    rechtsform = EXCLUDED.rechtsform, accepte = EXCLUDED.accepte, peuple_accepte = EXCLUDED.peuple_accepte, cantons_accepte = EXCLUDED.cantons_accepte,
                    participation = EXCLUDED.participation, oui_pct = EXCLUDED.oui_pct, oui = EXCLUDED.oui, non = EXCLUDED.non, cantons_oui = EXCLUDED.cantons_oui,
                    cantons_non = EXCLUDED.cantons_non, oui_par_canton = EXCLUDED.oui_par_canton, url = EXCLUDED.url, maj = now()
                """,
                new { v.Anr, Date = v.Date.ToDateTime(TimeOnly.MinValue), v.TitreFr, v.TitreDe, v.TitreEn, v.Rechtsform, v.Accepte, v.PeupleAccepte, v.CantonsAccepte, v.Participation, v.OuiPct, v.Oui, v.Non, v.CantonsOui, v.CantonsNon, v.OuiParCanton, v.Url });
        }
        return votations.Count;
    }

    /// <summary>Convertit le CSV (texte complet) en votations depuis 2000. Statique et sans réseau : testable.</summary>
    public static IReadOnlyList<VotationCh> Convertir(string csv)
    {
        var lignes = LireCsv(csv);
        if (lignes.Count < 2) return [];
        var h = lignes[0].Select((nom, i) => (nom, i)).ToDictionary(x => x.nom, x => x.i);
        string Col(IReadOnlyList<string> l, string nom) => h.TryGetValue(nom, out var i) && i < l.Count ? l[i].Trim() : "";
        var res = new List<VotationCh>();
        foreach (var l in lignes.Skip(1))
        {
            if (!int.TryParse(Col(l, "anr"), out var anr)) continue;
            if (!DateOnly.TryParseExact(Col(l, "datum"), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) || date < Depuis) continue;
            if (!int.TryParse(Col(l, "rechtsform"), out var forme)) continue;
            var parCanton = new Dictionary<string, decimal>();
            foreach (var c in Cantons) if (Dec(Col(l, c + "-japroz")) is { } p) parCanton[c] = p;
            var url = Col(l, "swissvoteslink");
            res.Add(new VotationCh(anr, date, Col(l, "titel_kurz_f"), Col(l, "titel_kurz_d"), NonVide(Col(l, "titel_kurz_e")), forme, Bool(Col(l, "annahme")), Bool(Col(l, "volk")), Bool(Col(l, "stand")),
                Dec(Col(l, "bet")), Dec(Col(l, "volkja-proz")), Ent(Col(l, "volkja")), Ent(Col(l, "volknein")), Dec(Col(l, "kt-ja")), Dec(Col(l, "kt-nein")),
                parCanton.Count > 0 ? JsonSerializer.Serialize(parCanton) : null, NonVide(url)));
        }
        return res;
    }

    private static string? NonVide(string s) => s.Length == 0 ? null : s;
    /// <summary>« 1 » = oui, « 0 » = non ; tout le reste (« . », vide, codes spéciaux) = inconnu, jamais deviné.</summary>
    private static bool? Bool(string s) => s == "1" ? true : s == "0" ? false : null;
    private static decimal? Dec(string s) => decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
    private static int? Ent(string s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;

    /// <summary>Lecteur CSV minimal : séparateur « ; », champs entre guillemets (avec sauts de ligne et guillemets doublés).</summary>
    public static List<List<string>> LireCsv(string texte)
    {
        var lignes = new List<List<string>>();
        var ligne = new List<string>();
        var champ = new StringBuilder();
        var guillemets = false;
        for (var i = 0; i < texte.Length; i++)
        {
            var c = texte[i];
            if (guillemets)
            {
                if (c == '"') { if (i + 1 < texte.Length && texte[i + 1] == '"') { champ.Append('"'); i++; } else guillemets = false; }
                else champ.Append(c);
            }
            else if (c == '"') guillemets = true;
            else if (c == ';') { ligne.Add(champ.ToString()); champ.Clear(); }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < texte.Length && texte[i + 1] == '\n') i++;
                ligne.Add(champ.ToString()); champ.Clear();
                if (ligne.Count > 1 || ligne[0].Length > 0) lignes.Add(ligne);
                ligne = [];
            }
            else champ.Append(c);
        }
        if (champ.Length > 0 || ligne.Count > 0) { ligne.Add(champ.ToString()); lignes.Add(ligne); }
        return lignes;
    }
}
