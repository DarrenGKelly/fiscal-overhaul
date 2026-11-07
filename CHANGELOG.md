# Changelog

All notable changes to Fiscal Overhaul are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the mod uses [Semantic Versioning](https://semver.org/).
The version lives in `UI/mod.json` and `Mod/Properties/PublishConfiguration.xml` (`ModVersion`, with that release's
`ChangeLog`); keep both in step with the newest entry here.

## [Unreleased]

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

[Unreleased]: https://github.com/DarrenGKelly/fiscal-overhaul/compare/v1.2.0...HEAD
[1.2.0]: https://github.com/DarrenGKelly/fiscal-overhaul/compare/v1.1.0...v1.2.0
[1.1.0]: https://github.com/DarrenGKelly/fiscal-overhaul/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/DarrenGKelly/fiscal-overhaul/releases/tag/v1.0.0
