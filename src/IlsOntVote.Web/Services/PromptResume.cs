using System.Text;

namespace IlsOntVote.Web.Services;

/// <summary>
/// Prompt prêt à coller dans l'assistant d'IA de son choix (bouton « Résumer avec une IA »). Le site n'appelle AUCUNE IA lui-même (pas de clé, pas de coût) :
/// il prépare un texte qui impose la neutralité et les sources, et c'est le lecteur qui l'envoie, ou non, à l'outil qu'il choisit. Le prompt ne contient que des
/// données publiques de la page (titre du texte, liens officiels), jamais de donnée sur le lecteur.
/// </summary>
public static class PromptResume
{
    /// <summary>
    /// Construit le prompt. Demande le résumé du CONTENU du texte (ce que la loi prévoit), pas des métadonnées ni des votes : aucun chiffre de scrutin n'est donné à l'IA,
    /// seulement le titre et les adresses où le texte lui-même se lit. Longueur libre (un texte de loi ne se résume pas en 8 lignes). Si l'assistant ne peut pas lire
    /// les adresses, il doit le dire au lieu de résumer le titre ou d'inventer (constaté : sans cette consigne, les IA résumaient les métadonnées).
    /// </summary>
    public static string Construire(Langue l, string titre, IEnumerable<string> liens)
    {
        // Textes en base (clés « prompt-resume-… ») : le prompt est dans la langue de l'interface, sans aucun texte dans le code.
        var sb = new StringBuilder();
        sb.Append(l.T("prompt-resume-corps")).Append("\n\n");
        sb.Append(l.T("prompt-resume-texte")).Append(titre).Append('\n');
        sb.Append(l.T("prompt-resume-sources")).Append('\n');
        foreach (var x in liens.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct()) sb.Append("- ").Append(x).Append('\n');
        return sb.ToString().TrimEnd();
    }
}
