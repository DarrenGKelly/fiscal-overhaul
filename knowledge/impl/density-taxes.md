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

So only the tax paid changes; the stored average rate other systems read is untouched. Why not adjust once and
leave it: companies blend new income into the average at weight ≈ ½ after payment (not 1), so a leftover offset
would compound every payment.

## Classifying a building

`DensityClassifier.TryClassify` (Burst-safe copy of `PropertyUtils.GetZoneDensity`): `PropertyRenter.m_Property` →
`PrefabRef` → `SpawnableBuildingData.m_ZonePrefab` → `ZoneData` + `ZonePropertiesData`. Residential:
low / medium / high. Commercial: low / high. Office (industrial area + office flag): low / high. Industrial:
low only. Households count only in residential-zoned buildings and companies only in commercial/industrial/office ones,
so commercial companies in mixed-housing zones and commuters are not affected.

## Storage and UI

Offsets live in the mod's settings (`Setting.DensityTaxOffsets`, 12 comma-separated ints). They're **global, not per
save**: a known v1 limit. Options has a reset button. `DensityTaxUISystem` publishes
`densityTaxes = { maxOffset, cells[{ zone, density, offset, estimate, payers }] }` and `setDensityTax(zone, density, offset)`.
The estimate is `EstimateExtraRevenue`: each cell's last-month tax paid scaled back to its taxable base, times the offset.
Recomputed every ~2 s while the panel is open.

The Taxation tab wraps each vanilla `TaxationGroup` and adds a collapsible "By density" section of vanilla
`TaxSlider`s (`UI/src/taxation/density-rows.tsx`), with a note that offsets change revenue only.

## Follow-ups

- Check in game that a non-zero offset changes the zone's tax revenue (so far only the rows, sliders and payer
  counts are seen working, 2026-11-03).
- Per-save storage.
- Demand/happiness reacting to density rates would mean replacing several vanilla systems with patched copies.
