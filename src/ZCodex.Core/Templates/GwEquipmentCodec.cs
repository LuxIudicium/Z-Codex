using ZCodex.Core.Data;
using ZCodex.Core.Models;

namespace ZCodex.Core.Templates;

/// <summary>Une ligne du récapitulatif d'équipement : emplacement (« F1 Arme », « Torse ») et
/// libellé composé de l'objet, tous deux dans la langue d'affichage.</summary>
public sealed record EquipmentSummaryLine(string Slot, string Label);

/// <summary>
/// Encodeur/décodeur du format de template équipement GW1 (codes P...).
/// Spec: https://wiki.guildwars.com/wiki/Equipment_template_format
///
/// Layout bit-à-bit (version 0) :
///   4  type header (= 15)
///   4  version (= 0)
///   4  bitsPerItem  (valeur LITTÉRALE, pas un code : ≥ 9 en pratique)
///   4  bitsPerMod   (valeur LITTÉRALE, pas un code)
///   3  itemCount (0..7)
///   Pour chaque item :
///     3  slot (0-6)
///     n  itemId   (bitsPerItem bits ; 0 = slot vide)
///     2  modCount (0-3)
///     4  dye
///     Pour chaque modifier :
///       n  modifierId (bitsPerMod bits)
///   (pas de tail bit — uniquement padding base64)
/// </summary>
public static class GwEquipmentCodec
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

    public static EquipmentBuild? Decode(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        try
        {
            var bits = new BitReader(CodeToBytes(code));

            if (bits.Read(4) != 15) return null; // type = equipment template
            bits.Read(4);                         // version (ignoré)

            int bitsPerItem = bits.Read(4); // valeur littérale (ex: 9)
            int bitsPerMod  = bits.Read(4); // valeur littérale (ex: 9)
            int itemCount   = bits.Read(3);

            // Même faiblesse que les codes O : le header seul laisse passer n'importe quel texte
            // commençant par P, f, v ou / (« Pareil », « fusion »...). Un code sans aucun objet
            // n'a pas de sens, et le slot est borné par la spec.
            if (bitsPerItem < 1 || bitsPerMod < 1 || itemCount == 0) return null;

            var items = new List<EquipmentItem>();
            for (int i = 0; i < itemCount; i++)
            {
                int slot     = bits.Read(3);
                int itemId   = bits.Read(bitsPerItem);
                int modCount = bits.Read(2);
                int dye      = bits.Read(4);

                if (slot > 6) return null; // 3 bits lus, mais la spec s'arrête à 6

                var modIds = new List<int>();
                for (int j = 0; j < modCount; j++)
                    modIds.Add(bits.Read(bitsPerMod));

                items.Add(new EquipmentItem
                {
                    Slot        = slot,
                    ItemId      = itemId,
                    Dye         = dye,
                    ModifierIds = modIds,
                });
            }

            if (bits.Overrun) return null; // flux tronqué = code invalide, pas un objet vide

            // Un code P = une vue plate (armes + armure) → armure partagée + set F1.
            return EquipmentBuild.FromFlat(items);
        }
        catch { return null; }
    }

    // Un code P par set : encode les armes du set demandé + l'armure partagée.
    public static string Encode(EquipmentBuild build, int set) => Encode(build.ItemsOfSet(set));

    // Contenu d'un fichier template : un code P par ligne, un par set (un seul set →
    // une ligne, fichier compatible jeu). L'inverse de DecodeLines + Combine.
    public static string EncodeFileText(EquipmentBuild build) =>
        string.Join(Environment.NewLine, build.ExportSets().Select(k => Encode(build, k)));

    // Décode tous les codes P d'un texte multi-lignes (fichier template : un code par ligne).
    // Les lignes non reconnues sont ignorées ; liste vide si aucun code d'équipement.
    public static List<EquipmentBuild> DecodeLines(string text) =>
        text.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .Select(Decode)
            .Where(b => b != null)
            .Select(b => b!)
            .ToList();

    // Tous les codes d'un équipement : un seul set → code brut (collable en jeu) ;
    // plusieurs sets → une ligne "F1 : Pxxx" par set non vide.
    public static string EncodeAllSets(EquipmentBuild build)
    {
        var sets = build.ExportSets();
        return sets.Count == 1
            ? Encode(build, sets[0])
            : string.Join(Environment.NewLine, sets.Select(k => $"F{k + 1} : {Encode(build, k)}"));
    }

    public static string Encode(IEnumerable<EquipmentItem> items)
    {
        var capped = items.Take(7).ToList();

        int maxItemId = capped.Count > 0 ? capped.Max(i => i.ItemId) : 0;
        int maxModId  = capped.SelectMany(i => i.ModifierIds).DefaultIfEmpty(0).Max();

        int bitsPerItem = Math.Max(9, BitsNeeded(maxItemId));
        int bitsPerMod  = Math.Max(9, BitsNeeded(maxModId));

        var bits = new BitWriter();
        bits.Write(15, 4);
        bits.Write(0,  4);
        bits.Write(bitsPerItem, 4);
        bits.Write(bitsPerMod,  4);
        bits.Write(capped.Count, 3);

        foreach (var item in capped)
        {
            bits.Write(item.Slot,  3);
            bits.Write(item.ItemId, bitsPerItem);
            var mods = item.ModifierIds.Take(3).ToList();
            bits.Write(mods.Count, 2);
            bits.Write(item.Dye,   4);
            foreach (int modId in mods)
                bits.Write(modId, bitsPerMod);
        }

        return BytesToCode(bits.ToBytes());
    }

    /// <summary>
    /// Retourne un résumé lisible d'un équipement, pour l'affichage dans l'UI (infobulle ⚔).
    /// Chaque ligne porte le nom COMPOSÉ de l'emplacement — insigne + rune en armure, préfixe +
    /// suffixe en arme — exactement comme la liste de l'éditeur d'équipement, plus l'inscription.
    /// Un seul set d'armes : "Torse: Robe krytienne du survivant (Vigueur : bonus supérieur)" ;
    /// plusieurs sets : les lignes d'armes sont préfixées "F1 ", "F2 ", ...
    /// </summary>
    public static string Summarize(EquipmentBuild build) =>
        build.IsEmpty
            ? Empty
            : string.Join("\n", SummarizeLines(build).Select(l => $"{l.Slot}: {l.Label}"));

    /// <summary>
    /// Le même récapitulatif, mais emplacement et libellé SÉPARÉS : l'aperçu du navigateur les
    /// aligne en deux colonnes et les style différemment, ce qu'une chaîne unique interdit.
    /// <see cref="Summarize"/> en découle, pour que les deux ne puissent pas diverger.
    /// Liste vide pour un équipement vide (l'appelant décide quoi afficher à la place).
    /// </summary>
    /// <param name="withInscriptionEffects">Fait suivre chaque inscription de son effet
    /// (« "Tout en muscles" - Dégâts +15% ; énergie -5 »). Son nom seul ne dit pas ce qu'elle
    /// fait, contrairement à un insigne ou à un préfixe d'arme. Coûte une ligne plus longue :
    /// l'aperçu du navigateur l'active, l'infobulle ⚔ non.</param>
    public static IReadOnlyList<EquipmentSummaryLine> SummarizeLines(
        EquipmentBuild build, bool withInscriptionEffects = false)
    {
        var lines = new List<EquipmentSummaryLine>();
        if (build.IsEmpty) return lines;

        // Un seul set d'armes → pas de préfixe ; plusieurs → "F1 Arme", "F2 Arme"…
        bool multiSet = build.WeaponSets.Count(s => !s.IsEmpty) > 1;
        for (int k = 0; k < build.WeaponSets.Count; k++)
            foreach (var item in build.WeaponSets[k].Items)
                lines.Add(new EquipmentSummaryLine(
                    (multiSet ? $"F{k + 1} " : "") + GwEquipmentData.DisplaySlot(item.Slot),
                    ItemLabel(item, withInscriptionEffects)));

        foreach (var item in build.Armor)
            lines.Add(new EquipmentSummaryLine(GwEquipmentData.DisplaySlot(item.Slot),
                                               ItemLabel(item, withInscriptionEffects)));

        return lines;
    }

    // Marqueur d'emplacement vide, dans la langue d'affichage.
    private static string Empty => ZCodex.Core.Models.AppLanguage.IsFr ? "(vide)" : "(empty)";

    // Nom d'objet affiché : FR si connu, sinon EN sans le préfixe « PvP ».
    private static string ItemName(string en) =>
        GwEquipmentItemsFr.DisplayName(GwEquipmentInfo.DisplayItemName(en));

    // Nom d'un mod dans la langue d'affichage (le nom EN du catalogue reste la clé logique).
    // Les insignes ne sont PAS dans GwEquipmentModsFr : ils ont leur propre table (GwInsigniaFr),
    // qui ne donne que le fragment (« du survivant ») → on lui rend son substantif.
    private static string ModName(int modId)
    {
        if (!GwEquipmentData.Modifiers.TryGetValue(modId, out var en)) return $"#mod{modId}";
        if (!IsCategory(modId, ModCategory.Insignia))
            return GwEquipmentModsFr.DisplayName(modId, en);
        return AppLanguage.IsFr && GwInsigniaFr.ComposedForm(modId) is { } f ? $"Insigne {f}" : en;
    }

    private static bool IsCategory(int modId, ModCategory c) =>
        GwEquipmentInfo.Mods.TryGetValue(modId, out var m) && m.Category == c;

    // Libellé complet d'un emplacement : nom composé (insigne/rune, préfixe/suffixe) SUIVI de
    // l'inscription — celle-ci n'entre jamais dans le nom composé, ni en EN ni en FR.
    // Un emplacement sans objet mais porteur d'upgrades (l'éditeur l'autorise) liste ses mods.
    private static string ItemLabel(EquipmentItem item, bool withInscriptionEffects = false)
    {
        var mods = item.ModifierIds;
        if (item.ItemId == 0)
            return mods.Count == 0 ? Empty : $"{Empty} [{string.Join(", ", mods.Select(ModName))}]";

        string label = GwEquipmentInfo.ComposeItemName(item.ItemId, mods);
        var inscriptions = mods.Where(id => IsCategory(id, ModCategory.Inscription))
            .Select(id => withInscriptionEffects ? InscriptionLabel(id) : ModName(id))
            .ToList();
        return inscriptions.Count == 0 ? label : $"{label} · {string.Join(" · ", inscriptions)}";
    }

    // Inscription suivie de son effet : « "Tout en muscles" - Dégâts +15% ; énergie -5 ».
    // Repli sur le nom seul si le mod n'a pas de description répertoriée.
    private static string InscriptionLabel(int modId)
    {
        var name = ModName(modId);
        return GwEquipmentModDetails.ByModId.TryGetValue(modId, out var d)
            && GwEquipmentDescFr.Translate(d.Description) is { Length: > 0 } effect
                ? $"{name} - {effect}"
                : name;
    }

    public static string SummarizeItem(EquipmentItem item)
    {
        string slotName = GwEquipmentData.DisplaySlot(item.Slot);
        string itemName = item.ItemId == 0
            ? Empty
            : GwEquipmentData.Items.TryGetValue(item.ItemId, out var n) ? ItemName(n) : $"#item{item.ItemId}";
        string dyeName  = GwEquipmentData.DisplayDye(item.Dye);

        string mods = item.ModifierIds.Count > 0
            ? " [" + string.Join(", ", item.ModifierIds.Select(ModName)) + "]"
            : string.Empty;

        return $"[{slotName}] {itemName} — {dyeName}{mods}";
    }

    private static int BitsNeeded(int value)
    {
        if (value <= 0) return 0;
        int n = 0;
        while (value > 0) { value >>= 1; n++; }
        return n;
    }

    // ── Encodage Base64 LSB-first (identique aux skill templates) ─────────────

    private static byte[] CodeToBytes(string code)
    {
        var bytes  = new List<byte>();
        int buffer = 0, bitsIn = 0;

        foreach (char c in code)
        {
            int val = Alphabet.IndexOf(c);
            if (val < 0) continue;
            buffer |= val << bitsIn;
            bitsIn += 6;
            while (bitsIn >= 8)
            {
                bytes.Add((byte)(buffer & 0xFF));
                buffer >>= 8;
                bitsIn  -= 8;
            }
        }
        // Émettre l'octet partiel final, sinon les bits de poids fort du dernier item/mod
        // sont perdus quand le flux ne termine pas sur une frontière d'octet (cf. GwTemplateCodec).
        if (bitsIn > 0)
            bytes.Add((byte)(buffer & 0xFF));
        return bytes.ToArray();
    }

    private static string BytesToCode(byte[] bytes)
    {
        var sb     = new System.Text.StringBuilder();
        int buffer = 0, bitsIn = 0;

        foreach (byte b in bytes)
        {
            buffer |= b << bitsIn;
            bitsIn += 8;
            while (bitsIn >= 6)
            {
                sb.Append(Alphabet[buffer & 0x3F]);
                buffer >>= 6;
                bitsIn  -= 6;
            }
        }
        if (bitsIn > 0)
            sb.Append(Alphabet[buffer & 0x3F]);

        return sb.ToString();
    }

    // ── Bit I/O ───────────────────────────────────────────────────────────────

    private class BitReader(byte[] data)
    {
        private int _pos;

        // Vrai dès qu'une lecture a dépassé la fin du flux (cf. GwTemplateCodec.BitReader).
        public bool Overrun { get; private set; }

        public int Read(int count)
        {
            int result = 0;
            for (int i = 0; i < count; i++)
            {
                int byteIndex = _pos / 8;
                int bitIndex  = _pos % 8;
                if (byteIndex >= data.Length) { Overrun = true; break; }
                result |= ((data[byteIndex] >> bitIndex) & 1) << i;
                _pos++;
            }
            return result;
        }
    }

    private class BitWriter
    {
        private readonly List<byte> _bytes = [];
        private int _buffer, _bitsIn;

        public void Write(int value, int count)
        {
            for (int i = 0; i < count; i++)
            {
                _buffer |= ((value >> i) & 1) << _bitsIn;
                _bitsIn++;
                if (_bitsIn == 8)
                {
                    _bytes.Add((byte)_buffer);
                    _buffer = 0;
                    _bitsIn = 0;
                }
            }
        }

        public byte[] ToBytes()
        {
            var result = new List<byte>(_bytes);
            if (_bitsIn > 0) result.Add((byte)_buffer);
            return result.ToArray();
        }
    }
}
