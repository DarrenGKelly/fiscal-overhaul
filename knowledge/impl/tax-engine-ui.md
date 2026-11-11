---
type: Implementation
title: Taxation tab
description: The Taxation tab's side of the tax engine — the densityTaxes binding and its estimate booked as charged (each payer's clamped density part), the Enable density taxes switch, the engine warning line, the collapsible Education / Resources then Density sections, density rows as vanilla rows, totals including density tax, each row's reset button and target marker, and per-district rates (taxRates, setTaxRate, clearTaxRate, the district picker with Outside Districts, the district view and its own estimates).
resource:
  - Mod/Systems/DensityTaxUISystem.cs
  - Mod/Systems/TaxRatesUISystem.cs
  - Mod/Systems/DistrictTaxEstimateUISystem.cs
  - Mod/Logic/DistrictTaxEstimate.cs
  - Mod/Logic/PayerTaxParts.cs
  - Mod/Logic/ChargeWindow.cs
  - Mod/Systems/DistrictListUISystem.cs
  - Mod/Logic/TaxRateRows.cs
  - Mod/Logic/DensityTaxTable.cs
  - UI/src/taxation
  - UI/src/district
  - UI/src/common
---

# Taxation tab

The UI of the [tax engine](tax-engine.md): the city's density tax rows, the totals, and each district's own rates.
The rates themselves (what is stored, what is charged) are [tax-engine.md](tax-engine.md); what they change besides
revenue is [tax-engine-effects.md](tax-engine-effects.md).

## Density bindings and the estimate

`DensityTaxUISystem` publishes `densityTaxes = { minRate, maxRate, cells[{ zone, density, offset, estimate }] }`
(`offset` = the city's density tax rate) and `setDensityTax(zone, density, rate)`.

The **estimate** is what the cell's density tax brings in a month **as the city books it** (the tax engine's
booking: a positive density part is tax income, a negative one the zone's subsidy): each payer's own clamped density
part, `PayerTaxParts.DensityPart` = (charged rate − rate without density) × base / 100, summed per cell. Both rates are
`EffectiveTaxRate.Compute`'s with the payer's resolver adjustments ([tax-engine.md § The rate model](tax-engine.md#the-rate-model)),
so a payer under a district's density override counts at that district's rate, and one near the limit only for the
part the clamp lets through. The base is the untaxed income **the engine last charged** the payer, × 32
(`PayerTaxParts.FromCharge`), from the engine's charge records (`TaxEngineSystem.GetChargeRecords`: per payer, the
untaxed income and the engine tick of its last charge), and only a payer charged in the last 1/32 month counts
(`ChargeWindow.IsCurrent`: within the last 16 engine ticks). After a load the engine counts its ticks from 1, so for the first 16 ticks (8,192 frames, a little over 2 minutes at 1x) only that many update-frame groups have been charged: the city's density cells and district parts and the district view's estimates are multiplied by `ChargeWindow.WarmUpScale` (16 / ticks, 1 after a full window, 0 before the first tick), each group being an even share of the payers. Before, they climbed from a sixteenth of the whole (issue #92: a district read 29% of its Budget taxes after 30 s). That is the base the ledger books from, so a settled
booking window's density booked equals the estimate, and it is restated at the payer's rates **now**, so a moved slider
moves the estimate at once (within the 10-frame refresh) and the next charge leaves it where it is. Each part is the engine's own rounded per-charge tax (`DensityTaxBooking.Tax`, vanilla's `GetTax`) at each rate, split as a charge is (`DensityTaxBooking.Parts`), × 32, not base × rate: on small untaxed incomes the per-charge rounding is a large share of the tax, and base × rate read 5-7% above the booking (issue #83). The engine, like
vanilla, skips a payer with no untaxed income and never resets its `m_AverageTaxPaid`: a household that stopped
earning keeps its last payment for good. The earlier base, paid over the rate charged (`PaymentRate`, now gone),
counted those payers and ran about 1.65x the booking in game. A payer with no income at its charge, or a loss, adds
nothing; one the density tax took to a 0 rate still has its charged income as its base, so its subsidy shows. A negative estimate is drawn as vanilla draws a negative amount. While the engine doesn't
charge, every estimate is 0. Recomputed every ~2 s while the panel is open and density taxes are on, and within 10
frames of a rate change (a dragged slider changes it every frame). The older forms (`EstimateFromBase`,
`EstimateExtraRevenue`, the city's rate on every payer's base) are kept only for their tests.

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
an untested version (vanilla) or an incompatible game. None while the master switch is off, and none on an untested
version the player forced on (Run on untested game version: their choice, so no line; removed by request).

## Layout

With density taxes on, the wrapper around each vanilla `TaxationGroup` (`UI/src/taxation/tax-sections-view.tsx`) draws
collapsible sections under the zone's main slider, each a header row with an arrow and its title over its rows (no
count summary, removed by request): first the zone's main income tax, vanilla's own sub-rows re-hosted as vanilla
`TaxationItem`s from `taxation.areaResources$` (**Education** for Residential, **Resources** for the other zones), then
the additional taxes charged on top of it: **Density**, one row per density the zone has (`density-rows.tsx`; a user
decision: the main income tax first, then the taxes on top). All start collapsed; the open ones are kept for the UI session
(`sectionExpandState`, not reset when the panel closes). The section list is data (`TAX_SECTIONS` in
`tax-sections.ts`: id, `kind` "main" or "additional", title per zone; `orderedSections` draws the main section first,
then the additional ones in list order; `sectionsForArea` keeps the ones with rows, none for a locked zone),
and `TAX_SECTION_ROWS` in the view maps each id to its rows: a new tax type is one "additional" entry in each and a title in
`l10n/en-US.json`. Vanilla's own expander (toggle button, gamepad hint, content) is hidden by adding classes to the
live `taxation-group.module.scss` class object, which `TaxationGroup` reads on every render (`groupClasses`): the
toggle is `visibility: hidden` so the header slider keeps its place. Off, the names are vanilla's again and no
sections are drawn, so the tab is vanilla. Rows show no payer count (removed by request, 2026-11-07).

## Reset buttons and markers

A user decision: every slider row (zone header,
education/resource row, density row) has a **reset button** in the slider column's right padding, just left of the
divider before the estimate, and a **marker** (a thin tick) on its track at the reset target. Entire City: vanilla's
circular-arrow glyph (`Media/Glyphs/ArrowCircular.svg`, the Options page's reset-binding glyph, tinted through vanilla
`TintedIcon`) sets the row back to its default: a zone to vanilla's default rate (`TaxRateModel.DefaultZoneRate`, 10,
sent as `taxRates.defaults`), an education/resource row to offset 0 from its zone's current rate (provisional: so it
follows the zone slider), a density row to 0; through vanilla's own `setAreaTaxRate`/`setResourceTaxRate` or
`setDensityTax`. A district (or Outside) view: the same glyph with a small city glyph (`Media/Game/Icons/City.svg`,
masked) in the middle clears that row's override (`clearTaxRate(type, zone, "key@area")`,
`TaxRateModel.ClearDistrictValue`), so the row follows the city: its target is what the district has without it (the
city's value, or for a level/resource row under a district zone override, the district's zone rate plus the city's
offset). The button shows only while the row differs from its target (city view) or stores an override (district
view); `RESET_WHEN_UNCHANGED` in `common/slider-reset.ts` switches that to dimmed (provisional, pending the in-game
look). The pieces are shared for the Services tab: `UI/src/common/reset-button.tsx`, `slider-marker.tsx` and
`slider-reset-frame.tsx` (re-measuring every frame after a render until the track and the button slot are found and
have held for 3 frames, at most 120: `keepMeasuring`; the panel's layout takes several frames and a paused game may
not render the row again, so measuring once left rows with no button or marker until a slider was touched) (a vanilla slider takes no children, so the frame wraps it, takes its layout class, measures
the track and the slot with `getBoundingClientRect` after each render and on the next frame, and draws both on top);
the maths is `common/slider-reset.ts` (`resetVisibility`, `markerFraction`, `markerLeft`) and `rowReset` in
`taxation/district-tax.ts`; the Taxation glue is `taxation/tax-row-slider.tsx` (it widens vanilla's 22rem padding to
40rem). The whole-district `resetDistrictTaxRates` trigger stays in C# with no UI.

## Density rows

Each density row is built exactly as vanilla `TaxationItem` builds an education/resource row (`TaxationItem` itself
reads its values from per-resource bindings, so it can't take ours): the same `ActiveFocusDiv` wrapper
(`game-ui/common/focus/focus-div.tsx`), the live `taxation-item.module.scss` class names (`taxationItemClasses`: row,
icon, title, and `slider`, which fixes the TaxSlider at vanilla's 580rem) and vanilla `TaxSlider`, so the icon, title,
% column, slider and estimate pill sit in the same columns as the rows below (`density-rows.module.scss` holds copies
of those rules, used only if a patch moves the module). The icon is the zoning toolbar's (`Media/Game/Icons/Zone*.svg`
via `densityIcon` in `density-icons.ts`; an empty box where the game has none, e.g. Commercial/Office medium).
**Density tax is its own tax**, charged on top of income tax (a user decision), so the row shows only its own rate
("3 %", "0 %" by default), never the zone rate plus it, and the section header reads "Density". The slider's range
is one constant, `DENSITY_RATE_RANGE` = −5..+10 in `density-slider.ts` (with `densitySliderValues`/`rateFromSlider`,
which rounds and clamps what the slider reports), matching `TaxRateTable.DensityMin/Max`. The estimate is the density
tax's revenue, so it is a neutral ¢0 (no "+" pill, vanilla `NumberField`'s own rule for 0) until a rate is set.

## Totals

**Totals include the density tax and the district parts** (user decisions), as the city books the engine's charges.
Each zone group's total (its header slider's income) and ESTIMATED TOTAL add the zone's (all unlocked zones') extra:
the sum of its density row `estimate`s (a subsidy lowers it) plus its **district part**, what district and Outside
income-tax overrides add to vanilla's tax (`PayerTaxParts.DistrictPart` = base × (rate without density − vanilla
rate) / 100, summed per payer zone, `TaxPayerZone` as the booking; `densityTaxes.districtParts[4]`, published while
the engine charges whatever the density switch). Vanilla computes both totals in C# (`TaxationUISystem`:
`areaTaxIncomes`, and `taxIncome` = the four zones' sum, from the vanilla rates only) and draws them inside its own
components, so the UI changes the **drawn value**, not the binding (`UI/src/taxation/density-totals.tsx`): the
TaxationGroup wrapper provides the zone's extra and a TaxationPage wrapper the total's through a React context; an
extended `TaxSlider` adds the zone's to the `primary` (header) slider, an extended `NumberField` adds the total's to
the field whose className is the page's `totalValue` class. The values go through vanilla's own components, so sign
and money format are vanilla's. The education/resource rows stay vanilla's. With no density tax paid and no
override, the extra is 0 and both pass through unchanged. The maths is `densityExtraForArea`/`districtPartForArea`/
`zoneExtra`/`totalExtra`/`zoneTotalIncome`/`estimatedTotalValue` in `density.ts`.

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
`clearTaxRate(type, zone, target)` removes one override (a row's reset button); `resetDistrictTaxRates(area)` removes
every override of the district (no UI uses it now). Both, and `setTaxRate`, take "outside" as the area.

The district list is the one `districts` binding (`DistrictListUISystem`, shared by every tab's selector), published
while any district feature needs it, the tax engine charging included (`FeatureGates.PublishesDistrictList(records
district budget, shows district service budgets, tax engine charges)`), so the Taxation selector works with the
budget chart off.

The district is picked in the **Economy panel header** (the one picker for Budget, Taxation and Services:
[impl/budget-chart.md § District selector](budget-chart.md#district-selector)), over `district/selection.ts`. On the
Taxation tab it shows while the engine charges and the city has a district (`showsTaxDistrictSelector`, through
`headerPicker`): the city (by its name), each district, then **Outside Districts** (`taxDistrictOptions`, as Budget and
Services; a user decision: the buildings in no district set their own rates). On the tab
(`district-tax.ts` pure, `district-tax-view.tsx`), a TaxationPage wrapper provides the view (`taxViewKey`) through
`TaxDistrictView` and tells C# which area's estimates to compute (`setTaxEstimateArea`). A **district view is the
Entire City layout** with the district's values: the zone header slider, Education/Resources and Density (while
density taxes are on). Each slider says which rate it is through `TaxRowTarget` (the TaxationGroup wrapper: the zone
rate, for the `primary` header; each re-hosted `TaxationItem`: its level or resource), and the extended `TaxSlider`
swaps in the district's value and setter (`useRowSlider`, `rowRate`: district value else city; a level/resource is the
district zone rate + the district's offset else the city's, as in C#) and adds `overridden` (an accent bar down the
slider's left edge) where it differs from the city's. A zone header's % label reads vanilla's `range` (the zone's
resources' rates, `areaResourceTaxRanges`) over `rate`, so the district view passes the district's span too
(`districtHeaderRange`: the city's span moved by the district's zone rate, widened to the resources it sets itself).
Density rows do the same themselves. Entire City is unchanged.

**District estimates.** A user decision: a district (or
Outside) view shows its own row estimates, zone totals and ESTIMATED TOTAL. `DistrictTaxEstimateUISystem` publishes
`districtTaxEstimates = { area, items[{ zone, key, estimate }], density[{ zone, density, estimate }], zones[4], total }`
for the area `setTaxEstimateArea(area)` names (a district key or "outside"; "" = none). Its job sums the area's
payers' `PayerTaxParts` (the same booking as the city's density cells): the income part (the rounded tax at the rate without density,
the district's overrides included) on the payer's level/resource row (a household's split over its workers' education
levels by wage, none: level 0; a company's on its output resource, Office for a weightless one), the density part on
its density row (`DistrictTaxEstimate.AddHousehold`/`AddCompany`, `Compute`: rows rounded, each zone the sum of its
rows and density rows, the total the sum of the zones, so they always add up). With no override the rows add up to
what the district's buildings pay at city rates. Same refresh as the city's (~2 s, within 10 frames of a rate
change); the UI reads them through `districtRowEstimate`/`districtTotalEstimate` (0 until they are for the shown
area).
