---
type: Implementation
title: Budget tab line chart
description: The replacement Budget tab — where its history data comes from (the game's own per-source statistics), how it is turned into monthly series, how per-district budgets and the per-service upkeep split are recorded and selected, and how the filter rows, chart, tooltip and detail column fit together.
resource:
  - Mod/Systems/BudgetHistoryUISystem.cs
  - Mod/Logic/BudgetHistoryMath.cs
  - Mod/Logic/FeatureGates.cs
  - Mod/Systems/DistrictBudgetSystem.cs
  - Mod/Logic/DistrictBudgetMath.cs
  - Mod/Systems/ServiceUpkeepSystem.cs
  - Mod/Logic/ServiceUpkeepSplit.cs
  - UI/src/budget
  - UI/src/chart
---

# Budget tab line chart

The vanilla Budget tab (bar chart + revenue/expense lists + detail) is replaced wholesale:
`moduleRegistry.extend(".../budget-page/budget-page.tsx", "BudgetPage", …)` renders our `BudgetPage` in its place, or
the vanilla page while **Options > Enable budget chart** (`Setting.EnableBudgetChart`, default on) is off. The switch is
live: `BudgetHistoryUISystem` publishes it as `budgetChartEnabled` (a `ValueBinding<bool>` read from the setting every
frame), and while it is off the system builds no history (`FeatureGates.BuildsPanelData`).

## Data: the game already keeps the history

`BudgetApplySystem` (1024 times per in-game month) enqueues a `StatisticsEvent` per income and
expense source: `StatisticType.Income`/`Expense`, parameter = `IncomeSource`/`ExpenseSource` index, change =
that source's current monthly amount / 1024. `CityStatisticsSystem` closes a sample every 8192 frames (32 per month),
so a sample's `m_Value` is (the monthly rate during that 1/32 month) / 32. `CityStatisticsSystem` appends one `CityStatistic` per sample and **saves
them in the save game**, so a loaded city has its whole history.

`BudgetHistoryUISystem` reads each source's buffer (`GetStatisticArray(type, source)`), takes each sample's
`m_Value` (what was added in that sample), and `BudgetHistoryMath.MonthlyRates` multiplies it by 32, which gives the
same "per month" rate the vanilla panel shows (`CityServiceBudgetSystem.GetTotalIncome()`, the live rate). **Don't sum
the trailing 32 samples**: that is the past month's average, which lags a growing city (the chart read ¢40k while the
row read ¢52k). It doesn't use `m_TotalValue`, whose meaning depends on the statistic's collection
type (cumulative / point / daily). The deltas mean the same thing for every type.

**The newest sample is still filling up** (the current 1/32 of a month), so `ReadMonthly` drops it
(`CompleteSamples`, also when it is a new city's only sample); with it, every
line dipped at "Now". In its place `WithLiveNow` appends the live value (`CityServiceBudgetSystem.GetIncome` /
`GetExpense`, what the rows show), so "Now" equals the row by construction. The last saved sample alone read ¢268k of
expenses against the row's ¢284,785 in the playtest. The binding rebuilds on a new sample, a new span, or every 60
frames while the panel is open, so "Now" follows the rows even while the game is paused.

It sends only the visible span (`setSpan(months)`, 0 = all), downsampled to ≤240 points ending on the newest sample
(`PickIndices`), as `history = { span, samples, monthsAgo[], income[14][], expense[15][] }`. Expenses are positive
amounts. It rebuilds only while the chart is enabled and the Economy panel is open, and then when the sample count or
span changed (and straight away when the panel opens or the chart is switched back on).

## Per-district recording

The game keeps no per-district budget, so `DistrictBudgetSystem` (`GameSimulation`, every 8192 frames = 32 samples
a month, panel open or not) records one while **Enable budget chart** and **Record district budgets**
(`Setting.EnableDistrictBudget`, default on) are both on (`FeatureGates.RecordsDistrictBudget`). Each sample weighs
every district (`District` without `Temp`) plus "outside" (`Entity.Null`, buildings in no district) per source, and
`DistrictBudgetMath.Samples` splits the vanilla Budget row (`CityServiceBudgetSystem.GetIncome`/`GetExpense`) by those
weights:

| Source | Weight per district |
|---|---|
| Taxes, subsidies | payers' `TaxPayer.m_AverageTaxPaid` (households residential, `ServiceAvailable` commercial, processing companies industrial or office, as `TaxSystem`), scaled by `TaxScale` = vanilla row / city-wide paid restated at the city rate (`AtCityRate`: paid x city rate / the rate the payer's district charges now) |
| Electricity, water | `ElectricityConsumer.m_FulfilledConsumption`; `WaterConsumer.m_FulfilledFresh + m_FulfilledSewage` |
| Healthcare, education | `Patient` / `Student` buffer length |
| Garbage | `GarbageProducer.m_Garbage` |
| Parking | parked cars x their lane's `ParkingLane.m_ParkingFee`, by the lane's district (`BorderDistrict` side, else the owner building's `CurrentDistrict`) |
| Public transport | household residents |
| Service upkeep | each `CityServiceUpkeep` entity's upkeep as `CityServiceBudgetSystem`'s building job; the network part (`CollectedCityServiceUpkeepData.m_BaseCost`) stays city-only |
; the money part runs at the building's district % for its service when per-district service budgets apply (`ServiceUpkeepCost.EffectiveBudget`), else the city %; the per-service weights use the same %, so the children still sum to the district slot |

**Taxes are not apportioned**: vanilla's tax rows are policy-blind estimates (taxable income x city rate), while
`m_AverageTaxPaid` includes a district's Low Commercial Tax policy; scaling the paid sums keeps a discount in its
district, so the districts' taxes sum to the row only without such policies. The restatement uses the district's
current rate, never the payer's own `m_AverageTaxRate` (#30): that average blends a household's members' job levels,
lags rate changes, and vanilla blends office companies toward the *industrial* rate (`ProcessingCompanySystem`), so
using it put residential 2% low and office 20% high. Saves made before the game tracked tax paid
(`FormatTags.TrackCompanyCustomersAndTaxes`) load with every `m_AverageTaxPaid` 0: the first sample or two after
loading one has few or no payers, and a row with none records 0 for every district. A building's district is its
`CurrentDistrict`, or its owner's (upgrades, sub-buildings). The system runs `UpdateAfter` the density tax restore
system, so a sample on a tax frame reads the stored rates.

Each area's samples go into a `DistrictHistory` (36 months, `MaxSamples`); a district gone since the last sample is
dropped. The system is `IDefaultSerializable`, so the game saves it with the city (`FormatVersion`, then per area its
`Entity`, sample count and `ToFlat()` floats); a save loaded without the mod skips the block
(`ObsoleteSystemSerializer`). For the UI: `TryGetHistory(district, out history)`, `Areas`, and `LiveValues(district)`
(the latest weights x the live vanilla rows). `DistrictBudgetSystem.LogSamples` (off by default; set it for an in-game check) writes one `FiscalOverhaul.Mod.log` line per sample
(district count, tax scales, sums against the vanilla rows).

So a district's value is a share of the vanilla row (weights above), except taxes: those are the district's payer
sums x the tax scale, so a Low Commercial Tax district shows its lower commercial tax. Paid Parking counts through
the lane fees, and usage policies (water, electricity, ...) through usage. History starts when recording does (the
first save with the mod), not with the city.

## Per-service upkeep split

Vanilla records Service Upkeep as one expense source (`ExpenseSource.ServiceUpkeep`, 5) and keeps no per-service
series, so the mod records the split itself, as **weights** per service (by service prefab name, `ServiceColumns`), and
splits the recorded row by them when it builds the history (`ServiceUpkeepSplit.Split`: largest-remainder rounding, so
each point's parts are whole numbers summing exactly to the row as written; a negative or zero weight counts 0).

- **Entire City**: `ServiceUpkeepSystem` (`GameSimulation`, every 8192 frames at offset 4096, halfway between the
  statistics samples) records while **Enable budget chart** is on (`FeatureGates.RecordsServiceUpkeep`, panel open or
  not). A sample is `CityServiceBudgetSystem.GetEstimatedServiceBudget(service)` per service prefab (the
  `CollectedCityServiceBudgetData` entities); vanilla's ServiceUpkeep expense is exactly the sum of these. Each sample is
  tagged with `CityStatisticsSystem.sampleCount`, the statistic sample filling at the time; at build time point `local`
  of the ServiceUpkeep series reads the tag `ServiceUpkeepSplit.CityTag` (the live point uses the live estimates). Its
  own `IDefaultSerializable` block (`ServiceUpkeepSave.WriteCity`: version, names, tagged samples, 36 months).
- **A district**: `DistrictBudgetSystem`'s upkeep job also adds each building's upkeep under its service
  (`ServiceObjectData.m_Service`) into per-area weights, kept beside each `DistrictHistory` as a `ServiceWeightHistory`
  aligned with its newest sample (`ServiceUpkeepSplit.DistrictWeightIndex`); the split is of the district's own upkeep
  slot, so networks stay out as they do there. Save **format 2** (`DistrictHistory.FormatVersion`,
  `ServiceUpkeepSave.WriteDistricts`): version, service names, then per area its key, samples and weights. Format 1
  saves (before the split) still load, with no weights: a district's split starts with the next sample.
- **Gaps**: points before the first recorded weights have no split (`start`, the line starts there; months before the
  mod recorded have none); after it, the last weights carry forward over gaps (recording switched off for a while), and
  Now uses the latest weights. A service at 0 on every point with data is left out (`ServiceRows.Build`).
- The save codec is pure (`ISaveReader`/`ISaveWriter`, wrapped round the game's `IReader`/`IWriter` by `SaveReader` /
  `SaveWriter` in `ServiceUpkeepSystem.cs`, which are classes so a struct reader advances in one place), so the format 1
  load is tested. A future per-district service budget % changes the weights only; `Split` takes any weights.

### Bindings

`BudgetHistoryUISystem` publishes `districtBudgetEnabled` (recording on: `FeatureGates.RecordsDistrictBudget`); the
list, `districts` (`DistrictChoice[]` = `{ key, name }`, sorted by name), is `DistrictListUISystem`'s, shared with the
Services tab and `[]` while no district feature is on (`FeatureGates.PublishesDistrictList`). It takes `setDistrict(key)`: `""` = Entire City, `"outside"`, or `"<index>:<version>"`; an
unknown key falls back to Entire City. `history` then carries `district` (`""` for Entire City) and `nowIncome[14]` /
`nowExpense[15]` (a district's live values, expenses positive; `null` for Entire City: every view writes the same
properties, `BudgetHistoryPayload`) with series from the district's samples; city-only sources are 0. Every view also
writes `services`: `[{ name, start, values }]` (`FiscalOverhaul.ServiceUpkeep`, never null, `[]` without data), the
Service Upkeep row split per service at the same points, in vanilla's Services tab order (`UIObjectData.m_Priority`).

### District selector

One picker for the Budget, Taxation and Services tabs, in the **Economy panel's header** at the left of the tabs
(`economy/economy-tab-bar.tsx`, drawn by the extended vanilla `TabBar`; the logic in `economy/header-picker.ts`). The
tabs follow it: [Budget | Taxation | Services], a divider line, [Production | Loans] (`withTabGroupDivider`, by tab id).
It shows on the selected tab only by that tab's own rule (`headerPicker`: Budget while recording is on, Services while
district service budgets are on, Taxation while the tax engine charges and the city has a district) and its choices
(Taxation has no Outside Districts: a shared choice of Outside shows the city there and stays for the other tabs). On
Production and Loans, or a tab whose feature is off, its slot stays empty so the tabs never move; with every district
feature off there is no slot (`reservesPickerSlot`). The city option is the **city's name** (vanilla
`toolbarBottom.cityName`; `cityLabel` falls back to "Entire City" while it is blank).

`district/district-selector.tsx` (`compact` in the header: 18rem name, clipped with an ellipsis): `‹ [name] ›`, span buttons on the rows' value-field box
(vanilla `NumberField`'s secondary background, `rgba(6,10,16,.7)` with 8rem corners: `$value-field-bg`), so the name
and both arrows read as controls; the name is bold (scoped under `.district-selector` so the later
`.span-button` rule doesn't shrink it). The arrows are the vanilla `ThickStrokeArrowRight` glyph, mirrored for the
left one (the font has no arrows). They cycle Entire City → each district (by name) → Outside Districts → Entire
City, wrapping both ways; the name opens a list of every choice, with a divider line between the three groups (Entire
City | the districts | Outside Districts; `dividerBefore`). The list's items sit in vanilla's `Scrollable`, capped at
520rem, so a long list gets the game's scroll bar instead of running past the panel. It shows whenever recording is on (`districtBudgetEnabled`, `showsDistrictSelector`), even in a city without
districts, so the per-district view can be found: then Entire City is the only choice and the arrows and the list
stay on it. With **Record district budgets** off it hides (vanilla behaviour). A choice no
longer offered falls back to Entire City (`effectiveKey`). The choice persists for the session (module-level, like
the span) and is sent on mount and on change.

With a district in `history` (`isDistrictView`; rows follow the binding, not the click, so rows and lines always
match): the city-only rows hide (`isCityOnlyItem`: every source outside `DistrictBudgetMath`'s lists: loan interest,
map tile upkeep, imports, exports, government subsidy), each row reads `nowIncome` / `nowExpense` (`rowValue`,
expenses negated to show as vanilla does), Revenues / Expenses / Monthly balance are the sums of the shown rows, the
detail pop-out is off (its breakdown is city-wide), and the chart draws the district's series. A district with no
samples yet has one point (its live Now), which the chart draws as a dot. A blank or localization-id name (an
unnamed district from `NameSystem.GetRenderedLabelName`) is translated, else shown as "District <n>" (its place in
the list; `districtLabel`). The pure logic is `district/district-select.ts` and `economy/header-picker.ts`.

## UI

- **Left column**: just the filter rows, inside vanilla's `Scrollable` (`.list-scroll`, `flex: 1 1 0; min-height: 0`
  under a `min-height: 0` flex chain), so expanded categories that overflow get the game's scroll bar; plain
  `overflow: auto` scrolled with no bar. There is no right-hand column, so
  the chart takes the rest of the width.
- **Spacing**: the page adds no side padding of its own (the economy panel's 32rem is the margin, as on the other
  Economy tabs), and rows and sections are kept tight.
- **Sounds**: every click that changes something plays a vanilla UI sound (`UI/src/sounds.ts`): a row toggles its line
  (`select-toggle`), a new span (`select-item`, none for the current one), the arrows (`select-previous-item` /
  `select-next-item`), opening the list (`select-dropdown`), a pick from it (`select-item`).
- **Rows**: Revenues total, each active revenue item, Expenses total, each active expense item, Monthly
  balance. Each row shows the vanilla current value (`NumberField`). Clicking a row toggles its line, and hovering it
  sets the detail. Items group sources exactly as vanilla does (`BudgetItem.sources`), from
  `economyBudget.incomeItems$`. **`incomeValues$`/`expenseValues$` are indexed by source, not by item**: an item's
  value is the sum of `values[source.index]` over its sources (`itemValue`, vanilla's `getItemValue`); indexing by the
  item's position shows the wrong numbers. The checked set and span persist for the session (module-level variables).
- **Subcategory rows** (`UI/src/budget/budget-rows.ts`): a category with more than one source (in the current view)
  has an arrow that expands it into one indented row per source, labelled `EconomyPanel.BUDGET_SUB_ITEM[<source id>]`
  (vanilla's `BudgetItemDetail` names), valued `values[source.index]`, with its own checkbox and line
  (`income:<itemId>:<sourceId>`, from `history.income[source.index]`; `buildSourceSeries`). Child colours are the
  parent's shaded lighter/darker (`childColor`), distinct from it and each other. A single-source category gets no
  arrow (`EXPAND_SINGLE_SOURCE`, provisional); totals and the balance never do. A district view hides city-only
  sources, the `isCityOnlyItem` rule per source. The expanded set persists like the checked set; expanding plays
  `expand-panel`. Hovering a child row pops out its category's detail.
- **Service rows** (`UI/src/budget/service-rows.ts`): the expense item holding Service Upkeep (a single source, so no
  source rows) expands into one row per service in `history.services`, labelled with vanilla's Services tab name
  `Services.NAME[<prefab name>]`, valued minus its newest point, keyed `expense:<itemId>:service:<name>`, coloured after
  any source rows. Its line (`buildServiceSeries`) starts at the service's `start`: `SeriesDef`/`ChartLine` `start`,
  which the chart skips when drawing, scaling (`shownValues`, `valueRange`), hit-testing (`nearestSeries`) and the span
  stats.
- **Chart (right)**: canvas line chart (`chart/line-chart.tsx`), span buttons 1M/3M/6M/1Y/3Y/All, opening on 3M (`DEFAULT_SPAN_MONTHS` / `BudgetHistoryMath.DefaultSpanMonths`). Expenses are plotted as
  positive so they compare against revenue on one axis; the balance line can go negative (the zero line is
  brighter). Hover picks the nearest sample on x, then the line nearest the cursor at that x
  (`nearestIndex`/`nearestSeries`). The tooltip shows that line's exact monthly value (signed, "¢57,466/mo" from `formatPerMonth` as one text
  node, plus `nowrap`: Gameface shrinks an absolutely positioned box, and wraps between React's text nodes even
  under `nowrap`) and how long ago, next to the cursor. The left margin is the widest
  y label plus 10rem, measured on the canvas, so there is no gap before the labels.
  The x axis has a tick and a faint vertical grid line (fainter than the horizontal ones) at every whole week, month
  or year back from now (`timeTicks`: a week is a quarter month; the step is the smallest that fits the width,
  ~120rem per label), labelled "-3 wk", "-1 mo 2 wk", "-2 yr" (`formatMonthsAxis`). Canvas text is 15rem.
  Every canvas size is in rem and scaled by the measured px-per-rem
  ([cs2-ui-modding.md § Gameface](../cs2-ui-modding.md#gameface-the-games-browser-limits)): unscaled, labels were
  unreadable; scaled by the root font size, the chart drew off-canvas.
- **Detail pop-out**: hovering an item row floats the vanilla `BudgetItemDetail` (breakdown by source) over the
  chart's left edge, next to the rows, with `pointer-events: none`. A line hover has no pop-out (its tooltip is
  enough). Totals have no vanilla detail. The pop-out always runs the chart panel's full height
  (`top` and `bottom` both set: a percentage `max-height` alone didn't constrain it in Gameface, and it ran below the
  panel), and while it shows, the plot starts after it (`LineChart insetLeftRem`): the y axis moves right and the x
  axis shrinks, so nothing is covered.
- **Span stats**: for the hovered row or line, **Change** and **Average** over the selected span, per month
  (`seriesStats`; no title), side by side on one dark box at the plot's top left: `LineChart`'s `overlay` slot, placed
  at the plot's left padding (after the y labels and any pop-out inset) and top padding, so it clears the y labels, the
  span buttons (above the chart area) and the pop-out. It takes no pointer events, and a line's tooltip draws over it.
  Now, Low and High are gone: Now is the row's value, the rest the line and its tooltip.

## Load time

The chart should draw within a frame or two of the tab mounting. What happens on a mount:
- **Data is there at once.** `useValue(history$)` subscribes, and `RawValueBinding.OnSubscribe` writes the history
  synchronously, for the span and district the C# side last had; the UI's `setSpan`/`setDistrict` on mount and the
  panel's first `OnUpdate` rebuild it the next frame.
- **The size is not.** Gameface lays out once a frame, so the just-mounted chart reads 0 wide in its layout effect and
  can't draw. It used to retry only on its 500 ms resize poll or the next history rebuild (every 60 frames), so it drew
  up to a second late. `LineChart` now measures every animation frame until it has a size and for
  `FAST_MEASURE_FRAMES` after mount (the panel settling), then polls every 500 ms (`UI/src/chart/load-timing.ts`
  `nextMeasure`). A draw resizes the canvas only when the size changed (a resize reallocates it).
- **Timings in Mod.log.** `BudgetHistoryUISystem` writes `BudgetChart: build <n> compute <ms>, write <ms>; <samples>,
  <points>, <series>, <services>, view <key>` for the first `BudgetLoadTiming.BuildsPerOpen` builds after each
  subscribe (`kLogEveryBuild` logs all), and the chart sends one line per mount through the `logTiming` trigger:
  `BudgetChart UI: data +<ms>, size +<ms>, draw +<ms>; draw took <ms>, <n> draws, <n> measures, <w>x<h> px, rem <r>,
  <lines> lines x <points> points` (milliseconds since the chart mounted; also on the UI console).

## Tests

`Tests/BudgetHistoryMathTests.cs` covers the C# maths, `Tests/BudgetLoadTimingTests.cs` and
`UI/src/chart/load-timing.test.ts` the load timings and the per-frame measuring, `Tests/FeatureGatesTests.cs` the on/off gates, `Tests/DistrictBudgetMathTests.cs` the district split. `UI/src/chart/chart-math.test.ts` covers series building,
ranges, value and time ticks, hit-testing and formatting; `UI/src/district/district-select.test.ts` and `UI/src/economy/header-picker.test.ts` the selector's
options, when it shows, cycling, names, city-only rows and row values; `UI/src/budget/budget-rows.test.ts` the
subcategory rows (keys, which categories expand, district filtering, colours, values and series);
`Tests/ServiceUpkeepSplitTests.cs` the per-service split (exact sums, rounding, zero / one / negative inputs, gaps,
alignment, the save codec and a format 1 load) and `UI/src/budget/service-rows.test.ts` the service rows and lines
with a `start`.
