/**
 * Economy panel tab order: Budget, Taxation, Services, Production, Loans, so the three district-aware tabs sit
 * together. Vanilla's order is Budget, Loan, Taxation, Services, Production (enum Dv = 0..4 in the game bundle).
 */

/** Vanilla tab enum value -> position in our order. */
const ORDER: Record<number, number> = { 0: 0, 2: 1, 3: 2, 4: 3, 1: 4 };

/** The economy panel's `feature` names, by tab id (EconomyPanel's tab list in the game bundle). */
const FEATURES: Record<number, string> = { 0: "City Budget", 1: "Loan", 2: "Taxation", 3: "Service Budgets", 4: "Production Panel" };

/**
 * The children in our order, or the same array when they are not the economy tab set (every child must carry a
 * numeric props.tab and the matching props.feature). Orders by `tab` id, never by position: vanilla indexes its
 * features by enum value, so only the render order changes. Vanilla drops Loan in some modes; that set still orders.
 */
export function orderEconomyTabs<T extends { props?: any }>(children: T[]): T[] {
  if (!Array.isArray(children) || children.length === 0) return children;
  const seen = new Set<number>();
  for (const c of children) {
    const tab = c?.props?.tab;
    if (typeof tab !== "number" || FEATURES[tab] !== c.props.feature || seen.has(tab)) return children;
    seen.add(tab);
  }
  return [...children].sort((a, b) => ORDER[a.props.tab] - ORDER[b.props.tab]);
}
