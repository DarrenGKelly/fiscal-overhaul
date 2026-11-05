# Changelog

All notable changes to Fiscal Overhaul are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the mod uses [Semantic Versioning](https://semver.org/).
The version lives in `UI/mod.json` and `Mod/Properties/PublishConfiguration.xml` (`ModVersion`, with that release's
`ChangeLog`); keep both in step with the newest entry here.

## [Unreleased]

## [1.0.0] - 2026-11-04

First public release, for Cities: Skylines II 1.6.x.

### Added

- **Budget tab:** the bar chart is replaced by a line chart of total revenue, total expenses, the monthly balance and
  each budget item, as per-month rates, from the game's own budget statistics (so a loaded city shows its whole
  history).
- Series filters: click a row to show or hide its line.
- Time spans of 1 month, 3 months, 6 months, 1 year, 3 years and all history.
- A tooltip with the hovered line's exact monthly value and how long ago it was, and the span's now / average / low /
  high / change under the rows.
- The vanilla detail panel (breakdown by source) when hovering a budget item.
- **Taxation tab (BETA, off by default; enable it in Options > Fiscal Overhaul):** a tax offset per building density under each zone (residential low / medium / high, commercial
  low / high, office low / high, industrial), capped at ±5 points, with an estimate of the revenue it adds. Offsets
  change tax revenue only, not demand or happiness, and are a global mod setting, not stored in saves.
- **Options:** Enable budget chart (on by default) and Enable density taxes (BETA, off by default); turning one off restores
  the vanilla tab, and Reset density tax offsets.

[Unreleased]: https://github.com/DarrenGKelly/fiscal-overhaul/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/DarrenGKelly/fiscal-overhaul/releases/tag/v1.0.0
