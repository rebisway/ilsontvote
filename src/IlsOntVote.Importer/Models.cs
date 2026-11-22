namespace IlsOntVote.Importer;

public static class Chambres
{
    public const string AN = "AN";
    public const string Senat = "SENAT";
    public const string UE = "UE";
    public const string Commons = "COMMONS";
    public const string Lords = "LORDS";
    public const string UsHouse = "USHOUSE";
    public const string UsSenate = "USSENATE";
    public const string ItCamera = "ITCAMERA";
    public const string ItSenate = "ITSENATE";
    public const string IeDail = "IEDAIL";
    public const string IeSeanad = "IESEANAD";
    public const string ChNr = "CHNR";
    public const string ChSr = "CHSR";
    public const string CaCommons = "CACOMMONS";
    public const string CaSenate = "CASENATE";
}

/// <summary>Position d'un vote, telle que stockée en base (colonne votes.position).</summary>
public static class Positions
{
    public const string Pour = "pour";
    public const string Contre = "contre";
    public const string Abstention = "abstention";
    public const string NonVotant = "non_votant";
}

public sealed record GroupeRow(
    string Id, string Chambre, int? Legislature, string Sigle, string Libelle, string? Couleur, int Ordre);

/// <summary>
/// Place d'un groupe dans l'hémicycle (gauche -> droite) selon son sigle, y compris
/// les groupes des législatures précédentes et ceux du Sénat. Valeur initiale
/// seulement : l'import ne l'écrase jamais (modifiable à la main en base).
/// </summary>
public static class GroupeOrdre
{
    public static int Pour(string sigle) => sigle.ToUpperInvariant() switch
    {
        // Assemblée nationale
        "LFI-NFP" or "LFI-NUPES" or "FI" or "LFI" => 10,
        "GDR" or "GDR-NUPES" or "GDR - NUPES" => 20,
        "ECOS" or "ECO" or "ECOLO" or "E" => 30,
        "SOC" or "SOC-A" or "SOCIAL" or "NG" or "S" or "SRC" => 40,
        "EDS" => 45,
        "LIOT" or "LT" or "LC" => 50,
        "DEM" or "MODEM" => 60,
        "EPR" or "RE" or "LAREM" or "LREM" or "AE" or "RRDP" => 70,
        "HOR" or "UAI" or "UDI" or "UDI-I" or "UDI_I" or "UDI-A-I" or "UDI-AGIR" or "AGIR-E" => 80,
        "DR" or "LR" or "UMP" or "LES RÉPUBLICAINS" => 90,
        "UDR" => 110,
        "AD" => 115,
        "RN" => 120,
        "NI" or "AUCUN" => 130,
        // Parlement européen (sigles HowTheyVote)
        "THE LEFT" or "GUE/NGL" => 11,
        "GREENS/EFA" => 31,
        "S&D" => 41,
        "RENEW" => 61,
        "EPP" => 81,
        "ECR" => 101,
        "PFE" => 111,
        "ESN" => 121,
        // Sénat (codes de la base « Sénateurs »)
        "CRCE-K" or "CRC-SPG" or "CRCE" or "CRC" or "COM" or "CRCE-" or "C" => 10,
        "GEST" or "EST" or "ECOLO-SEN" or "EELV" => 30,
        "SER" or "SOCR" or "SOC-SEN" => 40,
        "RDSE" or "RDSE-SEN" or "RSE" or "RDE" or "GD" => 55,
        "RDPI" or "LREM-SEN" or "RTLI" or "LIRT" => 65,
        "UC" or "UC-UDF" or "UCR" or "UDF" or "UC-SEN" or "UCDP" => 75,
        "INDEP" or "LIRT-SEN" => 78,
        "UMP-SEN" or "LR-SEN" => 92,
        _ => 100,
    };
}

public sealed record ParlementaireRow(
    string Id, string Chambre, string? Civilite, string Prenom, string Nom, string Slug,
    string? Departement, string? Circonscription);

public sealed record MandatRow(string ParlementaireId, int? Legislature, DateOnly DateDebut, DateOnly? DateFin);

public sealed record AppartenanceRow(string ParlementaireId, string GroupeId, DateOnly DateDebut, DateOnly? DateFin);

public sealed record ScrutinRow(
    string Id, string Chambre, int Legislature, int Numero, DateOnly DateScrutin, string Titre,
    string? TypeVote, string? Sort, string? DossierRef, bool SurEnsemble,
    int? NbVotants, int? NbPour, int? NbContre, int? NbAbstentions, int? NbNonVotants,
    string? ModePublication,
    int NbVotesManquants = 0,
    string? Note = null,
    string? TypeVoteLibelle = null,
    string? TypeMajorite = null,
    int? SuffragesExprimes = null,
    int? SuffragesRequis = null,
    string? TitreEn = null);

/// <summary>
/// Un vote enregistré. <c>Position</c> est la position FINALE (après mise au
/// point éventuelle) ; <c>PositionOrigine</c> la position telle que publiée
/// au scrutin (null si le parlementaire n'apparaissait dans aucune liste, c'est-à-dire
/// absent à l'origine, et n'a fait qu'une mise au point). <c>OrigineInconnue</c> :
/// la source signale une mise au point sans dire quelle était la position d'origine (Sénat).
/// </summary>
public sealed record VoteRow(
    string ScrutinId, string ParlementaireId, string? GroupeId, string Position,
    bool ParDelegation, bool Rectifie, string? PositionOrigine, bool OrigineInconnue = false);

public sealed record Annuaire(
    IReadOnlyList<GroupeRow> Groupes,
    IReadOnlyList<ParlementaireRow> Parlementaires,
    IReadOnlyList<MandatRow> Mandats,
    IReadOnlyList<AppartenanceRow> Appartenances);

public sealed record ScrutinAvecVotes(ScrutinRow Scrutin, IReadOnlyList<VoteRow> Votes);
