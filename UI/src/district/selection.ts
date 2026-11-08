// The one selected district, shared by the Budget, Services and Taxation tabs so switching tabs keeps the choice.
// Module-level (survives the tabs unmounting). No game imports, so vitest runs it in Node.
import { useEffect, useState } from "react";
import { CITY_KEY } from "./district-select";

let selected = CITY_KEY;
const listeners = new Set<(key: string) => void>();

/** The shared choice's key; CITY_KEY (Entire City) until a tab sets one. */
export function getSelectedDistrict(): string {
  return selected;
}

/** Sets the shared choice and tells every subscriber (a no-op when unchanged). */
export function setSelectedDistrict(key: string): void {
  const next = key ?? CITY_KEY;
  if (next === selected) return;
  selected = next;
  listeners.forEach((l) => l(next));
}

/** Calls `listener` on every change; returns the unsubscribe. */
export function subscribeSelectedDistrict(listener: (key: string) => void): () => void {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

/**
 * The shared choice and its setter. A tab applies effectiveKey(its options, key) for display and leaves the shared
 * choice alone, so a district one tab doesn't offer is still remembered for the others.
 */
export function useSelectedDistrict(): [string, (key: string) => void] {
  const [key, setKey] = useState(selected);
  useEffect(() => {
    setKey(selected);
    return subscribeSelectedDistrict(setKey);
  }, []);
  return [key, setSelectedDistrict];
}
