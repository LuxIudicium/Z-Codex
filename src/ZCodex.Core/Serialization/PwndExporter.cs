using System.Text;
using ZCodex.Core.Importers;
using ZCodex.Core.Models;
using ZCodex.Core.Templates;

namespace ZCodex.Core.Serialization;

/// <summary>
/// Écriture du format de team build paw·ned² (.pwnd) — pendant de <see cref="PwndImporter"/>.
///
/// Structure, relevée sur la bibliothèque gw-tactics.de et reproduite OCTET POUR OCTET sur les
/// 20 fichiers de référence :
///
///   ligne 1   en-tête fixe, 80 caractères pile (d'où le premier repli juste après)
///   ensuite   '&gt;' + N emplacements + '&lt;', replié tous les 80 caractères, CRLF, CP1252
///   emplacement = [1 car. = longueur du code][code template GW][queue de 7 car.]["CCg"]
///
/// Le caractère de longueur compte les caractères base64 qui portent les DONNÉES, bit de queue
/// exclu : c'est exactement le <c>dataLength</c> de
/// <see cref="GwTemplateCodec.Encode(CharacterBuild, IReadOnlyDictionary{string, int}, out int)"/>,
/// et non <c>code.Length</c> — l'alignement sur l'octet ajoute parfois un caractère de bourrage
/// que paw·ned² ne compte pas.
///
/// Le format ne transporte QUE professions + attributs + 8 compétences. Nom de personnage,
/// équipement, notes, genre, rangs de titre, flux, roster de spike, cadenas et arborescence des
/// variantes ne survivent pas à l'export : l'appelant choisit quels personnages aplatir.
/// </summary>
public static class PwndExporter
{
    public const string Extension = PwndImporter.Extension;

    private const string Header =
        "pwnd0000?download paw·ned² @ www.gw-tactics.de Copyright numma_cway aka Redeemer";
    private const string Terminator = "CCg";

    // Queue par personnage : 24 bits à zéro, un compteur à zéro, 12 bits à zéro. paw·ned² y range
    // un champ non identifié (0 à 3 valeurs de 6 bits) ; le compteur à zéro est un état qu'il
    // écrit lui-même (fichiers TestPawned3.pwnd et Frustration Rupt Spike.pwnd, 8 emplacements
    // sur 8), donc un export neutre reste un fichier canonique.
    private const string EmptyTail = "AAAAAAA";

    private const int LineWidth = 80;

    // paw·ned² pense en équipes de 8 et tous ses fichiers en comptent exactement 8 : en dessous on
    // complète avec des emplacements vides plutôt que de livrer un fichier qu'il lira à moitié.
    private const int MinSlots = 8;

    public static void Save(IEnumerable<CharacterBuild> characters, string filePath,
                            IReadOnlyDictionary<string, int> skillIdsByName)
        => File.WriteAllText(filePath, Build(characters, skillIdsByName), Encoding.Latin1);

    /// <summary>Contenu texte du fichier, terminaisons CRLF incluses.</summary>
    public static string Build(IEnumerable<CharacterBuild> characters,
                              IReadOnlyDictionary<string, int> skillIdsByName)
    {
        var slots = characters.ToList();
        while (slots.Count < MinSlots) slots.Add(new CharacterBuild());

        var body = new StringBuilder(">");
        foreach (var slot in slots)
        {
            var code = GwTemplateCodec.Encode(slot, skillIdsByName, out int dataLength);
            body.Append(GwTemplateCodec.Base64Digit(dataLength))
                .Append(code, 0, dataLength)
                .Append(EmptyTail)
                .Append(Terminator);
        }
        body.Append('<');

        var text = new StringBuilder(Header).Append("\r\n");
        for (int i = 0; i < body.Length; i += LineWidth)
            text.Append(body, i, Math.Min(LineWidth, body.Length - i)).Append("\r\n");
        return text.ToString();
    }

    /// <summary>
    /// Aplatit un personnage et TOUTE sa descendance de variantes, dans l'ordre d'affichage
    /// (racine puis variantes, en profondeur).
    /// </summary>
    public static IEnumerable<CharacterBuild> Flatten(CharacterBuild character)
    {
        yield return character;
        foreach (var variant in character.Variants)
            foreach (var descendant in Flatten(variant))
                yield return descendant;
    }
}
