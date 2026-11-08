---
type: Implementation
title: Tax engine
description: The mod's own tax charge — a copy of vanilla TaxSystem's charge at each payer's income tax plus density tax with its district's own rates (the rate model), the game-version guard and the Simulation changes switch, how buildings are classified, what it does not affect (demand, happiness), storage and the Taxation tab rows.
resource:
  - Mod/Systems/TaxEngineSystem.cs
  - Mod/Systems/TaxEngineGuard.cs
  - Mod/Systems/TaxKeyResolver.cs
  - Mod/Systems/DensityClassifier.cs
  - Mod/Systems/DensityTaxUISystem.cs
  - Mod/Systems/DensityTaxState.cs
  - Mod/Systems/TaxRatesSystem.cs
  - Mod/Systems/TaxRatesUISystem.cs
  - Mod/Logic/TaxRateRows.cs
  - Mod/Logic/TaxRateModel.cs
  - Mod/Systems/TaxRateResolver.cs
  - Mod/Logic/EffectiveTaxRate.cs
  - Mod/Logic/TaxEngineGuardLogic.cs
  - Mod/Logic/TaxPayerZone.cs
  - Mod/Logic/DensityTaxTable.cs
  - Mod/Logic/TaxRateTable.cs
  - Mod/Logic/FeatureGates.cs
  - Mod/Setting.cs
  - UI/src/taxation
---

# Tax engine

User decisions (2026-11-07): **income tax** is vanilla's (zone rate + education
level or resource rate); **density tax** is a separate tax per (zone, density) with its own rate, −5..+10 (negative is
a tax break), charged on top; a **district sets its own values** for any zone, education-level, resource or density
rate, and a building in it pays the district's value where set, else the city's. So far they change **revenue only**,
not demand or happiness (later slices). User decisions (2026-11-07): the mod
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
- **The one change**: the tax is `TaxSystem.GetTax` at the **charged rate**, `TaxRateResolver.ChargedRate` (below):
  the payer's stored `m_AverageTaxRate` plus its density tax and its district's income-tax difference, clamped by
  `EffectiveTaxRate.Compute` (`Mod/Logic/`) to the vanilla limits for its kind (`TaxParameterData.m_JobLevelTaxLimits`
  for households, `m_ResourceTaxLimits` for companies; both limit the full rate) and never below 0. The clamp never
  moves a rate past where vanilla put it, and refunds (zero or negative untaxed income) are at the vanilla rate, so
  **with no density tax and no district override the charge is exactly vanilla's**. The stored rate is never touched;
  `m_AverageTaxPaid` records what was charged.
- **Threading.** `DensityTaxState` (the table and the switch) is main-thread state, changed by the sliders, the Options
  switch and the reset buttons at any time. Each update the engine builds a `TaxRateResolver` whose `TempJob` arrays
  are computed from it, plus the wage per education level (`EconomyParameterData.GetWage`), all disposed with the
  jobs' handle.

## The rate model

`TaxRateModel` (`Mod/Logic/`, tested) mirrors how vanilla combines its 92-int rate array (`TaxSystem.cs`, 1.6.0f1):
a zone rate is `rates[0] + rates[1 + zone]`; an education level's is the residential zone rate + `rates[5 + level]`; a
resource's is the zone rate + `rates[10 + resource]` (Commercial) or `rates[51 + resource]` (Industrial and Office
share these offsets). **A district's** level/resource rate is its zone rate (its override, else the city's) plus its
offset (its override, else the city's), clamped to vanilla's item limits; with no override in the zone it is exactly
the city's. Its density tax for a cell is its override, else the city's.

The income systems (`PayWageSystem`, the company systems) are not copied, so `m_AverageTaxRate` keeps blending the
**city's** rates. The engine adds the difference instead (`TaxRateModel.DistrictDeltas`: district rate − city rate per
level and resource):

- **Households**: the mean over the household's workers (`HouseholdCitizen` → `Worker.m_Level`) of the difference at
  each worker's level, **weighted by that level's wage** (a provisional choice: `EconomyParameterData.GetWage(level)`;
  equal weights would be the alternative). No worker: level 0's (vanilla taxes other income at level 0's rate).
- **Companies**: the difference for the company's output resource (`IndustrialProcessData.m_Output`) in the zone
  vanilla books it on (`TaxPayerZone.Of`: industrial with weightless output is Office).
- Then **density tax**: the cell of its building (`DensityClassifier`), the district's rate else the city's; 0 while
  density taxes are off.

`TaxRateResolver` (`Mod/Systems/`, Burst-safe) is **the one effective-rate computation**: `Create` on the main thread
(the city's rates and limits from `TaxSystem`, the table, the wages), then `ChargedRate(taxPayer, payer, property,
kind, limits)` or `Adjustments(...)` in a job, then `Dispose(handle)`. The district of a building is `CurrentDistrict`
up the `Owner` chain (`TaxKeyResolver`). Payers in no district, Outside, or with no property (homeless) pay the city's
values. **Demand, happiness and home choice** must call it rather than add density and district adjustments themselves.
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

## Classifying a building

`DensityClassifier.TryClassify` (Burst-safe copy of `PropertyUtils.GetZoneDensity`): `PropertyRenter.m_Property` →
`PrefabRef` → `SpawnableBuildingData.m_ZonePrefab` → `ZoneData` + `ZonePropertiesData`. Residential:
low / medium / high. Commercial: low / high. Office (industrial area + office flag): low / high. Industrial:
low only. Households count only in residential-zoned buildings and companies only in commercial/industrial/office ones,
so commercial companies in mixed-housing zones and commuters are not affected.

## Storage and UI

Rates are **per save**. Each city keeps a `TaxRateTable` (`Mod/Logic/TaxRateTable.cs`): sparse entries keyed by
(`TaxType`, zone, key, **area**) to percentage points, the area being the city (0:0) or a district entity (index and
version: ints in the logic, an `Entity` in the save so the game remaps it; **district keys change on every load**, so
the UI takes them from the `districts` binding only and never caches one). Types: `Density` (key = density; the city's
rate and district overrides, −5..+10), `ZoneRate` (a district's full zone rate) and `ResourceRate` (a district's
education-level or resource rate, stored like vanilla as an offset from the zone rate); `District` (1) is the retired
±adjustment, never stored. A city value of 0 is the default and not stored; a district entry is an override and is
stored even at 0. New types (wealth, land value, building level) take the next enum numbers and work the same way. The
city's zone and resource rates are vanilla's own array, not the table's. `DensityTaxTable` is a view of the city's
density entries.

`TaxRatesSystem` is `IDefaultSerializable`: the save block is `FormatVersion` (2), a count, then per entry type, zone,
key, area (`Entity`, `Entity.Null` = the city) and value. A version-1 block (the unshipped ±adjustment model) keeps
its density entries as the city's density rates and drops its district adjustments; any other version loads empty. On
load it drops district entries whose entity is gone (`Entity.Null` on read; `PruneDistricts` on
`OnGameLoadingComplete`) and points `DensityTaxState` at the city's table (`DensityTaxState.Use`); sliders write it in
place, and it is saved with the city.
**Migration and the template.** `Setting.DensityTaxOffsets` (12 comma-separated ints) is now only the template for a
city with no block: a new city, or a save from before per-save rates. `SetDefaults` seeds that city's density entries
from it once (`TaxRateTable.SeedDensityFrom`); the next save carries the block, so it is never seeded again. Sliders no
longer write the setting. Options > reset clears the template and the loaded city's density rates (the city's and every district's).
`DensityTaxUISystem` publishes
`densityTaxes = { minRate, maxRate, cells[{ zone, density, offset, estimate }] }` (`offset` = the city's density tax rate) and `setDensityTax(zone, density, rate)`.
The estimate is `EstimateExtraRevenue`: each cell's monthly tax paid (`TaxPayer.m_AverageTaxPaid`, the last payment
× 32) scaled back to its taxable base, times the offset. The engine charged that payment at the stored rate **plus**
the offset, so the base is paid ÷ (rate + offset); dividing by the stored rate alone overstated it by (rate + offset) / rate.
Recomputed every ~2 s while the panel is open and density taxes are on.

**Options > Enable density taxes (BETA)** (`Setting.EnableDensityTaxes`, default **off**: density taxes are a BETA feature, `FeatureGates.DensityTaxesDefault`) switches the feature off live. The options
page calls `ApplyAndSave` on every change; `Mod` listens on `onSettingsApplied` and calls
`DensityTaxState.SetEnabled`. Off, the density rates the engine reads are all 0 (`TaxRateModel.DistrictDensityRates`
with the switch off), so buildings pay no density tax (district income-tax overrides still apply while the engine runs); the stored table and the template are left as they are, so switching back on restores them. `DensityTaxUISystem` publishes the switch as `densityTaxesEnabled`, and
the rows ask `cellsForArea` (`UI/src/taxation/density.ts`), which gives no cells while it is off.

**Taxation tab layout.** With density taxes on, the wrapper around each vanilla `TaxationGroup`
(`UI/src/taxation/tax-sections-view.tsx`) draws collapsible sections under the zone's main slider, each a header row
with an arrow (and an optional summary) over its rows: **Density tax** first (summary: "2 rates set", nothing when
every rate is 0; `rateSummary` in `density.ts`), one row per density the zone has (`density-rows.tsx`). **Density
rows are vanilla rows**: each is built exactly as vanilla `TaxationItem` builds an education/resource row (the same
`ActiveFocusDiv`, the live `taxation-item.module.scss` class names `taxationItemClasses`, vanilla `TaxSlider`), so icon,
title, % column, slider and estimate line up (`density-rows.module.scss` holds copies of those rules for a moved
module). The icon is the zoning toolbar's (`densityIcon` in `density-icons.ts`; an empty box where the game has none).
The row shows the density tax's **own rate**, never the zone rate plus it; the slider range is one constant,
`DENSITY_RATE_RANGE` = −5..+10 in `density-slider.ts`, matching `TaxRateTable.DensityMin/Max`. After it, vanilla's own sub-rows, re-hosted as
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

**Per-district rates** (a user decision: a district sets its own values). `TaxRatesUISystem` publishes
`taxRates = { engineOn, districts[{ area, type, zone, key, value }] }`: every stored district override (as stored:
full zone rate, level/resource offset, density rate), keyed by `DistrictKey` ("index:version"), built by
`TaxRateRows.DistrictRows`; `engineOn` is `TaxEngineMode.Engine`. `setTaxRate(type, zone, target, value)` takes the
value **as the slider shows it** (a full rate) and a target "key@area" (a trigger takes at most four arguments;
`TaxRateRows.SplitTarget`); `TryResolve` accepts the city ("" area) for density only, and the system checks the
district still exists, then `TaxRateModel.SetDistrictValue` clamps to vanilla's limits, turns a level/resource rate
into its offset from the district's zone rate, and **removes the override when the value equals what the district
would have without it**, so dragging a slider back to the city's value makes it follow the city again.
`resetDistrictTaxRates(area)` removes every override of the district ("Use city rates"). The district list is
`BudgetHistoryUISystem`'s `districts` binding, filled only while the budget chart and district recording are on (until
`DistrictListUISystem` takes it over).

On the tab (`district-tax.ts` pure, `district-tax-view.tsx`), a TaxationPage wrapper draws the shared
`DistrictSelector` (`district/selection.ts`, the same choice as the Budget tab) above the page while the engine charges
and the city has a district (`showsTaxDistrictSelector`): Entire City, then each district, no Outside Districts
(`taxDistrictOptions`), and under it, in a district view, a vanilla `Button` **Use city rates** (disabled with no
override). It provides the view (`taxViewKey`) through `TaxDistrictView`. A **district view is the Entire City layout**
with the district's values: the zone header slider, Density tax (while density taxes are on; summary "N overrides",
the cells differing from the city's) and Education/Resources. Each slider says which rate it is through
`TaxRowTarget` (the TaxationGroup wrapper: the zone rate, for the `primary` header; each re-hosted `TaxationItem`: its
level or resource), and the extended `TaxSlider` swaps in the district's value and setter (`useDistrictSlider`,
`rowRate`: district value else city; a level/resource is the district zone rate + the district's offset else the
city's, as in C#), adds `overridden` (an accent bar down the slider's left edge) where it differs from the city's,
and hides the estimate column. Density rows do the same themselves. **Estimates are hidden in a district view**
(row incomes, zone totals and ESTIMATED TOTAL, which are the city's; a provisional choice: a per-district estimate
would need a per-district paid-tax sum). Entire City is unchanged.
## Follow-ups

- Check in game that no density tax and no override match vanilla's monthly tax income within 1%, and that a density
  tax or a district override changes the zone's tax revenue.
- Confirm in Mod.log that `Game.Version.current.shortVersion` reads `1.6.0f1` (the known-list format).
- Demand/happiness reacting to adjusted rates: later slices (S3/S4).
