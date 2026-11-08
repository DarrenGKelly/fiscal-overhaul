---
type: Implementation
title: Tax engine
description: The mod's own tax charge — a copy of vanilla TaxSystem's charge at each payer's income tax plus density tax with its district's own rates (the rate model), TaxRateResolver as the one effective-rate computation, the city's side (TaxBookingSystem books what the engine charges beyond vanilla as tax income or subsidy), the game-version guard and the Simulation changes switch, how buildings are classified, and how rates are stored per save.
resource:
  - Mod/Systems/TaxEngineSystem.cs
  - Mod/Systems/TaxBookingSystem.cs
  - Mod/Logic/DensityTaxBooking.cs
  - Mod/Logic/TaxBookingLedger.cs
  - Mod/Systems/TaxEngineGuard.cs
  - Mod/Systems/TaxKeyResolver.cs
  - Mod/Systems/TaxRateResolver.cs
  - Mod/Systems/DensityClassifier.cs
  - Mod/Systems/DensityTaxState.cs
  - Mod/Systems/TaxRatesSystem.cs
  - Mod/Logic/TaxRateModel.cs
  - Mod/Logic/EffectiveTaxRate.cs
  - Mod/Logic/TaxEngineGuardLogic.cs
  - Mod/Logic/TaxPayerZone.cs
  - Mod/Logic/DensityTaxTable.cs
  - Mod/Logic/TaxRateTable.cs
  - Mod/Logic/FeatureGates.cs
  - Mod/Setting.cs
---

# Tax engine

User decisions (2026-11-07): **income tax** is vanilla's (zone rate + education level or resource rate); **density
tax** is a separate tax per (zone, density) with its own rate, −5..+10 (negative is a tax break), charged on top; a
**district sets its own values** for any zone, education-level, resource or density rate, and a building in it pays
the district's value where set, else the city's. The rates change revenue, demand, citizen happiness and where
households live ([tax-engine-effects.md](tax-engine-effects.md)); the Taxation tab is
[tax-engine-ui.md](tax-engine-ui.md). The mod charges tax with its own copy of vanilla's charge (copying decompiled
game code is fine: minimal copies, each with a header naming the source class, method, line range and game version);
on an unknown game version it falls back to vanilla with a warning unless the player forces it; a "Simulation
changes" master switch turns every simulation change off live.

## How vanilla charges tax

Households and companies carry a `TaxPayer { m_UntaxedIncome, m_AverageTaxRate, … }`. Income systems
(`PayWageSystem`, `ServiceCompanySystem`, `ProcessingCompanySystem`, `ExtractorCompanySystem`) add income and blend
the applicable vanilla rate into `m_AverageTaxRate`. `TaxSystem` (every 512 frames, one of 16 `UpdateFrame` groups)
charges `untaxed * averageRate / 100` and zeroes `m_UntaxedIncome`. Every vanilla rate lookup happens inside Burst
jobs we can't patch.

**The charge does not pay the city.** `PayTaxJob` only takes the money from the payer and enqueues its taxable income
(`ResidentialTaxableIncome`, `Commercial…`, `Industrial…`, `Office…`, per level or resource). The city's tax money is
`CityServiceBudgetSystem`'s **estimate**, recomputed every frame: per tax item (an education level or a resource), the
city's rate for it × that item's taxable-income statistic (`TaxSystem.GetEstimatedTaxAmount`); an item above 0 adds to
`TaxResidential` / `TaxCommercial` / `TaxIndustrial` / `TaxOffice` income, an item below 0 to `SubsidyResidential` /
`Commercial` / `Industrial` / `Office` expense (never netted), and `BudgetApplySystem` pays those arrays into
`PlayerMoney` 1024 times a month (1.6.3f1). Vanilla's Budget panel shows the subsidies as expense rows. So the engine
books its own side ([The city's side](#the-citys-side)).

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
  `m_TaxPaidMultiplier` load from `OnGameLoaded` (`:242-258`, `ModeSettingData`), game version 1.6.3f1; each copied
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
  are computed from it, plus the wage per education level (`TaxRateResolver.Wages`), all disposed with the jobs'
  handle.

## The city's side

The city receives exactly what payers are charged: vanilla's estimate pays it at the stored rates, and the engine's
charges beyond that are booked on top.

- **Per charge** (`PayTaxJob`): the tax at the stored rate, at the rate without density tax
  (`EffectiveTaxRate.Compute(base, 0, districtDelta, …)`) and at the charged rate split into the **district part**
  (without density − stored) and the **density part** (charged − without density), `DensityTaxBooking.Parts`
  (`Mod/Logic/`, tested). Each chunk sums its charges per zone (office detection as vanilla's) and queues one
  `TaxBookingEvent` per zone it charged: the district parts, the density parts of each sign, and the tax at the stored
  rates (only for the log's scale check, never booked).
- **Accumulation**: the next engine update drains the queue into `TaxEngineSystem.Ledger` (`TaxBookingLedger`,
  `Mod/Logic/`, tested) under that tick's update frame, replacing what the frame booked a month earlier. Each charge is
  kept × 32 (`DensityTaxBooking.Monthly`, vanilla's `kUpdatesPerDay`; the scale of `m_AverageTaxPaid` and of the
  taxable-income statistic), and every payer is charged once per 16 frames, so the sum over the 16 frames is the monthly
  amount. Density parts of each sign are summed apart. The ledger clears on load and while the engine isn't charging,
  and fills over the first 1/32 month after a load. The scale is checked in the tests against both vanilla's estimate
  (rate × the taxable-income statistic, `untaxed × 32` per charge) and the Taxation tab's density estimate
  (`TaxBookingScaleTests`).
- **Booking**: `TaxBookingSystem` (`Mod/Systems/`), `UpdateAfter<…, CityServiceBudgetSystem>` in ModificationEnd (as
  `DistrictServiceUpkeepSystem`), adds the ledger to the arrays vanilla just computed (`GetIncomeArray` /
  `GetExpenseArray`, then `AddArrayReader`), per zone by `DensityTaxBooking.BookZone`: the district part on the zone's
  tax income (should it go below 0, the rest is subsidy), a positive density part on tax income, a negative one on the
  zone's subsidy expense. It adds once per vanilla update (`CityServiceBudgetSystem.LastSystemVersion`) and writes
  nothing while the ledger is empty, so **no density tax and no override is vanilla's numbers, bit for bit**.
- **Charge records**: every charged payer's untaxed income and engine tick go into a per-payer map
  (`ChargeRecordEvent` → `RecordChargesJob`, pruned every 16 ticks, cleared on load and while the engine isn't
  charging); the Taxation estimates read it (`GetChargeRecords` / `AddChargeRecordsReader`,
  [tax-engine-ui.md](tax-engine-ui.md)) so they count exactly the payers and the base the ledger books.
- **Log**: while the engine charges, one Mod.log line per 1/32 month (`TaxBooking (monthly):`, `TaxBookingLog`), per
  zone: vanilla's income and subsidy, the engine's charges at vanilla rates with `scale` = vanilla net / that (about 1
  when the ledger's scale is vanilla's), the district and density parts booked, and the result. Compare the density
  part with the Taxation tab's estimate there rather than the Budget row, which drifts with the city. The head reads
  `settled` once all 16 frames in the window were charged at the current rates (`RateSignature`: vanilla's rates, the
  density switch and the rate table; `TaxBookingLedger.Settled`), else `NOT settled`: a moved rate mixes old and new
  charges for up to 1/32 month (plus one tick), so compare only settled lines. After the zones: each density cell with a
  rate or a booked part (`cells:`, the charged payers' monthly taxable income, the density booked, and taxable x the
  city's rate, which the Taxation row's estimate should match), then every district override other than density
  (`district rate overrides:`, `Type/zone/key@district:version=value`), which is where a district part comes from.
- **What it shows**: the Budget tab reads vanilla's arrays and the Income/Expense statistics `BudgetApplySystem`
  records from them, so the zone's tax income row and chart line include the density and district parts and a negative
  density tax appears under Subsidies (Residential subsidies, …). `CityServiceBudgetSystem.GetTotalTaxIncome` (a
  private total taken inside vanilla's job, used by the event journal) stays vanilla's.
  The per-district split weighs the same parts (`DistrictBudgetMath.PayerTaxWeights`,
  [budget-chart.md](budget-chart.md)), so a district's shares add up to the booked row and its negative density tax
  lands in its subsidies.

## The rate model

`TaxRateModel` (`Mod/Logic/`, tested) mirrors how vanilla combines its 92-int rate array (`TaxSystem.cs`, 1.6.3f1):
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

**`TaxRateResolver`** (`Mod/Systems/`, Burst-safe) is **the one effective-rate computation**, and nothing else adds
density or district adjustments. `Create` on the main thread (the city's rates and limits from `TaxSystem`, the table,
the wages), then in a job, `Dispose(handle)` after:

- `ChargedRate(taxPayer, payer, property, kind, limits)` / `Adjustments(...)`: a payer's charge (the engine; the
  demand view's companies; the Taxation tab's estimate).
- `Locate(property, …)` then `ResidentRate(block, densityTax, level, vanillaLevelRate, limits)`: what a resident of
  one education level pays in a home, the city's rate for that level plus the home district's difference **at that
  level only** plus the home's density tax (`TaxRateModel.ResidentRate`). Happiness, home choice and the demand
  view's education slots use it ([tax-engine-effects.md](tax-engine-effects.md)), so a district's Highly Educated
  override moves only highly educated residents.

The district of a building is `CurrentDistrict` up the `Owner` chain (`TaxKeyResolver`). A building in no district
pays **Outside Districts'** overrides (a user decision: Outside sets its own rates like a district; the
resolver's `m_OutsideBlock`, picked by `TaxRateModel.AreaBlock` in `Locate`), else the city's values; payers with no
property (homeless) pay the city's. The managed twins `TaxRateModel.ResidentRates` and
`HouseholdChargedRate` compose the same pieces for the tests: a level override moves only that level, density stacks,
and every value at the city's is bit-identical to vanilla.

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
`TaxEngineGuardLogic.WarningFor` gives the Taxation tab's warning ([tax-engine-ui.md](tax-engine-ui.md#the-engine-warning)).

**A new game version**: compare vanilla `TaxSystem` (PayTaxJob, OnUpdate, OnGameLoaded) with the copy, update the
copy and its headers, then add the version to `KnownGameVersions`; do the same for the effect systems' copies
([tax-engine-effects.md](tax-engine-effects.md)). The known list is `1.6.0f1` and `1.6.3f1` (the copies' source); any
other version stays blocked unless forced.

`Setting.EnableSimulationChanges` (default on, `FeatureGates.SimulationChangesDefault`) and
`Setting.RunOnUntestedGameVersion` (default off) sit in Options > Features; `Mod.ApplyFeatureSwitches` calls
`TaxEngineSystem.ApplyMode` on every change.

## Classifying a building

`DensityClassifier.TryClassify` (Burst-safe copy of `PropertyUtils.GetZoneDensity`): `PropertyRenter.m_Property` →
`PrefabRef` → `SpawnableBuildingData.m_ZonePrefab` → `ZoneData` + `ZonePropertiesData`. Residential:
low / medium / high. Commercial: low / high. Office (industrial area + office flag): low / high. Industrial:
low only. Households count only in residential-zoned buildings and companies only in commercial/industrial/office ones,
so commercial companies in mixed-housing zones and commuters are not affected.

## Storage

Rates are **per save**. Each city keeps a `TaxRateTable` (`Mod/Logic/TaxRateTable.cs`): sparse entries keyed by
(`TaxType`, zone, key, **area**) to percentage points, the area being the city (0:0), a district entity (index and
version: ints in the logic, an `Entity` in the save so the game remaps it; **district keys change on every load**, so
the UI takes them from the `districts` binding only and never caches one) or Outside Districts
(`TaxRateTable.OutsideArea`, -1:0, an index no entity has; the UI's key "outside", `TaxRateRows.AreaKey`/`TryArea`).
Types: `Density` (key = density; the city's rate and district overrides, −5..+10, `TaxRateTable.DensityMin/Max`), `ZoneRate` (a district's full zone rate) and
`ResourceRate` (a district's education-level or resource rate, stored like vanilla as an offset from the zone rate);
`District` (1) is the retired ±adjustment, never stored. A city value of 0 is the default and not stored; a district
entry is an override and is stored even at 0. New types (wealth, land value, building level) take the next enum
numbers and work the same way. The city's zone and resource rates are vanilla's own array, not the table's.
`DensityTaxTable` is a view of the city's density entries.

`TaxRatesSystem` is `IDefaultSerializable`: the save block is `FormatVersion` (3), a count, then per entry type, zone,
key, the area's kind (`TaxRateTable.AreaKind`: city, district, Outside), its entity (a district's; `Entity.Null`
otherwise) and value; `TaxRateTable.LoadSavedEntry` reads each. A version-2 block (no kind: `Entity.Null` was the
city) loads as before; a version-1 block (the unshipped ±adjustment model) keeps its density entries as the city's
density rates and drops its district adjustments; any other version loads empty. On load it drops district entries
whose entity is gone (`Entity.Null` on read; `PruneDistricts` on `OnGameLoadingComplete`, which keeps the city's and
Outside's) and points `DensityTaxState` at the city's table (`DensityTaxState.Use`); sliders write it in
place, and it is saved with the city.

**Migration and the template.** `Setting.DensityTaxOffsets` (12 comma-separated ints) is now only the template for a
city with no block: a new city, or a save from before per-save rates. `SetDefaults` seeds that city's density entries
from it once (`TaxRateTable.SeedDensityFrom`); the next save carries the block, so it is never seeded again. Sliders no
longer write the setting. Options > reset clears the template and the loaded city's density rates (the city's and
every district's).

## Follow-ups

- Check in game that no density tax and no override match vanilla's monthly tax income exactly, that density +10
  raises the zone's tax income by about the Taxation tab's estimate, and that a negative density tax shows a subsidies
  row.
- Confirm in Mod.log that `Game.Version.current.shortVersion` reads `1.6.3f1` (the known-list format).
