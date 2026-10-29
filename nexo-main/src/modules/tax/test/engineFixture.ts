// Real engine output (orken-tax-engine/1.0.0) on the seeded catalogue: TaxSimulator.Compare of a regular-regime
// 2026 sale (line 1 fully informed; line 2 with taxes in the price unknown and under a special regime) against
// 'conditional discounts made unconditional'. The data lives in engine-scenarios.json, as produced by the engine;
// this module only gives it the API type so the screens are tested against what the API returns.
import type { ScenarioResult } from "../api/tax.api";
import scenarios from "./engine-scenarios.json";

export const ENGINE_SCENARIOS = scenarios as unknown as ScenarioResult[];
