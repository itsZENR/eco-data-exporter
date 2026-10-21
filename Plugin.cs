using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using UnityEngine;

namespace EcoMarketDumper;

[BepInPlugin("zenr.eco.marketdumper", "Eco Market Dumper", "0.2.5")]
public sealed class Plugin : BasePlugin
{
    public override void Load()
    {
        DumpBehaviour.PluginLog = Log;
        AddComponent<DumpBehaviour>();
        Log.LogInfo("Eco Market Dumper 0.2.5 loaded. F8 = market, F9 = recipes/skills/items/talents/modifier probes.");
    }
}

public sealed class DumpBehaviour : MonoBehaviour
{
    internal static ManualLogSource PluginLog;

    public DumpBehaviour(IntPtr ptr) : base(ptr) { }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F8)) DumpMarket();
        if (Input.GetKeyDown(KeyCode.F9)) DumpGameData();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    // ---------------- MARKET ----------------

    private static void DumpMarket()
    {
        try
        {
            var viewers = Resources.FindObjectsOfTypeAll<EconomyViewerUI>();
            if (viewers == null || viewers.Length == 0)
            {
                PluginLog?.LogWarning("EconomyViewerUI was not found. Open Economy Viewer first, then press F8.");
                return;
            }

            EconomyTrackerView tracker = null;
            for (var i = 0; i < viewers.Length; i++)
            {
                var candidate = viewers[i];
                if (candidate == null) continue;
                try
                {
                    var t = candidate.EconomyTrackerView;
                    if (t?.Trades != null)
                    {
                        tracker = t;
                        break;
                    }
                }
                catch { }
            }

            if (tracker?.Trades == null)
            {
                PluginLog?.LogWarning("Economy Viewer exists, but Trades is not populated. Reopen it and press F8 again.");
                return;
            }

            var trades = tracker.Trades;
            var rows = new List<MarketRow>(trades.Count);

            for (var i = 0; i < trades.Count; i++)
            {
                var trade = trades[i];
                if (trade == null) continue;
                var p = trade.StorePosition;

                rows.Add(new MarketRow
                {
                    ItemTypeId = trade.ItemTypeID,
                    Item = Safe(() => trade.ReferencedItem?.DisplayName) ?? Safe(() => trade.ReferencedItemMarkedUpName) ?? string.Empty,
                    ItemServerName = Safe(() => trade.ReferencedItem?.ServerName) ?? string.Empty,
                    Source = Safe(() => trade.SourceName.ToString()) ?? string.Empty,
                    NumberAvailable = trade.NumberAvailable,
                    SalePrice = Finite(trade.SalePrice),
                    NumberWanted = Finite(trade.NumberWanted),
                    WantedPrice = Finite(trade.WantedPrice),
                    Currency = Safe(() => trade.Currency?.Display(1f)) ?? string.Empty,
                    CurrencyOwner = Safe(() => trade.Currency?.OwnerDisplayName) ?? string.Empty,
                    CurrencyCategory = Safe(() => trade.Currency?.CategoryName.ToString()) ?? string.Empty,
                    StoreBalance = Finite(trade.StoreBalance),
                    LimitedFunds = trade.LimitedFunds,
                    LimitedStorage = trade.LimitedStorage,
                    IsBarter = trade.IsBarter,
                    IsWorldObjectSale = trade.IsWorldObjectSale,
                    Auth = trade.Auth,
                    MinDurability = Finite(trade.MinDurability),
                    MaxDurability = Finite(trade.MaxDurability),
                    MinIntegrity = Finite(trade.MinIntegrity),
                    MaxIntegrity = Finite(trade.MaxIntegrity),
                    StoreX = Finite(p.x),
                    StoreY = Finite(p.y),
                    StoreZ = Finite(p.z),
                    Settlement = Safe(() => trade.Settlement?.TypeAndName.ToString()) ?? string.Empty,
                    Tag = Safe(() => trade.Tag?.Name) ?? string.Empty,
                    StatusIcons = Safe(() => trade.StatusIcons) ?? string.Empty
                });
            }

            var payload = new MarketDump { DumpedAtLocal = DateTime.Now, Count = rows.Count, Trades = rows };
            WriteDump("MarketDumps", "market", payload);
            PluginLog?.LogInfo($"Market dump complete: {rows.Count} rows.");
        }
        catch (Exception ex)
        {
            PluginLog?.LogError($"Market dump failed: {ex}");
        }
    }

    // ---------------- RECIPES + SKILLS ----------------

    private static void DumpGameData()
    {
        try
        {
            var global = GlobalData.Obj;
            if (global == null)
            {
                PluginLog?.LogWarning("GlobalData.Obj is null. Join a server/world first, then press F9.");
                return;
            }

            var itemIndex = new Dictionary<int, ItemRow>();
            var skills = DumpSkills(global, itemIndex);
            var recipes = DumpRecipes(global, itemIndex);
            ApplyPlayerSkillStateToRecipes(recipes, skills);
            var talents = DumpTalents(global, recipes);
            var modifiers = DumpModifiers(global, recipes);
            var items = new List<ItemRow>(itemIndex.Values);
            items.Sort((a, b) => a.TypeId.CompareTo(b.TypeId));

            WriteDump("EcoDataDumps", "recipes", recipes);
            WriteDump("EcoDataDumps", "skills", skills);
            WriteDump("EcoDataDumps", "items", new ItemsDump
            {
                DumpedAtLocal = DateTime.Now,
                Count = items.Count,
                Items = items
            });
            WriteDump("EcoDataDumps", "talents", talents);
            WriteDump("EcoDataDumps", "modifiers", modifiers);

            PluginLog?.LogInfo($"Eco data dump complete: {recipes.Count} recipes, {skills.SkillTrees.Count} skill trees, {skills.PlayerSkills.Count} player skills, {items.Count} referenced items, {talents.Skills.Count} talent-bearing skill rows, {modifiers.DynamicValues.Count} dynamic-value probes, {modifiers.UpgradeItems.Count} upgrade/module item probes.");
        }
        catch (Exception ex)
        {
            PluginLog?.LogError($"Eco data dump failed: {ex}");
        }
    }

    private static RecipesDump DumpRecipes(GlobalDataView global, Dictionary<int, ItemRow> itemIndex)
    {
        var result = new RecipesDump { DumpedAtLocal = DateTime.Now, DynamicEvaluation = "DynamicValueExtensions.GetCurrentValue" };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var all = global.AllRecipes;

        if (all == null)
        {
            PluginLog?.LogWarning("GlobalData.Obj.AllRecipes is null.");
            return result;
        }

        foreach (var pair in all)
        {
            var list = pair.Value;
            if (list == null) continue;

            for (var i = 0; i < list.Count; i++)
            {
                var recipe = list[i];
                if (recipe == null) continue;
                var family = recipe.Family;

                var key = (Safe(() => recipe.Name) ?? "") + "|" + (Safe(() => family?.RecipeName) ?? "") + "|" + i;
                if (!seen.Add(key)) continue;

                var row = new RecipeRow
                {
                    Name = Safe(() => recipe.Name) ?? string.Empty,
                    DisplayName = Safe(() => recipe.DisplayName.ToString()) ?? string.Empty,
                    FamilyRecipeName = Safe(() => family?.RecipeName) ?? string.Empty,
                    FamilyDisplayName = Safe(() => family?.DisplayName.ToString()) ?? string.Empty,
                    FamilyMarkedUpName = Safe(() => family?.MarkedUpName.ToString()) ?? string.Empty,
                    CraftingTable = ItemName(family?.CraftingTable),
                    CraftingTableServerName = Safe(() => family?.CraftingTable?.ServerName) ?? string.Empty,
                    CraftMinutesBase = DynamicBase(family?.CraftMinutes),
                    CraftMinutesCurrent = DynamicCurrent(family?.CraftMinutes),
                    Labor = family == null ? null : Finite(family.Labor),
                    LaborCaloriesBase = DynamicBase(family?.LaborInCalories),
                    LaborCaloriesCurrent = DynamicCurrent(family?.LaborInCalories),
                    CraftableDefault = family?.CraftableDefault ?? false,
                    RequiresTalentUnlock = family?.RequiresTalentUnlock ?? false,
                    RequiredTalentName = Safe(() => family?.RequiredTalentName.ToString()) ?? string.Empty,
                    UsesPlayerInventory = recipe.UsesPlayerInventory,
                    RequiresStrangeBlueprint = recipe.RequiresStrangeBlueprint,
                    ZeroSalvageCost = recipe.ZeroSalvageCost
                };

                AddItem(itemIndex, family?.CraftingTable);

                DumpIngredients(recipe.Ingredients, row.Ingredients, itemIndex);
                DumpProducts(recipe.Products, row.Products, itemIndex);

                if (family != null)
                {
                    DumpIngredients(family.Ingredients, row.FamilyIngredients, itemIndex);
                    DumpProducts(family.Product, row.FamilyProducts, itemIndex);

                    if (family.RequiredSkills != null)
                    {
                        for (var s = 0; s < family.RequiredSkills.Count; s++)
                        {
                            var req = family.RequiredSkills[s];
                            if (req == null) continue;
                            row.RequiredSkills.Add(new RequiredSkillRow
                            {
                                SkillTypeId = req.SkillTypeID,
                                Level = req.Level,
                                SkillType = Safe(() => req.SkillType?.ToString()) ?? string.Empty,
                                Description = Safe(() => req.Description.ToString()) ?? string.Empty,
                                IsMet = SafeBool(() => req.IsMet)
                            });
                        }
                    }

                    if (family.RequiredModules != null)
                    {
                        for (var m = 0; m < family.RequiredModules.Count; m++)
                        {
                            var module = family.RequiredModules[m];
                            if (module == null) continue;
                            row.RequiredModules.Add(new RequiredModuleRow
                            {
                                ModuleName = Safe(() => module.ModuleName) ?? string.Empty,
                                ModuleLink = Safe(() => module.ModuleLink.ToString()) ?? string.Empty,
                                Description = Safe(() => module.Description.ToString()) ?? string.Empty
                            });
                        }
                    }
                }

                result.Recipes.Add(row);
            }
        }

        result.Count = result.Recipes.Count;
        return result;
    }

    private static SkillsDump DumpSkills(GlobalDataView global, Dictionary<int, ItemRow> itemIndex)
    {
        var result = new SkillsDump { DumpedAtLocal = DateTime.Now };
        var seenTrees = new HashSet<string>(StringComparer.Ordinal);

        void AddTree(SkillTreeView tree, bool researchTree)
        {
            if (tree == null) return;
            var staticSkill = tree.StaticSkill;
            var key = (staticSkill?.TypeID ?? 0) + "|" + (Safe(() => staticSkill?.ServerName) ?? "") + "|" + (Safe(() => tree.MarkedUpName.ToString()) ?? "");
            if (!seenTrees.Add(key)) return;

            var row = new SkillTreeRow
            {
                IsResearchTree = researchTree,
                MarkedUpName = Safe(() => tree.MarkedUpName.ToString()) ?? string.Empty,
                State = Safe(() => tree.State.ToString()) ?? string.Empty,
                Discovered = tree.Discovered,
                RequiresScroll = tree.RequiresScroll,
                IsUsingSkillBook = tree.IsUsingSkillBook,
                TreeResearchRank = tree.TreeResearchRank,
                TimeDiscovered = Finite(tree.TimeDiscovered),
                Skill = SkillToRow(staticSkill, itemIndex),
                Parent = TreeName(tree.Parent),
                Root = TreeName(tree.Root),
                Specialty = TreeName(tree.Specialty),
                SkillBookItem = ItemName(tree.SkillBookItem),
                SkillScrollItem = ItemName(tree.SkillScrollItem),
                SkillBookTable = ItemName(tree.SkillBookTable),
                SkillScrollMarkedUpName = Safe(() => tree.SkillScrollMarkedUpName) ?? string.Empty
            };

            AddItem(itemIndex, tree.SkillBookItem);
            AddItem(itemIndex, tree.SkillScrollItem);
            AddItem(itemIndex, tree.SkillBookTable);

            DumpIngredients(tree.SkillItemIngredients, row.SkillItemIngredients, itemIndex);

            if (tree.RequiredSkillLevelsMap != null)
            {
                foreach (var req in tree.RequiredSkillLevelsMap)
                {
                    row.RequiredSkillLevels.Add(new TreeRequirementRow
                    {
                        SkillTree = TreeName(req.Key),
                        Level = req.Value
                    });
                }
            }

            if (tree.LevelToRecipeUnlocked != null)
            {
                foreach (var unlocked in tree.LevelToRecipeUnlocked)
                {
                    var u = new UnlockedRecipesRow { Level = unlocked.Key };
                    var list = unlocked.Value;
                    if (list != null)
                    {
                        for (var i = 0; i < list.Count; i++)
                        {
                            var fam = list[i];
                            if (fam == null) continue;
                            u.Recipes.Add(Safe(() => fam.RecipeName) ?? Safe(() => fam.DisplayName.ToString()) ?? string.Empty);
                        }
                    }
                    row.UnlockedRecipes.Add(u);
                }
            }

            result.SkillTrees.Add(row);

            if (tree.ProfessionChildren != null)
            {
                for (var i = 0; i < tree.ProfessionChildren.Count; i++)
                    AddTree(tree.ProfessionChildren[i], researchTree);
            }
        }

        if (global.SkillTrees != null)
            for (var i = 0; i < global.SkillTrees.Count; i++) AddTree(global.SkillTrees[i], false);

        if (global.ResearchSkillTrees != null)
            for (var i = 0; i < global.ResearchSkillTrees.Count; i++) AddTree(global.ResearchSkillTrees[i], true);

        try
        {
            var user = Player.obj?.UserView;
            if (user != null)
            {
                result.PlayerName = Safe(() => user.OverheadName) ?? Safe(() => Player.obj.UserName) ?? string.Empty;

                if (user.UserXP != null)
                {
                    result.StarsAvailable = user.UserXP.StarsAvailable;
                    result.TotalStarsEarned = user.UserXP.TotalStarsEarned;
                    result.NextStarCost = user.UserXP.NextStarCost;
                    result.SkillRate = Finite(user.UserXP.SkillRate);
                    result.XP = Finite(user.UserXP.XP);
                }

                var skillset = user.Skillset;
                if (skillset != null)
                {
                    result.ProfessionCount = skillset.ProfessionCount;
                    result.SpecialtyCount = skillset.SpecialtyCount;
                    result.CostPerAdditionalSpecialty = Finite(skillset.CostPerAdditionalSpecialty);

                    if (skillset.Skills != null)
                    {
                        for (var i = 0; i < skillset.Skills.Count; i++)
                        {
                            var skill = skillset.Skills[i];
                            if (skill == null) continue;
                            result.PlayerSkills.Add(SkillToRow(skill, itemIndex));
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            PluginLog?.LogWarning($"Player skill state could not be fully dumped: {ex.Message}");
        }

        // StaticSkill inside SkillTreeView is the definition object and normally has Level/XP = 0.
        // Skillset.Skills contains the player-specific SkillView instances. Attach those to each tree.
        var playerByTypeId = new Dictionary<int, SkillRow>();
        for (var i = 0; i < result.PlayerSkills.Count; i++)
        {
            var ps = result.PlayerSkills[i];
            if (ps != null && ps.TypeId != 0) playerByTypeId[ps.TypeId] = ps;
        }

        for (var i = 0; i < result.SkillTrees.Count; i++)
        {
            var tree = result.SkillTrees[i];
            if (tree?.Skill == null) continue;
            if (playerByTypeId.TryGetValue(tree.Skill.TypeId, out var ps))
            {
                tree.PlayerSkill = ps;
                tree.PlayerLevel = ps.Level;
                tree.PlayerExperience = ps.Experience;
                tree.PlayerPercentTowardsNextLevel = ps.PercentTowardsNextLevel;
            }
        }

        return result;
    }

    private static SkillRow SkillToRow(SkillView skill, Dictionary<int, ItemRow> itemIndex)
    {
        if (skill == null) return null;
        AddItem(itemIndex, skill);

        var row = new SkillRow
        {
            TypeId = skill.TypeID,
            ServerName = Safe(() => skill.ServerName) ?? string.Empty,
            DisplayName = Safe(() => skill.DisplayName) ?? string.Empty,
            NotTranslatedName = Safe(() => skill.NotTranslatedName) ?? string.Empty,
            Level = skill.Level,
            MaxLevel = skill.MaxLevel,
            Tier = skill.Tier,
            IsRoot = skill.IsRoot,
            IsSpecialty = skill.IsSpecialty,
            BaseSpecialtyCost = skill.BaseSpecialtyCost,
            EffectiveSpecialtyCost = skill.EffectiveSpecialtyCost,
            SpecialtyCost = skill.SpecialtyCost,
            Experience = Finite(skill.Experience),
            ExperienceToLevel = Finite(skill.ExperienceToLevel),
            PercentTowardsNextLevel = Finite(skill.PercentTowardsNextLevel),
            Education = Finite(skill.Education),
            IsMet = SafeBool(() => skill.IsMet)
        };

        if (skill.Prerequisites != null)
        {
            for (var i = 0; i < skill.Prerequisites.Count; i++)
            {
                var req = skill.Prerequisites[i];
                if (req == null) continue;
                row.Prerequisites.Add(new RequiredSkillRow
                {
                    SkillTypeId = req.SkillTypeID,
                    Level = req.Level,
                    SkillType = Safe(() => req.SkillType?.ToString()) ?? string.Empty,
                    Description = Safe(() => req.Description.ToString()) ?? string.Empty,
                    IsMet = SafeBool(() => req.IsMet)
                });
            }
        }

        return row;
    }

    private static void ApplyPlayerSkillStateToRecipes(RecipesDump recipes, SkillsDump skills)
    {
        if (recipes?.Recipes == null || skills?.PlayerSkills == null) return;

        var playerByTypeId = new Dictionary<int, SkillRow>();
        for (var i = 0; i < skills.PlayerSkills.Count; i++)
        {
            var ps = skills.PlayerSkills[i];
            if (ps != null && ps.TypeId != 0) playerByTypeId[ps.TypeId] = ps;
        }

        for (var r = 0; r < recipes.Recipes.Count; r++)
        {
            var recipe = recipes.Recipes[r];
            if (recipe == null) continue;

            var allMet = true;
            for (var i = 0; i < recipe.RequiredSkills.Count; i++)
            {
                var req = recipe.RequiredSkills[i];
                if (req == null) continue;

                if (playerByTypeId.TryGetValue(req.SkillTypeId, out var ps))
                {
                    req.PlayerLevel = ps.Level;
                    req.PlayerExperience = ps.Experience;
                    req.PlayerPercentTowardsNextLevel = ps.PercentTowardsNextLevel;
                    req.PlayerHasRequiredLevel = ps.Level >= req.Level;
                }
                else
                {
                    req.PlayerHasRequiredLevel = req.Level <= 0;
                }

                if (!req.PlayerHasRequiredLevel) allMet = false;
            }

            recipe.PlayerMeetsRequiredSkillLevels = allMet;
        }
    }

    // ---------------- TALENTS + MODIFIERS ----------------

    private static TalentsDump DumpTalents(GlobalDataView global, RecipesDump recipes)
    {
        var result = new TalentsDump { DumpedAtLocal = DateTime.Now };
        var playerSkills = new Dictionary<int, SkillView>();

        try
        {
            var skillset = Player.obj?.UserView?.Skillset;
            if (skillset?.Skills != null)
            {
                for (var i = 0; i < skillset.Skills.Count; i++)
                {
                    var skill = skillset.Skills[i];
                    if (skill != null && skill.TypeID != 0) playerSkills[skill.TypeID] = skill;
                }

                result.PlayerTalentState = SnapshotMatchingProperties(skillset, 4, "Talent", "Bonus");
            }
        }
        catch (Exception ex)
        {
            result.Warnings.Add("Player talent-state probe failed: " + ex.Message);
        }

        var seen = new HashSet<int>();

        void AddTree(SkillTreeView tree)
        {
            if (tree == null) return;
            var skill = tree.StaticSkill;
            var id = skill?.TypeID ?? 0;

            if (id != 0 && seen.Add(id))
            {
                var row = new TalentSkillRow
                {
                    SkillTypeId = id,
                    SkillServerName = Safe(() => skill.ServerName) ?? string.Empty,
                    SkillDisplayName = Safe(() => skill.DisplayName) ?? string.Empty,
                    TreeState = Safe(() => tree.State.ToString()) ?? string.Empty,
                    PlayerLevel = playerSkills.TryGetValue(id, out var playerSkill) ? playerSkill.Level : null
                };

                object definitionCollection = null;
                object playerCollection = null;
                try { definitionCollection = skill?.Talents; } catch { }
                try { if (playerSkill != null) playerCollection = playerSkill.Talents; } catch { }

                row.DefinitionCollectionType = definitionCollection?.GetType().FullName ?? string.Empty;
                row.PlayerCollectionType = playerCollection?.GetType().FullName ?? string.Empty;
                row.DefinitionTalents = BuildTalentEntries(definitionCollection);
                row.PlayerTalents = BuildTalentEntries(playerCollection);

                // Keep rows even if the collection is empty: that makes it explicit that the
                // client exposed no talents for this skill instead of silently dropping it.
                result.Skills.Add(row);
            }

            if (tree.ProfessionChildren != null)
            {
                for (var i = 0; i < tree.ProfessionChildren.Count; i++) AddTree(tree.ProfessionChildren[i]);
            }
        }

        if (global.SkillTrees != null)
            for (var i = 0; i < global.SkillTrees.Count; i++) AddTree(global.SkillTrees[i]);

        if (global.ResearchSkillTrees != null)
            for (var i = 0; i < global.ResearchSkillTrees.Count; i++) AddTree(global.ResearchSkillTrees[i]);

        if (recipes?.Recipes != null)
        {
            for (var i = 0; i < recipes.Recipes.Count; i++)
            {
                var recipe = recipes.Recipes[i];
                if (recipe == null || !recipe.RequiresTalentUnlock) continue;
                result.RecipeTalentRequirements.Add(new RecipeTalentRequirementRow
                {
                    Recipe = recipe.Name ?? string.Empty,
                    DisplayName = recipe.DisplayName ?? string.Empty,
                    RequiredTalent = recipe.RequiredTalentName ?? string.Empty,
                    CraftingTable = recipe.CraftingTable ?? string.Empty
                });
            }
        }

        result.Count = result.Skills.Count;
        return result;
    }

    private static List<TalentEntryRow> BuildTalentEntries(object collection)
    {
        var result = new List<TalentEntryRow>();
        foreach (var entry in EnumerateUnknown(collection, 64))
        {
            if (entry == null) continue;
            var declared = TryRewrapDeclaredView(entry, out var declaredType, out var declaredError);
            var description = ReadStringProperty(entry, "Description", "DisplayDescription", "Tooltip", "GetDescription");
            if (string.IsNullOrWhiteSpace(description) && declared != null)
                description = ReadStringProperty(declared, "Description", "DisplayDescription", "Tooltip", "GetDescription");

            var row = new TalentEntryRow
            {
                RuntimeType = entry.GetType().FullName ?? entry.GetType().Name,
                DeclaredViewType = declaredType ?? string.Empty,
                DeclaredViewError = declaredError ?? string.Empty,
                Name = ReadStringProperty(entry, "Name", "ServerName"),
                DisplayName = ReadStringProperty(entry, "DisplayName", "MarkedUpName", "Title"),
                Description = description ?? string.Empty,
                Level = ReadIntProperty(entry, "Level", "RequiredLevel", "MinLevel"),
                StarCost = ReadIntProperty(entry, "StarCost", "Cost"),
                MaxTalentLevel = ReadIntProperty(entry, "MaxTalentLevel", "MaxLevel"),
                IsSelected = ReadBoolProperty(entry, "IsSelected", "Selected", "IsLearned", "Learned", "Unlocked"),
                TalentStrings = ReadStringListProperty(declared ?? entry, "TalentStrings"),
                Benefits = ReadStringListProperty(declared ?? entry, "Benefits"),
                Raw = SnapshotValue(entry, 5, 64, 64),
                DeclaredHints = SnapshotMatchingProperties(declared, 5, "Talent", "Bonus", "Effect", "Cause", "Description", "Level", "Star", "Value", "Percent", "Multiplier", "Benefit"),
                DeclaredRaw = SnapshotValue(declared, 5, 64, 64)
            };
            result.Add(row);
        }
        return result;
    }

    private static ModifiersDump DumpModifiers(GlobalDataView global, RecipesDump recipes)
    {
        var result = new ModifiersDump { DumpedAtLocal = DateTime.Now };
        var seenDynamic = new HashSet<string>(StringComparer.Ordinal);
        var seenUpgradeItems = new HashSet<int>();
        const int maxDynamicRows = 300;

        void AddDynamic(string kind, string context, IDynamicValueView value, string skillHint)
        {
            if (value == null || result.DynamicValues.Count >= maxDynamicRows) return;
            var runtimeType = value.GetType().FullName ?? value.GetType().Name;
            var key = runtimeType + "|" + kind + "|" + (skillHint ?? string.Empty);
            if (!seenDynamic.Add(key)) return;

            result.DynamicValues.Add(new DynamicValueProbeRow
            {
                Kind = kind,
                Context = context ?? string.Empty,
                SkillHint = skillHint ?? string.Empty,
                RuntimeType = runtimeType,
                BaseValue = DynamicBase(value),
                CurrentValue = DynamicCurrent(value),
                Raw = SnapshotValue(value, 5, 64, 64)
            });
        }

        try
        {
            var all = global.AllRecipes;
            if (all != null)
            {
                foreach (var pair in all)
                {
                    var list = pair.Value;
                    if (list == null) continue;
                    for (var i = 0; i < list.Count; i++)
                    {
                        var recipe = list[i];
                        if (recipe == null) continue;
                        var family = recipe.Family;
                        var recipeName = Safe(() => recipe.Name) ?? Safe(() => family?.RecipeName) ?? string.Empty;
                        var skillHint = SkillHint(family);

                        AddDynamic("CraftMinutes", recipeName, family?.CraftMinutes, skillHint);
                        AddDynamic("LaborInCalories", recipeName, family?.LaborInCalories, skillHint);

                        if (recipe.Ingredients != null)
                        {
                            for (var n = 0; n < recipe.Ingredients.Count; n++)
                            {
                                var x = recipe.Ingredients[n];
                                if (x == null) continue;
                                AddDynamic("IngredientQuantity", recipeName + " :: " + IngredientName(x), x.Quantity, skillHint);
                            }
                        }

                        if (recipe.Products != null)
                        {
                            for (var n = 0; n < recipe.Products.Count; n++)
                            {
                                var x = recipe.Products[n];
                                if (x == null) continue;
                                AddDynamic("ProductQuantity", recipeName + " :: " + ItemName(x.Item), x.Quantity, skillHint);
                                TryAddUpgradeItem(result, seenUpgradeItems, x.Item, recipeName, family);
                            }
                        }

                        if (family?.Product != null)
                        {
                            for (var n = 0; n < family.Product.Count; n++)
                            {
                                var x = family.Product[n];
                                if (x == null) continue;
                                TryAddUpgradeItem(result, seenUpgradeItems, x.Item, recipeName, family);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            result.Warnings.Add("Recipe/dynamic modifier probe failed: " + ex.Message);
        }

        try
        {
            result.GlobalCandidates = SnapshotMatchingProperties(global, 4, "Talent", "Bonus", "Module", "Modifier", "Upgrade", "Efficiency");
        }
        catch (Exception ex)
        {
            result.Warnings.Add("GlobalData modifier-property probe failed: " + ex.Message);
        }

        try
        {
            var user = Player.obj?.UserView;
            if (user != null)
            {
                result.PlayerCandidates = SnapshotMatchingProperties(user, 4, "Talent", "Bonus", "Module", "Modifier", "Upgrade", "Efficiency");
                if (user.Skillset != null)
                    result.SkillsetCandidates = SnapshotMatchingProperties(user.Skillset, 4, "Talent", "Bonus", "Module", "Modifier", "Upgrade", "Efficiency");
            }
        }
        catch (Exception ex)
        {
            result.Warnings.Add("Player modifier-property probe failed: " + ex.Message);
        }

        result.Notes.Add("DynamicValues are runtime probes. BaseValue/CurrentValue are authoritative for the current client context; Raw is reflection data used to discover the formula/causes behind them.");
        result.Notes.Add("UpgradeItems include base ItemView data plus a v4.5 re-wrap of the same native pointer as its declared concrete *View type. Benefits is explicitly decoded from List<LocString>; GenericMultiplier/SkillMultiplier/SkillTypeID are also surfaced when the client DTO sends them.");
        result.Notes.Add("Object-specific installed crafting-table modules are not assumed. If their state is not present in client DTOs, a later dumper can target the concrete table object after we identify the relevant view/component.");
        return result;
    }

    private static void TryAddUpgradeItem(ModifiersDump result, HashSet<int> seen, ItemView item, string recipeName, RecipeFamilyView family)
    {
        if (item == null || !LooksLikeUpgradeOrModule(item)) return;
        var id = item.TypeID;
        if (!seen.Add(id)) return;

        var declared = TryRewrapDeclaredView(item, out var declaredType, out var declaredError);
        var classInfo = ReadProperty(item, "ClassInfo");
        var baseTypeName = ReadStringProperty(classInfo, "BaseTypeName");
        var isEfficiencyModule = string.Equals(baseTypeName, "EfficiencyModuleView", StringComparison.OrdinalIgnoreCase) ||
                                 (declaredType ?? string.Empty).IndexOf("UpgradeItemView", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                 HasTypeInHierarchy(declared, "EfficiencyModuleView");

        result.UpgradeItems.Add(new UpgradeItemProbeRow
        {
            ItemTypeId = id,
            ServerName = Safe(() => item.ServerName) ?? string.Empty,
            DisplayName = Safe(() => item.DisplayName) ?? string.Empty,
            SourceRecipe = recipeName ?? string.Empty,
            RuntimeType = item.GetType().FullName ?? item.GetType().Name,
            DeclaredViewType = declaredType ?? string.Empty,
            DeclaredBaseTypeName = baseTypeName ?? string.Empty,
            DeclaredViewError = declaredError ?? string.Empty,
            IsEfficiencyModule = isEfficiencyModule,
            Efficiency = ReadFloatProperty(declared, "Efficiency"),
            GenericMultiplier = ReadFloatProperty(declared, "GenericMultiplier"),
            SkillMultiplier = ReadFloatProperty(declared, "SkillMultiplier"),
            SkillTypeId = ReadIntProperty(declared, "SkillTypeID", "SkillTypeId"),
            SkillType = ReadSkillTypeName(declared),
            Benefits = ReadStringListProperty(declared, "Benefits"),
            Hints = SnapshotMatchingProperties(item, 4, "Module", "Efficiency", "Skill", "Multiplier", "Type", "Description", "Value", "Bonus", "Benefit"),
            Raw = SnapshotValue(item, 4, 64, 64),
            DeclaredHints = SnapshotMatchingProperties(declared, 5, "Module", "Efficiency", "Skill", "Multiplier", "Type", "Description", "Value", "Bonus", "Percent", "Benefit"),
            DeclaredRaw = SnapshotValue(declared, 5, 64, 64),
            FamilyHints = SnapshotMatchingProperties(family, 3, "Module", "Efficiency", "Skill", "Multiplier", "Upgrade", "Bonus")
        });
    }

    private static bool LooksLikeUpgradeOrModule(ItemView item)
    {
        var server = Safe(() => item.ServerName) ?? string.Empty;
        var display = Safe(() => item.DisplayName) ?? string.Empty;
        return server.IndexOf("Upgrade", StringComparison.OrdinalIgnoreCase) >= 0 ||
               server.IndexOf("Module", StringComparison.OrdinalIgnoreCase) >= 0 ||
               display.IndexOf("улучш", StringComparison.OrdinalIgnoreCase) >= 0 ||
               display.IndexOf("модул", StringComparison.OrdinalIgnoreCase) >= 0 ||
               display.IndexOf("upgrade", StringComparison.OrdinalIgnoreCase) >= 0 ||
               display.IndexOf("module", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string SkillHint(RecipeFamilyView family)
    {
        if (family?.RequiredSkills == null || family.RequiredSkills.Count == 0) return string.Empty;
        var parts = new List<string>();
        for (var i = 0; i < family.RequiredSkills.Count; i++)
        {
            var req = family.RequiredSkills[i];
            if (req == null) continue;
            var name = Safe(() => req.Description.ToString()) ?? Safe(() => req.SkillType?.ToString()) ?? req.SkillTypeID.ToString();
            parts.Add(name + " @" + req.Level);
        }
        return string.Join("; ", parts);
    }

    private static string IngredientName(IngredientElementView x)
    {
        if (x == null) return string.Empty;
        if (x.IsSpecificItem) return ItemName(x.Item);
        return Safe(() => x.Tag?.DisplayName.ToString()) ?? Safe(() => x.Tag?.Name) ?? string.Empty;
    }

    // Reflection probe intentionally works only on public proxy properties and converts
    // everything into ordinary managed dictionaries/lists/scalars before JSON serialization.
    // This avoids System.Text.Json trying to traverse Il2Cpp proxy graphs itself.
    private static object SnapshotMatchingProperties(object obj, int depth, params string[] keywords)
    {
        if (obj == null) return null;
        var result = new Dictionary<string, object>(StringComparer.Ordinal);
        var type = obj.GetType();
        result["$type"] = type.FullName ?? type.Name;

        PropertyInfo[] props;
        try { props = type.GetProperties(BindingFlags.Instance | BindingFlags.Public); }
        catch { return result; }

        for (var i = 0; i < props.Length; i++)
        {
            var p = props[i];
            if (p == null || p.GetIndexParameters().Length != 0 || !p.CanRead) continue;
            var match = false;
            for (var k = 0; k < keywords.Length; k++)
            {
                if (p.Name.IndexOf(keywords[k], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    match = true;
                    break;
                }
            }
            if (!match) continue;

            try
            {
                var value = p.GetValue(obj);
                result[p.Name] = SnapshotValue(value, depth - 1, 48, 48);
            }
            catch (Exception ex)
            {
                result[p.Name] = "<error: " + ex.GetType().Name + ": " + ex.Message + ">";
            }
        }
        return result;
    }

    private static object SnapshotValue(object value, int depth, int maxProperties, int maxItems)
    {
        return SnapshotValue(value, depth, maxProperties, maxItems, new HashSet<object>(ReferenceComparer.Instance));
    }

    private static object SnapshotValue(object value, int depth, int maxProperties, int maxItems, HashSet<object> visited)
    {
        if (value == null) return null;
        var type = value.GetType();

        if (value is string || value is char || value is bool ||
            value is byte || value is sbyte || value is short || value is ushort ||
            value is int || value is uint || value is long || value is ulong || value is decimal)
            return value;

        if (value is float f) return float.IsNaN(f) || float.IsInfinity(f) ? null : f;
        if (value is double d) return double.IsNaN(d) || double.IsInfinity(d) ? null : d;

        if (value is DateTime dt) return dt;
        if (type.IsEnum) return value.ToString();

        var typeName = type.FullName ?? type.Name;
        // Do not use a substring check for LocString here: List<LocString> also contains
        // "LocString" in its generic type name and v4.4 therefore collapsed Benefits
        // into just the collection type name instead of enumerating it.
        if (IsLocStringType(type)) return StringifyValue(value);
        if (typeName.IndexOf("StringView", StringComparison.OrdinalIgnoreCase) >= 0 ||
            string.Equals(typeName, "System.Type", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(typeName, "Il2CppSystem.Type", StringComparison.OrdinalIgnoreCase))
        {
            return StringifyValue(value);
        }

        if (depth <= 0)
        {
            try { return value.ToString(); } catch { return typeName; }
        }

        if (!type.IsValueType)
        {
            if (!visited.Add(value)) return "<cycle: " + typeName + ">";
        }

        var collection = TrySnapshotCollection(value, depth, maxProperties, maxItems, visited);
        if (collection != null)
        {
            if (!type.IsValueType) visited.Remove(value);
            return collection;
        }

        var dict = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["$type"] = typeName
        };

        PropertyInfo[] props;
        try { props = type.GetProperties(BindingFlags.Instance | BindingFlags.Public); }
        catch
        {
            if (!type.IsValueType) visited.Remove(value);
            return dict;
        }

        var count = 0;
        for (var i = 0; i < props.Length && count < maxProperties; i++)
        {
            var p = props[i];
            if (p == null || !p.CanRead || p.GetIndexParameters().Length != 0) continue;
            if (SkipReflectionProperty(p.Name)) continue;

            try
            {
                var child = p.GetValue(value);
                dict[p.Name] = SnapshotValue(child, depth - 1, maxProperties, maxItems, visited);
            }
            catch (Exception ex)
            {
                dict[p.Name] = "<error: " + ex.GetType().Name + ": " + ex.Message + ">";
            }
            count++;
        }

        if (!type.IsValueType) visited.Remove(value);
        return dict;
    }

    private static object TrySnapshotCollection(object value, int depth, int maxProperties, int maxItems, HashSet<object> visited)
    {
        var type = value.GetType();

        // Managed collections / arrays first.
        if (value is System.Collections.IEnumerable enumerable && value is not string)
        {
            var list = new List<object>();
            try
            {
                foreach (var item in enumerable)
                {
                    if (list.Count >= maxItems) break;
                    list.Add(SnapshotValue(item, depth - 1, maxProperties, maxItems, visited));
                }
                return new Dictionary<string, object>
                {
                    ["$type"] = type.FullName ?? type.Name,
                    ["$items"] = list,
                    ["$truncated"] = list.Count >= maxItems
                };
            }
            catch { }
        }

        // Il2Cpp generic List<T>/IReadOnlyList-like wrappers expose Count + get_Item.
        try
        {
            var countProp = type.GetProperty("Count", BindingFlags.Instance | BindingFlags.Public);
            var itemProp = type.GetProperty("Item", BindingFlags.Instance | BindingFlags.Public);
            if (countProp != null && itemProp != null)
            {
                var rawCount = countProp.GetValue(value);
                if (rawCount is int count)
                {
                    var list = new List<object>();
                    var take = Math.Min(count, maxItems);
                    for (var i = 0; i < take; i++)
                    {
                        object item = null;
                        try { item = itemProp.GetValue(value, new object[] { i }); } catch { }
                        list.Add(SnapshotValue(item, depth - 1, maxProperties, maxItems, visited));
                    }
                    return new Dictionary<string, object>
                    {
                        ["$type"] = type.FullName ?? type.Name,
                        ["$count"] = count,
                        ["$items"] = list,
                        ["$truncated"] = count > take
                    };
                }
            }
        }
        catch { }

        return null;
    }

    private static IEnumerable<object> EnumerateUnknown(object collection, int maxItems)
    {
        if (collection == null) yield break;

        if (collection is System.Collections.IEnumerable enumerable && collection is not string)
        {
            var n = 0;
            System.Collections.IEnumerator e = null;
            try { e = enumerable.GetEnumerator(); } catch { }
            if (e != null)
            {
                while (n < maxItems)
                {
                    bool moved;
                    try { moved = e.MoveNext(); } catch { yield break; }
                    if (!moved) yield break;
                    object current = null;
                    try { current = e.Current; } catch { }
                    yield return current;
                    n++;
                }
                yield break;
            }
        }

        var type = collection.GetType();
        PropertyInfo countProp = null;
        PropertyInfo itemProp = null;
        try
        {
            countProp = type.GetProperty("Count", BindingFlags.Instance | BindingFlags.Public);
            itemProp = type.GetProperty("Item", BindingFlags.Instance | BindingFlags.Public);
        }
        catch { }

        if (countProp != null && itemProp != null)
        {
            int count;
            try { count = Convert.ToInt32(countProp.GetValue(collection)); }
            catch { yield break; }
            count = Math.Min(count, maxItems);
            for (var i = 0; i < count; i++)
            {
                object item = null;
                try { item = itemProp.GetValue(collection, new object[] { i }); } catch { }
                yield return item;
            }
            yield break;
        }

        // Last resort for Il2Cpp IEnumerableView wrappers.
        MethodInfo getEnumerator = null;
        try { getEnumerator = type.GetMethod("GetEnumerator", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null); }
        catch { }
        if (getEnumerator == null) yield break;

        object iterator;
        try { iterator = getEnumerator.Invoke(collection, null); }
        catch { yield break; }
        if (iterator == null) yield break;

        var iteratorType = iterator.GetType();
        var moveNext = iteratorType.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.Public);
        var currentProp = iteratorType.GetProperty("Current", BindingFlags.Instance | BindingFlags.Public);
        if (moveNext == null || currentProp == null) yield break;

        for (var i = 0; i < maxItems; i++)
        {
            bool moved;
            try { moved = Convert.ToBoolean(moveNext.Invoke(iterator, null)); }
            catch { yield break; }
            if (!moved) yield break;
            object current = null;
            try { current = currentProp.GetValue(iterator); } catch { }
            yield return current;
        }
    }

    private static object TryRewrapDeclaredView(object obj, out string declaredTypeName, out string error)
    {
        declaredTypeName = string.Empty;
        error = string.Empty;
        if (obj == null) return null;

        try
        {
            declaredTypeName = ReadStringProperty(obj, "TypeName");
            if (string.IsNullOrWhiteSpace(declaredTypeName))
            {
                var classInfo = ReadProperty(obj, "ClassInfo");
                declaredTypeName = ReadStringProperty(classInfo, "Name", "TypeName");
            }
            if (string.IsNullOrWhiteSpace(declaredTypeName))
            {
                error = "declared view type name was not exposed";
                return null;
            }

            var sourceType = obj.GetType();
            var asm = sourceType.Assembly;
            Type targetType = null;
            try { targetType = asm.GetType(declaredTypeName, false, true); } catch { }
            if (targetType == null)
            {
                try
                {
                    var types = asm.GetTypes();
                    for (var i = 0; i < types.Length; i++)
                    {
                        var t = types[i];
                        if (t == null) continue;
                        if (string.Equals(t.Name, declaredTypeName, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(t.FullName, declaredTypeName, StringComparison.OrdinalIgnoreCase))
                        {
                            targetType = t;
                            break;
                        }
                    }
                }
                catch { }
            }
            if (targetType == null)
            {
                error = "interop type not found in " + asm.GetName().Name;
                return null;
            }

            object ptrValue = null;
            Type scan = sourceType;
            while (scan != null && ptrValue == null)
            {
                try
                {
                    var pp = scan.GetProperty("Pointer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (pp != null && pp.CanRead) ptrValue = pp.GetValue(obj);
                }
                catch { }
                if (ptrValue == null)
                {
                    try
                    {
                        var pf = scan.GetField("Pointer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ??
                                 scan.GetField("pointer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ??
                                 scan.GetField("_pointer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (pf != null) ptrValue = pf.GetValue(obj);
                    }
                    catch { }
                }
                scan = scan.BaseType;
            }

            if (ptrValue is not IntPtr ptr || ptr == IntPtr.Zero)
            {
                error = "native Pointer was not available";
                return null;
            }

            try
            {
                return Activator.CreateInstance(targetType, new object[] { ptr });
            }
            catch (Exception ex)
            {
                error = "proxy construction failed: " + ex.GetType().Name + ": " + ex.Message;
                return null;
            }
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name + ": " + ex.Message;
            return null;
        }
    }

    private static bool HasTypeInHierarchy(object obj, string typeName)
    {
        if (obj == null || string.IsNullOrWhiteSpace(typeName)) return false;
        try
        {
            var t = obj.GetType();
            while (t != null)
            {
                if (string.Equals(t.Name, typeName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(t.FullName, typeName, StringComparison.OrdinalIgnoreCase)) return true;
                t = t.BaseType;
            }
        }
        catch { }
        return false;
    }

    private static float? ReadFloatProperty(object obj, params string[] names)
    {
        var value = ReadProperty(obj, names);
        if (value == null) return null;
        try
        {
            var x = Convert.ToSingle(value);
            return float.IsNaN(x) || float.IsInfinity(x) ? null : x;
        }
        catch { return null; }
    }

    private static string ReadStringProperty(object obj, params string[] names)
    {
        var value = ReadProperty(obj, names);
        return StringifyValue(value);
    }

    private static bool IsLocStringType(Type type)
    {
        if (type == null) return false;
        var name = type.Name ?? string.Empty;
        var full = type.FullName ?? string.Empty;
        return string.Equals(name, "LocString", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(full, "Eco.Shared.Localization.LocString", StringComparison.OrdinalIgnoreCase);
    }

    private static string StringifyValue(object value)
    {
        if (value == null) return string.Empty;
        if (value is string s) return s;

        var type = value.GetType();
        if (IsLocStringType(type))
        {
            // Eco exposes a public implicit LocString -> string conversion. Calling it
            // by reflection keeps this probe independent of the concrete interop wrapper.
            try
            {
                var methods = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                for (var i = 0; i < methods.Length; i++)
                {
                    var m = methods[i];
                    if (m == null || !string.Equals(m.Name, "op_Implicit", StringComparison.Ordinal) || m.ReturnType != typeof(string)) continue;
                    var ps = m.GetParameters();
                    if (ps.Length != 1) continue;
                    try
                    {
                        var converted = m.Invoke(null, new[] { value }) as string;
                        if (!string.IsNullOrEmpty(converted)) return converted;
                    }
                    catch { }
                }
            }
            catch { }
        }

        try { return value.ToString() ?? string.Empty; }
        catch { return string.Empty; }
    }

    private static List<string> ReadStringListProperty(object obj, params string[] names)
    {
        var result = new List<string>();
        var collection = ReadProperty(obj, names);
        if (collection == null) return result;
        foreach (var entry in EnumerateUnknown(collection, 64))
        {
            var text = StringifyValue(entry);
            if (!string.IsNullOrWhiteSpace(text)) result.Add(text);
        }
        return result;
    }

    private static string ReadSkillTypeName(object obj)
    {
        var skillType = ReadProperty(obj, "SkillType");
        if (skillType == null) return string.Empty;
        var name = ReadStringProperty(skillType, "ServerName", "Name", "DisplayName", "TypeName");
        return string.IsNullOrWhiteSpace(name) ? StringifyValue(skillType) : name;
    }

    private static int? ReadIntProperty(object obj, params string[] names)
    {
        var value = ReadProperty(obj, names);
        if (value == null) return null;
        try { return Convert.ToInt32(value); } catch { return null; }
    }

    private static bool? ReadBoolProperty(object obj, params string[] names)
    {
        var value = ReadProperty(obj, names);
        if (value == null) return null;
        try { return Convert.ToBoolean(value); } catch { return null; }
    }

    private static object ReadProperty(object obj, params string[] names)
    {
        if (obj == null) return null;
        var type = obj.GetType();
        for (var i = 0; i < names.Length; i++)
        {
            try
            {
                var p = type.GetProperty(names[i], BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
                if (p == null || !p.CanRead || p.GetIndexParameters().Length != 0) continue;
                return p.GetValue(obj);
            }
            catch { }
        }
        return null;
    }

    private static bool SkipReflectionProperty(string name)
    {
        if (string.IsNullOrEmpty(name)) return true;
        return name.Equals("Pointer", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("Handle", StringComparison.OrdinalIgnoreCase) ||
               name.IndexOf("NativeMethod", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Il2CppClass", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceComparer Instance = new();
        public new bool Equals(object x, object y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }

    private static void DumpIngredients(Il2CppSystem.Collections.Generic.List<IngredientElementView> list, List<IngredientRow> output, Dictionary<int, ItemRow> itemIndex)
    {
        if (list == null) return;
        for (var i = 0; i < list.Count; i++)
        {
            var x = list[i];
            if (x == null) continue;
            AddItem(itemIndex, x.Item);
            output.Add(new IngredientRow
            {
                IsSpecificItem = x.IsSpecificItem,
                ItemTypeId = x.Item?.TypeID ?? 0,
                Item = ItemName(x.Item),
                ItemServerName = Safe(() => x.Item?.ServerName) ?? string.Empty,
                TagId = x.Tag?.Id ?? 0,
                Tag = Safe(() => x.Tag?.Name) ?? string.Empty,
                TagDisplayName = Safe(() => x.Tag?.DisplayName.ToString()) ?? string.Empty,
                QuantityBase = DynamicBase(x.Quantity),
                QuantityCurrent = DynamicCurrent(x.Quantity)
            });
        }
    }

    private static void DumpProducts(Il2CppSystem.Collections.Generic.List<CraftingElementView> list, List<ProductRow> output, Dictionary<int, ItemRow> itemIndex)
    {
        if (list == null) return;
        for (var i = 0; i < list.Count; i++)
        {
            var x = list[i];
            if (x == null) continue;
            AddItem(itemIndex, x.Item);
            output.Add(new ProductRow
            {
                ItemTypeId = x.Item?.TypeID ?? 0,
                Item = ItemName(x.Item),
                ItemServerName = Safe(() => x.Item?.ServerName) ?? string.Empty,
                QuantityBase = DynamicBase(x.Quantity),
                QuantityCurrent = DynamicCurrent(x.Quantity)
            });
        }
    }

    private static void AddItem(Dictionary<int, ItemRow> index, ItemView item)
    {
        if (item == null) return;
        var id = item.TypeID;
        if (index.ContainsKey(id)) return;
        index[id] = new ItemRow
        {
            TypeId = id,
            ServerName = Safe(() => item.ServerName) ?? string.Empty,
            DisplayName = Safe(() => item.DisplayName) ?? string.Empty,
            DisplayNamePlural = Safe(() => item.DisplayNamePlural) ?? string.Empty,
            NotTranslatedName = Safe(() => item.NotTranslatedName) ?? string.Empty,
            IconName = Safe(() => item.IconName) ?? string.Empty,
            IsTool = item.IsTool,
            CanBeHeld = item.CanBeHeld,
            IsConstructible = SafeBool(() => item.IsConstructible),
            IsPlaceable = SafeBool(() => item.IsPlaceable)
        };
    }

    private static string ItemName(ItemView item) => item == null ? string.Empty : (Safe(() => item.DisplayName) ?? Safe(() => item.ServerName) ?? string.Empty);
    private static string TreeName(SkillTreeView tree) => tree == null ? string.Empty : (Safe(() => tree.StaticSkill?.DisplayName) ?? Safe(() => tree.MarkedUpName.ToString()) ?? string.Empty);

    private static float? DynamicBase(IDynamicValueView value)
    {
        if (value == null) return null;
        try { return Finite(value.GetBaseValue); }
        catch { return null; }
    }

    private static float? DynamicCurrent(IDynamicValueView value)
    {
        if (value == null) return null;

        // ECO provides this helper specifically to evaluate a dynamic value in the
        // current local-player context (skills, modifiers, talents, etc.).
        try { return Finite(DynamicValueExtensions.GetCurrentValue(value)); }
        catch { }

        // Fallback kept for compatibility with values that only implement the raw interface.
        try { return Finite(value.GetCurrentValue(null)); }
        catch { return null; }
    }

    private static void WriteDump<T>(string folder, string baseName, T payload)
    {
        var dir = Path.Combine(Paths.GameRootPath, folder);
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        File.WriteAllText(Path.Combine(dir, $"{baseName}_{timestamp}.json"), json);
        File.WriteAllText(Path.Combine(dir, $"{baseName}_latest.json"), json);
    }

    private static float? Finite(float value) => float.IsNaN(value) || float.IsInfinity(value) ? null : value;
    private static double? Finite(double value) => double.IsNaN(value) || double.IsInfinity(value) ? null : value;

    private static string Safe(Func<string> getter)
    {
        try { return getter(); }
        catch { return null; }
    }

    private static bool SafeBool(Func<bool> getter)
    {
        try { return getter(); }
        catch { return false; }
    }
}

// ---------------- DTOs ----------------

public sealed class MarketDump
{
    public int SchemaVersion { get; set; } = 2;
    public string PluginVersion { get; set; } = "0.2.5";
    public DateTime DumpedAtLocal { get; set; }
    public int Count { get; set; }
    public List<MarketRow> Trades { get; set; }
}

public sealed class MarketRow
{
    public int ItemTypeId { get; set; }
    public string Item { get; set; }
    public string ItemServerName { get; set; }
    public string Source { get; set; }
    public int NumberAvailable { get; set; }
    public float? SalePrice { get; set; }
    public float? NumberWanted { get; set; }
    public float? WantedPrice { get; set; }
    public string Currency { get; set; }
    public string CurrencyOwner { get; set; }
    public string CurrencyCategory { get; set; }
    public float? StoreBalance { get; set; }
    public bool LimitedFunds { get; set; }
    public bool LimitedStorage { get; set; }
    public bool IsBarter { get; set; }
    public bool IsWorldObjectSale { get; set; }
    public bool Auth { get; set; }
    public float? MinDurability { get; set; }
    public float? MaxDurability { get; set; }
    public float? MinIntegrity { get; set; }
    public float? MaxIntegrity { get; set; }
    public float? StoreX { get; set; }
    public float? StoreY { get; set; }
    public float? StoreZ { get; set; }
    public string Settlement { get; set; }
    public string Tag { get; set; }
    public string StatusIcons { get; set; }
}

public sealed class RecipesDump
{
    public int SchemaVersion { get; set; } = 2;
    public string PluginVersion { get; set; } = "0.2.5";
    public DateTime DumpedAtLocal { get; set; }
    public string DynamicEvaluation { get; set; } = string.Empty;
    public int Count { get; set; }
    public List<RecipeRow> Recipes { get; set; } = new();
}

public sealed class RecipeRow
{
    public string Name { get; set; }
    public string DisplayName { get; set; }
    public string FamilyRecipeName { get; set; }
    public string FamilyDisplayName { get; set; }
    public string FamilyMarkedUpName { get; set; }
    public string CraftingTable { get; set; }
    public string CraftingTableServerName { get; set; }
    public float? CraftMinutesBase { get; set; }
    public float? CraftMinutesCurrent { get; set; }
    public float? Labor { get; set; }
    public float? LaborCaloriesBase { get; set; }
    public float? LaborCaloriesCurrent { get; set; }
    public bool CraftableDefault { get; set; }
    public bool RequiresTalentUnlock { get; set; }
    public string RequiredTalentName { get; set; }
    public bool UsesPlayerInventory { get; set; }
    public bool RequiresStrangeBlueprint { get; set; }
    public bool ZeroSalvageCost { get; set; }
    public bool PlayerMeetsRequiredSkillLevels { get; set; }
    public List<IngredientRow> Ingredients { get; set; } = new();
    public List<ProductRow> Products { get; set; } = new();
    public List<IngredientRow> FamilyIngredients { get; set; } = new();
    public List<ProductRow> FamilyProducts { get; set; } = new();
    public List<RequiredSkillRow> RequiredSkills { get; set; } = new();
    public List<RequiredModuleRow> RequiredModules { get; set; } = new();
}

public sealed class IngredientRow
{
    public bool IsSpecificItem { get; set; }
    public int ItemTypeId { get; set; }
    public string Item { get; set; }
    public string ItemServerName { get; set; }
    public int TagId { get; set; }
    public string Tag { get; set; }
    public string TagDisplayName { get; set; }
    public float? QuantityBase { get; set; }
    public float? QuantityCurrent { get; set; }
}

public sealed class ProductRow
{
    public int ItemTypeId { get; set; }
    public string Item { get; set; }
    public string ItemServerName { get; set; }
    public float? QuantityBase { get; set; }
    public float? QuantityCurrent { get; set; }
}

public sealed class RequiredSkillRow
{
    public int SkillTypeId { get; set; }
    public int Level { get; set; }
    public string SkillType { get; set; }
    public string Description { get; set; }
    public bool IsMet { get; set; }
    public int? PlayerLevel { get; set; }
    public float? PlayerExperience { get; set; }
    public float? PlayerPercentTowardsNextLevel { get; set; }
    public bool PlayerHasRequiredLevel { get; set; }
}

public sealed class RequiredModuleRow
{
    public string ModuleName { get; set; }
    public string ModuleLink { get; set; }
    public string Description { get; set; }
}

public sealed class SkillsDump
{
    public int SchemaVersion { get; set; } = 2;
    public string PluginVersion { get; set; } = "0.2.5";
    public DateTime DumpedAtLocal { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public int StarsAvailable { get; set; }
    public int TotalStarsEarned { get; set; }
    public int NextStarCost { get; set; }
    public float? SkillRate { get; set; }
    public float? XP { get; set; }
    public int ProfessionCount { get; set; }
    public int SpecialtyCount { get; set; }
    public float? CostPerAdditionalSpecialty { get; set; }
    public List<SkillTreeRow> SkillTrees { get; set; } = new();
    public List<SkillRow> PlayerSkills { get; set; } = new();
}

public sealed class SkillTreeRow
{
    public bool IsResearchTree { get; set; }
    public string MarkedUpName { get; set; }
    public string State { get; set; }
    public bool Discovered { get; set; }
    public bool RequiresScroll { get; set; }
    public bool IsUsingSkillBook { get; set; }
    public int TreeResearchRank { get; set; }
    public double? TimeDiscovered { get; set; }
    public SkillRow Skill { get; set; }
    public SkillRow PlayerSkill { get; set; }
    public int? PlayerLevel { get; set; }
    public float? PlayerExperience { get; set; }
    public float? PlayerPercentTowardsNextLevel { get; set; }
    public string Parent { get; set; }
    public string Root { get; set; }
    public string Specialty { get; set; }
    public string SkillBookItem { get; set; }
    public string SkillScrollItem { get; set; }
    public string SkillBookTable { get; set; }
    public string SkillScrollMarkedUpName { get; set; }
    public List<IngredientRow> SkillItemIngredients { get; set; } = new();
    public List<TreeRequirementRow> RequiredSkillLevels { get; set; } = new();
    public List<UnlockedRecipesRow> UnlockedRecipes { get; set; } = new();
}

public sealed class SkillRow
{
    public int TypeId { get; set; }
    public string ServerName { get; set; }
    public string DisplayName { get; set; }
    public string NotTranslatedName { get; set; }
    public int Level { get; set; }
    public int MaxLevel { get; set; }
    public int Tier { get; set; }
    public bool IsRoot { get; set; }
    public bool IsSpecialty { get; set; }
    public int BaseSpecialtyCost { get; set; }
    public int EffectiveSpecialtyCost { get; set; }
    public int SpecialtyCost { get; set; }
    public float? Experience { get; set; }
    public float? ExperienceToLevel { get; set; }
    public float? PercentTowardsNextLevel { get; set; }
    public float? Education { get; set; }
    public bool IsMet { get; set; }
    public List<RequiredSkillRow> Prerequisites { get; set; } = new();
}

public sealed class TreeRequirementRow
{
    public string SkillTree { get; set; }
    public int Level { get; set; }
}

public sealed class UnlockedRecipesRow
{
    public int Level { get; set; }
    public List<string> Recipes { get; set; } = new();
}

public sealed class TalentsDump
{
    public int SchemaVersion { get; set; } = 2;
    public string PluginVersion { get; set; } = "0.2.5";
    public DateTime DumpedAtLocal { get; set; }
    public int Count { get; set; }
    public List<TalentSkillRow> Skills { get; set; } = new();
    public List<RecipeTalentRequirementRow> RecipeTalentRequirements { get; set; } = new();
    public object PlayerTalentState { get; set; }
    public List<string> Warnings { get; set; } = new();
}

public sealed class TalentSkillRow
{
    public int SkillTypeId { get; set; }
    public string SkillServerName { get; set; }
    public string SkillDisplayName { get; set; }
    public string TreeState { get; set; }
    public int? PlayerLevel { get; set; }
    public string DefinitionCollectionType { get; set; }
    public string PlayerCollectionType { get; set; }
    public List<TalentEntryRow> DefinitionTalents { get; set; } = new();
    public List<TalentEntryRow> PlayerTalents { get; set; } = new();
}

public sealed class TalentEntryRow
{
    public string RuntimeType { get; set; }
    public string DeclaredViewType { get; set; }
    public string DeclaredViewError { get; set; }
    public string Name { get; set; }
    public string DisplayName { get; set; }
    public string Description { get; set; }
    public int? Level { get; set; }
    public int? StarCost { get; set; }
    public int? MaxTalentLevel { get; set; }
    public bool? IsSelected { get; set; }
    public List<string> TalentStrings { get; set; } = new();
    public List<string> Benefits { get; set; } = new();
    public object Raw { get; set; }
    public object DeclaredHints { get; set; }
    public object DeclaredRaw { get; set; }
}

public sealed class RecipeTalentRequirementRow
{
    public string Recipe { get; set; }
    public string DisplayName { get; set; }
    public string RequiredTalent { get; set; }
    public string CraftingTable { get; set; }
}

public sealed class ModifiersDump
{
    public int SchemaVersion { get; set; } = 2;
    public string PluginVersion { get; set; } = "0.2.5";
    public DateTime DumpedAtLocal { get; set; }
    public List<DynamicValueProbeRow> DynamicValues { get; set; } = new();
    public List<UpgradeItemProbeRow> UpgradeItems { get; set; } = new();
    public object GlobalCandidates { get; set; }
    public object PlayerCandidates { get; set; }
    public object SkillsetCandidates { get; set; }
    public List<string> Notes { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public sealed class DynamicValueProbeRow
{
    public string Kind { get; set; }
    public string Context { get; set; }
    public string SkillHint { get; set; }
    public string RuntimeType { get; set; }
    public float? BaseValue { get; set; }
    public float? CurrentValue { get; set; }
    public object Raw { get; set; }
}

public sealed class UpgradeItemProbeRow
{
    public int ItemTypeId { get; set; }
    public string ServerName { get; set; }
    public string DisplayName { get; set; }
    public string SourceRecipe { get; set; }
    public string RuntimeType { get; set; }
    public string DeclaredViewType { get; set; }
    public string DeclaredBaseTypeName { get; set; }
    public string DeclaredViewError { get; set; }
    public bool IsEfficiencyModule { get; set; }
    public float? Efficiency { get; set; }
    public float? GenericMultiplier { get; set; }
    public float? SkillMultiplier { get; set; }
    public int? SkillTypeId { get; set; }
    public string SkillType { get; set; }
    public List<string> Benefits { get; set; } = new();
    public object Hints { get; set; }
    public object Raw { get; set; }
    public object DeclaredHints { get; set; }
    public object DeclaredRaw { get; set; }
    public object FamilyHints { get; set; }
}

public sealed class ItemsDump
{
    public int SchemaVersion { get; set; } = 2;
    public string PluginVersion { get; set; } = "0.2.5";
    public DateTime DumpedAtLocal { get; set; }
    public int Count { get; set; }
    public List<ItemRow> Items { get; set; } = new();
}

public sealed class ItemRow
{
    public int TypeId { get; set; }
    public string ServerName { get; set; }
    public string DisplayName { get; set; }
    public string DisplayNamePlural { get; set; }
    public string NotTranslatedName { get; set; }
    public string IconName { get; set; }
    public bool IsTool { get; set; }
    public bool CanBeHeld { get; set; }
    public bool IsConstructible { get; set; }
    public bool IsPlaceable { get; set; }
}
