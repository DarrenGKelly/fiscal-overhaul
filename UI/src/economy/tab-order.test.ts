import { describe, expect, it } from "vitest";
import { orderEconomyTabs } from "./tab-order";

const FEATURES = ["City Budget", "Loan", "Taxation", "Service Budgets", "Production Panel"];
const tab = (id: number) => ({ props: { tab: id, feature: FEATURES[id] } });
const ids = (xs: any[]) => xs.map((x) => x.props.tab);

describe("orderEconomyTabs", () => {
  it("reorders the five economy tabs to Budget, Taxation, Services, Production, Loan", () => {
    expect(ids(orderEconomyTabs([0, 1, 2, 3, 4].map(tab)))).toEqual([0, 2, 3, 4, 1]);
  });

  it("orders a Loan-less set", () => {
    expect(ids(orderEconomyTabs([0, 2, 3, 4].map(tab)))).toEqual([0, 2, 3, 4]);
  });

  it("leaves a foreign set untouched", () => {
    const foreign = [{ props: { tab: 1, feature: "Other" } }, { props: { tab: 0, feature: "Thing" } }];
    expect(orderEconomyTabs(foreign)).toBe(foreign);
    const noTab = [{ props: {} }, { props: {} }];
    expect(orderEconomyTabs(noTab)).toBe(noTab);
  });

  it("does not mutate the input", () => {
    const input = [0, 1, 2, 3, 4].map(tab);
    orderEconomyTabs(input);
    expect(ids(input)).toEqual([0, 1, 2, 3, 4]);
  });
});
