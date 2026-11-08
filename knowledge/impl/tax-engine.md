---
type: Implementation
title: Tax engine
description: The mod's own tax charge — a copy of vanilla TaxSystem's charge that adds each payer's density and district adjustments, the game-version guard and the Simulation changes switch, happiness at each citizen's home rate (a copy of CitizenHappinessSystem's update), households choosing homes by each home's rate (a copy of HouseholdFindPropertySystem's update), how buildings are classified, what it does not affect (demand), storage and the Taxation tab rows.
resource:
  - Mod/Systems/TaxEngineSystem.cs
  - Mod/Systems/TaxEngineGuard.cs
  - Mod/Systems/TaxKeyResolver.cs
  - Mod/Systems/TaxHappinessSystem.cs
  - Mod/Systems/TaxFindPropertySystem.cs
  - Mod/Systems/DensityClassifier.cs
  - Mod/Systems/DensityTaxUISystem.cs
  - Mod/Systems/DensityTaxState.cs
  - Mod/Systems/TaxRatesSystem.cs
  - Mod/Systems/TaxRatesUISystem.cs
  - Mod/Logic/TaxRateRows.cs
  - Mod/Logic/EffectiveTaxRate.cs
  - Mod/Logic/TaxEngineGuardLogic.cs
  - Mod/Logic/TaxHappinessMath.cs
  - Mod/Logic/TaxPropertyScoreMath.cs
  - Mod/Logic/TaxPayerZone.cs
  - Mod/Logic/DensityTaxTable.cs
  - Mod/Logic/TaxRateTable.cs
  - Mod/Logic/FeatureGates.cs
  - Mod/Setting.cs
  - UI/src/taxation
---

# Tax engine

User decisions (2026-11-03): density is **another adjustment** on top of the vanilla rate (zone rate + education/product
offset), capped at ±5 points (`DensityTaxTable.MaxOffset`); district adjustments are relative to the city rate, capped
±10. They change revenue, demand (through the [demand view](#demand)), citizen happiness ([§ Happiness](#happiness)) and where
households choose to live ([§ Where people move](#where-people-move)). User decisions (2026-11-07): the mod
charges tax with its own copy of vanilla's charge (copying decompiled game code is fine: minimal copies, each with a
header naming the source class, method, line range and game version); on an unknown game version it falls back to
vanilla with a warning unless the player forces it; a "Simulation changes" master switch turns every simulation change
off live.

## How vanilla charges tax

Households and companies carry a `TaxPayer { m_UntaxedIncome, m_AverageTaxRate, … }`. Income systems
(`PayWageSystem`, `ServiceCompanySystem`, `ProcessingCompanySystem`, `ExtractorCompanySystem`) add income and blend
the applicable vanilla rate into `m_AverageTaxRate`. `TaxSystem` (every 512 frames, one of 16 `UpdateFrame` groups)
charges `untaxed * averageRate / 100` and zeroes `m_UntaxedIncome`. Every vanilla rate lookup happens inside Burst
jobs we can't patch.

## The engine

Vanilla `TaxSystem` stays the **data store**: its 92-int rate array, saved by vanilla and read by ~19 systems and the
UI, and its setters (which clamp with `TaxParameterData`). Only its **update** is replaced:

- `TaxEngineSystem` (`Mod/Systems/`), `UpdateBefore<…, TaxSystem>` with TaxSystem's interval (512 frames, 16
  `UpdateFrame` groups), so it inherits TaxSystem's update offset and charges the same payers on the same frames
  ([cs2-ui-modding.md](../cs2-ui-modding.md#scheduling-simulation-systems)).
- While the engine runs, `TaxSystem.Enabled = false` (that skips its `OnUpdate` only). Anything else turns it back on,
  live: the engine decides every update and whenever Options change, before TaxSystem on the same frame, so switching
  back to vanilla charges that very tick.
- The copy: `PayTaxJob` and `OnUpdate` (`TaxSystem.cs:30-150`, `:373-436`), the queries from `OnCreate` and the
  `m_TaxPaidMultiplier` load from `OnGameLoaded` (`:242-258`, `ModeSettingData`), game version 1.6.0f1; each copied
  block has a header naming its source. `TaxSystem.GetTaxParameterData()` is called each tick (runs vanilla's
  `EnsureTaxParameterData`).
- **The one change**: the tax is `TaxSystem.GetTax` at the **charged rate**, `EffectiveTaxRate.Compute` (`Mod/Logic/`):
  the payer's stored `m_AverageTaxRate` plus its density and district adjustments, clamped to the vanilla limits for its
  kind (`TaxParameterData.m_JobLevelTaxLimits` for households, `m_ResourceTaxLimits` for companies; both limit the full
  rate) and never below 0. The clamp never moves a rate past where vanilla put it, and refunds (zero or negative untaxed
  income) are at the vanilla rate, so **with every adjustment 0 the charge is exactly vanilla's**. The stored rate is
  never touched; `m_AverageTaxPaid` records what was charged.
- Keys per payer: `TaxKeyResolver` (Burst-safe lookups): the density cell of its `PropertyRenter.m_Property`
  (`DensityClassifier`) and the district (`CurrentDistrict`, up the `Owner` chain as `DistrictBudgetSystem`). The
  district adjustment is per zone, the zone being the tax line vanilla books the payer on (`TaxPayerZone.Of`: industrial
  with weightless output is Office). Payers with no property (homeless households) pay the vanilla rate.
- **Threading.** `DensityTaxState` (the table and the switch) is main-thread state, changed by the sliders, the Options
  switch and the reset button at any time. Each update the engine copies `DensityTaxState.Snapshot()` (all 0 while
  density taxes are off) into a `TempJob` array and the district entries into a `TempJob` map (district entity →
  `int4` per zone), disposed with the jobs' handle.

## The guard and the switches

`TaxEngineGuardLogic.Decide(versionKnown, structureOk, forceUntested, masterOn)` (`Mod/Logic/`, tested):
**Simulation changes** off → `Vanilla`; structure checks failed → `Blocked` even when forced; game version not in
`KnownGameVersions` → `Blocked` unless **Run on untested game version**; else `Engine`. Only `Engine` disables
vanilla. `TaxEngineGuard` (`Mod/Systems/`) supplies the inputs once at creation: the version is
`Game.Version.current.shortVersion` (its first word must equal a known one exactly), and the structure check confirms
by reflection that `TaxSystem.m_TaxRates` (`NativeArray<int>`), `m_TaxPaidMultiplier` (`float3`), `m_TaxParameterData`
and `GetTax(TaxPayer)` exist and `GetTaxRates().Length == 92`. **Back-off**: if `TaxSystem` is already disabled when
the engine first decides (on `OnGameLoadingComplete`, after every mod's `OnLoad`) and the engine didn't do it, another
mod owns the charge: the engine counts as incompatible for the session and never touches `Enabled`.

Mod.log carries the version, the structure result and every decision change (`TaxEngine: decision Engine (…)`).
`TaxEngineGuardLogic.WarningFor` gives the Taxation tab's warning: `DensityTaxUISystem` publishes it as
`taxEngineWarning` (int), and `EngineWarningLine` (`UI/src/taxation/engine-warning-line.tsx`, text from
`engine-warning.ts`) draws one line above the TaxationPage for an untested version (vanilla, or forced) or an
incompatible game. None while the master switch is off.

**A new game version**: compare vanilla `TaxSystem` (PayTaxJob, OnUpdate, OnGameLoaded) with the copy, update the
copy and its headers, then add the version to `KnownGameVersions`.

`Setting.EnableSimulationChanges` (default on, `FeatureGates.SimulationChangesDefault`) and
`Setting.RunOnUntestedGameVersion` (default off) sit in Options > Features; `Mod.ApplyFeatureSwitches` calls
`TaxEngineSystem.ApplyMode` on every change.

## Demand

Vanilla demand reads only city-level rates: Residential averages `10 − rate` over the five education levels
(`ResidentialDemandSystem.cs:116-120`), Commercial and Industrial/Office use each resource's rate
(`CommercialDemandSystem.cs:180`, `IndustrialDemandSystem.cs:402`). The demand systems are not copied; they are handed
a second rate array, the **view**:

- `DemandTaxViewSystem` (`Mod/Systems/`, every frame, `UpdateAfter<…, TaxEngineSystem>`) sums every payer's charged
  adjustment (`DemandViewMath.PayerAdjustment`: the engine's keys and clamp, as a change from its stored rate) per rate
  slot every **4096 frames** (a single-threaded job, read back the next update). **Each payer weighs 1** (provisional:
  untaxed income is zeroed at every charge, so it is a noisy weight). Households count in all five education slots
  (5..9), companies in their output resource's slot (10+r commercial, 51+r industrial, flagged Office when the output
  weighs nothing); payers with no property count with adjustment 0.
- `DemandViewMath.Compose` (`Mod/Logic/`, tested): view = vanilla's array with each leaf slot that has payers moved by
  its payers' mean adjustment (halves to even, as `math.round`), the slot's full rate clamped like `EffectiveTaxRate`
  (job-level limits for residential, resource limits otherwise; never past vanilla). Slots 0..4 and payer-less slots stay
  vanilla, so **all-zero adjustments give vanilla's array**. It is recomposed every frame from the live vanilla rates and
  written only when it changes, after completing the view's readers (vanilla's `AddReader` pattern).
- **The swap**: each demand system's private `m_TaxSystem` is pointed (reflection) at a `DemandTaxView : TaxSystem`.
  The demand systems call only the non-virtual `GetTaxRates()` (returns the instance's private `m_TaxRates`, set by
  reflection to the view) and `AddReader()` on it. The view is built with `FormatterServices.GetUninitializedObject`,
  never through the World: no constructor or `OnCreate`, never updated, and **never saved** — the game's
  `SystemSerializerLibrary.Initialize` (Colossal.Core) serializes only systems in `World.Systems`, keyed by exact type.
  On creation the system checks a sentinel written to the view reads back through `GetTaxRates()` and that the view is
  not in `World.Systems`, and logs `DemandTaxView: structure ok`.
- **Off**: whenever the engine's mode isn't `Engine` (Simulation changes off, guard blocked) the fields go back to
  vanilla TaxSystem, live. `TaxEngineGuard.DemandStructureOk` checks the three demand systems still have
  `m_TaxSystem : TaxSystem` and TaxSystem `m_TaxRates`; a mismatch leaves demand vanilla (the engine still charges). A
  field another mod re-pointed is left alone (logged once).
- Residential demand reacts through the five education slots; residential happiness through [§ Happiness](#happiness).

## Classifying a building

`DensityClassifier.TryClassify` (Burst-safe copy of `PropertyUtils.GetZoneDensity`): `PropertyRenter.m_Property` →
`PrefabRef` → `SpawnableBuildingData.m_ZonePrefab` → `ZoneData` + `ZonePropertiesData`. Residential:
low / medium / high. Commercial: low / high. Office (industrial area + office flag): low / high. Industrial:
low only. Households count only in residential-zoned buildings and companies only in commercial/industrial/office ones,
so commercial companies in mixed-housing zones and commuters are not affected.

## Storage and UI

Offsets are **per save**. Each city keeps a `TaxRateTable` (`Mod/Logic/TaxRateTable.cs`): sparse entries keyed by
(`TaxType`, zone, key, key version) to percentage points, clamped to a per-type cap (`Density` ±5; `District` ±10, a
provisional constant for the district slice; later types take the next enum numbers). Density entries are keyed by
`Density`; district entries by the district entity's index and version (ints in the logic, an `Entity` in the save so
the game remaps it). `DensityTaxTable` is a view of a table's density entries (its classification and estimate maths
stay; the charged rate is `EffectiveTaxRate`'s).

`TaxRatesSystem` is `IDefaultSerializable`: the save block is `FormatVersion`, a count, then per entry type, zone,
key (an `Entity` for districts, else key and key version) and value. An unknown version loads empty. On load it drops
district entries whose entity is gone (`Entity.Null` on read; `PruneDistricts` on `OnGameLoadingComplete`) and points
`DensityTaxState` at the city's table (`DensityTaxState.Use`); sliders write it in place, and it is saved with the city.

**Migration and the template.** `Setting.DensityTaxOffsets` (12 comma-separated ints) is now only the template for a
city with no block: a new city, or a save from before per-save rates. `SetDefaults` seeds that city's density entries
from it once (`TaxRateTable.SeedDensityFrom`); the next save carries the block, so it is never seeded again. Sliders no
longer write the setting. Options > reset clears both the template and the loaded city's density offsets.
`DensityTaxUISystem` publishes
`densityTaxes = { maxOffset, cells[{ zone, density, offset, estimate }] }` and `setDensityTax(zone, density, offset)`.
The estimate is `EstimateExtraRevenue`: each cell's monthly tax paid (`TaxPayer.m_AverageTaxPaid`, the last payment
× 32) scaled back to its taxable base, times the offset. The engine charged that payment at the stored rate **plus**
the offset, so the base is paid ÷ (rate + offset); dividing by the stored rate alone overstated it by (rate + offset) / rate.
Recomputed every ~2 s while the panel is open and density taxes are on.

**Options > Enable density taxes (BETA)** (`Setting.EnableDensityTaxes`, default **off**: density taxes are a BETA feature, `FeatureGates.DensityTaxesDefault`) switches the feature off live. The options
page calls `ApplyAndSave` on every change; `Mod` listens on `onSettingsApplied` and calls
`DensityTaxState.SetEnabled` with `FeatureGates.AppliesDensityTaxes(EnableSimulationChanges, EnableDensityTaxes)`:
the master **Simulation changes** switch off counts as density taxes off ([district-service-budgets.md](district-service-budgets.md#the-master-switch)),
and the engine itself steps aside for vanilla `TaxSystem` then. Off, the density adjustments the engine reads are all 0
(`DensityTaxTable.EffectiveOffsets(false)`), so buildings pay no density adjustment (district adjustments still apply
while the engine runs); the stored table and the template are left as they are, so switching back on restores them. `DensityTaxUISystem` publishes the switch as `densityTaxesEnabled`, and
the rows ask `cellsForArea` (`UI/src/taxation/density.ts`), which gives no cells while it is off.

**Taxation tab layout.** With density taxes on, the wrapper around each vanilla `TaxationGroup`
(`UI/src/taxation/tax-sections-view.tsx`) draws collapsible sections under the zone's main slider, each a header row
with an arrow (and an optional summary) over its rows: **Density** first (one vanilla `TaxSlider` per density the zone
has, `density-rows.tsx`; the old "revenue only" note is gone, density is a real tax charge now; each row leads with
the zoning toolbar's icon as a 32rem `<img>` like vanilla's education rows, `Media/Game/Icons/Zone*.svg` via
`densityIcon` in `density-icons.ts`, an empty box where the game has none, e.g. Commercial/Office medium), then vanilla's own sub-rows, re-hosted as
vanilla `TaxationItem`s from `taxation.areaResources$` (**Education** for Residential, **Resources** for the other
zones). All start collapsed; the open ones are kept for the UI session (`sectionExpandState`, not reset when the panel
closes). The section list is data (`TAX_SECTIONS` in `tax-sections.ts`: id, title per zone; `sectionsForArea` keeps the
ones with rows, in order, none for a locked zone), and `TAX_SECTION_ROWS` in the view maps each id to its rows: a new
tax type is one entry in each and a title in `l10n/en-US.json`. Vanilla's own expander (toggle button, gamepad hint,
content) is hidden by adding classes to the live `taxation-group.module.scss` class object, which `TaxationGroup` reads
on every render (`groupClasses`): the toggle is `visibility: hidden` so the header slider keeps its place. Off, the
names are vanilla's again and no sections are drawn, so the tab is vanilla. Rows show no payer count (removed by
request, 2026-11-07).

**Totals include the offsets** (a user decision). Each zone group's total (its header slider's income) and
ESTIMATED TOTAL add the sum of the zone's (all zones') row `estimate`s; negative offsets lower them. Vanilla computes
both in C# (`TaxationUISystem`: `areaTaxIncomes`, and `taxIncome` = the four zones' sum, from the vanilla rates only)
and draws them inside its own components, so the UI changes the **drawn value**, not the binding
(`UI/src/taxation/density-totals.tsx`): the TaxationGroup wrapper provides the zone's extra and a TaxationPage wrapper
the total's through a React context; an extended `TaxSlider` adds the zone's to the `primary` (header) slider, an
extended `NumberField` adds the total's to the field whose className is the page's `totalValue` class. The values go
through vanilla's own components, so sign and money format are vanilla's. Off, or every offset 0: the extra is 0 and
both pass through unchanged. The maths is `densityExtraForArea`/`densityExtraTotal`/`zoneTotalIncome`/
`estimatedTotalValue` in `density.ts`.

**Per-district rates** (a user decision: an adjustment of ±`TaxRateTable.DistrictCap` points on the city
rate, density on top, the sum clamped to vanilla's limits by the engine). `TaxRatesUISystem` publishes
`taxRates = { engineOn, districtCap, districts[{ key, zone, value }] }`: every stored district entry (sparse, a missing
row is 0) keyed by `DistrictKey` ("index:version"), built by `TaxRateRows.DistrictRows`; `engineOn` is
`TaxEngineMode.Engine`. `setTaxRate(type, zone, key, value)` takes a `TaxType`, a zone and a district key (or a density
"0".."2"); `TaxRateRows.TryResolve` rejects Entire City, Outside, bad keys and zones, and the system also checks the
district entity still exists, then writes through `DensityTaxState.SetRate` (bumping its version). The UI picks the
selected district's rows itself, so the selection stays the UI's. The district list is `DistrictListUISystem`'s
`districts` binding, published while any district feature is on, the engine charging included
(`FeatureGates.PublishesDistrictList`), so the selector has its districts with the budget chart off.

On the tab (`district-tax.ts` pure, `district-tax-view.tsx`), a TaxationPage wrapper draws the shared
`DistrictSelector` (`district/selection.ts`, the same choice as the Budget tab) above the page while the engine charges
and the city has a district (`showsTaxDistrictSelector`): Entire City, then each district, no Outside Districts
(`taxDistrictOptions`). It provides the view (`taxViewKey`: Entire City for the engine off, Outside or a gone district)
through `TaxDistrictView`. Entire City is the layout above. A district view shows only the **District** section
(`TAX_SECTIONS` entry with `view: "district"`, `startsOpen`), whatever the density switch: one `TaxSlider`
(−cap..+cap, its estimate column hidden, as there is no revenue estimate) labelled "+N% vs city", also the header's
summary. Density and vanilla's rows are city-wide, so a district view leaves them out (`view: "city"`), and each zone's
header slider shows the city rate read-only (`headerSliderDisabled` in the extended `TaxSlider`).

## District demand (info only)

The game simulates one city-wide demand per zone, so a district has none. A district view's District section shows an
estimate under the slider, "Demand (info): X% vs city Y%" with the note "Estimate only", never feeding the simulation
(`UI/src/taxation/district-demand.ts`, `DemandInfo` in `district-tax-view.tsx`). The city demand is vanilla's
`cityInfo` bindings (`residentialLow/Medium/HighDemand` averaged, `commercialDemand`, `industrialDemand`,
`officeDemand`; 0..1). `taxRates.taxEffect` carries `DemandParameterData.m_TaxEffect` (x residential, y commercial, z
industrial and office). The district figure is the city demand minus adjustment x weight x a per-point scale, clamped
to 0..1. The scales (`RESIDENTIAL_DEMAND_PER_POINT` 0.01, `OTHER_DEMAND_PER_POINT` 0.05) are provisional, read off
vanilla's formulas (residential `x * (10 - rate)` on a 0..100 demand; others `-0.05 * (rate - 10) * weight`).

## Happiness

The tax happiness factor (a user decision) follows each citizen's own rate, so a district's rates have a real
effect (unhappy households move out through vanilla `HouseholdBehaviorSystem`).

Vanilla `CitizenHappinessSystem` computes each citizen's wellbeing every 16 frames (16 `UpdateFrame` groups) and keeps
the city's happiness factors (`m_HappinessFactors`, 26 factors × 16 frames, saved by vanilla and read by the city info
UI through `GetHappinessFactor`). Its tax bonus is `GetTaxBonuses(educationLevel, taxRates, …)`: the city's residential
rate for the education level, `(10 - rate)`, the city's `TaxHappiness` modifier, times the level's (negative)
`CitizenHappinessParameterData.m_Tax*Multiplier`.

- `TaxHappinessSystem` (`Mod/Systems/`) is a copy of its update: `CitizenHappinessJob`, `HappinessFactorJob`, `OnCreate`
  and `OnUpdate` and three private helpers (headers name each source range, game version 1.6.0f1; changes are marked
  `CHANGED`/`ADDED`; vanilla's debug distribution is left out). The public static `Get*Bonuses` helpers are called, not
  copied. `UpdateBefore<…, CitizenHappinessSystem>` with its interval (16), so it inherits its offset.
- **The one change**: the tax bonus is at the citizen's **home rate**, the vanilla rate for their education level plus
  the density and district adjustments of their household's `PropertyRenter.m_Property` (`TaxKeyResolver`, the
  district's Residential entry), inside the job-level limits (`TaxHappinessMath.ResidentRate`, i.e.
  `EffectiveTaxRate.Compute` for a household with income). When that equals the vanilla rate (no adjustment, or no home)
  the job calls vanilla's own `GetTaxBonuses`, so all-zero adjustments are exactly vanilla; otherwise
  `TaxHappinessMath.Bonus` (`Mod/Logic/`, tested: a port of `GetTaxBonuses` with the rate and modifier passed in).
- Vanilla stays the **data store**: the copy writes vanilla's private `m_HappinessFactors` (read by reflection once) and
  sets vanilla's private `m_LastDeps` to its own handle each update, so `GetHappinessFactor` waits for it. Its first job
  also waits on vanilla's last handle (the switch-over update).
- **When it runs**: exactly while the tax engine charges, `TaxEngineGuardLogic.RunsHappiness(engineMode, structureOk,
  foreignDisabled)` (tested). While it runs `CitizenHappinessSystem.Enabled = false`; anything else turns it back on,
  live (every update, and `Mod.ApplyFeatureSwitches` after the engine's `ApplyMode`). The structure check
  (`TaxEngineGuard.HappinessStructureOk`) confirms the two private fields' types and the array's length (416); if
  vanilla was already disabled by someone else, it backs off for the session. Mod.log: `TaxHappiness: structure …` once
  and `TaxHappiness: running|vanilla (…)` on each change.

**Known limits** (still the city rate): the building happiness shown in the building info panel
(`GetBuildingHappinessFactors`, CitizenHappinessSystem.cs:1817) and company factors (:1591), which `PropertyUtils` and
the UI call directly; household net income (vanilla's income estimates elsewhere use the city rate); companies
moving away (`CompanyMoveAway`); the Taxes factor in the city info panel is the city average of the per-citizen
bonuses, so it moves with the share of citizens under an adjustment.

## Where people move

Households prefer lower-tax homes (a user decision: the cheaper partial of per-district demand, with district
happiness above and an info-only district demand figure).

Vanilla `HouseholdFindPropertySystem` (every 16 frames) starts a pathfind for a household looking for a home, then
compares the best candidate found against its current home with `PropertyUtils.GetPropertyScore` (`:533` current home,
`:602` candidate) and moves, stays or leaves. The score's tax term is `2 ×` the adults' average tax happiness bonus
(`PropertyUtils.cs:511-560`), at the **city** rate.

- `TaxFindPropertySystem` (`Mod/Systems/`) is a copy of its update: `PreparePropertyJob`, `FindPropertyJob`, `OnCreate`
  and `OnUpdate` (headers name each source range, game version 1.6.0f1; changes marked `CHANGED`/`ADDED`; vanilla's
  debug counters and distributions are left out). Vanilla's public structs, statics and `PropertyUtils` are called,
  not copied. `UpdateBefore<…, HouseholdFindPropertySystem>` with its interval (16), so it inherits its offset.
- **The one change**: both scores go through `Score`: vanilla's `GetPropertyScore`, then its tax term recomputed at the
  home's rate (the vanilla rate for each adult's education level plus that property's density and district Residential
  adjustments, `TaxHappinessMath.ResidentRate`, inside the job-level limits) and the difference added
  (`TaxPropertyScoreMath`, `Mod/Logic/`, tested: vanilla's integer average and weight 2). A home with no adjustment,
  and a shelter (park or abandoned building: its residents are homeless and pay the vanilla rate), score exactly as
  vanilla. So a +5 home loses against an otherwise equal 0 home, and its residents look for a better one sooner.
- **When it runs**: exactly while the tax engine charges, `TaxEngineGuardLogic.RunsHomeChoice(engineMode,
  foreignDisabled)` (tested). While it runs `HouseholdFindPropertySystem.Enabled = false`; anything else turns it back
  on, live (every update, and `Mod.ApplyFeatureSwitches` after the engine's). It touches no private vanilla state (only
  vanilla's debug counters are private and read elsewhere), so there is no structure check of its own; if vanilla was
  already disabled by someone else, it backs off for the session.
- Mod.log: `TaxHomeChoice: running|vanilla (…)` on each change, and about every 512 updates
  `TaxHomeChoice: N of M home scores moved by the home's tax rate (summed delta D)` (D < 0: homes scored lower).

**Known limits**: the candidates the pathfinder offers are pre-scored in `CitizenPathfindSetup.SetupFindHome` (inside
`PathfindSetupSystem`, which can't be replaced alone) at the city rate, so which home is found first ignores the
adjustments; only the final current-vs-candidate comparison sees them. The household income it checks against rent
(`EconomyUtils.GetHouseholdIncome`) is still at the city rate.

## Follow-ups

- Check in game that all-zero adjustments match vanilla's monthly tax income within 1%, and that a non-zero density
  adjustment changes the zone's tax revenue.
- Confirm in Mod.log that `Game.Version.current.shortVersion` reads `1.6.0f1` (the known-list format).
- Check in game that +5 on every residential density lowers the residential demand tax factor, and that all 0 or
  Simulation changes off gives vanilla demand.
- Check in game that the happiness Taxes factor with all-zero adjustments equals vanilla's, and that +5 in one district
  lowers its residents' wellbeing over a couple of in-game days.
- Check in game that households leave or avoid +5 high-density residential faster than 0-adjusted buildings, and that
  `TaxHomeChoice` lines in Mod.log show moved scores only while an adjustment is set.
