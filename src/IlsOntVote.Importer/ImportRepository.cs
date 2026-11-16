using Dapper;
using Npgsql;
using NpgsqlTypes;

namespace IlsOntVote.Importer;

/// <summary>Écriture en base (Dapper + COPY binaire). Idempotent : on peut rejouer l'import.</summary>
public sealed class ImportRepository(string connectionString)
{
    /// <summary>Ouvre une ligne du journal (import_runs). Son état reste NULL tant que l'import n'est pas terminé : un import interrompu se voit.</summary>
    public async Task<long> DemarrerAsync(string source)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        return await conn.ExecuteScalarAsync<long>(
            "INSERT INTO import_runs (source) VALUES (@source) RETURNING id", new { source });
    }

    /// <summary>Clôt la ligne du journal avec le résultat (succès, compteurs, message d'erreur éventuel).</summary>
    public async Task TerminerAsync(long runId, bool ok, int scrutins, long votes, string? message)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.ExecuteAsync(
            """
            UPDATE import_runs
               SET finished_at = now(), ok = @ok, scrutins = @scrutins, votes = @votes, message = @message
             WHERE id = @runId
            """,
            new { runId, ok, scrutins, votes = (int)Math.Min(votes, int.MaxValue), message });
    }

    public async Task<IReadOnlyDictionary<string, string>> LireSlugsAsync()
    {
        await using var conn = new NpgsqlConnection(connectionString);
        var rows = await conn.QueryAsync<(string Id, string Slug)>("SELECT id, slug FROM parlementaires");
        return rows.ToDictionary(r => r.Id, r => r.Slug);
    }

    /// <param name="legislatures">AN : législatures remplacées. Sénat : null (remplacement complet de la chambre).</param>
    public async Task EcrireAnnuaireAsync(Annuaire a, string chambre, int[]? legislatures)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await conn.ExecuteAsync(
            """
            INSERT INTO groupes (id, chambre, legislature, sigle, libelle, couleur, ordre)
            VALUES (@Id, @Chambre, @Legislature, @Sigle, @Libelle, @Couleur, @Ordre)
            ON CONFLICT (id) DO UPDATE SET chambre = EXCLUDED.chambre, legislature = EXCLUDED.legislature,
                sigle = EXCLUDED.sigle, libelle = EXCLUDED.libelle, couleur = COALESCE(EXCLUDED.couleur, groupes.couleur)
            """, a.Groupes.Select(g => new { g.Id, g.Chambre, Legislature = (short?)g.Legislature, g.Sigle, g.Libelle, g.Couleur, g.Ordre }), tx);

        await conn.ExecuteAsync(
            """
            INSERT INTO parlementaires (id, chambre, civilite, prenom, nom, slug, departement, circonscription)
            VALUES (@Id, @Chambre, @Civilite, @Prenom, @Nom, @Slug, @Departement, @Circonscription)
            ON CONFLICT (id) DO UPDATE SET chambre = EXCLUDED.chambre, civilite = EXCLUDED.civilite,
                prenom = EXCLUDED.prenom, nom = EXCLUDED.nom, departement = EXCLUDED.departement,
                circonscription = EXCLUDED.circonscription
            """, a.Parlementaires, tx);

        if (legislatures is not null)
        {
            // AN : on remplace les mandats et appartenances de CES législatures seulement.
            var legs = legislatures.Select(x => (short)x).ToArray();
            await conn.ExecuteAsync("DELETE FROM mandats WHERE legislature = ANY(@legs)", new { legs }, tx);
            await conn.ExecuteAsync(
                "DELETE FROM groupe_appartenances WHERE groupe_id IN (SELECT id FROM groupes WHERE chambre = @chambre AND legislature = ANY(@legs))",
                new { chambre, legs }, tx);
        }
        else
        {
            await conn.ExecuteAsync(
                "DELETE FROM mandats WHERE parlementaire_id IN (SELECT id FROM parlementaires WHERE chambre = @chambre)",
                new { chambre }, tx);
            await conn.ExecuteAsync(
                "DELETE FROM groupe_appartenances WHERE groupe_id IN (SELECT id FROM groupes WHERE chambre = @chambre)",
                new { chambre }, tx);
        }

        await conn.ExecuteAsync(
            """
            INSERT INTO mandats (parlementaire_id, legislature, date_debut, date_fin)
            VALUES (@ParlementaireId, @Legislature, @DateDebut, @DateFin)
            """,
            a.Mandats.Select(m => new
            {
                m.ParlementaireId, Legislature = (short?)m.Legislature,
                DateDebut = m.DateDebut.ToDateTime(default), DateFin = m.DateFin?.ToDateTime(default),
            }), tx);

        await conn.ExecuteAsync(
            """
            INSERT INTO groupe_appartenances (parlementaire_id, groupe_id, date_debut, date_fin)
            VALUES (@ParlementaireId, @GroupeId, @DateDebut, @DateFin)
            """,
            a.Appartenances.Select(m => new
            {
                m.ParlementaireId, m.GroupeId,
                DateDebut = m.DateDebut.ToDateTime(default), DateFin = m.DateFin?.ToDateTime(default),
            }), tx);

        await tx.CommitAsync();
    }

    public async Task<int> EcrireTextesAsync(IReadOnlyList<TexteRow> textes)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        foreach (var lot in textes.Chunk(1000))
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO textes (id, type_code, type_libelle, titre, titre_normalise, intitule_normalise, etat_code, etat_libelle,
                                    numero_loi, date_promulgation, date_jo, numero_jo, url_legifrance, url_dossier_senat, url_dossier_an,
                                    saisine_cc_date, saisine_cc_par, decision_cc_numero, decision_cc_date, decision_cc_code,
                                    decision_cc_libelle, decision_cc_url)
                VALUES (@Id, @TypeCode, @TypeLibelle, @Titre, @TitreNormalise, @IntituleNormalise, @EtatCode, @EtatLibelle,
                        @NumeroLoi, @DatePromulgation, @DateJo, @NumeroJo, @UrlLegifrance, @UrlDossierSenat, @UrlDossierAn,
                        @SaisineCcDate, @SaisineCcPar, @DecisionCcNumero, @DecisionCcDate, @DecisionCcCode,
                        @DecisionCcLibelle, @DecisionCcUrl)
                ON CONFLICT (id) DO UPDATE SET type_code = EXCLUDED.type_code, type_libelle = EXCLUDED.type_libelle,
                    titre = EXCLUDED.titre, titre_normalise = EXCLUDED.titre_normalise, intitule_normalise = EXCLUDED.intitule_normalise,
                    etat_code = EXCLUDED.etat_code, etat_libelle = EXCLUDED.etat_libelle, numero_loi = EXCLUDED.numero_loi,
                    date_promulgation = EXCLUDED.date_promulgation, date_jo = EXCLUDED.date_jo, numero_jo = EXCLUDED.numero_jo,
                    url_legifrance = EXCLUDED.url_legifrance, url_dossier_senat = EXCLUDED.url_dossier_senat,
                    url_dossier_an = EXCLUDED.url_dossier_an, saisine_cc_date = EXCLUDED.saisine_cc_date,
                    saisine_cc_par = EXCLUDED.saisine_cc_par, decision_cc_numero = EXCLUDED.decision_cc_numero,
                    decision_cc_date = EXCLUDED.decision_cc_date, decision_cc_code = EXCLUDED.decision_cc_code,
                    decision_cc_libelle = EXCLUDED.decision_cc_libelle, decision_cc_url = EXCLUDED.decision_cc_url
                """,
                lot.Select(t => new
                {
                    t.Id, t.TypeCode, t.TypeLibelle, t.Titre, t.TitreNormalise, t.IntituleNormalise, t.EtatCode, t.EtatLibelle,
                    t.NumeroLoi, DatePromulgation = t.DatePromulgation?.ToDateTime(default), DateJo = t.DateJo?.ToDateTime(default),
                    t.NumeroJo, t.UrlLegifrance, t.UrlDossierSenat, t.UrlDossierAn,
                    SaisineCcDate = t.SaisineCcDate?.ToDateTime(default), t.SaisineCcPar,
                    t.DecisionCcNumero, DecisionCcDate = t.DecisionCcDate?.ToDateTime(default), t.DecisionCcCode,
                    t.DecisionCcLibelle, t.DecisionCcUrl,
                }), tx);
        }

        await tx.CommitAsync();
        return textes.Count;
    }

    /// <summary>
    /// Rattache chaque vote sur l'ensemble d'un texte à son dossier, par égalité de titres normalisés.
    /// Plusieurs textes possibles : on écarte les dossiers « fusionnés », puis on préfère un texte promulgué ;
    /// s'il en reste plusieurs, le scrutin reste sans texte (jamais de choix au hasard).
    /// Retourne (rattachés, ambigus, sans texte).
    /// </summary>
    public async Task<(int Rattaches, int Ambigus, int SansTexte)> RattacherScrutinsAsync()
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        var textes = (await conn.QueryAsync<(string Id, string Titre, string Intitule, string? Etat, DateTime? Promulgation)>(
            "SELECT id, titre_normalise, intitule_normalise, etat_code, date_promulgation FROM textes")).ToList();

        var parTitre = textes.ToLookup(t => t.Titre);
        var parIntitule = textes.ToLookup(t => t.Intitule);

        var scrutins = (await conn.QueryAsync<(string Id, string Titre)>(
            "SELECT id, titre FROM scrutins WHERE sur_ensemble AND titre ~* '(projet|proposition) de loi'")).ToList();

        var ids = new List<string>();
        var texteIds = new List<string>();
        int ambigus = 0, sans = 0;

        foreach (var (id, titre) in scrutins)
        {
            var cle = TitreTexte.CleScrutin(titre);
            var candidats = parTitre[cle].Concat(parIntitule[cle]).DistinctBy(t => t.Id).ToList();
            if (candidats.Count > 1) candidats = candidats.Where(t => t.Etat != "02").ToList();
            if (candidats.Count > 1 && candidats.Any(t => t.Promulgation is not null))
                candidats = candidats.Where(t => t.Promulgation is not null).ToList();

            if (candidats.Count == 1) { ids.Add(id); texteIds.Add(candidats[0].Id); }
            else if (candidats.Count > 1) ambigus++;
            else sans++;
        }

        await using var tx = await conn.BeginTransactionAsync();
        await conn.ExecuteAsync("UPDATE scrutins SET texte_id = NULL WHERE texte_id IS NOT NULL", transaction: tx);
        await conn.ExecuteAsync(
            "UPDATE scrutins s SET texte_id = v.t FROM unnest(@ids::text[], @tids::text[]) AS v(i, t) WHERE s.id = v.i",
            new { ids = ids.ToArray(), tids = texteIds.ToArray() }, tx);

        // 2e passe : les autres scrutins publics qui citent un texte (amendement, sous-amendement, article…). Le titre du texte est la fin de
        // l'intitulé, à partir de « projet de loi » / « proposition de loi » : on en tire la même clé que pour un vote sur l'ensemble.
        // Plusieurs textes possibles -> même règle de départage que ci-dessus ; si l'ambiguïté demeure, le scrutin reste non rattaché.
        var autres = (await conn.QueryAsync<(string Id, string Titre)>(
            "SELECT id, titre FROM scrutins WHERE NOT sur_ensemble AND titre ~* '(projet|proposition) de loi'")).ToList();
        var idsLies = new List<string>();
        var texteLies = new List<string>();
        foreach (var (id, titre) in autres)
        {
            var m = System.Text.RegularExpressions.Regex.Match(titre, @"\b(projet|proposition) de loi\b.*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!m.Success) continue;
            var cle = TitreTexte.CleScrutin(m.Value);
            var candidats = parTitre[cle].Concat(parIntitule[cle]).DistinctBy(t => t.Id).ToList();
            if (candidats.Count > 1) candidats = candidats.Where(t => t.Etat != "02").ToList();
            if (candidats.Count > 1 && candidats.Any(t => t.Promulgation is not null))
                candidats = candidats.Where(t => t.Promulgation is not null).ToList();
            if (candidats.Count == 1) { idsLies.Add(id); texteLies.Add(candidats[0].Id); }
        }
        await conn.ExecuteAsync("UPDATE scrutins SET texte_lie_id = NULL WHERE texte_lie_id IS NOT NULL", transaction: tx);
        await conn.ExecuteAsync(
            "UPDATE scrutins s SET texte_lie_id = v.t FROM unnest(@ids::text[], @tids::text[]) AS v(i, t) WHERE s.id = v.i",
            new { ids = idsLies.ToArray(), tids = texteLies.ToArray() }, tx);
        Console.WriteLine($"  scrutins liés à un texte (amendements, articles…) : {idsLies.Count} sur {autres.Count}");
        await tx.CommitAsync();

        return (ids.Count, ambigus, sans);
    }

    /// <summary>Retourne (scrutins écrits, votes écrits, votes ignorés faute de parlementaire connu).</summary>
    /// <summary>
    /// Recalcule les statistiques de participation (vue matérialisée, migration 013). CONCURRENTLY quand la vue est déjà remplie :
    /// le site continue de lire l'ancienne version pendant le calcul (plusieurs minutes). La toute première fois (vue vide), c'est impossible.
    /// </summary>
    public async Task RafraichirStatsAsync()
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        foreach (var vue in new[] { "stats_participation_groupe", "stats_participation_elu" })
        {
            var remplie = await conn.ExecuteScalarAsync<bool>("SELECT ispopulated FROM pg_matviews WHERE matviewname = @vue", new { vue });
            await conn.ExecuteAsync(remplie ? $"REFRESH MATERIALIZED VIEW CONCURRENTLY {vue}" : $"REFRESH MATERIALIZED VIEW {vue}", commandTimeout: 3600);
        }
    }

    // ------------------------------------------------------------------ Titres en anglais

    /// <summary>
    /// Titres français à traduire : ceux des textes affichés sur le site (au moins un scrutin ou une étape) et des votes sur l'ensemble
    /// français, sans titre anglais et pas déjà dans la table des traductions. Distincts : un titre partagé n'est traduit qu'une fois.
    /// </summary>
    public async Task<IReadOnlyList<string>> LireTitresATraduireAsync(int limite)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        return (await conn.QueryAsync<string>(
            """
            SELECT x.fr FROM (
                SELECT t.titre AS fr FROM textes t
                 WHERE t.titre_en IS NULL
                   AND (EXISTS (SELECT 1 FROM scrutins s WHERE s.texte_id = t.id) OR EXISTS (SELECT 1 FROM texte_etapes e WHERE e.texte_id = t.id))
                UNION
                SELECT s.titre FROM scrutins s WHERE s.titre_en IS NULL AND s.chambre <> 'UE' AND s.sur_ensemble
            ) x
            WHERE NOT EXISTS (SELECT 1 FROM traductions_titres tr WHERE tr.fr = x.fr)
            ORDER BY x.fr LIMIT @limite
            """, new { limite })).ToList();
    }

    /// <summary>Enregistre les traductions (clé : titre français) puis les applique aux textes et scrutins concernés.</summary>
    public async Task<int> EcrireTraductionsAsync(IEnumerable<(string Fr, string En)> traductions, string modele)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await conn.ExecuteAsync(
            """
            INSERT INTO traductions_titres (fr, en, modele) VALUES (@Fr, @En, @modele)
            ON CONFLICT (fr) DO UPDATE SET en = EXCLUDED.en, modele = EXCLUDED.modele, traduit_le = now()
            """, traductions.Select(t => new { t.Fr, t.En, modele }), tx);

        // Application : seulement là où il n'y a pas déjà un titre anglais (source UE ou traduction précédente) ; `titre_en_auto` le signale.
        var n = await conn.ExecuteAsync(
            """
            UPDATE textes t SET titre_en = tr.en, titre_en_auto = true FROM traductions_titres tr WHERE tr.fr = t.titre AND t.titre_en IS NULL;
            """, transaction: tx);
        n += await conn.ExecuteAsync(
            """
            UPDATE scrutins s SET titre_en = tr.en, titre_en_auto = true FROM traductions_titres tr
             WHERE tr.fr = s.titre AND s.titre_en IS NULL AND s.chambre <> 'UE';
            """, transaction: tx);
        await tx.CommitAsync();
        return n;
    }

    /// <summary>Titres anglais d'origine (HowTheyVote) des votes UE déjà importés : rattrapage de la colonne titre_en.</summary>
    public async Task<int> MettreTitresEnUeAsync(IEnumerable<(string Id, string Titre)> titres)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        return await conn.ExecuteAsync("UPDATE scrutins SET titre_en = @Titre, titre_en_auto = false WHERE id = @Id AND chambre = 'UE'",
            titres.Select(t => new { Id = "EP-" + t.Id, t.Titre }));
    }

    // ------------------------------------------------------------------ Étapes de lecture (Dossiers législatifs de l'AN)

    /// <summary>
    /// Remplace les étapes d'une législature (suppression puis insertion : l'import se rejoue sans doublon) puis rattache chaque dossier à
    /// un texte : (1) par l'adresse du dossier AN enregistrée sur le texte, (2) par un scrutin public cité par une décision du dossier,
    /// (3) les autres décisions du même dossier suivent. Un dossier sans rattachement reste en base, sans texte (non affiché).
    /// </summary>
    public async Task<(int Etapes, int SansScrutin, int Rattachees)> EcrireEtapesAsync(IReadOnlyList<EtapeRow> etapes, int legislature)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await conn.ExecuteAsync("DELETE FROM texte_etapes WHERE dossier_an LIKE @prefixe", new { prefixe = $"DLR5L{legislature}N%" }, tx);

        // Le scrutin cité n'est lié que s'il existe en base (clé étrangère) ; sinon l'étape reste « avec scrutin public » sans lien.
        var connus = (await conn.QueryAsync<string>("SELECT id FROM scrutins WHERE chambre = 'AN' AND legislature = @l", new { l = (short)legislature }, tx)).ToHashSet();
        await conn.ExecuteAsync(
            """
            INSERT INTO texte_etapes (id, dossier_an, senat_chemin, code_acte, date_acte, resultat, scrutin_id, sans_scrutin_public, article_49_3)
            VALUES (@Id, @DossierAn, @SenatChemin, @CodeActe, @DateActe, @Resultat, @ScrutinId, @SansScrutinPublic, @Article493)
            ON CONFLICT (id) DO NOTHING
            """,
            etapes.Select(e => new
            {
                e.Id, e.DossierAn, e.SenatChemin, e.CodeActe, DateActe = e.DateActe.ToDateTime(default), e.Resultat,
                ScrutinId = e.ScrutinId is not null && connus.Contains(e.ScrutinId) ? e.ScrutinId : null, e.SansScrutinPublic, e.Article493,
            }), tx);

        // (1) adresse du dossier AN portée par le texte (…/DLR5L17N50450.asp) ; DISTINCT ON : un seul texte par dossier, toujours le même.
        await conn.ExecuteAsync(
            """
            UPDATE texte_etapes e SET texte_id = x.texte_id
              FROM (SELECT DISTINCT ON (e2.dossier_an) e2.dossier_an, t.id AS texte_id
                      FROM texte_etapes e2 JOIN textes t ON t.url_dossier_an LIKE '%' || e2.dossier_an || '.%'
                     WHERE e2.dossier_an LIKE @prefixe ORDER BY e2.dossier_an, t.id) x
             WHERE e.dossier_an = x.dossier_an AND e.texte_id IS NULL
            """, new { prefixe = $"DLR5L{legislature}N%" }, tx);
        // (1 bis) dossier du Sénat cité par le dossier AN = signet de l'adresse du dossier Sénat du texte (…/pjl24-123.html).
        await conn.ExecuteAsync(
            """
            UPDATE texte_etapes e SET texte_id = x.texte_id
              FROM (SELECT DISTINCT ON (e2.dossier_an) e2.dossier_an, t.id AS texte_id
                      FROM texte_etapes e2 JOIN textes t ON t.url_dossier_senat LIKE '%/' || e2.senat_chemin || '.html%'
                     WHERE e2.dossier_an LIKE @prefixe AND e2.senat_chemin IS NOT NULL AND e2.texte_id IS NULL ORDER BY e2.dossier_an, t.id) x
             WHERE e.dossier_an = x.dossier_an AND e.texte_id IS NULL
            """, new { prefixe = $"DLR5L{legislature}N%" }, tx);
        // (2) un scrutin public de la décision est déjà rattaché à un texte (rapprochement par titre à l'import des textes).
        await conn.ExecuteAsync(
            """
            UPDATE texte_etapes e SET texte_id = s.texte_id
              FROM scrutins s WHERE e.texte_id IS NULL AND e.scrutin_id = s.id AND s.texte_id IS NOT NULL
            """, transaction: tx);
        // (3) les autres décisions du même dossier suivent le texte trouvé.
        await conn.ExecuteAsync(
            """
            UPDATE texte_etapes e SET texte_id = x.texte_id
              FROM (SELECT dossier_an, min(texte_id) AS texte_id FROM texte_etapes WHERE texte_id IS NOT NULL GROUP BY dossier_an) x
             WHERE e.dossier_an = x.dossier_an AND e.texte_id IS NULL
            """, transaction: tx);

        var stats = await conn.QuerySingleAsync<(int Total, int Sans, int Rattachees)>(
            "SELECT count(*)::int, count(*) FILTER (WHERE sans_scrutin_public)::int, count(*) FILTER (WHERE texte_id IS NOT NULL)::int FROM texte_etapes WHERE dossier_an LIKE @prefixe",
            new { prefixe = $"DLR5L{legislature}N%" }, tx);
        await tx.CommitAsync();
        return (stats.Total, stats.Sans, stats.Rattachees);
    }

    // ------------------------------------------------------------------ Parlement européen (import incrémental)

    /// <summary>Identifiants des scrutins UE déjà en base (l'import UE ne relit que les votes nouveaux) et date du plus récent.</summary>
    public async Task<(HashSet<string> Ids, DateOnly? PlusRecent)> LireScrutinsUeAsync()
    {
        await using var conn = new NpgsqlConnection(connectionString);
        var rows = (await conn.QueryAsync<(string Id, DateTime Date)>("SELECT id, date_scrutin FROM scrutins WHERE chambre = 'UE'")).ToList();
        return (rows.Select(r => r.Id).ToHashSet(), rows.Count == 0 ? null : DateOnly.FromDateTime(rows.Max(r => r.Date)));
    }

    /// <summary>Références de procédure (ex. « 2025/0264(CNS) ») des votes principaux UE, pour le rattrapage des titres français.</summary>
    public async Task<IReadOnlyList<string>> LireReferencesUeAsync()
    {
        await using var conn = new NpgsqlConnection(connectionString);
        return (await conn.QueryAsync<string>(
            "SELECT DISTINCT dossier_ref FROM scrutins WHERE chambre = 'UE' AND sur_ensemble AND dossier_ref IS NOT NULL")).ToList();
    }

    /// <summary>Remplace le titre (et la note de source) de tous les votes principaux d'une procédure UE.</summary>
    public async Task<int> MettreTitreUeAsync(string reference, string titre, string note)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        return await conn.ExecuteAsync(
            "UPDATE scrutins SET titre = @titre, note = @note WHERE chambre = 'UE' AND sur_ensemble AND dossier_ref = @reference",
            new { reference, titre, note });
    }

    /// <summary>Ajoute ou met à jour groupes et eurodéputés (upsert, jamais de suppression : l'import UE est incrémental).</summary>
    public async Task EcrireMembresUeAsync(IEnumerable<GroupeRow> groupes, IEnumerable<ParlementaireRow> parlementaires)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await conn.ExecuteAsync(
            """
            INSERT INTO groupes (id, chambre, legislature, sigle, libelle, couleur, ordre)
            VALUES (@Id, @Chambre, @Legislature, @Sigle, @Libelle, @Couleur, @Ordre)
            ON CONFLICT (id) DO UPDATE SET sigle = EXCLUDED.sigle, libelle = EXCLUDED.libelle, couleur = COALESCE(groupes.couleur, EXCLUDED.couleur)
            """, groupes.Select(g => new { g.Id, g.Chambre, Legislature = (short?)g.Legislature, g.Sigle, g.Libelle, g.Couleur, g.Ordre }), tx);
        await conn.ExecuteAsync(
            """
            INSERT INTO parlementaires (id, chambre, civilite, prenom, nom, slug, departement, circonscription)
            VALUES (@Id, @Chambre, @Civilite, @Prenom, @Nom, @Slug, @Departement, @Circonscription)
            ON CONFLICT (id) DO UPDATE SET prenom = EXCLUDED.prenom, nom = EXCLUDED.nom,
                departement = EXCLUDED.departement, circonscription = EXCLUDED.circonscription
            """, parlementaires, tx);
        await tx.CommitAsync();
    }

    /// <summary>
    /// Reconstruit les mandats et groupes des eurodéputés à partir des votes : le PE n'a pas d'annuaire daté en open data.
    /// Mandat = du premier au dernier vote où l'élu figure (y compris « n'a pas pris part »), sans fin s'il figure dans le vote le
    /// plus récent. Fusionné avec l'existant pour que l'import reste incrémental, dans les deux sens :
    /// - votes PLUS RÉCENTS que ceux déjà en base : un élu qu'ils ne listent plus a quitté le PE (fin = ancien dernier vote) ;
    /// - votes PLUS ANCIENS (reprise de l'historique) : on avance seulement le début des mandats, on ne termine personne
    ///   (un élu absent des vieux votes est simplement arrivé plus tard).
    /// Groupe : le dernier connu (les votes, eux, portent le groupe du jour du vote).
    /// </summary>
    public async Task EcrireMandatsUeAsync(IReadOnlyDictionary<string, (DateOnly Premier, DateOnly Dernier, string GroupeId)> vus, DateOnly? ancienPlusRecent)
    {
        var leg = (short)ParlementEuropeenSource.Legislature;
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var existants = (await conn.QueryAsync<(string Id, DateTime Debut, DateTime? Fin)>(
            "SELECT parlementaire_id, date_debut, date_fin FROM mandats WHERE legislature = @leg", new { leg }, tx))
            .ToDictionary(r => r.Id);

        var plusRecentLu = vus.Values.Max(v => v.Dernier);
        var remonteLhistorique = ancienPlusRecent is not null && plusRecentLu <= ancienPlusRecent;

        var lignes = new List<(string Id, DateTime Debut, DateTime? Fin, string GroupeId)>();
        foreach (var (id, e) in existants)
        {
            if (vus.ContainsKey(id)) continue;
            // Non listé : parti du PE seulement si cet import contient des votes plus récents que ceux déjà connus.
            DateTime? fin = remonteLhistorique ? e.Fin : e.Fin ?? ancienPlusRecent?.ToDateTime(default) ?? e.Debut;
            lignes.Add((id, e.Debut, fin, ""));
        }
        foreach (var (id, v) in vus)
        {
            var premier = v.Premier.ToDateTime(default);
            var aExistant = existants.TryGetValue(id, out var e);
            var debut = aExistant && e.Debut < premier ? e.Debut : premier;
            DateTime? fin = aExistant
                ? (remonteLhistorique ? e.Fin : (v.Dernier >= plusRecentLu ? null : v.Dernier.ToDateTime(default)))
                : (v.Dernier >= plusRecentLu && !remonteLhistorique ? null : v.Dernier.ToDateTime(default));
            lignes.Add((id, debut, fin, v.GroupeId));
        }

        await conn.ExecuteAsync("DELETE FROM mandats WHERE legislature = @leg", new { leg }, tx);
        await conn.ExecuteAsync(
            "INSERT INTO mandats (parlementaire_id, legislature, date_debut, date_fin) VALUES (@Id, @leg, @Debut, @Fin)",
            lignes.Select(x => new { x.Id, leg, x.Debut, x.Fin }), tx);

        // Groupe : une appartenance par eurodéputé (la plus récente connue), du début du mandat à sa fin. Un élu non listé dans
        // cet import garde l'appartenance existante.
        await conn.ExecuteAsync(
            "DELETE FROM groupe_appartenances WHERE parlementaire_id = ANY(@ids) AND groupe_id IN (SELECT id FROM groupes WHERE chambre = 'UE')",
            new { ids = vus.Keys.ToArray() }, tx);
        await conn.ExecuteAsync(
            "INSERT INTO groupe_appartenances (parlementaire_id, groupe_id, date_debut, date_fin) VALUES (@Id, @GroupeId, @Debut, @Fin)",
            lignes.Where(x => x.GroupeId != "").Select(x => new { x.Id, x.GroupeId, x.Debut, x.Fin }), tx);
        await tx.CommitAsync();
    }

    public async Task<(int Scrutins, long Votes, long Ignores)> EcrireScrutinsAsync(
        IEnumerable<ScrutinAvecVotes> scrutins, Annuaire annuaire, int tailleLot = 250)
    {
        var connus = annuaire.Parlementaires.Select(d => d.Id).ToHashSet();
        var groupesConnus = annuaire.Groupes.Select(g => g.Id).ToHashSet();

        int nbScrutins = 0;
        long nbVotes = 0, ignores = 0;

        foreach (var lot in scrutins.Chunk(tailleLot))
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            // `featured*` n'est jamais écrasé : c'est de l'éditorial (le choix des textes à la une se fait dans /Admin).
        // UPSERT : l'import se rejoue sans doublon et corrige les données si la source change (ex. une mise au point).
            await conn.ExecuteAsync(
                """
                INSERT INTO scrutins (id, chambre, legislature, numero, date_scrutin, titre, type_vote, sort, dossier_ref,
                                      sur_ensemble, nb_votants, nb_pour, nb_contre, nb_abstentions, nb_non_votants, mode_publication,
                                      nb_votes_manquants, nominatif_incomplet, note,
                                      type_vote_libelle, type_majorite, suffrages_exprimes, suffrages_requis, titre_en)
                VALUES (@Id, @Chambre, @Legislature, @Numero, @DateScrutin, @Titre, @TypeVote, @Sort, @DossierRef,
                        @SurEnsemble, @NbVotants, @NbPour, @NbContre, @NbAbstentions, @NbNonVotants, @ModePublication,
                        @NbVotesManquants, @NbVotesManquants > 0, @Note,
                        @TypeVoteLibelle, @TypeMajorite, @SuffragesExprimes, @SuffragesRequis, @TitreEn)
                ON CONFLICT (id) DO UPDATE SET titre = EXCLUDED.titre, type_vote = EXCLUDED.type_vote, sort = EXCLUDED.sort,
                    dossier_ref = EXCLUDED.dossier_ref, sur_ensemble = EXCLUDED.sur_ensemble,
                    nb_votants = EXCLUDED.nb_votants, nb_pour = EXCLUDED.nb_pour, nb_contre = EXCLUDED.nb_contre,
                    nb_abstentions = EXCLUDED.nb_abstentions, nb_non_votants = EXCLUDED.nb_non_votants,
                    mode_publication = EXCLUDED.mode_publication,
                    nb_votes_manquants = EXCLUDED.nb_votes_manquants, nominatif_incomplet = EXCLUDED.nominatif_incomplet,
                    note = EXCLUDED.note, type_vote_libelle = EXCLUDED.type_vote_libelle,
                    type_majorite = EXCLUDED.type_majorite, suffrages_exprimes = EXCLUDED.suffrages_exprimes,
                    suffrages_requis = EXCLUDED.suffrages_requis,
                    -- titre_en : seulement quand la source en fournit un (UE) ; une traduction automatique déjà enregistrée n'est jamais effacée.
                    titre_en = COALESCE(EXCLUDED.titre_en, scrutins.titre_en)
                """,
                lot.Select(l => new
                {
                    l.Scrutin.Id, l.Scrutin.Chambre, Legislature = (short)l.Scrutin.Legislature, l.Scrutin.Numero,
                    DateScrutin = l.Scrutin.DateScrutin.ToDateTime(default),
                    l.Scrutin.Titre, l.Scrutin.TypeVote, l.Scrutin.Sort, l.Scrutin.DossierRef, l.Scrutin.SurEnsemble,
                    l.Scrutin.NbVotants, l.Scrutin.NbPour, l.Scrutin.NbContre, l.Scrutin.NbAbstentions,
                    l.Scrutin.NbNonVotants, l.Scrutin.ModePublication, l.Scrutin.NbVotesManquants, l.Scrutin.Note,
                    l.Scrutin.TypeVoteLibelle, l.Scrutin.TypeMajorite, l.Scrutin.SuffragesExprimes, l.Scrutin.SuffragesRequis, l.Scrutin.TitreEn,
                }), tx);

            // Les votes d'un scrutin sont REMPLACÉS en bloc (suppression puis COPY) : plus simple et plus sûr qu'un
            // différentiel, et la transaction garantit qu'on ne voit jamais un scrutin à moitié importé.
            await conn.ExecuteAsync("DELETE FROM votes WHERE scrutin_id = ANY(@ids)",
                new { ids = lot.Select(l => l.Scrutin.Id).ToArray() }, tx);

            await using (var copy = await conn.BeginBinaryImportAsync(
                """
                COPY votes (scrutin_id, parlementaire_id, groupe_id, position, par_delegation, rectifie, position_origine, origine_inconnue)
                FROM STDIN (FORMAT BINARY)
                """))
            {
                foreach (var l in lot)
                foreach (var v in l.Votes)
                {
                    if (!connus.Contains(v.ParlementaireId)) { ignores++; continue; }
                    await copy.StartRowAsync();
                    await copy.WriteAsync(v.ScrutinId, NpgsqlDbType.Text);
                    await copy.WriteAsync(v.ParlementaireId, NpgsqlDbType.Text);
                    await copy.WriteAsync(v.GroupeId is not null && groupesConnus.Contains(v.GroupeId) ? v.GroupeId : null, NpgsqlDbType.Text);
                    await copy.WriteAsync(v.Position, NpgsqlDbType.Text);
                    await copy.WriteAsync(v.ParDelegation, NpgsqlDbType.Boolean);
                    await copy.WriteAsync(v.Rectifie, NpgsqlDbType.Boolean);
                    await copy.WriteAsync(v.PositionOrigine, NpgsqlDbType.Text);
                    await copy.WriteAsync(v.OrigineInconnue, NpgsqlDbType.Boolean);
                    nbVotes++;
                }
                await copy.CompleteAsync();
            }

            await tx.CommitAsync();
            nbScrutins += lot.Length;
        }

        return (nbScrutins, nbVotes, ignores);
    }
}
