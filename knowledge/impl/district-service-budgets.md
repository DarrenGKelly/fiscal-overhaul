---
type: Implementation
title: District service budgets
description: Per-district service budget % (BETA, simulation side) — the store in the save, the efficiency pass after vanilla's, the Service Upkeep expense overwrite, the setDistrictServiceBudget trigger, and the master "Simulation changes" switch every simulation-changing feature gates on.
resource:
  - Mod/Systems/DistrictServiceBudgetSystem.cs
  - Mod/Systems/DistrictServiceUpkeepSystem.cs
  - Mod/Systems/DistrictServiceBudgetUISystem.cs
  - Mod/Systems/ServiceUpkeepCost.cs
  - Mod/Logic/DistrictServiceBudgetTable.cs
  - Mod/Logic/ServiceUpkeepMath.cs
  - Mod/Logic/ServiceBudgetPass.cs
  - Mod/Logic/FeatureGates.cs
  - Mod/Setting.cs
---

# District service budgets

Each district (and Outside Districts, buildings in no district) can run a service at its own budget %: an
**absolute** % from 50 to 150 per (service, district), the vanilla slider's range. A building whose (service, district)
has no override runs at the city slider, as in vanilla. BETA, off by default. This slice is the simulation side only;
the Services tab UI comes later.

## How vanilla uses the budget %

Two places, both reading the `ServiceBudgetData` singleton buffer (service prefab → %, 100 when absent):

- **Efficiency.** `CityServiceEfficiencySystem` (ModificationEnd) sets each service building's
  `EfficiencyFactor.ServiceBudget` to `BuildingEfficiencyParameterData.m_ServiceBudgetEfficiencyFactor.Evaluate(% / 100)`
  when the building (or an installed upgrade) has a money upkeep, else 1. It writes every building when the buffer is
  `Updated` (a slider moved), otherwise only `Created` / `Updated` buildings.
- **Upkeep.** `CityServiceBudgetSystem` (ModificationEnd, every frame) collects per service `m_BaseCost` (network
  upkeep) and per resource `m_FullCost` (each building's upkeep at 100%, rounded per building; money includes wages and
  the base-upkeep city modifier, a tenth while inactive), then the expense array's Service Upkeep entry is
  `Σ services (m_BaseCost + Σ non-money m_FullCost + round(money m_FullCost × % / 100))`.
  `BudgetApplySystem` charges that array (`GetExpenseArray`) and the Budget tab shows it.
  (`CityServiceUpkeepSystem` also reads the % for material consumption; not changed here.)

## The store

`DistrictServiceBudgetSystem` (`IDefaultSerializable`, like `DistrictBudgetSystem`) keeps a
`DistrictServiceBudgetTable<Entity>` (pure, `Mod/Logic/`): service **prefab name** (stable across saves) + district.
`Set` clamps to 50-150 and 0 clears; `Effective(service, district, city%)` is the override or the city %; `Prune`
drops deleted districts (on load and whenever a `District` is created / updated / deleted). The save writes
`FormatVersion`, then per entry name, `isOutside`, district `Entity`, %. An unknown version loads empty; a district
entry whose entity comes back `Entity.Null` (deleted before saving) is dropped, never turned into an Outside override.

Service keys are prefab names (e.g. `Police & Administration`, not `Police`). An unknown key is ignored: the system
logs `District service budget: unknown service '<key>' ignored` and stores nothing (`IsKnownService` on the table is the
gate); on load `PruneServices` drops stored keys that name no service, so saves holding junk clean up.

For jobs the table is resolved to a `NativeHashMap<ServiceDistrictKey, int>` (service entity, district entity;
`Entity.Null` = outside), rebuilt on the main thread after its readers complete: `GetOverrides()` +
`AddOverridesReader(handle)`. A later slice (`DistrictBudgetSystem` following the district %) reads it from there.

## The efficiency pass

`DistrictServiceBudgetSystem` runs `UpdateAfter<CityServiceEfficiencySystem>` (ModificationEnd, every frame) and
rewrites `ServiceBudget` with vanilla's rule but the building's effective % (district from `CurrentDistrict`, up the
`Owner` chain). `ServiceBudgetPass.Plan` decides the pass:

- **Full** when overrides apply and anything could have moved a building's %: the table changed, the slider buffer is
  `Updated`, a `District` is Created/Updated/Deleted (`CurrentDistrictSystem` rewrites `CurrentDistrict` without
  marking buildings `Updated`), a city just loaded, the switches changed; and a safety pass every 1/32 month.
- **Changed** (only `Created` / `Updated` buildings, the ones vanilla just reset) otherwise.
- **Restore**: when overrides stop applying, one full pass with the city % everywhere, then **None** (idle). A load
  owes a pass either way, as the save's `Efficiency` buffers may carry override values.

## The upkeep overwrite

`DistrictServiceUpkeepSystem` runs `UpdateAfter<CityServiceBudgetSystem>`. A Burst job over vanilla's upkeep group sums
each building's money full cost (`ServiceUpkeepCost.MoneyFullCost`, vanilla's per-building maths, reusable) grouped by
(service, effective %); a second job **overwrites** `GetExpenseArray()[ServiceUpkeep]` with
`Σ services ServiceUpkeepMath.ServiceUpkeep(m_BaseCost, non-money m_FullCost, Σ groups money × % / 100)` and
`AddArrayReader`s it. Overwrite, never a delta: vanilla recomputes the entry every frame, so a skipped frame or a switch
going off simply leaves vanilla's number. With a single % per service the sum is vanilla's own float maths, so with no
overrides it equals vanilla exactly (`ServiceUpkeepMathTests`); the system writes nothing at all while overrides don't
apply. The overwrite reads this frame's collected costs where vanilla's entry used last frame's: one frame apart.

## Trigger and switches

`DistrictServiceBudgetUISystem`: `setDistrictServiceBudget(serviceKey, districtKey, budget)` in group
`"fiscalOverhaul"`. `serviceKey` is the service prefab's name (or its entity as `index:version`); `districtKey` is
`"outside"` or a `DistrictKey`; `budget` 0 clears. Stored whatever the switches say.

Options > Features: **District service budgets (BETA)** (`Setting.EnableDistrictServiceBudget`, default off,
`FeatureGates.DistrictServiceBudgetDefault`) and **Reset district service budgets** (clears the loaded city's table).
`FeatureGates.AppliesDistrictServiceBudgets(master, feature, hasAny)` gates both systems.

`DistrictServiceBudgetSystem.LogPasses` (on while BETA) writes a Mod.log line per override change and per triggered
full pass (each override and the efficiency its % gives), and the upkeep system one line after each change: the
Service Upkeep row charged against the row at the city %.

## The master switch

**Simulation changes** (`Setting.EnableSimulationChanges`, default **on**, `FeatureGates.SimulationChangesDefault`):
off, every feature that changes how vanilla simulates (demand, happiness, service efficiency and upkeep, taxes charged)
runs vanilla whatever its own switch says; stored values are kept. Today that is density taxes
(`FeatureGates.AppliesDensityTaxes`) and district service budgets. **Any new simulation-changing feature, including the
coming tax engine, must take this switch first in its `FeatureGates` gate.** The charts and recorded histories are not
simulation changes and ignore it.
