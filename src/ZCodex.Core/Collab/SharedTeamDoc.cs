using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ZCodex.Core.Models;
using ZCodex.Core.Serialization;

namespace ZCodex.Core.Collab;

/// <summary>
/// Horodatage d'une unité du document partagé : horloge logique (de Lamport) et auteur. Le plus
/// grand l'emporte ; à horloge égale, l'identifiant d'auteur départage — un ordre TOTAL, le même
/// sur tous les postes, qui est ce qui garantit qu'ils finissent tous sur le même build.
/// </summary>
public sealed class UnitStamp : IComparable<UnitStamp>
{
    public static readonly UnitStamp Zero = new();

    [JsonPropertyName("c")] public long Clock { get; set; }
    [JsonPropertyName("by")] public string By { get; set; } = string.Empty;
    /// <summary>Unité supprimée (pierre tombale) : sans elle, un participant qui n'a pas encore
    /// vu la suppression ferait réapparaître le personnage en renvoyant sa copie.</summary>
    [JsonPropertyName("del")] public bool Deleted { get; set; }

    public UnitStamp() { }
    public UnitStamp(long clock, string by, bool deleted) { Clock = clock; By = by; Deleted = deleted; }

    public int CompareTo(UnitStamp? other)
    {
        if (other is null) return 1;
        int c = Clock.CompareTo(other.Clock);
        return c != 0 ? c : string.CompareOrdinal(By, other.By);
    }
}

/// <summary>
/// Le teambuild partagé, tel que CE poste le connaît, découpé en UNITÉS horodatées :
/// <list type="bullet">
/// <item><c>c:&lt;id&gt;</c> — un personnage racine avec toutes ses variantes ;</item>
/// <item><c>order</c> — l'ordre des personnages racines ;</item>
/// <item><c>k:&lt;clé&gt;</c> — chaque réglage du teambuild (spike, cadenas, rituels, flux…).</item>
/// </list>
///
/// Pourquoi des horodatages et pas une simple comparaison « avant / après » : le serveur ne fait
/// que recopier des teambuilds COMPLETS. Quand A retouche le perso 1 pendant que B retouche le
/// perso 2, chacun envoie un build où l'AUTRE perso est encore dans son ancien état. Sans savoir
/// quelle version de chaque perso est la plus récente, B défaisait le travail de A (et
/// réciproquement) — c'est le défaut qu'avait le premier jet de cette fusion, trouvé en
/// relecture. Avec une horloge par unité, chaque perso garde sa version la plus récente, d'où
/// qu'elle vienne, et tous les postes convergent sans arbitre.
///
/// La prise d'un personnage ajoute une règle : son preneur a toujours le dernier mot. Une
/// retouche faite ailleurs est annulée avant envoi (<see cref="CommitLocal"/>), et si une
/// modification concurrente passe quand même, le preneur réaffirme sa version avec un horodatage
/// plus récent (<see cref="MergeRemote"/>).
///
/// Travailler sur le JSON du <c>.zcx</c> plutôt que sur le modèle : les champs d'une version de
/// format plus récente traversent la fusion, et l'égalité ne dépend d'aucune méthode à tenir à
/// jour pour chaque classe du modèle.
///
/// Pas de verrou interne : appelé depuis un seul fil (l'interface).
/// </summary>
public sealed class SharedTeamDoc
{
    /// <summary>
    /// Clés que chacun garde pour soi. <c>name</c> en fait partie : le nom de l'onglet de l'hôte
    /// est celui de SON fichier, qu'un renommage venu d'un invité désaccorderait du nom sur disque
    /// (piège déjà vécu : 16 fichiers sur 269 dont le nom interne diverge du nom de fichier).
    /// </summary>
    public static readonly IReadOnlySet<string> LocalOnlyKeys =
        new HashSet<string>(StringComparer.Ordinal) { "id", "version", "createdAt", "updatedAt", "name", "gameMode" };

    private const string CharactersKey = "characters";
    private const string OrderUnit = "order";

    private readonly Dictionary<string, UnitStamp> _stamps = new(StringComparer.Ordinal);

    /// <summary>
    /// Contenu HORODATÉ de l'unité d'ordre, gardé à part du document. ⚠ Il ne se relit pas dans
    /// le document : un personnage ajouté pendant qu'un autre réordonnait est rangé en fin de
    /// liste à l'affichage, et relire l'ordre affiché ferait dériver ce contenu sans changer son
    /// horodatage — deux postes au même horodatage n'auraient plus le même ordre (divergence
    /// trouvée par le banc de convergence aléatoire : 5 parties sur 500). Il voyage donc tel quel
    /// dans les paquets (<see cref="RoomPacket.Order"/>).
    /// </summary>
    private List<string> _order;

    public string Me { get; }

    /// <summary>Dernier état partagé connu : ce qui a été envoyé, ou le résultat de la dernière
    /// fusion. C'est aussi ce qu'on renvoie à un arrivant qui demande l'état.</summary>
    public JsonObject Doc { get; private set; }

    public long Clock { get; private set; }

    public IReadOnlyDictionary<string, UnitStamp> Stamps => _stamps;

    public IReadOnlyList<string> Order => _order;

    private SharedTeamDoc(string me, JsonObject doc, List<string>? order)
    {
        Me = me;
        Doc = doc;
        _order = order ?? DerivedOrder(doc);
    }

    /// <summary>Hôte : le document de départ est le sien, rien n'est encore horodaté.</summary>
    public static SharedTeamDoc FromLocal(string me, JsonObject doc) => new(me, (JsonObject)doc.DeepClone(), null);

    /// <summary>Invité : il adopte le document reçu et ses horodatages tels quels.</summary>
    public static SharedTeamDoc FromRemote(string me, JsonObject doc, IReadOnlyDictionary<string, UnitStamp>? stamps,
                                           IReadOnlyList<string>? order)
    {
        var s = new SharedTeamDoc(me, (JsonObject)doc.DeepClone(), order?.ToList());
        if (stamps is not null)
            foreach (var (k, v) in stamps)
            {
                s._stamps[k] = v;
                s.Clock = Math.Max(s.Clock, v.Clock);
            }
        return s;
    }

    public sealed record CommitOutcome(JsonObject Document, IReadOnlyList<Guid> RevertedRoots, bool Changed);

    /// <summary>
    /// Enregistre les modifications faites à l'écran depuis le dernier état partagé : chaque unité
    /// qui a bougé reçoit un horodatage neuf. Avant ça, toute retouche d'un personnage pris par
    /// QUELQU'UN D'AUTRE est annulée (reprise de sa dernière version reçue) — elle ne part jamais,
    /// et l'appelant sait lesquelles rétablir à l'écran.
    /// </summary>
    public CommitOutcome CommitLocal(JsonObject local, Func<Guid, string?> ownerOfNode, Func<int, int>? canon)
    {
        var enforced = EnforceForeignClaims(local, ownerOfNode, canon, out var reverted);
        var before = Units(Doc);
        var after = Units(enforced);
        bool changed = false;
        foreach (var key in before.Keys.Union(after.Keys).ToList())
        {
            before.TryGetValue(key, out var a);
            after.TryGetValue(key, out var b);
            if (Equal(a, b, canon)) continue;
            _stamps[key] = new UnitStamp(++Clock, Me, b is null);
            if (key == OrderUnit) _order = DerivedOrder(enforced);
            changed = true;
        }
        Doc = (JsonObject)enforced.DeepClone();
        return new CommitOutcome(enforced, reverted, changed);
    }

    public sealed record MergeOutcome(JsonObject Document, bool Changed, bool ShouldRepublish);

    /// <summary>
    /// Intègre un document reçu : chaque unité prend la version au plus grand horodatage.
    /// <see cref="MergeOutcome.ShouldRepublish"/> signale que CE poste détient des versions plus
    /// récentes que celles du paquet reçu : les renvoyer maintient à jour le « dernier état » que le
    /// serveur garde pour les arrivants, et rattrape un participant resté en arrière.
    /// </summary>
    public MergeOutcome MergeRemote(JsonObject remote, IReadOnlyDictionary<string, UnitStamp>? remoteStamps,
                                    IReadOnlyList<string>? remoteOrder,
                                    Func<Guid, string?> ownerOfNode, Func<int, int>? canon)
    {
        remoteStamps ??= new Dictionary<string, UnitStamp>();
        foreach (var s in remoteStamps.Values) Clock = Math.Max(Clock, s.Clock);

        var mine = Units(Doc);
        mine[OrderUnit] = new JsonArray(_order.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray());
        var theirs = Units(remote);
        if (remoteOrder is not null)
            theirs[OrderUnit] = new JsonArray(remoteOrder.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray());
        var content = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        bool remoteBehind = false, reasserted = false;

        foreach (var key in mine.Keys.Union(theirs.Keys).Union(_stamps.Keys).Union(remoteStamps.Keys).ToList())
        {
            var ms = _stamps.GetValueOrDefault(key) ?? UnitStamp.Zero;
            var rs = remoteStamps.GetValueOrDefault(key) ?? UnitStamp.Zero;
            mine.TryGetValue(key, out var mc);
            theirs.TryGetValue(key, out var tc);
            if (ms.Deleted) mc = null;
            if (rs.Deleted) tc = null;

            int cmp = rs.CompareTo(ms);
            if (cmp > 0)
            {
                if (IsCharacterUnit(key) && OwnerOf(mc, ownerOfNode) == Me && !Equal(mc, tc, canon))
                {
                    // Mon personnage, modifié ailleurs en même temps que je le prenais : ma version
                    // l'emporte, avec un horodatage plus récent que le sien pour que tous l'adoptent.
                    _stamps[key] = new UnitStamp(++Clock, Me, mc is null);
                    content[key] = mc;
                    reasserted = true;
                }
                else
                {
                    _stamps[key] = rs;
                    content[key] = tc;
                }
            }
            else
            {
                content[key] = mc;
                if (cmp < 0) remoteBehind = true;
            }
        }

        _order = content.GetValueOrDefault(OrderUnit) is JsonArray o
            ? o.Select(n => n is JsonValue v && v.TryGetValue<string>(out var s) ? s.ToLowerInvariant() : null)
               .OfType<string>().ToList()
            : [];
        var merged = Materialize(content, template: Doc);
        bool changed = !SameDocument(Doc, merged, canon);
        Doc = merged;
        return new MergeOutcome((JsonObject)merged.DeepClone(), changed, remoteBehind || reasserted);
    }

    // ── Personnages pris par d'autres ─────────────────────────────────────────

    /// <summary>
    /// Annule, dans le document local, toute retouche d'un personnage pris par QUELQU'UN D'AUTRE,
    /// en reprenant sa version du dernier état partagé (la dernière reçue de son preneur).
    /// </summary>
    private JsonObject EnforceForeignClaims(JsonObject local, Func<Guid, string?> ownerOfNode,
                                            Func<int, int>? canon, out List<Guid> reverted)
    {
        reverted = [];
        var result = (JsonObject)local.DeepClone();
        if (result[CharactersKey] is not JsonArray chars)
        {
            chars = [];
            result[CharactersKey] = chars;
        }
        var baseArr = Doc[CharactersKey] as JsonArray;
        var baseById = Index(baseArr);

        for (int i = 0; i < chars.Count; i++)
        {
            if (chars[i] is not JsonObject node || IdOf(node) is not { } id) continue;
            baseById.TryGetValue(id, out var nb);
            var owner = OwnerOf(node, ownerOfNode) ?? OwnerOf(nb, ownerOfNode);
            if (owner is null || owner == Me || nb is null || Equal(nb, node, canon)) continue;
            chars[i] = nb.DeepClone();
            if (Guid.TryParse(id, out var g)) reverted.Add(g);
        }

        // Personnage d'un autre SUPPRIMÉ en local : il revient à sa place.
        var present = chars.OfType<JsonObject>().SelectMany(SubtreeIds).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var baseOrder = baseArr?.OfType<JsonObject>().Select(IdOf).OfType<string>().ToList() ?? [];
        foreach (var id in baseOrder)
        {
            var nb = baseById[id];
            var owner = OwnerOf(nb, ownerOfNode);
            if (owner is null || owner == Me || SubtreeIds(nb).Any(present.Contains)) continue;
            chars.Insert(InsertIndex(chars, baseOrder, id), nb.DeepClone());
            present.UnionWith(SubtreeIds(nb));
            if (Guid.TryParse(id, out var g)) reverted.Add(g);
        }
        return result;
    }

    /// <summary>Position de réinsertion : juste après le personnage qui le précédait.</summary>
    private static int InsertIndex(JsonArray chars, List<string> baseOrder, string id)
    {
        var ids = chars.Select(n => n is JsonObject o ? IdOf(o) : null).ToList();
        for (int i = baseOrder.IndexOf(id) - 1; i >= 0; i--)
        {
            int k = ids.FindIndex(x => string.Equals(x, baseOrder[i], StringComparison.OrdinalIgnoreCase));
            if (k >= 0) return k + 1;
        }
        return 0;
    }

    // ── Unités ────────────────────────────────────────────────────────────────

    private static bool IsCharacterUnit(string key) => key.StartsWith("c:", StringComparison.Ordinal);

    private static List<string> DerivedOrder(JsonObject doc)
        => (doc[CharactersKey] as JsonArray)?.OfType<JsonObject>().Select(IdOf).OfType<string>()
               .Distinct(StringComparer.Ordinal).ToList() ?? [];

    /// <summary>Découpe un document en unités (contenu brut, non cloné).</summary>
    private static Dictionary<string, JsonNode?> Units(JsonObject doc)
    {
        var u = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        foreach (var (key, value) in doc)
        {
            if (LocalOnlyKeys.Contains(key)) continue;
            if (key == CharactersKey)
            {
                var order = new JsonArray();
                if (value is JsonArray arr)
                    foreach (var root in arr.OfType<JsonObject>())
                        if (IdOf(root) is { } id && !u.ContainsKey("c:" + id))
                        {
                            u["c:" + id] = root;
                            order.Add(id);
                        }
                u[OrderUnit] = order;
            }
            else u["k:" + key] = value;
        }
        return u;
    }

    /// <summary>
    /// Reconstruit un document à partir des unités retenues. Ne dépend QUE des unités et de leurs
    /// horodatages — jamais du côté d'où elles viennent : deux postes qui ont les mêmes unités
    /// produisent le même document, condition pour qu'ils convergent.
    /// </summary>
    private JsonObject Materialize(Dictionary<string, JsonNode?> content, JsonObject template)
    {
        var doc = new JsonObject();
        var done = new HashSet<string>(StringComparer.Ordinal);

        void AddKey(string key)
        {
            if (!done.Add(key)) return;
            if (LocalOnlyKeys.Contains(key))
            {
                if (template[key] is { } local) doc[key] = local.DeepClone();
                return;
            }
            if (key == CharactersKey) { doc[key] = BuildCharacters(content); return; }
            if (content.TryGetValue("k:" + key, out var v) && v is not null) doc[key] = v.DeepClone();
        }

        foreach (var (key, _) in template) AddKey(key);
        if (!done.Contains(CharactersKey) && content.Keys.Any(IsCharacterUnit)) AddKey(CharactersKey);
        foreach (var key in content.Keys.Where(k => k.StartsWith("k:", StringComparison.Ordinal)).Select(k => k[2..]).ToList())
            AddKey(key);
        return doc;
    }

    private JsonArray BuildCharacters(Dictionary<string, JsonNode?> content)
    {
        var alive = content.Where(kv => IsCharacterUnit(kv.Key) && kv.Value is JsonObject)
                           .ToDictionary(kv => kv.Key[2..], kv => (JsonObject)kv.Value!, StringComparer.OrdinalIgnoreCase);

        var ordered = new List<string>();
        if (content.GetValueOrDefault(OrderUnit) is JsonArray order)
            foreach (var n in order)
                if (n is JsonValue v && v.TryGetValue<string>(out var id) && alive.ContainsKey(id)
                    && !ordered.Contains(id, StringComparer.OrdinalIgnoreCase))
                    ordered.Add(id);

        // Personnages vivants absents de l'ordre retenu (ajoutés en même temps que quelqu'un
        // réordonnait) : en fin de liste, dans un ordre DÉTERMINISTE — le même partout.
        ordered.AddRange(alive.Keys
            .Where(id => !ordered.Contains(id, StringComparer.OrdinalIgnoreCase))
            .OrderBy(id => _stamps.GetValueOrDefault("c:" + id) ?? UnitStamp.Zero)
            .ThenBy(id => id, StringComparer.Ordinal));

        // Deux réorganisations concurrentes (une variante promue racine d'un côté, retouchée comme
        // variante de l'autre) peuvent faire apparaître une ligne deux fois. Première occurrence
        // gardée : un doublon d'id casserait les cadenas et le spike, qui y renvoient.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new JsonArray();
        foreach (var id in ordered)
        {
            var node = alive[id];
            var ids = SubtreeIds(node).ToList();
            if (ids.Any(seen.Contains)) continue;
            seen.UnionWith(ids);
            result.Add(node.DeepClone());
        }
        return result;
    }

    // ── Comparaison ───────────────────────────────────────────────────────────

    /// <summary>
    /// Deux documents disent-ils la même chose ? Les clés propres à chaque poste
    /// (<see cref="LocalOnlyKeys"/>) sont ignorées : deux onglets de noms différents portent bien
    /// le MÊME build.
    /// </summary>
    public static bool SameDocument(JsonObject a, JsonObject b, Func<int, int>? canon)
    {
        foreach (var key in a.Select(kv => kv.Key).Union(b.Select(kv => kv.Key)))
        {
            if (LocalOnlyKeys.Contains(key)) continue;
            if (!NodeEquals(a[key], b[key], canon, key)) return false;
        }
        return true;
    }

    private static bool Equal(JsonNode? a, JsonNode? b, Func<int, int>? canon) => NodeEquals(a, b, canon, null);

    /// <summary>
    /// Égalité structurelle, avec une exception : dans un tableau <c>skillIds</c>, deux ids sont
    /// égaux s'ils désignent la même compétence à la variante PvE/PvP près (<paramref name="canon"/>).
    ///
    /// ⚠ Indispensable dès que le filtre PvE/PvP est actif : l'écran y substitue « Heal Party » et
    /// « Heal Party (PvP) » selon le mode (MainViewModel.ApplyGameModeTo). Sans cette équivalence,
    /// un joueur en PvE et un autre en PvP verraient chacun l'autre « modifier » le build à chaque
    /// réception, et se le renverraient sans fin.
    /// </summary>
    private static bool NodeEquals(JsonNode? a, JsonNode? b, Func<int, int>? canon, string? propertyName)
    {
        if (a is null || b is null) return a is null && b is null;
        switch (a)
        {
            case JsonObject oa when b is JsonObject ob:
                if (oa.Count != ob.Count) return false;
                foreach (var (k, va) in oa)
                    if (!ob.TryGetPropertyValue(k, out var vb) || !NodeEquals(va, vb, canon, k)) return false;
                return true;
            case JsonArray aa when b is JsonArray ab:
                if (aa.Count != ab.Count) return false;
                bool skills = canon is not null && propertyName == "skillIds";
                for (int i = 0; i < aa.Count; i++)
                {
                    if (skills && TryInt(aa[i], out var x) && TryInt(ab[i], out var y))
                    {
                        if (canon!(x) != canon(y)) return false;
                    }
                    else if (!NodeEquals(aa[i], ab[i], canon, null)) return false;
                }
                return true;
            case JsonValue:
                return b is JsonValue && JsonNode.DeepEquals(a, b);
            default:
                return false;
        }
    }

    private static bool TryInt(JsonNode? n, out int v)
    {
        v = 0;
        return n is JsonValue jv && jv.TryGetValue(out v);
    }

    // ── Arbre des personnages ─────────────────────────────────────────────────

    private static string? IdOf(JsonObject o)
        => o["id"] is JsonValue v && v.TryGetValue<string>(out var s) ? s.ToLowerInvariant() : null;

    private static Dictionary<string, JsonObject> Index(JsonArray? arr)
    {
        var d = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
        if (arr is null) return d;
        foreach (var n in arr)
            if (n is JsonObject o && IdOf(o) is { } id)
                d.TryAdd(id, o);
        return d;
    }

    /// <summary>Ids de toutes les lignes du sous-arbre (la racine et ses variantes, récursivement).</summary>
    public static IEnumerable<string> SubtreeIds(JsonObject node)
    {
        if (IdOf(node) is { } id) yield return id;
        if (node["variants"] is JsonArray vs)
            foreach (var v in vs.OfType<JsonObject>())
                foreach (var d in SubtreeIds(v))
                    yield return d;
    }

    /// <summary>Preneur d'un sous-arbre : la prise d'une ligne couvre toute sa famille (racine et
    /// variantes) — « prendre un personnage » dans une équipe, c'est prendre le joueur entier.</summary>
    private static string? OwnerOf(JsonNode? node, Func<Guid, string?> ownerOfNode)
    {
        if (node is not JsonObject o) return null;
        foreach (var id in SubtreeIds(o))
            if (Guid.TryParse(id, out var g) && ownerOfNode(g) is { } owner)
                return owner;
        return null;
    }

    /// <summary>Ids de TOUTES les lignes du document (racines et variantes).</summary>
    public static HashSet<Guid> AllNodeIds(JsonObject doc)
        => (doc[CharactersKey] as JsonArray)?.OfType<JsonObject>().SelectMany(SubtreeIds)
               .Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty)
               .ToHashSet() ?? [];
}

/// <summary>Verdict de compatibilité d'un document reçu avec CE poste.</summary>
public sealed record RoomCompatResult(int DocumentFormat, IReadOnlyList<int> MissingSkillIds)
{
    /// <summary>Document écrit par une application plus récente : le relire puis le renvoyer
    /// effacerait les champs que celle-ci ne connaît pas.</summary>
    public bool NewerFormat => DocumentFormat > TeamBuildSerializer.FormatVersion;

    /// <summary>Compétences inconnues du catalogue local : le sérialiseur les réécrirait en
    /// emplacement VIDE au premier renvoi — le travail des autres effacé en silence (§6 de la
    /// demande). Seule parade : ne rien renvoyer tant que le catalogue n'est pas à jour.</summary>
    public bool CatalogOutdated => MissingSkillIds.Count > 0;

    public bool CanEdit => !NewerFormat && !CatalogOutdated;
}

public static class RoomCompat
{
    /// <summary>
    /// Relit un document reçu avec LE sérialiseur de ce poste et le réécrit : les documents
    /// comparés ont ainsi tous exactement la même forme (mêmes champs, mêmes valeurs par défaut),
    /// quelle que soit la version de l'émetteur. Rend null si le document est illisible.
    /// </summary>
    public static JsonObject? Normalize(JsonObject doc, IReadOnlyDictionary<int, Skill> skillsById,
                                        out RoomCompatResult compat)
    {
        int format = doc["version"] is JsonValue v && v.TryGetValue<int>(out var f) ? f : 0;
        var unresolved = new List<int>();
        var model = TeamBuildSerializer.Deserialize(doc.ToJsonString(), skillsById, unresolved);
        compat = new RoomCompatResult(format, unresolved.Distinct().ToList());
        if (model is null) return null;
        model.UpdatedAt = default;
        return JsonNode.Parse(TeamBuildSerializer.Serialize(model)) as JsonObject;
    }
}
