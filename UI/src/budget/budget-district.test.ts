import { describe, expect, it } from "vitest";
import { BudgetHistory, BudgetItemLike } from "../chart/chart-math";
import { isCityOnlyItem, rowValue } from "./budget-district";

const item = (id: string, ...indices: number[]): BudgetItemLike => ({
  id,
  color: { r: 1, g: 1, b: 1, a: 1 },
  sources: indices.map((index) => ({ id: `s${index}`, index })),
  active: true,
});

// Game.City.ExpenseSource.LoanInterest = 1; IncomeSource.TaxResidential = 0, FeeElectricity = 4, ExportElectricity = 5.
const LOAN_INTEREST = item("LoanInterest", 1);
const RESIDENTIAL_TAX = item("ResidentialTaxes", 0);
const ELECTRICITY = item("Electricity", 4, 5);

describe("isCityOnlyItem", () => {
  it("the loan interest item is city-only", () => {
    expect(isCityOnlyItem(LOAN_INTEREST, "expense")).toBe(true);
  });

  it("the residential tax item isn't", () => {
    expect(isCityOnlyItem(RESIDENTIAL_TAX, "income")).toBe(false);
  });

  it("an item mixing district and city-only sources isn't", () => {
    expect(isCityOnlyItem(ELECTRICITY, "income")).toBe(false);
  });

  it("the same index means different sources for income and expense", () => {
    // Index 1: TaxCommercial (district) as income, LoanInterest (city-only) as expense.
    expect(isCityOnlyItem(item("x", 1), "income")).toBe(false);
    expect(isCityOnlyItem(item("x", 1), "expense")).toBe(true);
  });
});

describe("rowValue", () => {
  const vanillaIncome = [500, 0, 0, 0, 70, 30];
  const vanillaExpense = [-40, -900];
  const cityHistory: BudgetHistory = { span: 3, samples: 1, monthsAgo: [0], income: [], expense: [], district: "" };
  const districtHistory: BudgetHistory = {
    ...cityHistory,
    district: "12:1",
    nowIncome: [120, 0, 0, 0, 9, 0],
    nowExpense: [15, 0],
  };

  it("Entire City reads the vanilla values", () => {
    expect(rowValue(RESIDENTIAL_TAX, vanillaIncome, cityHistory, "income")).toBe(500);
    expect(rowValue(ELECTRICITY, vanillaIncome, cityHistory, "income")).toBe(100);
    expect(rowValue(item("subsidy", 0), vanillaExpense, null, "expense")).toBe(-40);
  });

  it("a district reads nowIncome / nowExpense (expenses shown negative, as vanilla)", () => {
    expect(rowValue(RESIDENTIAL_TAX, vanillaIncome, districtHistory, "income")).toBe(120);
    expect(rowValue(ELECTRICITY, vanillaIncome, districtHistory, "income")).toBe(9);
    expect(rowValue(item("subsidy", 0), vanillaExpense, districtHistory, "expense")).toBe(-15);
  });

  // The binding now writes nowIncome / nowExpense as null for Entire City (one property set per Gameface type).
  it("Entire City with null live sums still reads the vanilla values", () => {
    const cityWithNulls: BudgetHistory = { ...cityHistory, nowIncome: null, nowExpense: null };
    expect(rowValue(RESIDENTIAL_TAX, vanillaIncome, cityWithNulls, "income")).toBe(500);
    expect(rowValue(item("subsidy", 0), vanillaExpense, cityWithNulls, "expense")).toBe(-40);
  });

  it("a district with null live sums reads 0, not the city's values", () => {
    const districtWithNulls: BudgetHistory = { ...districtHistory, nowIncome: null, nowExpense: null };
    expect(rowValue(RESIDENTIAL_TAX, vanillaIncome, districtWithNulls, "income")).toBe(0);
    expect(rowValue(item("subsidy", 0), vanillaExpense, districtWithNulls, "expense")).toBe(-0);
  });
});
