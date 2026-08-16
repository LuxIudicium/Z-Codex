using ZCodex.Core.Models;

namespace ZCodex.Core.Templates;

/// <summary>
/// Encodeur/décodeur du format de template GW1 (skill bar).
/// Spec: https://wiki.guildwars.com/wiki/Skill_template_format
///
/// Layout bit-à-bit (version 0) :
///   4  type header (= 14)
///   4  version (= 0)
///   2  prof_code  → bits_per_profession = prof_code * 2 + 4
///   n  primary profession
///   n  secondary profession
///   4  numAttr
///   4  attr_code  → bits_per_attribute  = attr_code + 4
///   (numAttr × [ n attrId | 4 points ])
///   4  skill_code → bits_per_skill      = skill_code + 8
///   (8 × [ n skillId ])
///   1  tail bit (= 0)
/// </summary>
public static class GwTemplateCodec
{
    // Alphabet GW1 : identique au Base64 standard RFC 3548
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

    // Code "format chat" paw·ned² : [index profil - nom;code]
    //   ex. [1 W/Mo - Bob;OwBj...]  ou  [1 W/Mo - Unnamed character;OwBj...]
    public static string FormatChatCode(int index, Profession primary, Profession secondary,
                                        string? name, string code)
    {
        var display = string.IsNullOrWhiteSpace(name) || name == "(unnamed)"
            ? "Unnamed character" : name.Trim();
        return $"[{index} {primary.Profile(secondary)} - {display};{code}]";
    }

    // Lecture inverse de FormatChatCode : extrait les codes GW1 d'un texte collé ou d'un fichier.
    //   • code de chat        "[nom;CODE]"
    //   • ligne .txt Z-Codex  "[N prof - nom;codeO;codeP1;codeP2]" (tous les segments après le nom)
    //   • sinon               le texte tel quel, considéré comme un code brut
    public static List<string> ExtractCodes(string? raw)
    {
        raw = (raw ?? string.Empty).Trim();
        if (raw.StartsWith('[') && raw.EndsWith(']'))
        {
            var segments = raw[1..^1].Split(';');
            if (segments.Length > 1)
                return segments.Skip(1).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        }
        return [raw];
    }

    public static CharacterBuild? Decode(string code, IReadOnlyDictionary<int, Skill> skillsById)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        try
        {
            var bits = new BitReader(CodeToBytes(code));

            if (bits.Read(4) != 14) return null; // type = skill template
            bits.Read(4);                         // version (ignoré)

            int profCode = bits.Read(2);
            int profBits = profCode * 2 + 4;
            var primary   = (Profession)bits.Read(profBits);
            var secondary = (Profession)bits.Read(profBits);

            int attrCount = bits.Read(4);
            int attrCode  = bits.Read(4);
            int attrBits  = attrCode + 4;

            var attributes = new Dictionary<string, int>();
            for (int i = 0; i < attrCount; i++)
            {
                int attrId     = bits.Read(attrBits);
                int attrPoints = bits.Read(4);
                attributes[AttributeKey(attrId)] = attrPoints;
            }

            int skillCode = bits.Read(4);
            int skillBits = skillCode + 8;

            var skills = new Skill?[8];
            for (int i = 0; i < 8; i++)
            {
                int skillId = bits.Read(skillBits);
                if (skillId != 0 && skillsById.TryGetValue(skillId, out var skill))
                    skills[i] = skill;
            }

            // Le header 4 bits ne qualifie PAS un code : tout texte dont le premier caractère
            // base64 vaut 14 modulo 16 (O, e, u, +) le franchit, et les bits absents étant lus
            // à 0, « erreur » ou « Oui » s'importaient en build fantôme (professions hors enum,
            // aucune skill) sans le moindre message. Un vrai code porte TOUT le layout et des
            // professions du domaine.
            if (bits.Overrun) return null;
            if (!Enum.IsDefined(primary) || !Enum.IsDefined(secondary)) return null;

            return new CharacterBuild
            {
                PrimaryProfession   = primary,
                SecondaryProfession = secondary,
                Attributes          = attributes,
                Skills              = skills,
            };
        }
        catch { return null; }
    }

    public static string Encode(CharacterBuild build, IReadOnlyDictionary<string, int> skillIdsByName)
        => Encode(build, skillIdsByName, out _);

    /// <param name="dataLength">
    /// Nombre de caractères base64 qui portent réellement les données, bit de queue EXCLU.
    /// C'est la longueur que paw·ned² préfixe à chaque code dans un .pwnd, et elle vaut un
    /// caractère de moins que la chaîne rendue dès que l'alignement sur l'octet en ajoute un
    /// (cf. PwndExporter). Les caractères au-delà ne portent que des zéros.
    /// </param>
    public static string Encode(CharacterBuild build, IReadOnlyDictionary<string, int> skillIdsByName,
                                out int dataLength)
    {
        var bits = new BitWriter();

        bits.Write(14, 4); // type = skill template
        bits.Write(0,  4); // version = 0

        // prof_code = 0 → 4 bits par profession (couvre les 10 professions GW1)
        bits.Write(0, 2);
        bits.Write((int)build.PrimaryProfession,   4);
        bits.Write((int)build.SecondaryProfession, 4);

        var attrList = build.Attributes.Take(16).ToList();
        bits.Write(attrList.Count, 4);

        int maxAttrId = attrList.Count > 0 ? attrList.Select(a => ParseAttrId(a.Key)).Max() : 0;
        int attrCode  = MinCode(maxAttrId, 4);
        int attrBits  = attrCode + 4;
        bits.Write(attrCode, 4);

        foreach (var (key, points) in attrList)
        {
            bits.Write(ParseAttrId(key),              attrBits);
            bits.Write(Math.Clamp(points, 0, 15), 4);
        }

        var skillIds = new int[8];
        for (int i = 0; i < 8; i++)
        {
            var skill = build.Skills[i];
            if (skill != null && skillIdsByName.TryGetValue(skill.Name, out int sid))
                skillIds[i] = sid;
        }

        int maxSkillId = skillIds.Max();
        int skillCode  = MinCode(maxSkillId, 8);
        int skillBits  = skillCode + 8;
        bits.Write(skillCode, 4);

        foreach (int id in skillIds)
            bits.Write(id, skillBits);

        dataLength = (bits.BitCount + 5) / 6; // relevé AVANT le bit de queue
        bits.Write(0, 1); // tail bit obligatoire

        return BytesToCode(bits.ToBytes());
    }

    /// <summary>Caractère de l'alphabet GW1 (= Base64 RFC 3548) pour une valeur 0-63.</summary>
    public static char Base64Digit(int value) => Alphabet[value];

    // ── Conversion helpers ────────────────────────────────────────────────────

    public static AttributesBuild ToAttributesBuild(IReadOnlyDictionary<string, int> attrs) =>
        new() { Allocations = attrs
            .Select(kv => new AttributeAllocation(ParseAttrId(kv.Key), kv.Value))
            .Where(a => a.AttributeId >= 0)   // ID 0 = Fast Casting (Mesmer primaire) : ne pas filtrer
            .ToList() };

    public static Dictionary<string, int> ToAttributeDict(AttributesBuild? attrs) =>
        attrs?.Allocations.ToDictionary(a => AttributeKey(a.AttributeId), a => a.Points) ?? new();

    /// <summary>Clé d'un attribut dans CharacterBuild.Attributes : l'ID de template, JAMAIS le nom EN.
    /// Point de passage unique — tout code qui lit ce dictionnaire doit passer par ici.</summary>
    public static string AttributeKey(int attributeId) => $"attr_{attributeId}";

    // bits nécessaires pour représenter maxValue, moins baseBits (min 0)
    private static int MinCode(int maxValue, int baseBits)
    {
        if (maxValue <= 0) return 0;
        int n = 0, v = maxValue;
        while (v > 0) { v >>= 1; n++; }
        return Math.Max(0, n - baseBits);
    }

    private static int ParseAttrId(string key)
        => key.StartsWith("attr_") && int.TryParse(key[5..], out int id) ? id : -1;

    // ── Encodage Base64 LSB-first ─────────────────────────────────────────────

    private static byte[] CodeToBytes(string code)
    {
        var bytes = new List<byte>();
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
                bitsIn -= 8;
            }
        }
        // Émettre l'octet partiel final (bits restants < 8) : sinon les bits de poids fort
        // du dernier skill sont perdus quand le flux ne finit pas sur une frontière d'octet
        // (ex: 25 chars = 150 bits → 18 octets, 6 bits jetés → slot 8 corrompu).
        if (bitsIn > 0)
            bytes.Add((byte)(buffer & 0xFF));
        return bytes.ToArray();
    }

    private static string BytesToCode(byte[] bytes)
    {
        var sb = new System.Text.StringBuilder();
        int buffer = 0, bitsIn = 0;

        foreach (byte b in bytes)
        {
            buffer |= b << bitsIn;
            bitsIn += 8;
            while (bitsIn >= 6)
            {
                sb.Append(Alphabet[buffer & 0x3F]);
                buffer >>= 6;
                bitsIn -= 6;
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

        // Vrai dès qu'une lecture a dépassé la fin du flux. Les bits manquants sont rendus à 0 :
        // sans ce témoin, un flux trop court se décode silencieusement en valeurs nulles.
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

        public int BitCount => _bytes.Count * 8 + _bitsIn;

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
