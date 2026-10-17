# Sub Skills Display — development notes

How the mod is built and how it works. The player-facing description is in the
[README](../README.md).

## Environment this is built against

| Thing | Version |
| --- | --- |
| Game | Unity 2021.3.45f2, DLC1-4 installed (same build as KarmaClimb and EclipseAllMaps) |
| `RiskOfRain2.GameLibs` | `1.5.0-r.1` |
| `MMHOOK.RoR2` | `2026.10.8` |
| BepInEx | 5.4.21 |
| Dev profile | r2modman profile `SubSkillDisplay` (needs BepInExPack and HookGenPatcher) |

## Building

```
dotnet build SubSkillsDisplay/SubSkillsDisplay.csproj
```

The build drops `SubSkillsDisplay.dll` into
`%APPDATA%\r2modmanPlus-local\RiskOfRain2\profiles\SubSkillDisplay\BepInEx\plugins\SubSkillsDisplay`.
The copy is skipped if the profile folder is missing.

`dotnet build SubSkillsDisplay.sln -c Release` also writes `artifacts/SubSkillsDisplay-<version>.zip`, the
Thunderstore package. It fails if `Version` in the csproj, `version_number` in the manifest and
`PluginVersion` in the code disagree. `Thunderstore/icon.png` (256×256) is the Anima Harvester's four skill
icons, exported from the game's sprites.

## The Extra Skills tab

[ExtraSkillsTab.cs](../SubSkillsDisplay/ExtraSkillsTab.cs). Character select's left panel
(`CharacterSelectUIMain/.../SurvivorInfoPanel, Active`) has a tab bar, `SubheaderPanel (Overview, Skills,
Loadout)`, run by an `HGHeaderNavigationController` whose `headers` pair each button with a root:
`OverviewPanel`, `SkillPanel` and `LoadoutPanel`, stacked in `ContentPanel` (532 px high). The Skills tab
is `SkillPanel`, which is also `CharacterSelectController.skillStripContainer`; it holds the strip and filler
templates and the hover description box (`DescriptionPanel, Skill`), which each strip's `HGButton` writes
into.

After `CharacterSelectController.Awake` the mod copies the Skills button (placed last, after the Loadout
button; its click listener replaced so it selects the new tab) and `SkillPanel` (for its fonts and hover
box; its layout no longer force-expands children, so lines keep their height), and adds a header entry at
the end of the list, permanently: the tab is there for every survivor. That runs before the navigation
controller's `Start`, which builds its list from `headers`. Bumpers walk the list, so gamepads reach the tab
too. The button text auto-sizes (12-24 pt), so "Extra Skills" fits the four-button bar.

After every `CharacterSelectController.RebuildLocal` (survivor, loadout, unlock or entitlement change) the
tab lists every unlocked extra skill of the survivor, whatever the loadout picks, in lines per skill type
([SkillLines.cs](../SubSkillsDisplay/SkillLines.cs)), under an explanation (a copy of a strip's description
text). A survivor without any shows a line saying so. A survivor not unlocked on this client
(`SurvivorCatalog.SurvivorIsUnlockedOnThisClient`) shows nothing; vanilla doesn't let it be picked anyway.

Character select paints its tab buttons in the survivor's colour: their Images are in
`CharacterSelectController.primaryColorImages`, which `RebuildLocal` colours. The copy's counterparts of the
Skills button's entries are added to that list (and to `primaryColorTexts`, empty in this build).

The labels and explanations are the tokens in [ModTokens.cs](../SubSkillsDisplay/ModTokens.cs), set on
`Language.english`, which every language falls back to.

## Lines and hover text

The Loadout tab builds its rows from `LoadoutPanelController.rowPrefab` and `loadoutButtonPrefab` (loaded at
startup through `LegacyResourcesAPI`, so also there in a run): a 96 px row with a 120 px `SlotLabel`, a
`ButtonContainer` (left padding 128) of 76 px icon buttons (48 px minimum), and a `ButtonSelectionHighlight,
Checkbox` on the picked one; unpicked buttons get `colorMultiplier` 0.5.
[SkillLines.cs](../SubSkillsDisplay/SkillLines.cs) uses the same prefabs and drops what makes them look
choosable: the highlight is destroyed, every button stays at full brightness, and
`disablePointerClick`/`disableGamepadClick` leave hovering and gamepad selection only. Lines are Primary,
Secondary, Utility, Special (the Loadout tab's titles), then any other: a loadout row's own title
(Operator's Drone) or Misc.

Hovering writes into the panel's description box like the Loadout tab (`LOGBOOK_HOVER_DESCRIPTION_FORMAT`:
name, then description), followed by the keyword explanations the Skills tab shows
([SkillEntry.cs](../SubSkillsDisplay/SkillEntry.cs)), and the button's `TooltipProvider` gets the name and
description as in the Loadout tab. [Keywords.cs](../SubSkillsDisplay/Keywords.cs) adds, to a skill's own
`keywordTokens`, the keywords its description highlights: every keyword some skill or passive lists is
indexed by its name (the `cKeywordName` part of its text, in the current language; a name several keywords
share, like CHEF's per-skill "Boost", is skipped unless exactly one is a shared `KEYWORD_` one), and matched
only inside the description's `<style=c...>` spans, so "heavy projectile" isn't the Heavy keyword. Checked on
the English text: Soul Storm gets Stunning, Boosted Ice Box and Cryocharged Railgun Freezing, Boosted Oil
Spill Oil, Drifter's skills Junk.

## The pause menu's Abilities panel

[PauseAbilitiesPanel.cs](../SubSkillsDisplay/PauseAbilitiesPanel.cs). The pause screen (`PauseScreen`, canvas
1920×1080) lists `GenericMenuButton`s in `MainPanel/OptionsPanel (JUICED)`. After `PauseScreenController.Awake`
the mod copies the Inventory button (`inventoryButtonObject`) as Abilities, right after it; after `OnEnable`
it shows it only in a running run with a master, as vanilla does Inventory.

Abilities opens a submenu the way `OpenSettingsMenu` does: the panel goes under `rootPanel` as
`submenuObject`, `mainPanel` is hidden, and when the panel is destroyed `PauseScreenController.Update` brings
`mainPanel` back. The panel holds the survivor's name, an explanation, and lines: Passive (the skill
locator's passive and visible slots that are none of the four), then Primary to Special, each the loadout's
skill followed by what it turns into with this loadout, then any other lines. They sit in a copy of
character select's `SkillPanel` (640 px wide, its description box narrowed to 520 px) loaded from the
Addressables key `RoR2/Base/UI/CharacterSelectUIMain.prefab` (the same bundle as the pause screen).
Addressables keys are not the asset paths (`Assets/RoR2/Base/UI/CharacterSelect/...`); 0.1.0 used a
path-like key that doesn't exist, so the panel never opened. The real keys are in `catalog.json`'s base64
`m_KeyDataString`. The explanation asks its layout for the lines' width: a wrapping text's preferred width is
its whole text on one line, which would widen the panel (it fits its children).

Input follows Settings: the panel has its own `UILayerKey` on a runtime `UILayer` of priority 1005 (the pause
screen is 1000), so its `HGGamepadInputEvent` on `UICancel` goes Back while the pause screen's own Cancel
(Resume) waits. Every `MPButton` in the panel is bound to that key (`requiredTopLayer`); copies would otherwise
wait for the layer of the screen they came from and never be selectable. The Back button is the gamepad's
`defaultFallbackButton`. Escape closes the whole pause screen, as from Settings (`MPEventSystem` sends `pause`).

The skills come from the player's living body (`localUser.cachedMaster.GetBody()`): its slots' `baseSkill`
are the loadout's, sent to clients by `SkillLocator.OnSerialize`, and stay so while a form overrides them.
Which slot is which comes from the body prefab, by index: while Captain places a beacon his living
`SkillLocator.primary`/`secondary` point at the beacon slots. While dead, the body prefab with the profile's
loadout is used. The title is the body prefab's name, since the Anima Harvester renames the living body.

## Where the extra skills come from

[ExtraSkillCatalog.cs](../SubSkillsDisplay/ExtraSkillCatalog.cs). Skills are read from the game: fields of
components on the body, and fields of entity states as their `EntityStateConfiguration` sets them (a new
state instance gets them in the `EntityState` constructor). Only the link between a skill and what gives it,
and the slot whose skill it replaces on the HUD (its line), is written in the mod.
[SurvivorLoadout.cs](../SubSkillsDisplay/SurvivorLoadout.cs) gives the catalog the body and its picked skills:
in character select the body prefab and the loadout being edited, in a run the living body. The Extra Skills
tab lists every unlocked one; the pause panel only those the run's loadout gives. A skill the Loadout tab
offers for a visible slot is never listed, and two with the same name and description are listed once per
line.

| Survivor | Extra skills | Line | Read from | Given by |
| --- | --- | --- | --- | --- |
| Acolyte | Dark Summons / Anima Echo | Special | `AcolyteFormController.transformSkillSlot`, a slot character select hides; `SacrificeReady` puts it on the special at full Convictions | Its title, `LOADOUT_TRANSFORM` (Litany) |
| Acolyte | Torrential Slash, Harrowing Chains, Soul Storm, Banish | Each its own | `AcolyteBodySwapController.primary/secondary/utility/specialSkillOverride` (+ `reaperProjectileSkillOverrides`: Soul Storm's recast, a duplicate) | A skill entering `EnterBodySwap` (Dark Summons), named after the swap's `baseNameToken` (`REAPER_BODY_NAME`, Anima Harvester) |
| Acolyte | Blade Dash | Utility | `AcolyteBodySwapController.acolyteProjectileSkillOverrides` (Blade Sling's and Javelin's, identical) | The utility the override names |
| Captain | Beacons | Primary (beacon 1), Secondary (beacon 2) | Slots `SupplyDrop1/2`, hidden from character select; `SetupSupplyDrop` points the primary and secondary at them while placing | `CaptainSupplyDropController.prepSupplyDropSkill` (Orbital Supply Beacon) |
| CHEF | Boosted Dice, Sear, Ice Box, Roll, Oil Spill | Each its own | `ChefController.*SkillOverrideDef` | A skill entering `YesChef`, and the slot's skill having the boost's `skillName` (as `ChefController.OverrideYesChefSkillBySlot`) |
| Drifter | Bludgeon, Discard | Primary, Utility | `BaggedObject.primaryOverride/utilityOverride` | A skill entering `AimRepossess` |
| Drifter | Tornado Slam (the final slam) | Primary | `TornadoSlam.primaryOverride` | A skill entering `ChargeTornadoSlam` |
| Operator | Gunner, Healing and Transport Drone Mk 2 | Drone | Slots `Drone1/2`, hidden from character select, titled `LOADOUT_DRONE` | |
| Railgunner | M99, HH44 | Primary | `ActiveScopeHeavy/Light.primaryOverride` | A skill entering `WindUpScopeHeavy/Light` |
| Railgunner | Active Reload | Primary | `Reloading.primaryOverride` | A scope whose shot's state has `queueReload` (only `FireSnipeHeavy`: M99) |
| Railgunner | Supercharged / Cryocharged Railgun | Primary | `ChargedSuper/Cryo.primaryOverride` | A skill entering `ChargeSnipeSuper/Cryo` |
| Void Fiend | Corrupted Drown, Flood, Trespass, Suppress | Each its own | `CorruptMode.*OverrideSkillDef` | The passive slot. They reuse the base skill's name and description, so they are described by the base skill's own keyword (`VOIDSURVIVOR_*_UPRADE_TOOLTIP`, sic) |

Other slots hidden from character select (modded ones) get a line under their own title, or Misc.

### Unlocks

Nothing from a locked loadout choice is shown: an extra skill counts as unlocked when the variant giving it is
(`SkillFamily.Variant.unlockableDef` on the player's profile), and a CHEF boost also needs the boosted skill
unlocked. The debug log lists unlocked survivors only. Variants that need unlocks in this build: Acolyte's
Anima Echo, Javelin and Soul Jar; CHEF's Yes, CHEF!, Ice Box and Oil Spill (so all boosts); Drifter's Tornado
Slam, Junk Cube and Tinker; some of Captain's beacons and Operator's drones. The Anima Harvester, Bludgeon,
Discard and the corrupted skills come from defaults.

### Left out

- Same name and description as the skill they come from: Huntress's Ballista shot (`AimArrowSnipe`),
  Captain's probe calls (`SetupAirstrike(Alt)`), Operator's repeat leap (`ParkourState`), Enforcer's
  shielded and reactivated skills (`SkillTransformationController.transformationSkillDefs`).
- Placeholders: MUL-T's Cancel Power Mode and Cancel, Captain's Connection Lost and Unavailable, Engineer's
  harpoon targeting dummies.
- False Son's `ClubSwing` overrides (Club Slam, Lunar Spikes shotgun): no name or description tokens.
- Heresy items' skills: from items, for every survivor, and in the logbook. The Heretic is hidden from
  character select.

Commando, Huntress, Bandit, MUL-T, Engineer, Artificer, Mercenary, REX, Loader, Acrid, Seeker, False Son and
Enforcer have no extra skills; their tab says so.

## Modded survivors

[ModdedExtraSkills.cs](../SubSkillsDisplay/ModdedExtraSkills.cs), behind the `ModdedSurvivors` setting. A
survivor is modded when its content pack isn't one of the game's (`RoR2.BaseContent`, `RoR2.DLC1`-`DLC4`,
`RoR2.CU8`, `RoR2.Junk`; `RoR2.LegacyModContent` holds older mods' content and counts as modded). Its mod's
code is the plugins-folder assemblies defining its loadout skills' states or its body's components.

A skill counts when it has a name (its token resolves), no loadout of any body offers it, and there is
evidence it is swapped in:

1. **A loadout skill's state names it**: static fields, or instance fields of a fresh state with its entity
   state configuration applied. Tied to that loadout skill: follows its unlock.
2. **A component on the survivor's body names it**, directly or one level down in the data it holds (structs
   or plain classes, alone or in arrays and lists, like vanilla's `SkillTransformationController` pairs).
3. **The mod's code swaps it in.** [SkillOverrideSites.cs](../SubSkillsDisplay/SkillOverrideSites.cs) reads
   the mod's IL with Mono.Cecil (in BepInEx's core) for every `GenericSkill.SetSkillOverride` call and every
   store into vanilla's override holders (`SkillStateOverrideData.*SkillOverride`,
   `SkillTransformationController.SkillDefRelationship.overrideSkillDef`), and walks the stack back to where the
   skill is loaded: a static field, an instance field, a getter, an array element (the whole array), or a
   wrapper method's parameter (followed to its callers, two levels). At merge points every incoming path is
   followed, so both values of `a ? x : y` are found. The slot is read the same way from the GenericSkill the
   call is made on: a GenericSkill field named after its slot (`skillLocator.primary`), or
   `skillLocator.GetSkill(SkillSlot.X)`, followed through wrappers along with the skill.
   [OverrideCallScanner.cs](../SubSkillsDisplay/OverrideCallScanner.cs) then reads the values: static members
   directly, instance members from fresh states or from the components on the survivor bodies.

For 1 and 2 the line comes from the field's name (`primarySkillOverride` → Primary), unless 3 found the
skill's slot; with none, the skill goes in a Misc line. 2 and 3 are shown whatever the loadout, once the
survivor is unlocked. A skill read from a body's component goes to that survivor; otherwise to the mod's only
survivor, or the one whose name token stem (`ROB_HENRY_BODY_NAME` → `ROB_HENRY_`) starts the skill's token, or
whose states share the skill's state namespace; with no single match it is left out. A skill a mod only
creates (unused, or switched off in its config) has none of this evidence and is left out: a HenryMod build
with two unused slashes registered shows nothing.

Not covered: transformations into another body prefab (their skills are that body's loadout), skills chosen
at runtime from locals or computed values, and wrappers more than two calls deep.

### Testing the IL reading

[tools/OverrideSitesTest](../tools/OverrideSitesTest) compiles the mod's `SkillOverrideSites.cs` into a console
app that prints what it finds in any assembly:

```
dotnet build tools/OverrideSitesTest/FakeMod -c Release
dotnet run --project tools/OverrideSitesTest -c Release -- tools/OverrideSitesTest/FakeMod/bin/Release/netstandard2.1/FakeMod.dll
```

`FakeMod` holds the patterns character mods use (static field, state field, array element, property getter,
wrapper method, component field, `SkillStateOverrideData`, conditional, `GetSkill(SkillSlot)`): 10 loads,
each with its slot, the same for Debug and Release builds. On this build's `RoR2.dll` it finds 73 loads in about half a second, matching the
`SetSkillOverride` sites read in the decompiled source, including the Heresy items through
`CharacterBody.ReplaceSkillIfItemPresent`. HenryMod and ClayBenihisago give 0.

## Finding hidden skills after a game patch

Every skill swap in the game goes through `GenericSkill.SetSkillOverride`, so grepping the decompiled
`RoR2.dll` for it lists every place one can come from (about 35 files in this build, most of them monsters):

```
ilspycmd -p -o <dir> "<game>/Risk of Rain 2_Data/Managed/RoR2.dll"
grep -rl "SetSkillOverride(" <dir>
```

The skills those places use are set in assets. [tools/skill_census.py](../tools/skill_census.py) (UnityPy in a
scratch venv) lists, for given bundle groups, each skill family, each body's skill slots with
`hideInCharacterSelect`, and every other reference to a SkillDef with the field it comes from:

```
python -I -X utf8 tools/skill_census.py "<game>/Risk of Rain 2_Data/StreamingAssets" ror2-dlc4-acolyte ror2-dlc4-lilreaper
```

A REF target that isn't a variant of the survivor's own families, with its own name or description, is a
candidate. The Debug option `LogExtraSkills` logs what the mod finds for every survivor, across all
loadouts, the first time character select opens.

## Tested

In game: 0.1.0's Extra tab and its startup log; 1.0.0's tab (whose button colour and missing keywords led to
1.1.0's changes). Not yet in game: 1.1.0's lines and hover text, the Abilities panel (0.1.0's never opened),
and modded survivors (none installed locally with transformations; the IL reading is tested as above). Also
unchecked: gamepad navigation, split-screen character select (`CharacterSelectUILocal`), and multiplayer as a
client.
