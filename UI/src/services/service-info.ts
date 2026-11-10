// Pure logic for the Services tab's info column (service-table-view.tsx): which item vanilla's ServiceItemDetail
// explains, set by the row under the mouse. No game imports, so vitest runs it in Node.

/** Vanilla's economy-panel ServicePageItemType values the table's rows use (service-page-context.ts). */
export const ServiceInfoType = { Service: 0, BudgetSlider: 1, ServiceFee: 2 } as const;

export interface EntityLike {
  index: number;
  version: number;
}

/** Vanilla's item id: a service (its icon and description), its budget slider, or one of its fees (by name). */
export type ServiceInfoItem =
  | { type: typeof ServiceInfoType.Service; entity: EntityLike }
  | { type: typeof ServiceInfoType.BudgetSlider }
  | { type: typeof ServiceInfoType.ServiceFee; name: string };

/** What the info column shows: the service whose details it reads, and the item. Null: nothing hovered, empty. */
export interface ServiceInfoTarget {
  service: EntityLike;
  item: ServiceInfoItem;
}

/** The hovered part of a service row: its name and icon, its budget slider (only if adjustable), or a fee row. */
export function infoTarget(service: EntityLike, part: { kind: "service" } | { kind: "budget"; adjustable: boolean } | { kind: "fee"; name: string }): ServiceInfoTarget {
  if (part.kind === "fee") return { service, item: { type: ServiceInfoType.ServiceFee, name: part.name } };
  if (part.kind === "budget" && part.adjustable) return { service, item: { type: ServiceInfoType.BudgetSlider } };
  return { service, item: { type: ServiceInfoType.Service, entity: service } };
}

export function sameTarget(a: ServiceInfoTarget | null, b: ServiceInfoTarget | null): boolean {
  if (a === b) return true;
  if (!a || !b) return false;
  if (a.service.index !== b.service.index || a.service.version !== b.service.version || a.item.type !== b.item.type) return false;
  return a.item.type !== ServiceInfoType.ServiceFee || a.item.name === (b.item as { name: string }).name;
}

/** Leaving a row clears the column only if it still shows that row (another row may have been entered first), as vanilla. */
export function afterLeave(current: ServiceInfoTarget | null, left: ServiceInfoTarget): ServiceInfoTarget | null {
  return sameTarget(current, left) ? null : current;
}
