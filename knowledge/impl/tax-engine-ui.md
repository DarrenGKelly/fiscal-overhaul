---
type: Implementation
title: Taxation tab
description: The Taxation tab's side of the tax engine — the densityTaxes binding and its revenue estimate at each payer's resolved rate, the Enable density taxes switch, the engine warning line, the collapsible Density tax / Education / Resources sections, density rows as vanilla rows, totals including density tax, and per-district rates (taxRates, setTaxRate, the district picker, the district view).
resource:
  - Mod/Systems/DensityTaxUISystem.cs
  - Mod/Systems/TaxRatesUISystem.cs
  - Mod/Systems/DistrictListUISystem.cs
  - Mod/Logic/TaxRateRows.cs
  - Mod/Logic/DensityTaxTable.cs
  - UI/src/taxation
  - UI/src/district
---

# Taxation tab

The UI of the [tax engine](tax-engine.md): the city's density tax rows, the totals, and each district's own rates.
The rates themselves (what is stored, what is charged) are [tax-engine.md](tax-engine.md); what they change besides
revenue is [tax-engine-effects.md](tax-engine-effects.md).

## Density bindings and the estimate

`DensityTaxUISystem` publishes `densityTaxes = { minRate, maxRate, cells[{ zone, density, offset, estimate }] }`
(`offset` = the city's density tax rate) and `setDensityTax(zone, density, rate)`.

The **estimate** is what the city's rate brings in a month: each payer's monthly tax paid (`TaxPayer.m_AverageTaxPaid`,
the last payment × 32) scaled back to its taxable base at **the rate it was charged** (`DensityTaxTable.TaxableBase`;
the rate is the resolver's, [tax-engine.md § The rate model](tax-engine.md#the-rate-model): its own district and
density rates while the engine charges, its stored rate otherwise), summed per cell, times the city's rate
(`EstimateFromBase`). Each payer's base is its own, so a district's density override no longer skews the city cell
(the older cell-wide form, `EstimateExtraRevenue`, took every payer at the cell's average stored rate + the city's
rate). Recomputed every ~2 s while the panel is open and density taxes are on.

**Options > Enable density taxes (BETA)** (`Setting.EnableDensityTaxes`, default **off**: density taxes are a BETA
feature, `FeatureGates.DensityTaxesDefault`) switches the feature off live. The options page calls `ApplyAndSave` on
every change; `Mod` listens on `onSettingsApplied` and calls `DensityTaxState.SetEnabled`. Off, the density rates the
engine reads are all 0 (`TaxRateModel.DistrictDensityRates` with the switch off), so buildings pay no density tax
(district income-tax overrides still apply while the engine runs); the stored table and the template are left as they
are, so switching back on restores them. `DensityTaxUISystem` publishes the switch as `densityTaxesEnabled`, and the
rows ask `cellsForArea` (`UI/src/taxation/density.ts`), which gives no cells while it is off.

## The engine warning

`TaxEngineGuardLogic.WarningFor` ([tax-engine.md § The guard](tax-engine.md#the-guard-and-the-switches)):
`DensityTaxUISystem` publishes it as `taxEngineWarning` (int), and `EngineWarningLine`
(`UI/src/taxation/engine-warning-line.tsx`, text from `engine-warning.ts`) draws one line above the TaxationPage for
an untested version (vanilla, or forced) or an incompatible game. None while the master switch is off.

## Layout

With density taxes on, the wrapper around each vanilla `TaxationGroup` (`UI/src/taxation/tax-sections-view.tsx`) draws
collapsible sections under the zone's main slider, each a header row with an arrow and its title over its rows (no
count summary, removed by request): **Density tax** first, one row per density the zone has (`density-rows.tsx`),
then vanilla's own sub-rows, re-hosted as vanilla `TaxationItem`s from `taxation.areaResources$` (**Education** for
Residential, **Resources** for the other zones). All start collapsed; the open ones are kept for the UI session
(`sectionExpandState`, not reset when the panel closes). The section list is data (`TAX_SECTIONS` in
`tax-sections.ts`: id, title per zone; `sectionsForArea` keeps the ones with rows, in order, none for a locked zone),
and `TAX_SECTION_ROWS` in the view maps each id to its rows: a new tax type is one entry in each and a title in
`l10n/en-US.json`. Vanilla's own expander (toggle button, gamepad hint, content) is hidden by adding classes to the
live `taxation-group.module.scss` class object, which `TaxationGroup` reads on every render (`groupClasses`): the
toggle is `visibility: hidden` so the header slider keeps its place. Off, the names are vanilla's again and no
sections are drawn, so the tab is vanilla. Rows show no payer count (removed by request, 2026-11-07).

## Density rows

Each density row is built exactly as vanilla `TaxationItem` builds an education/resource row (`TaxationItem` itself
reads its values from per-resource bindings, so it can't take ours): the same `ActiveFocusDiv` wrapper
(`game-ui/common/focus/focus-div.tsx`), the live `taxation-item.module.scss` class names (`taxationItemClasses`: row,
icon, title, and `slider`, which fixes the TaxSlider at vanilla's 580rem) and vanilla `TaxSlider`, so the icon, title,
% column, slider and estimate pill sit in the same columns as the rows below (`density-rows.module.scss` holds copies
of those rules, used only if a patch moves the module). The icon is the zoning toolbar's (`Media/Game/Icons/Zone*.svg`
via `densityIcon` in `density-icons.ts`; an empty box where the game has none, e.g. Commercial/Office medium).
**Density tax is its own tax**, charged on top of income tax (a user decision), so the row shows only its own rate
("3 %", "0 %" by default), never the zone rate plus it, and the section header reads "Density tax". The slider's range
is one constant, `DENSITY_RATE_RANGE` = −5..+10 in `density-slider.ts` (with `densitySliderValues`/`rateFromSlider`,
which rounds and clamps what the slider reports), matching `TaxRateTable.DensityMin/Max`. The estimate is the density
tax's revenue, so it is a neutral ¢0 (no "+" pill, vanilla `NumberField`'s own rule for 0) until a rate is set.

## Totals

**Totals include the density tax** (a user decision). Each zone group's total (its header slider's income) and
ESTIMATED TOTAL add the sum of the zone's (all zones') row `estimate`s; negative rates lower them. Vanilla computes
both in C# (`TaxationUISystem`: `areaTaxIncomes`, and `taxIncome` = the four zones' sum, from the vanilla rates only)
and draws them inside its own components, so the UI changes the **drawn value**, not the binding
(`UI/src/taxation/density-totals.tsx`): the TaxationGroup wrapper provides the zone's extra and a TaxationPage wrapper
the total's through a React context; an extended `TaxSlider` adds the zone's to the `primary` (header) slider, an
extended `NumberField` adds the total's to the field whose className is the page's `totalValue` class. The values go
through vanilla's own components, so sign and money format are vanilla's. Off, or every rate 0: the extra is 0 and
both pass through unchanged. The maths is `densityExtraForArea`/`densityExtraTotal`/`zoneTotalIncome`/
`estimatedTotalValue` in `density.ts`.

## Per-district rates

A user decision: a district sets its own values. `TaxRatesUISystem` publishes
`taxRates = { engineOn, districts[{ area, type, zone, key, value }] }`: every stored district override (as stored:
full zone rate, level/resource offset, density rate), keyed by `DistrictKey` ("index:version"), built by
`TaxRateRows.DistrictRows`; `engineOn` is `TaxEngineMode.Engine`. `setTaxRate(type, zone, target, value)` takes the
value **as the slider shows it** (a full rate) and a target "key@area" (a trigger takes at most four arguments;
`TaxRateRows.SplitTarget`); `TryResolve` accepts the city ("" area) for density only, and the system checks the
district still exists, then `TaxRateModel.SetDistrictValue` clamps to vanilla's limits, turns a level/resource rate
into its offset from the district's zone rate, and **removes the override when the value equals what the district
would have without it**, so dragging a slider back to the city's value makes it follow the city again.
`resetDistrictTaxRates(area)` removes every override of the district ("Use city rates").

The district list is the one `districts` binding (`DistrictListUISystem`, shared by every tab's selector), published
while any district feature needs it, the tax engine charging included (`FeatureGates.PublishesDistrictList(records
district budget, shows district service budgets, tax engine charges)`), so the Taxation selector works with the
budget chart off.

On the tab (`district-tax.ts` pure, `district-tax-view.tsx`), **`TaxDistrictBar`** is the district picker, one
self-contained component (the shared `DistrictSelector` over `district/selection.ts`, the same choice as the Budget
tab, and in a district view a vanilla `Button` **Use city rates**, disabled with no override), shown while the engine
charges and the city has a district (`showsTaxDistrictSelector`): Entire City, then each district, no Outside
Districts (`taxDistrictOptions`). A TaxationPage wrapper draws it above the page and provides the view (`taxViewKey`)
through `TaxDistrictView`. A **district view is the Entire City layout** with the district's values: the zone header
slider, Density tax (while density taxes are on) and Education/Resources. Each slider says which rate it is through
`TaxRowTarget` (the TaxationGroup wrapper: the zone rate, for the `primary` header; each re-hosted `TaxationItem`: its
level or resource), and the extended `TaxSlider` swaps in the district's value and setter (`useDistrictSlider`,
`rowRate`: district value else city; a level/resource is the district zone rate + the district's offset else the
city's, as in C#), adds `overridden` (an accent bar down the slider's left edge) where it differs from the city's,
and hides the estimate column. Density rows do the same themselves. **Estimates are hidden in a district view**
(row incomes, zone totals and ESTIMATED TOTAL, which are the city's; a provisional choice: a per-district estimate
would need a per-district paid-tax sum). Entire City is unchanged.
