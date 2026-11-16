using Npgsql;

namespace IlsOntVote.Web;

/// <summary>
/// « dotnet IlsOntVote.Web.dll check » : dit ce qui manque encore à l'installation (secrets, base, schéma, données,
/// administrateur, imports) et se termine par le code 1 si quelque chose manque. Le script de mise à jour le lance
/// après chaque démarrage : une installation incomplète se voit tout de suite, au lieu de produire un site muet.
/// Il lit la configuration comme le site (appsettings.json, appsettings.Local.json, variables d'environnement).
/// </summary>
public static class SiteChecks
{
    /// <summary>Tables que les migrations doivent avoir créées (cf. db/*.sql) : leur absence = migrations non appliquées.</summary>
    private static readonly string[] TablesAttendues =
        ["scrutins", "votes", "parlementaires", "mandats", "textes", "import_runs", "page_views", "\"AspNetUsers\""];

    public static async Task<int> RunAsync()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Local.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var problemes = new List<string>();
        var notes = new List<string>();

        // 1. Chaîne de connexion, puis la base elle-même.
        var connexion = configuration.GetConnectionString("PostgreSQL_Release");
        NpgsqlConnection? db = null;
        if (EstAbsent(connexion))
        {
            problemes.Add("ConnectionStrings:PostgreSQL_Release est absente ou encore une valeur du modèle : le site ne peut pas lire ses données (voir appsettings.Local.example.json).");
        }
        else
        {
            try
            {
                db = new NpgsqlConnection(connexion);
                await db.OpenAsync();
            }
            catch (Exception ex)
            {
                problemes.Add($"La base ne répond pas ({ex.GetBaseException().Message}) : vérifier l'utilisateur, le mot de passe, et que la base existe (setup-site.sh).");
                db = null;
            }
        }

        // 2. Schéma, données, administrateur, imports : seulement si la base répond.
        if (db is not null)
        {
            await using (db)
            {
                var manquantes = new List<string>();
                foreach (var table in TablesAttendues)
                {
                    var existe = await ScalaireAsync<bool>(db, $"SELECT to_regclass('public.{table}') IS NOT NULL");
                    if (!existe) manquantes.Add(table.Trim('"'));
                }

                if (manquantes.Count > 0)
                {
                    problemes.Add($"Tables absentes ({string.Join(", ", manquantes)}) : les migrations ne sont pas appliquées (sudo bash deploy/migrate.sh).");
                }
                else
                {
                    if (await ScalaireAsync<long>(db, "SELECT count(*) FROM scrutins") == 0)
                        problemes.Add("La base ne contient aucun scrutin : le site est vide (premier import : voir DEPLOYMENT_SITE.fr.md, commande « import-all.sh historique » sous www-data).");

                    if (await ScalaireAsync<long>(db, "SELECT count(*) FROM \"AspNetUserRoles\"") == 0)
                        problemes.Add("Aucun administrateur : renseigner AdminBootstrapEmail et AdminBootstrapPassword dans appsettings.Local.json, puis redémarrer le service.");
                    else if (!EstAbsent(configuration["AdminBootstrapPassword"]))
                        notes.Add("AdminBootstrapPassword est encore dans appsettings.Local.json alors qu'un administrateur existe : le retirer.");

                    var dernier = await ScalaireAsync<DateTime?>(db, "SELECT max(finished_at) FROM import_runs WHERE ok");
                    if (dernier is null)
                        problemes.Add("Aucun import réussi n'est enregistré (import_runs) : les données ne sont pas alimentées.");
                    else if (DateTime.UtcNow - dernier.Value.ToUniversalTime() > TimeSpan.FromDays(3))
                        problemes.Add($"Le dernier import réussi date du {dernier:yyyy-MM-dd HH:mm} : le timer d'import ne tourne plus (systemctl status ilsontvote-import.timer).");
                }
            }
        }

        // 3. Le site doit pouvoir écrire dans App_Data (clés des cookies, clé des statistiques).
        try
        {
            var dossier = Path.Combine(Directory.GetCurrentDirectory(), "App_Data");
            Directory.CreateDirectory(dossier);
            var essai = Path.Combine(dossier, ".ecriture");
            await File.WriteAllTextAsync(essai, "ok");
            File.Delete(essai);
        }
        catch (Exception ex)
        {
            problemes.Add($"App_Data n'est pas accessible en écriture ({ex.Message}) : le site ne peut pas conserver ses clés (chown www-data).");
        }

        if (!string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Production", StringComparison.OrdinalIgnoreCase))
            notes.Add("ASPNETCORE_ENVIRONMENT n'est pas « Production » dans cet environnement (normal si le check est lancé à la main sans l'environnement du service).");

        Console.WriteLine("IlsOntVote - vérification de la configuration");
        foreach (var note in notes) Console.WriteLine("  note : " + note);

        if (problemes.Count == 0)
        {
            Console.WriteLine("  OK : rien ne manque.");
            return 0;
        }

        foreach (var probleme in problemes) Console.WriteLine("  MANQUE : " + probleme);
        return 1;
    }

    private static async Task<T> ScalaireAsync<T>(NpgsqlConnection db, string sql)
    {
        await using var commande = new NpgsqlCommand(sql, db);
        var valeur = await commande.ExecuteScalarAsync();
        return valeur is null or DBNull ? default! : (T)valeur;
    }

    // Vide, ou une valeur de modèle du fichier d'exemple (« <mot de passe> »).
    private static bool EstAbsent(string? valeur) => string.IsNullOrWhiteSpace(valeur) || valeur.Contains('<');
}
