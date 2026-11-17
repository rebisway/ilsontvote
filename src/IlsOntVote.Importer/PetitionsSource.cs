using System.Globalization;
using System.Text;

namespace IlsOntVote.Importer;

/// <summary>Une pétition telle que publiée par le Sénat (petitions.senat.fr), via le jeu data.gouv.fr « Pétitions du Sénat ».</summary>
public sealed record PetitionRow(string Id, string Titre, string? Description, DateTime? DatePublication, int NbVotes, string Statut, DateTime? DateLimiteVote, string Type, string Url);

/// <summary>
/// Source « pétitions du Sénat » : un seul CSV (séparateur « ; », UTF-8, champs entre guillemets pouvant contenir des sauts de ligne),
/// régénéré chaque lundi par data.gouv.fr. Licence Ouverte 2.0.
/// </summary>
public static class PetitionsSource
{
    /// <summary>Adresse du CSV (ressource « Pétitions au JJ-MM-AAAA » du jeu data.gouv.fr/datasets/petitions-du-senat).</summary>
    public const string Url = "https://data-pipeline-open.s3.sbg.io.cloud.ovh.net/senat_petitions/petitions.csv";

    /// <summary>Dossier des fichiers hebdomadaires archivés par data.gouv.fr (un CSV par semaine, depuis mars 2026).</summary>
    public const string UrlArchives = "https://files.data.gouv.fr/data-pipeline-open/senat_petitions/";

    /// <summary>Lit le CSV. Les lignes sans identifiant ou sans titre sont ignorées (rien n'est inventé).</summary>
    public static IReadOnlyList<PetitionRow> Lire(string chemin)
    {
        var lignes = AnalyserCsv(File.ReadAllText(chemin, new UTF8Encoding(false)).TrimStart('﻿'));
        if (lignes.Count == 0) return [];
        var entete = lignes[0].Select(c => c.Trim()).ToList();
        int Col(string nom) => entete.IndexOf(nom) is var i and >= 0 ? i : throw new InvalidDataException($"Colonne « {nom} » absente du CSV des pétitions (format modifié ?).");
        int iId = Col("identifiant"), iTitre = Col("titre"), iDesc = Col("description"), iPub = Col("date_publication"), iVotes = Col("nb_votes"),
            iStatut = Col("statut"), iLimite = Col("date_limite_vote"), iType = Col("type"), iUrl = Col("url");
        var dernier = new[] { iId, iTitre, iDesc, iPub, iVotes, iStatut, iLimite, iType, iUrl }.Max();

        DateTime? Date(string s) => DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.Date : null;
        var res = new List<PetitionRow>();
        foreach (var l in lignes.Skip(1))
        {
            if (l.Count <= dernier) continue;   // ligne tronquée : ignorée
            if (string.IsNullOrWhiteSpace(l[iId]) || string.IsNullOrWhiteSpace(l[iTitre])) continue;
            res.Add(new PetitionRow(l[iId].Trim(), l[iTitre].Trim(), string.IsNullOrWhiteSpace(l[iDesc]) ? null : l[iDesc].Trim(), Date(l[iPub]),
                int.TryParse(l[iVotes], out var n) ? n : 0, l[iStatut].Trim(), Date(l[iLimite]), l[iType].Trim(), l[iUrl].Trim()));
        }
        return res;
    }

    /// <summary>Analyse CSV minimale (RFC 4180, séparateur « ; ») : guillemets doublés et sauts de ligne dans un champ gérés.</summary>
    public static List<List<string>> AnalyserCsv(string texte)
    {
        var lignes = new List<List<string>>();
        var ligne = new List<string>();
        var champ = new StringBuilder();
        var entreGuillemets = false;
        for (var i = 0; i < texte.Length; i++)
        {
            var c = texte[i];
            if (entreGuillemets)
            {
                if (c == '"' && i + 1 < texte.Length && texte[i + 1] == '"') { champ.Append('"'); i++; }
                else if (c == '"') entreGuillemets = false;
                else champ.Append(c);
            }
            else if (c == '"') entreGuillemets = true;
            else if (c == ';') { ligne.Add(champ.ToString()); champ.Clear(); }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < texte.Length && texte[i + 1] == '\n') i++;
                ligne.Add(champ.ToString()); champ.Clear();
                if (ligne.Any(x => x.Length > 0)) lignes.Add(ligne);
                ligne = [];
            }
            else champ.Append(c);
        }
        if (champ.Length > 0 || ligne.Count > 0) { ligne.Add(champ.ToString()); if (ligne.Any(x => x.Length > 0)) lignes.Add(ligne); }
        return lignes;
    }
}
