import { describe, expect, it } from "vitest";
import { engineWarningKey, engineWarningText, TaxEngineWarning } from "./engine-warning";

describe("engineWarningKey", () => {
  it("shows nothing when there is no warning", () => {
    expect(engineWarningKey(TaxEngineWarning.None)).toBeNull();
    expect(engineWarningKey(null)).toBeNull();
    expect(engineWarningKey(undefined)).toBeNull();
    expect(engineWarningKey(42)).toBeNull(); // a value this build doesn't know
    expect(engineWarningKey(2)).toBeNull(); // the retired "untested but forced on" warning shows no line
  });

  it("picks the line for each warning", () => {
    expect(engineWarningKey(TaxEngineWarning.UntestedVersion)).toBe("TaxEngineUntestedVersion");
    expect(engineWarningKey(TaxEngineWarning.Incompatible)).toBe("TaxEngineIncompatible");
  });
});

describe("engineWarningText", () => {
  it("gives the English line without a translate, and null for none", () => {
    expect(engineWarningText(TaxEngineWarning.UntestedVersion, null)).toContain("hasn't been tested with this game version");
    expect(engineWarningText(TaxEngineWarning.None, null)).toBeNull();
  });
});
