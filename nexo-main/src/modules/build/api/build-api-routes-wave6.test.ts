import { describe, it, expect, vi, beforeEach } from "vitest";
import { apiClient } from "@/services/api-client";
import * as f from "./build-forecast.api";
import * as t from "./build-twin.api";
import * as platform from "@/modules/platform/services/platformApi";

// The HTTP layer is mocked: these tests pin the route, verb and body of the Wave 6 API functions.
vi.mock("@/services/api-client", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/services/api-client")>()),
  apiClient: { get: vi.fn(), post: vi.fn(), put: vi.fn(), patch: vi.fn(), delete: vi.fn(), postForm: vi.fn(), upload: vi.fn() },
}));

type Verb = "get" | "post" | "put";
type Case = [name: string, call: () => Promise<unknown>, verb: Verb, url: string, body?: unknown];

const B = "/v1/build";
const ok = { ok: true };

beforeEach(() => vi.clearAllMocks());

describe("Wave 6 API routes", () => {
  const cases: Case[] = [
    ["fetchForecast", () => f.fetchForecast("p1"), "get", `${B}/projects/p1/forecast`],
    ["recomputeForecast", () => f.recomputeForecast("p1"), "post", `${B}/projects/p1/forecast`, undefined],
    ["fetchForecastHistory default", () => f.fetchForecastHistory("p1"), "get", `${B}/projects/p1/forecast/history?take=60`],
    ["fetchForecastHistory take", () => f.fetchForecastHistory("p1", 10), "get", `${B}/projects/p1/forecast/history?take=10`],
    ["fetchProjectWeather", () => f.fetchProjectWeather("p1"), "get", `${B}/projects/p1/weather`],
    ["setStageWeatherSensitivity true", () => f.setStageWeatherSensitivity("s1", true), "put", `${B}/stages/s1/weather-sensitivity`, { weatherSensitive: true }],
    ["setStageWeatherSensitivity null", () => f.setStageWeatherSensitivity("s1", null), "put", `${B}/stages/s1/weather-sensitivity`, { weatherSensitive: null }],
    ["fetchModelSource", () => t.fetchModelSource("v1"), "get", `${B}/models/versions/v1/source`],
    ["fetchZoneStates", () => t.fetchZoneStates("p1", "2026-10-30"), "get", `${B}/projects/p1/twin/zones/state?date=2026-10-30`],
    ["fetchZoneStates without date", () => t.fetchZoneStates("p1"), "get", `${B}/projects/p1/twin/zones/state`],
    ["fetchElements by externalId", () => t.fetchElements("v1", { externalId: "a,b", take: 1 }), "get", `${B}/models/versions/v1/elements?take=1&externalId=a%2Cb`],
    ["fetchPlatformBuildOverview", () => platform.fetchPlatformBuildOverview(), "get", "/platform/build/overview"],
    ["fetchPlatformBuildJobs default", () => platform.fetchPlatformBuildJobs(), "get", "/platform/build/jobs?status=Failed&take=50"],
    ["fetchPlatformBuildJobs pending", () => platform.fetchPlatformBuildJobs("Pending", 10), "get", "/platform/build/jobs?status=Pending&take=10"],
    ["retryPlatformBuildJob", () => platform.retryPlatformBuildJob("j1"), "post", "/platform/build/jobs/j1/retry", undefined],
  ];

  it.each(cases)("%s", async (_name, call, verb, url, body) => {
    vi.mocked(apiClient[verb]).mockResolvedValue(ok as never);
    await expect(call()).resolves.toBe(ok);
    const args = vi.mocked(apiClient[verb]).mock.calls.at(-1)!;
    expect(args[0]).toBe(url);
    if (verb !== "get") expect(args[1]).toEqual(body);
  });
});
