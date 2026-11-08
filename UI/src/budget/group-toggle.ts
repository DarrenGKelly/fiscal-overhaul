// Pure logic for the Budget tab's checkboxes as a tree: a category row with child rows (sources, services) is a group
// whose box is on, off or partial, and a click on it turns the group and all its children on, or all off. Rows without
// children (totals, the balance, single-source categories) and child rows toggle only their own key. No game imports.

/** A group checkbox's state: every key on, none on, or some. */
export type CheckState = "on" | "off" | "partial";

/** The state of a row's box: from its own key alone without children, else from the group key and every child key. */
export function groupState(enabled: ReadonlySet<string>, key: string, childKeys: readonly string[] = []): CheckState {
  const keys = [key, ...childKeys];
  const on = keys.filter((k) => enabled.has(k)).length;
  if (on === 0) return "off";
  return on === keys.length ? "on" : "partial";
}

/**
 * The set after a click on a row's box (a new set; the old one is untouched): all on turns the row and its children
 * off; off or partial turns them all on. Without children it is a plain toggle of the row's key.
 */
export function toggledGroup(enabled: ReadonlySet<string>, key: string, childKeys: readonly string[] = []): Set<string> {
  const next = new Set(enabled);
  const keys = [key, ...childKeys];
  if (groupState(enabled, key, childKeys) === "on") keys.forEach((k) => next.delete(k));
  else keys.forEach((k) => next.add(k));
  return next;
}
