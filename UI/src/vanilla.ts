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

export const BUDGET_PAGE_PATH = `${ECONOMY}/budget-page/budget-page.tsx`;
export const TAXATION_GROUP_PATH = `${ECONOMY}/taxation-page/taxation-group.tsx`;
