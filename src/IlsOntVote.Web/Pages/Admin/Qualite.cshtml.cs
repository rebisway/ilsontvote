using Dapper;
using IlsOntVote.Web.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IlsOntVote.Web.Pages.Admin;

/// <summary>Un scrutin à signaler dans le tableau de bord qualité.</summary>
// Types = ceux que renvoie Npgsql (colonne `date` -> DateOnly, `nb_votes_manquants` entier non nul -> int) : Dapper lie un
// constructeur positionnel par nom ET par type exact, sans conversion (« A parameterless default constructor or one matching… »).
public sealed record ScrutinSignale(string Id, string Chambre, DateOnly DateScrutin, string Titre, int Manquants);

/// <summary>Tableau de bord « qualité des données » : ce que le site sait ne pas savoir (règle du doute et des données manquantes).</summary>
public class QualiteModel(DapperContext db) : PageModel
{
    public int ScrutinsIncomplets { get; private set; }
    public int EnsemblesSansTexte { get; private set; }
    public int MisesAuPointSansOrigine { get; private set; }
    public int Sieges { get; private set; }
    public IReadOnlyList<ScrutinSignale> Incomplets { get; private set; } = [];

    public async Task OnGetAsync()
    {
        using var cx = db.CreateConnection();
        // Décompte nominatif plus court que le total officiel : les « absents » de ces scrutins peuvent être de faux absents.
        ScrutinsIncomplets = await cx.ExecuteScalarAsync<int>("SELECT count(*)::int FROM scrutins WHERE nominatif_incomplet");
        // Votes sur l'ensemble d'un texte que le rapprochement par titre n'a pas pu rattacher (ambigu ou introuvable).
        EnsemblesSansTexte = await cx.ExecuteScalarAsync<int>("SELECT count(*)::int FROM scrutins WHERE sur_ensemble AND texte_id IS NULL");
        // Mises au point du Sénat dont la position d'origine n'est pas publiée.
        MisesAuPointSansOrigine = await cx.ExecuteScalarAsync<int>("SELECT count(*)::int FROM votes WHERE origine_inconnue");
        // Contrôle de cohérence des mandats AN : doit rester <= 577 (cf. CLAUDE.md, piège des suppléants).
        Sieges = await cx.ExecuteScalarAsync<int>(
            """
            SELECT count(DISTINCT m.parlementaire_id)::int
              FROM mandats m JOIN parlementaires p ON p.id = m.parlementaire_id
             WHERE p.chambre = 'AN' AND m.date_debut <= current_date AND (m.date_fin IS NULL OR m.date_fin >= current_date)
               AND m.legislature = (SELECT max(m2.legislature) FROM mandats m2 JOIN parlementaires p2 ON p2.id = m2.parlementaire_id WHERE p2.chambre = 'AN')
            """);
        Incomplets = (await cx.QueryAsync<ScrutinSignale>(
            // Alias = noms des paramètres du record : Dapper lie un constructeur positionnel par nom de colonne, sans conversion snake_case.
            "SELECT id, chambre, date_scrutin AS DateScrutin, titre, nb_votes_manquants AS Manquants FROM scrutins WHERE nominatif_incomplet ORDER BY date_scrutin DESC LIMIT 50")).ToList();
    }
}
