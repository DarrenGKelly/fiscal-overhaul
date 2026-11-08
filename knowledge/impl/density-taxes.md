---
type: Implementation
title: Density taxes
description: Per-density tax offsets — how they ride on the vanilla TaxSystem via an apply/restore system pair, how buildings are classified, what they do not affect (demand, happiness), storage and the Taxation tab rows.
resource:
  - Mod/Systems/DensityTaxSystems.cs
  - Mod/Systems/DensityClassifier.cs
  - Mod/Systems/DensityTaxUISystem.cs
  - Mod/Systems/DensityTaxState.cs
  - Mod/Logic/DensityTaxTable.cs
  - Mod/Logic/FeatureGates.cs
  - Mod/Setting.cs
  - UI/src/taxation
---

# Density taxes

User decisions (2026-11-03): density is **another offset** on top of the vanilla rate (zone rate + education/product
offset). In v1 it changes **revenue only**, not demand or happiness, and is capped at ±5 points
(`DensityTaxTable.MaxOffset`) so it can't be a free money lever.

## How vanilla charges tax

Households and companies carry a `TaxPayer { m_UntaxedIncome, m_AverageTaxRate, … }`. Income systems
(`PayWageSystem`, `ServiceCompanySystem`, `ProcessingCompanySystem`, `ExtractorCompanySystem`) add income and blend
the applicable vanilla rate into `m_AverageTaxRate`. `TaxSystem` (every 512 frames, one of 16 `UpdateFrame` groups)
charges `untaxed * averageRate / 100` and zeroes `m_UntaxedIncome`. Every vanilla rate lookup happens inside Burst
jobs we can't patch.

## The apply/restore pair

- `DensityTaxApplySystem`, `UpdateBefore<…, TaxSystem>`, same interval, so it inherits TaxSystem's update offset
  ([cs2-ui-modding.md](../cs2-ui-modding.md#scheduling-simulation-systems)). For the tax payers in this frame's
  `UpdateFrame` group with untaxed income, it adds the density offset to `m_AverageTaxRate` (never below 0:
  `AppliedDelta`) and records the exact delta per entity.
- `DensityTaxRestoreSystem`, `UpdateAfter<…, TaxSystem>`, subtracts exactly that delta again.

- **The restore skips a tick the apply left alone** (#14). The apply system sets `AppliedThisTick` when it schedules
  its job (reset at the top of each update); the restore returns before scheduling unless
  `DensityTaxRestoreGate.RestoreNeeded` (`Mod/Logic/`) says so. That is decided by what apply did this tick, never by
  the current switch or offsets: an update where the switch goes off or the offsets go to 0 after an apply must still
  take the raised rates back down. So with density taxes off (the default) no job writes TaxPayer on any tax tick.

So only the tax paid changes; the stored average rate other systems read is untouched. Why not adjust once and
leave it: companies blend new income into the average at weight ≈ ½ after payment (not 1), so a leftover offset
would compound every payment.

**Threading.** `DensityTaxState` (the table and the switch) is main-thread state, changed by the sliders, the Options
switch and the reset button at any time. The apply job never reads it: each update the apply system copies
`DensityTaxState.Snapshot()` into a `TempJob` array that is the job's alone and is disposed with the job's handle. A
shared persistent array would be overwritten under a running job, or freed under one on mod unload.

## Classifying a building

`DensityClassifier.TryClassify` (Burst-safe copy of `PropertyUtils.GetZoneDensity`): `PropertyRenter.m_Property` →
`PrefabRef` → `SpawnableBuildingData.m_ZonePrefab` → `ZoneData` + `ZonePropertiesData`. Residential:
low / medium / high. Commercial: low / high. Office (industrial area + office flag): low / high. Industrial:
low only. Households count only in residential-zoned buildings and companies only in commercial/industrial/office ones,
so commercial companies in mixed-housing zones and commuters are not affected.

## Storage and UI

Offsets live in the mod's settings (`Setting.DensityTaxOffsets`, 12 comma-separated ints). They're **global, not per
save**: a known v1 limit. Options has a reset button. `DensityTaxUISystem` publishes
`densityTaxes = { maxOffset, cells[{ zone, density, offset, estimate }] }` and `setDensityTax(zone, density, offset)`.
The estimate is `EstimateExtraRevenue`: each cell's monthly tax paid (`TaxPayer.m_AverageTaxPaid`, the last payment
× 32) scaled back to its taxable base, times the offset. That payment was charged at the stored rate **plus** the
offset, so the base is paid ÷ (rate + offset); dividing by the stored rate alone overstated it by (rate + offset) / rate.
Recomputed every ~2 s while the panel is open and density taxes are on.

**Options > Enable density taxes (BETA)** (`Setting.EnableDensityTaxes`, default **off**: density taxes are a BETA feature, `FeatureGates.DensityTaxesDefault`) switches the feature off live. The options
page calls `ApplyAndSave` on every change; `Mod` listens on `onSettingsApplied` and calls
`DensityTaxState.SetEnabled`. Off, the offsets the apply job reads are all 0
(`DensityTaxTable.EffectiveOffsets(false)`) and the apply system returns before scheduling anything
(`IsCharged(false)`), so buildings pay exactly the vanilla rates; the stored table and `DensityTaxOffsets` are left as
they are, so switching back on restores them. `DensityTaxUISystem` publishes the switch as `densityTaxesEnabled`, and
the rows ask `cellsForArea` (`UI/src/taxation/density.ts`), which gives no cells while it is off.

**Taxation tab layout.** With density taxes on, the wrapper around each vanilla `TaxationGroup`
(`UI/src/taxation/tax-sections-view.tsx`) draws collapsible sections under the zone's main slider, each a header row
with an arrow (and an optional summary) over its rows: **Density** first (one row per density the zone has,
`density-rows.tsx`, with a note that offsets change revenue only), then vanilla's own sub-rows, re-hosted as
vanilla `TaxationItem`s from `taxation.areaResources$` (**Education** for Residential, **Resources** for the other
zones). All start collapsed; the open ones are kept for the UI session (`sectionExpandState`, not reset when the panel
closes). The section list is data (`TAX_SECTIONS` in `tax-sections.ts`: id, title per zone; `sectionsForArea` keeps the
ones with rows, in order, none for a locked zone), and `TAX_SECTION_ROWS` in the view maps each id to its rows: a new
tax type is one entry in each and a title in `l10n/en-US.json`. Vanilla's own expander (toggle button, gamepad hint,
content) is hidden by adding classes to the live `taxation-group.module.scss` class object, which `TaxationGroup` reads
on every render (`groupClasses`): the toggle is `visibility: hidden` so the header slider keeps its place. Off, the
names are vanilla's again and no sections are drawn, so the tab is vanilla. Rows show no payer count (removed by
request, 2026-11-07).

**Density rows are vanilla rows.** Each density row is built exactly as vanilla `TaxationItem` builds an
education/resource row (`TaxationItem` itself reads its values from per-resource bindings, so it can't take ours): the
same `ActiveFocusDiv` wrapper (`game-ui/common/focus/focus-div.tsx`), the live `taxation-item.module.scss` class names
(`taxationItemClasses`: row, icon, title, and `slider`, which fixes the TaxSlider at vanilla's 580rem) and vanilla
`TaxSlider`, so the icon, title, % column, slider and estimate pill sit in the same columns as the rows below
(`density-rows.module.scss` holds copies of those rules, used only if a patch moves the module). The icon is the
zoning toolbar's (`Media/Game/Icons/Zone*.svg` via `densityIcon` in `density-icons.ts`; an empty box where the game
has none, e.g. Commercial/Office medium). Like the education rows the row shows an **absolute rate**: the zone rate
(`taxation.areaTaxRates$`) plus the offset, on a slider spanning zone rate ± `maxOffset`; the slider still sets the
offset (`densitySliderValues`/`offsetFromSlider` in `density-slider.ts`, switch `DENSITY_ROW_ABSOLUTE_RATE`; off
draws the bare offset on −max..+max). The estimate is the offset's extra revenue, so it is a neutral ¢0 (no "+" pill,
vanilla `NumberField`'s own rule for 0) until an offset is set.

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

## Follow-ups

- Check in game that a non-zero offset changes the zone's tax revenue (so far only the rows and sliders are
  seen working, 2026-11-03).
- Per-save storage.
- Demand/happiness reacting to density rates would mean replacing several vanilla systems with patched copies.
