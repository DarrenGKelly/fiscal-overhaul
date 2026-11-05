---
type: Reference
title: CS2 modding
description: How a Cities Skylines II mod is built here, where the game's logs, code and UI modules are, how to find and reuse vanilla components and bindings, and the traps (deploy order, runtimes, launching, Gameface limits and rem scale).
resource:
  - Mod/FiscalOverhaul.csproj
  - UI/webpack.config.js
  - UI/src/vanilla.ts
---

# CS2 modding

The mod has two halves that deploy into the same folder, `%CSII_USERDATAPATH%\Mods\FiscalOverhaul`
(`C:\Users\<you>\AppData\LocalLow\Colossal Order\Cities Skylines II\Mods\FiscalOverhaul`):

- **`Mod/`**: a C# (net48, C# 9) project from the official `dotnet new csiimod` template. Building runs the game's
  **ModPostProcessor** (Burst AOT for Windows/macOS/Linux, versioning) and then `DeployWIP` copies the output.
- **`UI/`**: a TypeScript/React module from the official UI template (`Cities2_Data/Content/Game/.ModdingToolchain/npx-create-csii-ui-mod`).
  Webpack writes `FiscalOverhaul.mjs`/`.css` straight into the Mods folder. `UI/types/` holds the game's type
  definitions for `cs2/*` modules, copied from that template. Refresh them after a game patch.

The game loads mods **only at startup**. To see a change in the game, rebuild and then restart the game. A running
game locks the mod DLL, so a C# deploy fails until it is closed (a UI-only build still copies, but isn't loaded).
**Start it through Steam** (`steam://rungameid/949230`): `Cities2.exe` started directly logs "A platform service
integration failed to initialize" and hangs at startup.

## Building

`tools/build.ps1` does it all. By hand:

```
dotnet build Mod/FiscalOverhaul.csproj -c Release   # C#, post-process, deploy, then the UI (BuildUIAfterDeploy)
npm run build --prefix UI                           # UI only
```

Traps:
- **Deploy order.** `DeployWIP` deletes and recreates the mod folder, which wipes the UI module. So
  `FiscalOverhaul.csproj` rebuilds the UI after every deploy (`BuildUIAfterDeploy`; skip with `-p:SkipUI=true`).
  An UI-only build never needs the C# build.
- **Runtimes.** The ModPostProcessor is a .NET **6** app, and the UI build needs Node. On this PC both are
  per-user installs in `%LOCALAPPDATA%\fiscal-overhaul-tools` (`node-v22.*-win-x64`, `dotnet6`), because the
  in-game toolchain's system installer stalled. `tools/build.ps1` puts them on `PATH`/`DOTNET_ROOT` when present.
  Without `DOTNET_ROOT` pointing at a .NET 6 install, the post-processor fails with "You must install or update .NET".
- **`CSII_*` env vars** are user-level variables the toolchain sets. A shell started before the toolchain was
  installed doesn't have them. `webpack.config.js` falls back to the default user data path; the C# build reads them from
  the registry itself (`Mod.props`).
- **UI type-check vs vitest.** vitest pulls in `@types/node`, which the template's TypeScript 4.8 can't parse, so
  `UI/tsconfig.json` sets `"types": []` and excludes `src/**/*.test.ts`; vitest type-checks the tests itself.
- **Linux builds on Windows.** The game also runs on the Linux PC under Proton; its Mods folder is
  `~/.steam/steam/steamapps/compatdata/949230/pfx/drive_c/users/steamuser/AppData/LocalLow/Colossal Order/Cities Skylines II/Mods`.
  The mod doesn't build there (the toolchain and Burst need the Windows Unity install), so `tools/build-linux.sh`
  builds on the Windows PC over SSH and copies the zip in. Over SSH the Windows PC can't fetch from GitHub (Git's
  credential manager and `gh` keep their logins in the desktop keyring), so the script feeds origin from a git bundle.
- **Tests** don't touch the game: pure logic lives in `Mod/Logic/` (no game or Unity types) and is compiled into
  `Tests/` (net10). UI pure logic is in `*.ts` files next to the components, tested with vitest.
  [testing.md](testing.md).

## Logs

Under `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II`:
- **`Player.log`** (the folder root, not `Logs/`): engine output and every exception, from startup on. Find the
  *first* error with `Select-String -Pattern "ERROR|Exception:" | Select-Object -First 10`; what follows it is usually
  the same error repeating every frame.
- **`Logs/Modding.log`**: `Registered UI Module {"m_ModuleId":"FiscalOverhaul"…}` proves the UI module loaded.
- **`Logs/UI.log`**: Gameface CSS warnings (`dashed is not supported`, `Unsupported CSS property`) from all mods; noise
  unless one names our class.
- A hung startup that never mentions `FiscalOverhaul` isn't the mod: "A platform service integration failed to
  initialize", then a `[FATAL]` in `AssetDatabase.PopulateFromDataSource`, then `GameManager` NullReferenceExceptions
  every frame, is the game started without Steam.

## Reading the game's code

- **C#**: decompile with `ilspycmd` (`dotnet tool install -g ilspycmd`):
  `ilspycmd -p -o <scratch dir> "$env:CSII_MANAGEDPATH\Game.dll"`. Decompile into a scratch folder, never into the repo.
  The useful namespaces are `Game.Simulation` (systems), `Game.UI.InGame` (UI systems and their bindings),
  `Game.Prefabs`, `Game.City`, `Game.Common/SystemOrder.cs` (where every system is scheduled).
- **UI**: the game's whole UI is one bundle, `Cities2_Data/Content/Game/UI/index.js`. Every module registers as
  `Q.add("<path>", { get <Export>() … })`. Find exports with
  `grep -o 'Q.add("game-ui/game/components/economy-panel/[^;]*;' index.js`, then read a component by grepping its
  minified variable (`var Ele=`) and dumping the bytes after it.

## Reusing and replacing vanilla UI

`cs2/modding`'s registrar (`UI/src/index.tsx`) gets the module registry:

- `override(path, export, Component)` replaces a component everywhere it's used. The Budget tab does this.
- `extend(path, export, Wrapped => props => …)` wraps one. The Taxation tab adds rows after each `TaxationGroup` this way.
- `getModule(path, export)` borrows a vanilla component. `UI/src/vanilla.ts` lists the ones we use (`NumberField`,
  `BudgetItemDetail`, `TaxSlider`) and their props as read from the bundle. **After a game patch, check those
  paths and props first**: a renamed module fails at runtime, not at build.
- Vanilla data comes through `cs2/bindings` (`economyBudget.incomeItems$`, `taxation.areaTypes$`, …) with
  `useValue` from `cs2/api`. Localised strings come from `useLocalization().translate("EconomyPanel.BUDGET_ITEM[<id>]")`.
  Keys are in the bundle's `EconomyPanel:{…}` table.

Our own data comes from C# `UISystemBase` systems: `RawValueBinding` (JSON via `IJsonWriter`) for
structured data, `TriggerBinding<…>` for UI→C# calls, all in group `fiscalOverhaul`. Register them in `Mod.OnLoad`
with `updateSystem.UpdateAt<T>(SystemUpdatePhase.UIUpdate)`. Only compute while the panel is open
(`GamePanelUISystem.activePanel is EconomyPanel`).

## Gameface (the game's browser) limits

Coherent Gameface isn't Chrome:
- **Use `<canvas>` 2D for charts**, not inline SVG. The vanilla statistics graphs use chart.js on canvas.
- No `ResizeObserver`; measure in a layout effect and poll (see `UI/src/chart/line-chart.tsx`).
- Patchy `Intl`; format numbers yourself (`formatMoney` in `chart-math.ts`).
- Sizes in `rem` follow the game's UI scale setting. The UI is laid out for 4K, so **1rem is less than a pixel**
  below 4K (about 0.67 px at 1440p): vanilla text is around 18–24rem.
- **Canvas pixels ignore rem.** Scale every canvas size (fonts, padding, line widths) by px-per-rem. Measure it from a
  hidden `width: 100rem` element (`offsetWidth / 100`): `getComputedStyle(document.documentElement).fontSize` is not
  the scale (`UI/src/chart/line-chart.tsx`).
- **Text that must stay on one line must be one text node.** `{value}/mo` in JSX is two text nodes, and Gameface
  wraps between them even under `white-space: nowrap`, above all in an absolutely positioned box with no width
  (which it shrinks). Build the string first (`formatPerMonth` in `chart-math.ts`).
- **A percentage `max-height` on an absolutely positioned box doesn't constrain it.** Set both `top` and `bottom` to
  give it a definite height (the Budget tab's item pop-out).

## Scheduling simulation systems

`UpdateSystem` runs a `GameSystemBase` every `GetUpdateInterval()` frames at an offset. A system registered with
`UpdateBefore<Ours, Theirs>`/`UpdateAfter` that has the **same interval and offset -1 (the default) inherits
`Theirs`'s offset**, so it runs on exactly the same frames. Density taxes depend on that:
[impl/density-taxes.md](impl/density-taxes.md).
