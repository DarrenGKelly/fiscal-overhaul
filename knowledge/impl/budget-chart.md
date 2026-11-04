---
type: Implementation
title: Budget tab line chart
description: The replacement Budget tab — where its history data comes from (the game's own per-source statistics), how it is turned into monthly series, and how the filter rows, chart, tooltip and detail column fit together.
resource:
  - Mod/Systems/BudgetHistoryUISystem.cs
  - Mod/Logic/BudgetHistoryMath.cs
  - UI/src/budget
  - UI/src/chart
---

# Budget tab line chart

The vanilla Budget tab (bar chart + revenue/expense lists + detail) is replaced wholesale with
`moduleRegistry.override(".../budget-page/budget-page.tsx", "BudgetPage", BudgetPage)`.

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

**The newest sample is still filling up** (the current 1/32 of a month), so `ReadMonthly` drops it; with it, every
line dipped at "Now". In its place `WithLiveNow` appends the live value (`CityServiceBudgetSystem.GetIncome` /
`GetExpense`, what the rows show), so "Now" equals the row by construction. The last saved sample alone read ¢268k of
expenses against the row's ¢284,785 in the playtest. The binding rebuilds on a new sample, a new span, or every 60
frames while the panel is open, so "Now" follows the rows even while the game is paused.

It sends only the visible span (`setSpan(months)`, 0 = all), downsampled to ≤240 points ending on the newest sample
(`PickIndices`), as `history = { span, samples, monthsAgo[], income[14][], expense[15][] }`. Expenses are positive
amounts. It rebuilds only while the Economy panel is open and when the sample count or span changed.

## UI

- **Left column**: the filter rows, then the span stats for the hovered row or line (the column scrolls when they
  don't fit). There is no right-hand column, so the chart takes the rest of the width.
- **Rows**: Revenues total, each active revenue item, Expenses total, each active expense item, Monthly
  balance. Each row shows the vanilla current value (`NumberField`). Clicking a row toggles its line, and hovering it
  sets the detail. Items group sources exactly as vanilla does (`BudgetItem.sources`), from
  `economyBudget.incomeItems$`. **`incomeValues$`/`expenseValues$` are indexed by source, not by item**: an item's
  value is the sum of `values[source.index]` over its sources (`itemValue`, vanilla's `getItemValue`); indexing by the
  item's position shows the wrong numbers. The checked set and span persist for the session (module-level variables).
- **Chart (right)**: canvas line chart (`chart/line-chart.tsx`), span buttons 1M/3M/6M/1Y/3Y/All. Expenses are plotted as
  positive so they compare against revenue on one axis; the balance line can go negative (the zero line is
  brighter). Hover picks the nearest sample on x, then the line nearest the cursor at that x
  (`nearestIndex`/`nearestSeries`). The tooltip shows that line's exact monthly value (signed, "¢57,466/mo", `nowrap`: Gameface shrinks an
  absolutely positioned box and wrapped it) and how long ago, next to the cursor. The left margin is the widest
  y label plus 10rem, measured on the canvas, so there is no gap before the labels.
  The x axis has a tick and a faint vertical grid line (fainter than the horizontal ones) at every whole week, month
  or year back from now (`timeTicks`: a week is a quarter month; the step is the smallest that fits the width,
  ~120rem per label), labelled "-3 wk", "-1 mo 2 wk", "-2 yr" (`formatMonthsAxis`). Canvas text is 15rem.
  Every canvas size is in rem and scaled by the measured px-per-rem
  ([cs2-ui-modding.md § Gameface](../cs2-ui-modding.md#gameface-the-games-browser-limits)): unscaled, labels were
  unreadable; scaled by the root font size, the chart drew off-canvas.
- **Detail pop-out**: the vanilla `BudgetItemDetail` for the hovered item (breakdown by source) floats over the
  chart, on the side away from the cursor (`popoutSideIsLeft`), with `pointer-events: none` so it never steals the
  chart's hover. Totals have no vanilla detail. The span stats (now / average / low / high / change) sit under the rows.

## Tests

`Tests/BudgetHistoryMathTests.cs` covers the C# maths. `UI/src/chart/chart-math.test.ts` covers series building,
ranges, value and time ticks, hit-testing and formatting.
