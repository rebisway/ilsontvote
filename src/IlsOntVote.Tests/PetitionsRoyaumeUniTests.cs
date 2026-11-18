using System.Text.Json;
using IlsOntVote.Importer;

namespace IlsOntVote.Tests;

/// <summary>Pétitions du Parlement britannique : conversion de l'API JSON et empreinte du relevé.</summary>
public class PetitionsRoyaumeUniTests
{
    private static PetitionRow Lire(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return PetitionsRoyaumeUniSource.Convertir(doc.RootElement.Clone());
    }

    [Fact]
    public void Petition_debattue_avec_reponse_du_gouvernement()
    {
        var p = Lire("""
            {"type":"petition","id":701,"attributes":{"action":"Call a General Election","background":"Fond.","additional_details":"Détails.","state":"closed","signature_count":3084711,
             "opened_at":"2024-11-20T09:13:21.152Z","closed_at":"2025-05-20T22:59:59.999Z","response_threshold_reached_at":"2024-11-23T16:51:20.000Z",
             "debate_threshold_reached_at":"2024-11-23T21:18:30.000Z","scheduled_debate_date":"2025-01-06",
             "government_response":{"responded_on":"2024-12-06","summary":"Réponse.","created_at":"2024-12-06T16:09:44.690Z"},
             "debate":{"debated_on":"2025-01-06","transcript_url":"https://hansard.parliament.uk/x","video_url":null}}}
            """);
        Assert.Equal("UK-701", p.Id);
        Assert.Equal("UK", p.Chambre);
        Assert.Equal("closed", p.Statut);
        Assert.Equal(3084711, p.NbVotes);
        Assert.Equal("https://petition.parliament.uk/petitions/701", p.Url);
        Assert.Equal(new DateTime(2024, 11, 20), p.DatePublication);
        Assert.Equal(new DateTime(2025, 5, 20), p.DateLimiteVote);
        Assert.Equal(new DateTime(2024, 12, 6), p.ReponseLe);
        Assert.Equal(new DateTime(2025, 1, 6), p.DebatLe);
        Assert.Equal(new DateTime(2025, 1, 6), p.DebatPrevuLe);
        Assert.Equal("https://hansard.parliament.uk/x", p.DebatUrl);
        Assert.Equal("Fond.\n\nDétails.", p.Description);
    }

    [Fact]
    public void Petition_ouverte_sans_reponse_ni_debat_les_etapes_restent_nulles()
    {
        // « government_response » et « debate » sont null tant que l'étape n'a pas eu lieu : manquant, jamais une date inventée ; soutiens absents = NULL (pas 0).
        var p = Lire("""{"type":"petition","id":9,"attributes":{"action":"A","background":"B","state":"open","signature_count":null,"opened_at":"2026-11-01T10:00:00Z","closed_at":"2027-05-01T10:00:00Z","government_response":null,"debate":null}}""");
        Assert.Null(p.NbVotes);
        Assert.Null(p.ReponseLe);
        Assert.Null(p.DebatLe);
        Assert.Null(p.DebatUrl);
        Assert.Equal("open", p.Statut);
    }

    [Fact]
    public void Empreinte_change_quand_une_reponse_apparait_meme_sans_changement_de_soutiens()
    {
        var avant = Lire("""{"type":"petition","id":5,"attributes":{"action":"A","background":"B","state":"open","signature_count":10000,"government_response":null,"debate":null}}""");
        var apres = Lire("""{"type":"petition","id":5,"attributes":{"action":"A","background":"B","state":"open","signature_count":10000,"government_response":{"responded_on":"2026-11-10"},"debate":null}}""");
        Assert.NotEqual(PetitionsRoyaumeUniSource.Empreinte([avant]), PetitionsRoyaumeUniSource.Empreinte([apres]));
        // Même contenu, autre ordre : même empreinte (tri interne).
        var autre = Lire("""{"type":"petition","id":6,"attributes":{"action":"A","background":"B","state":"open","signature_count":1}}""");
        Assert.Equal(PetitionsRoyaumeUniSource.Empreinte([avant, autre]), PetitionsRoyaumeUniSource.Empreinte([autre, avant]));
    }
}
