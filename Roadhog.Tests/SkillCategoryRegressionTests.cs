using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;
using System.Xml.Linq;
using Roadhog;
using Roadhog.Application;
using Roadhog.Application.Workers;
using Roadhog.Core.Accounts;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;
using Roadhog.Infrastructure.Vmm;

internal static class SkillCategoryRegressionTests
{
    private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
    private static readonly Lazy<IReadOnlyDictionary<uint, XElement>> SourceSkills = new(LoadSourceSkills);
    private static readonly (uint Id, string Name, string BaseName, int Tier)[] ScreenshotSkills =
    {
        (985, "断罪一击 IV", "断罪一击", 4),
        (1268, "击破连锁 IV", "击破连锁", 4),
        (1238, "共鸣锁链 III", "共鸣锁链", 3),
        (1232, "闪电锁链 II", "闪电锁链", 2),
        (1260, "流星一击 IV", "流星一击", 4),
        (1248, "白热一击 III", "白热一击", 3),
        (1229, "破裂击 I", "破裂击", 1),
        (1335, "打击锁链 I", "打击锁链", 1)
    };

    public static Task RealXmlAttackCategoriesAsync()
    {
        foreach (var id in new uint[] { 985, 1260, 1335 })
        {
            var skill = SourceSkill(id);
            Check(skill.XmlActivation == "Active" && skill.XmlTargetRelationRestriction == "Enemy",
                $"source skill {id} remains an active enemy-targeted skill");
            Check(Tokens(skill.XmlTags).Contains("status") && Tokens(skill.XmlTags).Contains("debuff"),
                $"source skill {id} retains its original status/debuff metadata");
            Check(Tokens(skill.XmlEffects).Contains("SkillATK_Instant"),
                $"source skill {id} has real direct-damage evidence");
            Check(Category(skill) == "主动技能",
                $"direct attack {id} must not become a status skill because of its secondary effect");
        }

        Check(SourceSkill(985).XmlSubType == "Debuff",
            "Hallowed Strike proves that subtype Attack alone cannot identify direct attacks");
        Check(SourceSkill(1335).XmlChainCategory is null && SourceSkill(1335).XmlPrechainCategory is null &&
            SourceSkill(1335).XmlChainTime is null,
            "Inescapable Judgment is a standalone attack despite its localized chain name");

        var otherAttacks = new (uint Id, string Effect, string Expected)[]
        {
            (448, "SpellATK_Instant", "主动技能"),
            (655, "BackDashATK", "主动技能"),
            (803, "MoveBehindATK", "主动技能"),
            (921, "DashATK", "DP技能"),
            (1461, "DelayedSpellATK_Instant", "主动技能"),
            (1278, "SpellATKDrain", "主动技能"),
            (1789, "FPATK", "主动技能"),
            (1729, "Bleed", "主动技能")
        };
        foreach (var test in otherAttacks)
        {
            var skill = SourceSkill(test.Id);
            Check(Tokens(skill.XmlEffects).Contains(test.Effect), $"source attack {test.Id} retains {test.Effect}");
            Check(Category(skill) == test.Expected,
                $"source attack {test.Id} preserves its attack mechanism instead of becoming a status skill");
        }
        Check(SourceSkill(448).XmlTargetSlot == "buff", "hostile damage with an attached buff still has attack intent");

        var spell = AttackFixture(9100, "Spell damage with debuff", ("type", "Magical"),
            ("effect1_type", "SpellATK_Instant"));
        Check(Category(spell) == "主动技能", "instant spell damage with a secondary status remains active");
        Check(Category(spell with { XmlEffects = " spellatk_instant , Stun " }) == "主动技能",
            "damage effect tokens tolerate source casing and surrounding whitespace");
        var poison = AttackFixture(9101, "Poison damage with stun", ("effect1_type", "Poison"));
        Check(!Tokens(poison.XmlEffects).Contains("SkillATK_Instant") && Category(poison) == "主动技能",
            "poison damage remains an active attack without an instant damage component");
        return Task.CompletedTask;
    }

    public static Task ScreenshotChainsOnlyOnceAsync() => OnFormAsync(form =>
    {
        var skills = ScreenshotSkills.Select(item => SourceSkill(item.Id)).ToArray();
        var orders = new[] { skills, skills.Reverse().ToArray(), skills.Concat(skills.Reverse()).ToArray() };
        string? firstTree = null;
        foreach (var order in orders)
        {
            using var tree = Populate(form, order);
            var chain = tree.Nodes.Cast<TreeNode>().Single(node => node.Text == "连续技");
            Check(chain.Nodes.Count == 2, "only the two actual chain roots appear in the chain category");
            AssertBranch(chain.Nodes.Cast<TreeNode>().Single(node => Id(node) == 985), 985, 1268, 1238, 1232);
            AssertBranch(chain.Nodes.Cast<TreeNode>().Single(node => Id(node) == 1260), 1260, 1248, 1229);
            var shown = SkillNodes(tree.Nodes).ToArray();
            Check(shown.Length == ScreenshotSkills.Length &&
                shown.Select(Id).Order().SequenceEqual(ScreenshotSkills.Select(item => item.Id).Order()),
                "chain roots, descendants and standalone attacks each appear once across all categories");
            Check(shown.Where(node => Id(node) is 985 or 1260).All(node => node.Parent == chain),
                "visible chain roots are absent from ordinary and status categories");
            var standalone = shown.Single(node => Id(node) == 1335);
            Check(standalone.Parent!.Text == "主动技能" && standalone.Nodes.Count == 0,
                "Inescapable Judgment remains a standalone active candidate");
            var keys = (HashSet<string>)FormStatic("GetChainRootSkillKeys").Invoke(null, new object[] { order })!;
            Check(keys.Count == 2, "root classification agrees with the two displayed branches");
            var signature = TreeSignature(tree.Nodes);
            Check(firstTree is null || firstTree == signature,
                "input order and duplicate snapshots do not alter candidate categories or branches");
            firstTree = signature;
        }
    });

    public static Task StatusWithoutDamageAsync()
    {
        // Both come from the supplied client data, not manually assigned tags.
        var buff = Project(SourceSkills.Value[1206], "Pure protective buff", "Pure protective buff", 4);
        var debuff = Project(SourceSkills.Value[1331], "Pure enemy debuff", "Pure enemy debuff", 4);
        Check(buff.XmlSubType == "Buff" && !Tokens(buff.XmlEffects).Contains("SkillATK_Instant"),
            "protective source fixture has no instant attack");
        Check(debuff.XmlEffects == "HealCastorOnAttacked",
            "enemy debuff fixture applies its non-damage mechanism");
        Check(Category(buff) == "状态技能" && Category(debuff) == "状态技能",
            "pure buffs and non-damaging enemy debuffs keep their status category");
        foreach (var id in new uint[] { 1560, 1219 })
            Check(Category(SourceSkill(id)) == "状态技能",
                $"reactive magic counter or on-attacked healing effect {id} does not become a direct attack");
        var stunOnly = SourceSkill(1499);
        Check(stunOnly.XmlSubType == "Attack" && stunOnly.XmlEffects == "Stun",
            "the real attack-subtype fixture contains only a non-damaging stun");
        Check(Category(stunOnly) == "状态技能",
            "attack subtype alone cannot promote a non-damaging enemy status to the active attack category");
        return Task.CompletedTask;
    }

    public static Task MechanicPrecedenceAsync()
    {
        var cases = new (SkillSnapshot Skill, string Expected)[]
        {
            (AttackFixture(9201, "Conditional attack", ("target_valid_status1", "Stun")), "条件技能"),
            (AttackFixture(9202, "Counter attack", ("counter_skill", "Block")), "触发技能"),
            (AttackFixture(9203, "DP attack", ("cost_dp", "2000")), "DP技能"),
            (AttackFixture(9204, "Toggle attack", ("activation_attribute", "Toggle")), "激活技能"),
            (AttackFixture(9205, "Chain child attack", ("prechain_category_name", "TestRoot"),
                ("chain_time", "3000")), "连续技"),
            (AttackFixture(9206, "DP conditional attack", ("cost_dp", "2000"),
                ("target_valid_status1", "Stun")), "DP技能"),
            (AttackFixture(9207, "Counter conditional attack", ("counter_skill", "Block"),
                ("target_valid_status1", "Stun")), "触发技能")
        };
        foreach (var test in cases)
        {
            Check(Tokens(test.Skill.XmlEffects).Contains("SkillATK_Instant") &&
                Tokens(test.Skill.XmlTags).Contains("status"), "priority cases retain attack and status evidence");
            Check(Category(test.Skill) == test.Expected,
                $"{test.Skill.Name} preserves mechanic priority ahead of direct-damage classification");
        }

        var namedCondition = AttackFixture(536, "脚踝重击 III", ("counter_skill", "LegacyCounter")) with
        {
            DisplayBaseName = "脚踝重击"
        };
        Check(Category(namedCondition) == "条件技能", "explicit condition mapping still precedes counter metadata");
        foreach (var test in new (uint Id, string Expected)[]
        {
            (1227, "触发技能"), (2286, "条件技能"), (778, "连续技"), (1176, "DP技能")
        })
            Check(Category(SourceSkill(test.Id)) == test.Expected,
                $"real XML attack {test.Id} preserves its {test.Expected} mechanism");
        return Task.CompletedTask;
    }

    public static Task RootWithoutVisibleChildAsync() => OnFormAsync(form =>
    {
        var roots = new[] { SourceSkill(985), SourceSkill(1260), SourceSkill(1335) };
        var passiveChild = SourceSkill(1268) with
        {
            Name = "Hidden passive child", DisplayBaseName = "Hidden passive child",
            XmlActivation = "Passive", XmlTags = "passive,chain,status,debuff"
        };
        var conditionChild = SourceSkill(1268) with
        {
            Name = "Conditional child", DisplayBaseName = "Conditional child",
            XmlTargetValidStatuses = "Stun", XmlTags = "active,manual,chain,status,debuff,condition"
        };
        foreach (var skills in new[] { roots, roots.Append(passiveChild).ToArray(), roots.Append(conditionChild).ToArray() })
        {
            using var tree = Populate(form, skills);
            Check(!tree.Nodes.Cast<TreeNode>().Any(node => node.Text == "连续技"),
                "a category token without a visible continuous child cannot create a chain root");
            foreach (var root in roots)
            {
                var node = SkillNodes(tree.Nodes).Single(candidate => Id(candidate) == root.SkillId);
                Check(node.Parent!.Text == "主动技能", "an unattached chain opener remains an active candidate");
            }
            Check(!SkillNodes(tree.Nodes).Any(node => node.Text == passiveChild.Name),
                "hidden passive metadata cannot suppress its visible opener");
        }
    });

    public static Task ConfiguredTreeRoundTripAsync() => OnFormAsync(form =>
    {
        using var available = Populate(form, ScreenshotSkills.Select(item => SourceSkill(item.Id)).ToArray());
        using var selected = new TreeView();
        var chain = available.Nodes.Cast<TreeNode>().Single(node => node.Text == "连续技");
        var standalone = SkillNodes(available.Nodes).Single(node => Id(node) == 1335);
        var add = FormStatic("AddAvailableSkillNode");
        foreach (var candidate in new[] { chain, standalone, chain, standalone })
            add.Invoke(null, new object[] { selected, candidate });
        Check(selected.Nodes.Count == 3, "adding categories and skills repeatedly keeps exactly three configured roots");
        var capture = FormStatic("CaptureSkillTree");
        var saved = (List<SkillConfigNode>)capture.Invoke(null, new object[] { selected.Nodes })!;
        AssertConfigBranch(saved.Single(node => node.SkillId == 985), 985, 1268, 1238, 1232);
        AssertConfigBranch(saved.Single(node => node.SkillId == 1260), 1260, 1248, 1229);
        Check(saved.Single(node => node.SkillId == 1335) is { Type: "主动技能", Children.Count: 0 },
            "saving standalone Judgment keeps its attack type and no synthetic descendants");

        var reloaded = JsonSerializer.Deserialize<List<SkillConfigNode>>(JsonSerializer.Serialize(saved))!;
        using var restored = new TreeView();
        FormStatic("PopulateSelectedSkillTreeFromConfig").Invoke(null, new object[] { restored, reloaded });
        var capturedAgain = (List<SkillConfigNode>)capture.Invoke(null, new object[] { restored.Nodes })!;
        Check(JsonSerializer.Serialize(saved) == JsonSerializer.Serialize(capturedAgain),
            "configuration serialization and UI reload preserve IDs, names, types, timing and parent-child order");
    });

    private static SkillSnapshot SourceSkill(uint id)
    {
        var display = ScreenshotSkills.SingleOrDefault(item => item.Id == id);
        if (display.Id == 0)
        {
            var name = SourceSkills.Value[id].Element("name")!.Value;
            return Project(SourceSkills.Value[id], name, name, 1);
        }
        return Project(SourceSkills.Value[id], display.Name, display.BaseName, display.Tier);
    }

    private static SkillSnapshot AttackFixture(uint id, string name, params (string Field, string Value)[] overrides)
    {
        var xml = new XElement(SourceSkills.Value[1335]);
        xml.SetElementValue("id", id);
        xml.SetElementValue("name", "CategoryRegression_" + id);
        foreach (var value in overrides) xml.SetElementValue(value.Field, value.Value);
        return Project(xml, name, name, 1);
    }

    private static SkillSnapshot Project(XElement xml, string name, string baseName, int tier)
    {
        var api = typeof(AionVmmGameApi);
        object?[] arguments = { xml, null };
        Check((bool)api.GetMethod("TryReadSkillXmlStaticDetail", PrivateStatic)!.Invoke(null, arguments)!,
            "the actual provider XML parser accepts the source skill");
        var detail = arguments[1]!;
        var learnedType = api.GetNestedType("LearnedSkillInfo", BindingFlags.NonPublic)!;
        var learned = Activator.CreateInstance(learnedType)!;
        learnedType.GetField("SkillId")!.SetValue(learned, (uint)detail.GetType().GetField("Id")!.GetValue(detail)!);
        learnedType.GetField("Name")!.SetValue(learned, name);
        learnedType.GetField("DisplayBaseName")!.SetValue(learned, baseName);
        learnedType.GetField("DisplayTier")!.SetValue(learned, tier);
        learnedType.GetField("HighestLevel")!.SetValue(learned, (ushort)1);
        learnedType.GetField("SkillLevel")!.SetValue(learned, 1u);
        learnedType.GetField("HasXmlStaticDetail")!.SetValue(learned, true);
        learnedType.GetField("XmlStaticDetail")!.SetValue(learned, detail);
        return (SkillSnapshot)api.GetMethod("ToSkillSnapshot", PrivateStatic)!.Invoke(null, new[] { learned })!;
    }

    private static IReadOnlyDictionary<uint, XElement> LoadSourceSkills()
    {
        var wanted = ScreenshotSkills.Select(item => item.Id).Concat(new uint[]
        {
            1206, 1331, 448, 655, 803, 921, 1461, 1278, 1789, 1729, 1560, 1219, 1499, 1227, 2286, 778, 1176
        }).ToHashSet();
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "Roadhog", "Source", "client_skills.xml");
            if (!File.Exists(path)) continue;
            var result = XDocument.Load(path).Root!.Elements("skill_base_client")
                .Where(element => wanted.Contains((uint?)element.Element("id") ?? 0))
                .ToDictionary(element => (uint)element.Element("id")!, element => new XElement(element));
            Check(result.Count == wanted.Count, "all regression skills are present in the actual source XML");
            return result;
        }
        throw new FileNotFoundException("Roadhog/Source/client_skills.xml was not found from the test workspace.");
    }

    private static string Category(SkillSnapshot skill) =>
        (string)FormStatic("GetManualSkillCategory").Invoke(null, new object[] { skill })!;
    private static MethodInfo FormStatic(string name) => typeof(AccountSettingsForm).GetMethod(name, PrivateStatic)!;
    private static string[] Tokens(string? value) =>
        (value ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    private static uint Id(TreeNode node) => (uint)node.Tag!.GetType().GetProperty("SkillId")!.GetValue(node.Tag)!;
    private static IEnumerable<TreeNode> SkillNodes(TreeNodeCollection nodes)
    {
        foreach (TreeNode node in nodes)
        {
            if (node.Tag is not null) yield return node;
            foreach (var descendant in SkillNodes(node.Nodes)) yield return descendant;
        }
    }
    private static string TreeSignature(TreeNodeCollection nodes) => string.Join("|", nodes.Cast<TreeNode>()
        .Select(node => $"{node.Text}:{(node.Tag is null ? 0 : Id(node))}[{TreeSignature(node.Nodes)}]"));
    private static void AssertBranch(TreeNode root, params uint[] ids)
    {
        var node = root;
        for (var index = 0; index < ids.Length; index++)
        {
            Check(Id(node) == ids[index], "the candidate chain preserves the exact screenshot skill ID at each stage");
            Check(node.Nodes.Count == (index + 1 == ids.Length ? 0 : 1), "the candidate chain retains every stage once");
            if (index + 1 < ids.Length) node = node.Nodes[0];
        }
    }
    private static void AssertConfigBranch(SkillConfigNode root, params uint[] ids)
    {
        var node = root;
        for (var index = 0; index < ids.Length; index++)
        {
            Check(node.SkillId == ids[index], "configured chain stage keeps its exact ID");
            Check(node.Type == (index == 0 ? "主动技能" : "连续技"),
                "displaying an opener in the chain category does not change its stored execution type");
            Check(node.ChainTimeMs == (index == 0 ? (int?)null : 3000), "configured child keeps its source chain window");
            Check(node.Children.Count == (index + 1 == ids.Length ? 0 : 1), "configured parent-child structure stays complete");
            if (index + 1 < ids.Length) node = node.Children[0];
        }
    }
    private static TreeView Populate(AccountSettingsForm form, IReadOnlyList<SkillSnapshot> skills)
    {
        var tree = new TreeView();
        typeof(AccountSettingsForm).GetMethod("PopulateAvailableSkillTreeFromSkills", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(form, new object[] { tree, skills });
        return tree;
    }
    private static Task OnFormAsync(Action<AccountSettingsForm> run)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var logger = new InMemoryRoadhogLogger();
                var account = new AccountConfig { AccountName = "skill-category-regressions", ScriptSettings = new() };
                using var form = new AccountSettingsForm(account.AccountName,
                    new RoadhogRuntime(new FakeGameApi(), logger, new AccountRuntimeManager(logger), null!),
                    new InMemoryAccountConfigStore(account), new InMemorySharedPathStore(), new InMemoryScriptProfileStore(),
                    new RecordingFolderLauncher(), "test-paths");
                run(form);
                completion.SetResult();
            }
            catch (Exception error) { completion.SetException(error); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
