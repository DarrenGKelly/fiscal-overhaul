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

`BudgetApplySystem` (every 8192 frames = 32 times per in-game month) enqueues a `StatisticsEvent` per income and
expense source: `StatisticType.Income`/`Expense`, parameter = `IncomeSource`/`ExpenseSource` index, change =
that source's monthly amount / 32. `CityStatisticsSystem` appends one `CityStatistic` per sample and **saves
them in the save game**, so a loaded city has its whole history.

`BudgetHistoryUISystem` reads each source's buffer (`GetStatisticArray(type, source)`), takes each sample's
`m_Value` (what was added in that sample), and `BudgetHistoryMath.MonthlyRates` sums the trailing 32 samples, which gives the same "per month"
figure the vanilla panel shows. It doesn't use `m_TotalValue`, whose meaning depends on the statistic's collection
type (cumulative / point / daily). The deltas mean the same thing for every type.

It sends only the visible span (`setSpan(months)`, 0 = all), downsampled to ≤240 points ending on the newest sample
(`PickIndices`), as `history = { span, samples, monthsAgo[], income[14][], expense[15][] }`. Expenses are positive
amounts. It rebuilds only while the Economy panel is open and when the sample count or span changed.

## UI

- **Rows (left)**: Revenues total, each active revenue item, Expenses total, each active expense item, Monthly
  balance. Each row shows the vanilla current value (`NumberField`). Clicking a row toggles its line, and hovering it
  sets the detail. Items group sources exactly as vanilla does (`BudgetItem.sources`), from
  `economyBudget.incomeItems$`. The checked set and span persist for the session (module-level variables).
- **Chart (middle)**: canvas line chart (`chart/line-chart.tsx`), span buttons 1M/3M/6M/1Y/3Y/All. Expenses are plotted as
  positive so they compare against revenue on one axis; the balance line can go negative (the zero line is
  brighter). Hover picks the nearest sample on x, then the line nearest the cursor at that x
  (`nearestIndex`/`nearestSeries`). The tooltip shows that line's exact monthly value (signed) and how long ago, next to the cursor.
- **Detail (right)**: the vanilla `BudgetItemDetail` for the hovered row or line (item breakdown by source), plus
  span stats for the hovered line (now / average / low / high / change). Totals have no vanilla detail.

## Tests

`Tests/BudgetHistoryMathTests.cs` covers the C# maths. `UI/src/chart/chart-math.test.ts` covers series building,
ranges, ticks, hit-testing and formatting.
