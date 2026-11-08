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
export const taxationPageClasses: Record<string, string> | undefined = getModule(`${ECONOMY}/taxation-page/taxation-page.module.scss`, "classes");

/**
 * Vanilla scroll container with the game's own scroll bar (Gameface draws none for `overflow: auto`): props
 * { className?, vertical?, trackVisibility?: "always" | "scrollable" | ..., children }. Constrain it with a max-height
 * on its className, as vanilla dropdown menus do.
 */
export const Scrollable: any = getModule("game-ui/common/scrolling/scrollable.tsx", "Scrollable");

/**
 * Extended for the Economy panel's header (economy/economy-tab-bar.tsx): our tab order, the district picker and the
 * group divider. TabBar draws `<div class=tabBar {className}>{children}</div>`; EconomyPanel passes children keyed by
 * `tab`, each with props { tab, selectedTab, feature, uiTag, onSelect }.
 */
export const TAB_BAR_PATH = "game-ui/common/tabs/tabs.tsx";
export const BUDGET_PAGE_PATH =`${ECONOMY}/budget-page/budget-page.tsx`;
export const TAXATION_GROUP_PATH = `${ECONOMY}/taxation-page/taxation-group.tsx`;
export const TAXATION_PAGE_PATH = `${ECONOMY}/taxation-page/taxation-page.tsx`;
/** Extended (not borrowed) for the zone totals: TaxationGroup draws each zone's total as its primary TaxSlider's income. */
export const TAX_SLIDER_PATH = `${ECONOMY}/taxation-page/tax-slider.tsx`;
/** Extended for ESTIMATED TOTAL, which TaxationPage draws with a NumberField of the `taxIncome` binding. */
export const MONEY_FIELD_PATH = `${ECONOMY}/components/money-field/money-field.tsx`;

// Services tab (UI/src/services/service-table-view.tsx): the page is replaced by a table built like the Taxation tab's.
/** Extended: props none; with the feature on our table is drawn instead (vanilla's service list, detail and info column). */
export const SERVICE_PAGE_PATH = `${ECONOMY}/service-page/service-page.tsx`;

/** Panel section (a frame with an optional header): props { header?, className?, theme?, focusKey?, children }. */
export const Section: any = getModule("game-ui/common/section/section.tsx", "Section");

/**
 * Vanilla's gradient slider (TaxSlider's and SliderItem's track): props { value, start, end, gamepadStep?,
 * valueTransformer?, gradient: { stops: [{ offset, color }] }, theme?, className?, disabled?, onChange }.
 */
export const GradientSlider: any = getModule("game-ui/common/input/slider/gradient-slider/gradient-slider.tsx", "GradientSlider");

/** The economy panel's gradient slider theme (TaxSlider and SliderItem pass it as `theme`). */
export const economyGradientSliderClasses: Record<string, string> | undefined = getModule(`${ECONOMY}/components/gradient-slider/gradient-slider.module.scss`, "classes");

/** The slider's whole-number value transformer (TaxSlider passes it). */
export const intTransformer: any = getModule("game-ui/common/input/slider/slider.tsx", "intTransformer");

/**
 * TaxSlider's class names (`taxSlider`, `sliderColumn`, `rate`, `slider`, `estimateColumn`, `estimateValue`): the
 * Services table's slider rows are drawn in its DOM so their columns line up as the Taxation tab's.
 */
export const taxSliderClasses: Record<string, string> | undefined = getModule(`${ECONOMY}/taxation-page/tax-slider.module.scss`, "classes");

/** Whether the legacy interface style is on: vanilla BudgetSliderItem picks its slider gradient by it. */
export const useLegacyInterfaceActive: () => boolean = getModule("game-ui/common/hooks/use-legacy-interface.tsx", "useLegacyInterfaceActive");

/** Vanilla's portal (`game-ui/common/portal/portal.tsx`): draws children in the screen-wide portal container, above every panel; tooltips use it. */
export const Portal: any = getModule("game-ui/common/portal/portal.tsx", "Portal");
/** The portal container Portal draws into (document.body unless a provider sets one). */
export const usePortalContainer: (() => HTMLElement | null) | undefined = getModule("game-ui/common/portal/portal.tsx", "usePortalContainer");

/** A glyph tinted by CSS (mask-image, background var(--iconColor)): props { src, className?, style? }. */
export const TintedIcon: any = getModule("game-ui/common/image/tinted-icon.tsx", "TintedIcon");
