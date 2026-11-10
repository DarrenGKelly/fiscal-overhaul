# Changelog

All notable changes to Fiscal Overhaul are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the mod uses [Semantic Versioning](https://semver.org/).
The version lives in `UI/mod.json` and `Mod/Properties/PublishConfiguration.xml` (`ModVersion`, with that release's
`ChangeLog`); keep both in step with the newest entry here.

## [Unreleased]

## [1.3.0] - 2026-11-09

### Added

- **One district picker for three tabs:** a picker at the left of the Economy panel's tabs, framed with Budget,
  Taxation and Services, chooses the city, one district or Outside Districts for all three. Long district names fit.
- **Tax rates per district:** each district (and Outside Districts) can set its own zone, education, resource and
  density rates; a district without its own rate follows the city's. The Taxation tab shows the district's own
  estimates, and demand, happiness and the homes households choose react to its rates.
- **Services tab as a table:** one row per service with its budget slider and its cost, a row for each fee with its
  revenue, an estimated total, reset buttons and default markers, and the game's info column for whatever you hover.
  Turn it off with **Enable services table** to get the vanilla tab back.
- **District service budgets (BETA, off by default):** a district can run a service at its own budget, from 50% to
  150%, changing its buildings' efficiency and upkeep.
- **Simulation changes** switch in Options: off, the game charges its own taxes and every service follows the city's
  slider again, with your rates and settings kept.
- Budget tab: collapsible categories, Service Upkeep broken down by service, and group checkboxes that turn a
  category and all its items on or off together.

### Changed

- Density taxes are now charged as a real tax: they pay the city (a negative rate is booked as a subsidy), and
  demand, happiness and home choice react to them. Density and district rates are kept in each save.
- The Economy tabs are ordered Budget, Taxation, Services, Production, Loans.
- Taxation: Education/Resources and Density are collapsible sections, density rows look like the game's own rows,
  and every row has a reset button and a marker at its default.
- The Options page is grouped into Features, Simulation changes and Reset.
- The budget chart draws as soon as the tab opens.
- New store page screenshots and description.

### Fixed

- Taxation estimates match what the city collects (they read 5-7% high on small incomes), and are right from the
  first moments after loading a save instead of starting low.
- The district list closes when you click outside it or switch tabs.

## [1.2.0] - 2026-11-06

### Changed

- The district selector is always at the top of the Budget tab while district recording is on, also in a city
  without districts (it then shows Entire City). Its name and arrows sit on a dark box, like the value fields.
- The span stats are now Change and Average, on one line at the top left of the chart.
- The chart's top gridline is always labelled and at or above every line's peak, and a negative balance gets a
  labelled gridline at or below it.
- New store page screenshots, with every budget line shown.

## [1.1.0] - 2026-11-06

### Added

- **Per-district budget:** a selector at the top of the Budget tab shows the chart and the rows for the Entire City,
  any one district, or Outside Districts (everything not in a district). Step through them with the arrows either
  side, or click the name to pick from a list, which scrolls when a city has many districts.
- District taxes are what that district's buildings pay, at the district's own rates, so district policies such as
  Low Commercial Tax count. Service fees and upkeep are shared out by each district's use. City-wide items (loan
  interest, map tile upkeep, trade, subsidies) show under Entire City only.
- District history is recorded from the first time the mod runs on a save, and kept in the save. A save still loads
  without the mod.
- Click sounds on the Budget and Taxation tabs, matching the game's own panels.

### Changed

- The Budget tab's padding matches the other Economy tabs, so the span stats under the rows no longer get cut off.
- The selected district's name is the tab's header.

## [1.0.0] - 2026-11-05

First public release, for Cities: Skylines II 1.6.x.

### Added

- **Budget tab:** the bar chart is replaced by a line chart of total revenue, total expenses, the monthly balance and
  each budget item, as per-month rates, from the game's own budget statistics (so a loaded city shows its whole
  history).
- Series filters: click a row to show or hide its line.
- Time spans of 1 month, 3 months, 6 months, 1 year, 3 years and all history; the chart opens on 3 months.
- A tooltip with the hovered line's exact monthly value and how long ago it was, and the span's now / average / low /
  high / change under the rows.
- The vanilla detail panel (breakdown by source) when hovering a budget item.
- **Taxation tab (BETA, off by default; enable it in Options > Fiscal Overhaul):** a tax offset per building density under each zone (residential low / medium / high, commercial
  low / high, office low / high, industrial), capped at ±5 points, with an estimate of the revenue it adds. Offsets
  change tax revenue only, not demand or happiness, and are a global mod setting, not stored in saves.
- **Options:** Enable budget chart (on by default) and Enable density taxes (BETA, off by default); turning one off restores
  the vanilla tab, and Reset density tax offsets.

[Unreleased]: https://github.com/DarrenGKelly/fiscal-overhaul/compare/v1.3.0...HEAD
[1.3.0]: https://github.com/DarrenGKelly/fiscal-overhaul/compare/v1.2.0...v1.3.0
[1.2.0]: https://github.com/DarrenGKelly/fiscal-overhaul/compare/v1.1.0...v1.2.0
[1.1.0]: https://github.com/DarrenGKelly/fiscal-overhaul/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/DarrenGKelly/fiscal-overhaul/releases/tag/v1.0.0
