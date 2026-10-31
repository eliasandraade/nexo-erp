import { cloneElement, type ReactElement } from "react";
import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import ProgressCurvesChart from "./ProgressCurvesChart";

// jsdom has no layout: give the chart a fixed size instead of the 0×0 ResponsiveContainer measurement.
vi.mock("recharts", async (importOriginal) => {
  const mod = await importOriginal<typeof import("recharts")>();
  return {
    ...mod,
    ResponsiveContainer: ({ children }: { children: ReactElement }) => (
      <div data-testid="chart-box">{cloneElement(children as ReactElement<{ width: number; height: number }>, { width: 800, height: 260 })}</div>
    ),
  };
});

describe("ProgressCurvesChart", () => {
  afterEach(() => cleanup());

  it("draws the planned, real and forecast series with their legend labels", () => {
    const { container } = render(<ProgressCurvesChart curves={{
      planned:  [{ date: "2026-10-01", percent: 0 }, { date: "2026-10-08", percent: 20 }, { date: "2026-10-15", percent: 40 }],
      actual:   [{ date: "2026-10-01", percent: 0 }, { date: "2026-10-08", percent: 15 }],
      forecast: [{ date: "2026-10-15", percent: 32 }, { date: "2026-10-22", percent: 50 }],
    }} />);

    // legend uses the Portuguese names
    expect(screen.getByText("Planejado")).toBeInTheDocument();
    expect(screen.getByText("Real")).toBeInTheDocument();
    expect(screen.getByText("Previsto")).toBeInTheDocument();
    // one line per series, with the planned/forecast dashes
    const lines = container.querySelectorAll(".recharts-line-curve");
    expect(lines).toHaveLength(3);
    const dashes = [...lines].map((l) => l.getAttribute("stroke-dasharray"));
    expect(dashes).toEqual(expect.arrayContaining(["6 4", "2 4"]));
    // x axis: the merged dates (one per distinct date, sorted) as dd/MM ticks
    const ticks = [...container.querySelectorAll(".recharts-xAxis .recharts-cartesian-axis-tick-value")].map((t) => t.textContent);
    expect(ticks).toEqual(["01/10", "08/10", "15/10", "22/10"]);
  });

  it("renders with empty curves (no lines with points)", () => {
    const { container } = render(<ProgressCurvesChart curves={{ planned: [], actual: [], forecast: [] }} />);
    expect(screen.getByTestId("chart-box")).toBeInTheDocument();
    expect(container.querySelectorAll(".recharts-xAxis .recharts-cartesian-axis-tick-value")).toHaveLength(0);
  });
});
