import { getModule } from "cs2/modding";

// Vanilla game components reused as-is, looked up through the module registry.
// Paths and export names come from the game's UI bundle (Cities2_Data/Content/Game/UI/index.js);
// see knowledge/cs2-ui-modding.md for how to find them after a game patch.
const ECONOMY = "game-ui/game/components/economy-panel";

/** Money value with sign colouring: props { value, importance?: "primary" | "secondary", className? }. */
export const NumberField: any = getModule(`${ECONOMY}/components/money-field/money-field.tsx`, "NumberField");

/** The Budget tab's right-hand detail panel: props { item: BudgetItem | null, values: number[], className? }. */
export const BudgetItemDetail: any = getModule(`${ECONOMY}/budget-page/budget-item-detail/budget-item-detail.tsx`, "BudgetItemDetail");

/** Taxation slider row: props { min, max, rate, income, className?, primary?, disabled?, onValueChanged }. */
export const TaxSlider: any = getModule(`${ECONOMY}/taxation-page/tax-slider.tsx`, "TaxSlider");

/** A tax group's resource/education row: props { areaType: TaxAreaType, resource: TaxResource } (taxation.areaResources$). */
export const TaxationItem: any = getModule(`${ECONOMY}/taxation-page/taxation-item.tsx`, "TaxationItem");

/**
 * TaxationItem's class names (`taxationItem` row, `icon`, `title`, `slider`): the density rows wear them so their
 * columns line up with the education/resource rows (taxation/density-rows.tsx).
 */
export const taxationItemClasses: Record<string, string> | undefined = getModule(`${ECONOMY}/taxation-page/taxation-item.module.scss`, "classes");

/** The focusable row wrapper TaxationItem draws its row in (hover and gamepad focus): props { className?, children }. */
export const ActiveFocusDiv: any = getModule("game-ui/common/focus/focus-div.tsx", "ActiveFocusDiv");

/**
 * TaxationGroup's class names, read by it on every render (`toggleButton`, `hint`, `content` among them): the live
 * object, so the density sections can hide vanilla's own expander (taxation/tax-sections.tsx).
 */
export const taxationGroupClasses: Record<string, string> | undefined = getModule(`${ECONOMY}/taxation-page/taxation-group.module.scss`, "classes");

/** The Taxation page's class names; `totalValue` is the ESTIMATED TOTAL NumberField's className (and its only class). */
export const taxationPageClasses: { totalValue?: string } | undefined = getModule(`${ECONOMY}/taxation-page/taxation-page.module.scss`, "classes");

/**
 * Vanilla scroll container with the game's own scroll bar (Gameface draws none for `overflow: auto`): props
 * { className?, vertical?, trackVisibility?: "always" | "scrollable" | ..., children }. Constrain it with a max-height
 * on its className, as vanilla dropdown menus do.
 */
export const Scrollable: any = getModule("game-ui/common/scrolling/scrollable.tsx", "Scrollable");

/** Extended to reorder the Economy panel's tabs (TabBar draws `<div class=tabBar>{children}</div>`; EconomyPanel passes children keyed by `tab`). */
export const TAB_BAR_PATH = "game-ui/common/tabs/tabs.tsx";
export const BUDGET_PAGE_PATH =`${ECONOMY}/budget-page/budget-page.tsx`;
export const TAXATION_GROUP_PATH = `${ECONOMY}/taxation-page/taxation-group.tsx`;
export const TAXATION_PAGE_PATH = `${ECONOMY}/taxation-page/taxation-page.tsx`;
/** Extended (not borrowed) for the zone totals: TaxationGroup draws each zone's total as its primary TaxSlider's income. */
export const TAX_SLIDER_PATH = `${ECONOMY}/taxation-page/tax-slider.tsx`;
/** Extended for ESTIMATED TOTAL, which TaxationPage draws with a NumberField of the `taxIncome` binding. */
export const MONEY_FIELD_PATH = `${ECONOMY}/components/money-field/money-field.tsx`;
