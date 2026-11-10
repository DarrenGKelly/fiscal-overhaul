import { describe, expect, it } from "vitest";
import { afterLeave, infoTarget, sameTarget, ServiceInfoType } from "./service-info";

const POWER = { index: 7, version: 1 };
const WATER = { index: 8, version: 1 };

describe("infoTarget", () => {
  it("the name explains the service, the slider its budget, a fee row that fee", () => {
    expect(infoTarget(POWER, { kind: "service" })).toEqual({ service: POWER, item: { type: ServiceInfoType.Service, entity: POWER } });
    expect(infoTarget(POWER, { kind: "budget", adjustable: true }).item).toEqual({ type: ServiceInfoType.BudgetSlider });
    expect(infoTarget(POWER, { kind: "fee", name: "Electricity" }).item).toEqual({ type: ServiceInfoType.ServiceFee, name: "Electricity" });
  });
  it("a service without a budget slider explains the service, not a budget", () => {
    expect(infoTarget(POWER, { kind: "budget", adjustable: false }).item.type).toBe(ServiceInfoType.Service);
    expect(infoTarget(POWER, { kind: "budget", adjustable: false }).item.type).not.toBe(ServiceInfoType.BudgetSlider);
  });
});

describe("sameTarget", () => {
  it("matches the same service and item", () => {
    expect(sameTarget(infoTarget(POWER, { kind: "service" }), infoTarget({ ...POWER }, { kind: "service" }))).toBe(true);
    expect(sameTarget(null, null)).toBe(true);
  });
  it("differs by service, item or fee name", () => {
    expect(sameTarget(infoTarget(POWER, { kind: "service" }), infoTarget(WATER, { kind: "service" }))).toBe(false);
    expect(sameTarget(infoTarget(POWER, { kind: "service" }), infoTarget(POWER, { kind: "budget", adjustable: true }))).toBe(false);
    expect(sameTarget(infoTarget(WATER, { kind: "fee", name: "Water" }), infoTarget(WATER, { kind: "fee", name: "Sewage" }))).toBe(false);
    expect(sameTarget(infoTarget(POWER, { kind: "service" }), null)).toBe(false);
  });
});

describe("afterLeave", () => {
  const name = infoTarget(POWER, { kind: "service" });
  const slider = infoTarget(POWER, { kind: "budget", adjustable: true });
  it("leaving the shown row empties the column", () => expect(afterLeave(name, name)).toBeNull());
  it("a late leave of the previous row keeps the row entered since", () => {
    expect(afterLeave(slider, name)).toBe(slider);
    expect(afterLeave(null, name)).toBeNull();
  });
});
