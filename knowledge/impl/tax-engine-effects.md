---
type: Implementation
title: Tax engine effects
description: What the tax engine's rates change besides revenue — demand through a rate view handed to the demand systems, citizen happiness at each home's rate (a copy of CitizenHappinessSystem's update) and households choosing homes by each home's rate (a copy of HouseholdFindPropertySystem's update), all through TaxRateResolver.
resource:
  - Mod/Systems/DemandTaxViewSystem.cs
  - Mod/Systems/TaxHappinessSystem.cs
  - Mod/Systems/TaxFindPropertySystem.cs
  - Mod/Systems/TaxRateResolver.cs
  - Mod/Logic/DemandViewMath.cs
  - Mod/Logic/DemandSwapLogic.cs
  - Mod/Logic/TaxHappinessMath.cs
  - Mod/Logic/TaxPropertyScoreMath.cs
  - Mod/Logic/TaxRateModel.cs
---

# Tax engine effects

The [tax engine](tax-engine.md) charges each payer its own rate. Three more systems react to those rates, and every
one gets them from **`TaxRateResolver`** ([tax-engine.md § The rate model](tax-engine.md#the-rate-model)), never adding
density or district adjustments itself. A resident's rate is `ResidentRate`: the city's rate for their education
level plus the home district's difference **at that level only** plus the home's density tax, inside the job-level
limits; so a district's Highly Educated override moves only highly educated residents, and with every value at the
city's each effect is exactly vanilla. Each runs only while the engine charges (`TaxEngineMode.Engine`).

## Demand

Vanilla demand reads only city-level rates: Residential averages `10 − rate` over the five education levels
(`ResidentialDemandSystem.cs:116-120`), Commercial and Industrial/Office use each resource's rate
(`CommercialDemandSystem.cs:180`, `IndustrialDemandSystem.cs:402`). The demand systems are not copied; they are handed
a second rate array, the **view**:

- `DemandTaxViewSystem` (`Mod/Systems/`, every frame, `UpdateAfter<…, TaxEngineSystem>`) sums every payer's
  adjustment per rate slot every **4096 frames** (a single-threaded job, read back the next update). **Each payer
  weighs 1** (provisional: untaxed income is zeroed at every charge, so it is a noisy weight). A household counts in
  all five education slots (5..9), each with **its home's rate for that level** minus the city's
  (`ResidentRate` − `CityResidentRate`); a company counts in its output resource's slot (10+r commercial, 51+r
  industrial, flagged Office when the output weighs nothing) with its charged adjustment
  (`DemandViewMath.PayerAdjustment` of the resolver's `Adjustments`). Payers with no property count with adjustment 0.
- `DemandViewMath.Compose` (`Mod/Logic/`, tested): view = vanilla's array with each leaf slot that has payers moved by
  its payers' mean adjustment (halves to even, as `math.round`), the slot's full rate clamped like `EffectiveTaxRate`
  (job-level limits for residential, resource limits otherwise; never past vanilla). Slots 0..4 and payer-less slots stay
  vanilla, so **all-zero adjustments give vanilla's array**. It is recomposed every frame from the live vanilla rates and
  written only when it changes, after completing the view's readers (vanilla's `AddReader` pattern).
- **The swap**: each demand system's private `m_TaxSystem` is pointed (reflection) at a `DemandTaxView : TaxSystem`.
  The demand systems call only the non-virtual `GetTaxRates()` (returns the instance's private `m_TaxRates`, set by
  reflection to the view) and `AddReader()` on it. The view is built with `FormatterServices.GetUninitializedObject`,
  never through the World: no constructor or `OnCreate`, never updated, and **never saved** — the game's
  `SystemSerializerLibrary.Initialize` (Colossal.Core) serializes only systems in `World.Systems`, keyed by exact type.
  On creation the system checks a sentinel written to the view reads back through `GetTaxRates()` and that the view is
  not in `World.Systems`, and logs `DemandTaxView: structure ok`. **Each demand system declares its own private
  `m_TaxSystem`**: a `FieldInfo` from one type throws `ArgumentException` on another, so `DemandSwapLogic.ResolveFields`
  takes one per type and `FitsInstances` checks each against its system instance (both tested).
- **Off**: whenever the engine's mode isn't `Engine` (Simulation changes off, guard blocked) the fields go back to
  vanilla TaxSystem, live. `TaxEngineGuard.DemandStructureOk` checks the three demand systems still have
  `m_TaxSystem : TaxSystem` and TaxSystem `m_TaxRates`; a mismatch leaves demand vanilla (the engine still charges). A
  field another mod re-pointed is left alone (logged once). A throw while swapping restores vanilla, logs one error and
  keeps demand vanilla for the session (`m_Faulted`), never a throw per frame. Each change of what demand reads logs once:
  `demand reads the adjusted view` / `demand reads vanilla rates` (the latter also on the first update with the engine off).

## Happiness

The tax happiness factor (a user decision) follows each citizen's own rate, so a district's rates have a real
effect (unhappy households move out through vanilla `HouseholdBehaviorSystem`).

Vanilla `CitizenHappinessSystem` computes each citizen's wellbeing every 16 frames (16 `UpdateFrame` groups) and keeps
the city's happiness factors (`m_HappinessFactors`, 26 factors × 16 frames, saved by vanilla and read by the city info
UI through `GetHappinessFactor`). Its tax bonus is `GetTaxBonuses(educationLevel, taxRates, …)`: the city's residential
rate for the education level, `(10 - rate)`, the city's `TaxHappiness` modifier, times the level's (negative)
`CitizenHappinessParameterData.m_Tax*Multiplier`.

- `TaxHappinessSystem` (`Mod/Systems/`) is a copy of its update: `CitizenHappinessJob`, `HappinessFactorJob`, `OnCreate`
  and `OnUpdate` and three private helpers (headers name each source range, game version 1.6.3f1; changes are marked
  `CHANGED`/`ADDED`; vanilla's debug distribution is left out). The public static `Get*Bonuses` helpers are called, not
  copied. `UpdateBefore<…, CitizenHappinessSystem>` with its interval (16), so it inherits its offset.
- **The one change**: the tax bonus is at the citizen's **home rate**, `TaxRateResolver.ResidentRate` for their
  education level and their household's `PropertyRenter.m_Property`. When that equals the vanilla rate (no override
  for the level, no density tax, or no home) the job calls vanilla's own `GetTaxBonuses`, so city values are exactly
  vanilla; otherwise `TaxHappinessMath.Bonus` (`Mod/Logic/`, tested: a port of `GetTaxBonuses` with the rate and
  modifier passed in).
- Vanilla stays the **data store**: the copy writes vanilla's private `m_HappinessFactors` (read by reflection once) and
  sets vanilla's private `m_LastDeps` to its own handle each update, so `GetHappinessFactor` waits for it. Its first job
  also waits on vanilla's last handle (the switch-over update).
- **When it runs**: exactly while the tax engine charges, `TaxEngineGuardLogic.RunsHappiness(engineMode, structureOk,
  foreignDisabled)` (tested). While it runs `CitizenHappinessSystem.Enabled = false`; anything else turns it back on,
  live (every update, and `Mod.ApplyFeatureSwitches` after the engine's `ApplyMode`). The structure check
  (`TaxEngineGuard.HappinessStructureOk`) confirms the two private fields' types and the array's length (416); if
  vanilla was already disabled by someone else, it backs off for the session. Mod.log: `TaxHappiness: structure …` once
  and `TaxHappiness: running|vanilla (…)` on each change.

**Known limits** (still the city rate): the building happiness shown in the building info panel
(`GetBuildingHappinessFactors`, CitizenHappinessSystem.cs:1817) and company factors (:1591), which `PropertyUtils` and
the UI call directly; household net income (vanilla's income estimates elsewhere use the city rate); companies
moving away (`CompanyMoveAway`); the Taxes factor in the city info panel is the city average of the per-citizen
bonuses, so it moves with the share of citizens under a district or density rate.

## Where people move

Households prefer lower-tax homes (a user decision: the cheaper partial of per-district demand, with district
happiness above and an info-only district demand figure).

Vanilla `HouseholdFindPropertySystem` (every 16 frames) starts a pathfind for a household looking for a home, then
compares the best candidate found against its current home with `PropertyUtils.GetPropertyScore` (`:533` current home,
`:602` candidate) and moves, stays or leaves. The score's tax term is `2 ×` the adults' average tax happiness bonus
(`PropertyUtils.cs:511-560`), at the **city** rate.

- `TaxFindPropertySystem` (`Mod/Systems/`) is a copy of its update: `PreparePropertyJob`, `FindPropertyJob`, `OnCreate`
  and `OnUpdate` (headers name each source range, game version 1.6.3f1; changes marked `CHANGED`/`ADDED`; vanilla's
  debug counters and distributions are left out). Vanilla's public structs, statics and `PropertyUtils` are called,
  not copied. `UpdateBefore<…, HouseholdFindPropertySystem>` with its interval (16), so it inherits its offset.
- **The one change**: both scores go through `Score`: vanilla's `GetPropertyScore`, then its tax term recomputed at the
  home's rate for each adult's education level (`TaxRateResolver.Locate` once per home, then `ResidentRate` per adult)
  and the difference added (`TaxPropertyScoreMath`, `Mod/Logic/`, tested: vanilla's integer average and weight 2). A
  home the resolver adds nothing to, and a shelter (park or abandoned building: its residents are homeless and pay the
  vanilla rate), score exactly as vanilla. So a +5 home loses against an otherwise equal 0 home, and its residents
  look for a better one sooner.
- **When it runs**: exactly while the tax engine charges, `TaxEngineGuardLogic.RunsHomeChoice(engineMode,
  foreignDisabled)` (tested). While it runs `HouseholdFindPropertySystem.Enabled = false`; anything else turns it back
  on, live (every update, and `Mod.ApplyFeatureSwitches` after the engine's). It touches no private vanilla state (only
  vanilla's debug counters are private and read elsewhere), so there is no structure check of its own; if vanilla was
  already disabled by someone else, it backs off for the session.
- Mod.log: `TaxHomeChoice: running|vanilla (…)` on each change, and about every 512 updates
  `TaxHomeChoice: N of M home scores moved by the home's tax rate (summed delta D)` (D < 0: homes scored lower).

**Known limits**: the candidates the pathfinder offers are pre-scored in `CitizenPathfindSetup.SetupFindHome` (inside
`PathfindSetupSystem`, which can't be replaced alone) at the city rate, so which home is found first ignores the
district and density rates; only the final current-vs-candidate comparison sees them. The household income it checks
against rent (`EconomyUtils.GetHouseholdIncome`) is still at the city rate.

## Follow-ups

- Check in game that +5 on every residential density lowers the residential demand tax factor, and that all at the
  city's or Simulation changes off gives vanilla demand.
- Check in game that the happiness Taxes factor with no density tax and no override equals vanilla's, and that a
  district's higher rate lowers its residents' wellbeing over a couple of in-game days.
- Check in game that households leave or avoid +5 high-density residential faster than untaxed buildings, and that
  `TaxHomeChoice` lines in Mod.log show moved scores only while a density tax or district rate is set.
