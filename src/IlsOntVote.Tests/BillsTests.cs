using System.Text.Json;
using IlsOntVote.Importer;

namespace IlsOntVote.Tests;

/// <summary>Projets de loi britanniques : conversion de l'API et rattachement des divisions par le titre.</summary>
public class BillsTests
{
    private static BillsRoyaumeUniSource.Candidat C(long id, string titre, string debut, string fin) =>
        new(id, BillsRoyaumeUniSource.Normaliser(titre), DateTime.Parse(debut), DateTime.Parse(fin).AddDays(7));

    [Theory]
    [InlineData("Finance Bill: Third Reading", "finance bill third reading")]
    [InlineData("Financial Services and Markets Bill [HL]", "financial services and markets bill")]   // « [HL] » : projet venu des Lords, absent des titres de division
    [InlineData("  Planning   and Infrastructure Bill — Report Stage ", "planning and infrastructure bill report stage")]
    public void Normaliser(string brut, string attendu) => Assert.Equal(attendu, BillsRoyaumeUniSource.Normaliser(brut));

    [Theory]
    [InlineData("Taxation (Energy and Vehicles) Act 2026", "taxation energy and vehicles bill")]
    [InlineData("Steel Industry (Special Measures) Act", "steel industry special measures bill")]
    [InlineData("Finance Bill", "finance bill")]
    public void Un_projet_devenu_loi_est_aussi_connu_sous_son_nom_de_projet(string titre, string variante) =>
        Assert.Contains(variante, BillsRoyaumeUniSource.VariantesTitre(titre));

    [Fact]
    public void Division_d_un_projet_devenu_loi_se_rattache_via_la_variante_Bill()
    {
        // La source renomme « … Bill » en « … Act 2026 » à la sanction : les divisions antérieures disent encore « Bill ».
        var c = BillsRoyaumeUniSource.VariantesTitre("Steel Industry (Nationalisation) Act 2026")
            .Select(v => new BillsRoyaumeUniSource.Candidat(7, v, new DateTime(2026, 5, 14), new DateTime(2026, 8, 19).AddDays(7))).ToList();
        var r = BillsRoyaumeUniSource.Rattacher("Steel Industry (Nationalisation) Bill Committee: New Clause 4", new DateTime(2026, 6, 10), c);
        Assert.Equal(7, r?.BillId);
        Assert.Equal("Committee: New Clause 4", r?.Etape);
    }

    [Fact]
    public void Division_rattachee_au_projet_dont_le_titre_ouvre_le_sien_et_etape_extraite()
    {
        var c = new[] { C(1, "Finance Bill", "2026-01-10", "2026-03-30") };
        var r = BillsRoyaumeUniSource.Rattacher("Finance Bill: Third Reading", new DateTime(2026, 3, 1), c);
        Assert.Equal(1, r?.BillId);
        Assert.Equal("Third Reading", r?.Etape);
        // Projet des Lords : le titre porte « [HL] », la division non.
        var hl = new[] { C(2, "Financial Services and Markets Bill [HL]", "2026-02-01", "2026-04-01") };
        var r2 = BillsRoyaumeUniSource.Rattacher("Financial Services and Markets Bill Report Stage: New Clause 3", new DateTime(2026, 3, 10), hl);
        Assert.Equal(2, r2?.BillId);
        Assert.Equal("Report Stage: New Clause 3", r2?.Etape);
    }

    [Fact]
    public void Le_plus_long_titre_l_emporte_et_un_prefixe_partiel_de_mot_ne_compte_pas()
    {
        var c = new[] { C(1, "Finance Bill", "2026-01-01", "2026-12-31"), C(2, "Finance Bill Scotland", "2026-01-01", "2026-12-31"), C(3, "Fin", "2026-01-01", "2026-12-31") };
        Assert.Equal(2, BillsRoyaumeUniSource.Rattacher("Finance Bill Scotland: Second Reading", new DateTime(2026, 5, 1), c)?.BillId);
        Assert.Equal(1, BillsRoyaumeUniSource.Rattacher("Finance Bill: Second Reading", new DateTime(2026, 5, 1), c)?.BillId);
    }

    [Fact]
    public void Meme_titre_chaque_annee_la_date_de_la_division_departage_et_sans_periode_aucun_rattachement()
    {
        var c = new[] { C(10, "Finance Bill", "2025-01-10", "2025-03-30"), C(11, "Finance Bill", "2026-01-10", "2026-03-30") };
        Assert.Equal(10, BillsRoyaumeUniSource.Rattacher("Finance Bill: Third Reading", new DateTime(2025, 3, 20), c)?.BillId);
        Assert.Equal(11, BillsRoyaumeUniSource.Rattacher("Finance Bill: Third Reading", new DateTime(2026, 3, 20), c)?.BillId);
        // Division hors de toute période : jamais d'approximation.
        Assert.Null(BillsRoyaumeUniSource.Rattacher("Finance Bill: Third Reading", new DateTime(2027, 6, 1), c));
        // Titre sans projet (instrument réglementaire, motion…).
        Assert.Null(BillsRoyaumeUniSource.Rattacher("Opposition Day: UK-EU relations", new DateTime(2026, 3, 1), c));
    }

    [Fact]
    public void Conversion_projet_devenu_loi_etapes_et_sanction_royale()
    {
        var resume = JsonDocument.Parse("""{"billId":100,"shortTitle":"Test Bill","currentHouse":"Lords","originatingHouse":"Commons","introducedSessionId":39,"isAct":true,"isDefeated":false,"billWithdrawn":null,"lastUpdate":"2026-05-02T10:00:00","currentStage":{"description":"Royal Assent"}}""").RootElement;
        var etapes = JsonDocument.Parse("""[{"sortOrder":1,"description":"1st reading","house":"Commons","stageSittings":[{"date":"2025-10-01T00:00:00"}]},{"sortOrder":2,"description":"2nd reading","house":"Commons","stageSittings":[{"date":"2025-11-05T00:00:00"}]},{"sortOrder":9,"description":"Royal Assent","house":"Lords","stageSittings":[{"date":"2026-05-01T00:00:00"}]},{"sortOrder":10,"description":"Pas encore","house":"Lords","stageSittings":[]}]""").RootElement;
        var b = BillsRoyaumeUniSource.Convertir(resume, etapes, "A Bill to test.");
        Assert.True(b.EstLoi);
        Assert.Equal(new DateTime(2026, 5, 1), b.DateLoi);
        Assert.Equal(new DateTime(2025, 10, 1), b.DateDepot);
        Assert.Equal("Royal Assent", b.EtapeCourante);
        Assert.Equal("https://bills.parliament.uk/bills/100", b.Url);
        Assert.Equal(4, b.Etapes.Count);
        Assert.Null(b.Etapes.Single(e => e.Libelle == "Pas encore").DateSeance);   // étape sans séance : date manquante, jamais inventée
    }
}
