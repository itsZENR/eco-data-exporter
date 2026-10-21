Eco Market Dumper v0.2.5 (v4.5)
================================

Target: ECO 0.14.1.1 + BepInEx 6 IL2CPP.

Keys:
  F8 - export current Economy Viewer market (open Economy Viewer first)
  F9 - export recipes, skill trees/player skills, referenced items, talents and modifier/upgrade probes

Build:
  dotnet build -c Release

Default game path in EcoMarketDumper.csproj:
  D:\SteamLibrary\steamapps\common\Eco

After a successful build the DLL is copied automatically to:
  D:\SteamLibrary\steamapps\common\Eco\BepInEx\plugins\EcoMarketDumper\EcoMarketDumper.dll

IMPORTANT: BepInEx does not hot-reload this plugin. Fully close and restart ECO after rebuilding/replacing the DLL.

F9 output:
  D:\SteamLibrary\steamapps\common\Eco\EcoDataDumps\recipes_latest.json
  D:\SteamLibrary\steamapps\common\Eco\EcoDataDumps\skills_latest.json
  D:\SteamLibrary\steamapps\common\Eco\EcoDataDumps\items_latest.json
  D:\SteamLibrary\steamapps\common\Eco\EcoDataDumps\talents_latest.json
  D:\SteamLibrary\steamapps\common\Eco\EcoDataDumps\modifiers_latest.json

v4.5 changes:
- Keeps all v4.3 exports.
- Re-wraps generic ItemView/TalentGroupView proxies using the declared concrete TypeName on the same native IL2CPP pointer.
- modifiers_latest.json adds DeclaredViewType, DeclaredBaseTypeName, IsEfficiencyModule, Efficiency, DeclaredHints and DeclaredRaw.
  This is intended to expose EfficiencyModuleView.Efficiency and inherited module/bonus properties that v4.3 could not see because its runtime wrapper was only ItemView.
- talents_latest.json adds DeclaredViewType, DeclaredHints and DeclaredRaw for each talent group. This tests whether concrete talent-group views expose extra bonus/effect fields beyond TalentStrings.
- talents/modifiers schema version bumped to 2.

What we learned from v4.3:
- Talent groups, unlock levels, star costs, MaxTalentLevel and TalentStrings are exposed correctly.
- Upgrade items identify themselves in ClassInfo as derived from EfficiencyModuleView, but v4.3 only held them as ItemView, hiding Efficiency. v4.5 targets that exact mismatch.
- The client may still not expose the internal causes/effects for unlearned talents. If DeclaredRaw still only contains group metadata, that is a client DTO limitation rather than a reflection bug.


v4.5 changes:
- fixes List<LocString> serialization (v4.4 treated the whole collection as a LocString because of a substring match);
- decodes LocString through Eco's implicit LocString -> string operator when available;
- UpgradeItems now expose Benefits, GenericMultiplier, SkillMultiplier, SkillTypeId and SkillType directly;
- talent rows now expose TalentStrings and Benefits directly; talent Description/GetDescription should now become readable text if the client sent it.
