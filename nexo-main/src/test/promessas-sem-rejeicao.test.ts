import { readdirSync, readFileSync, statSync } from "node:fs";
import { join } from "node:path";
import { afterEach, describe, expect, it, vi } from "vitest";
import { QueryClient, QueryObserver } from "@tanstack/react-query";

// What the `void` in front of invalidateQueries relies on: that promise does not reject when the refetch fails. See
// docs/audits/2026-11-06-sonarcloud-confiabilidade-por-achado.md. A library upgrade that changes this breaks these tests, and
// then every one of those call sites has to be looked at again.
describe("invalidateQueries, the contract behind the declared promises", () => {
  const KEY = ["contrato"];
  const unsubscribers: Array<() => void> = [];

  function watched(queryFn: () => Promise<string>) {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const observer = new QueryObserver(client, { queryKey: KEY, queryFn });
    unsubscribers.push(observer.subscribe(() => undefined));
    return { client, observer };
  }

  afterEach(() => {
    unsubscribers.splice(0).forEach((unsubscribe) => unsubscribe());
  });

  it("resolves when the refetch of a screen that is showing that query fails", async () => {
    const queryFn = vi.fn<() => Promise<string>>().mockResolvedValueOnce("antes").mockRejectedValue(new Error("sem conexão"));
    const { client } = watched(queryFn);
    await vi.waitFor(() => expect(client.getQueryData(KEY)).toBe("antes"));

    await expect(client.invalidateQueries({ queryKey: KEY })).resolves.toBeUndefined();

    expect(queryFn).toHaveBeenCalledTimes(2);
  });

  it("the failure stays in the query, which keeps the last data, for the screen to read", async () => {
    const queryFn = vi.fn<() => Promise<string>>().mockResolvedValueOnce("antes").mockRejectedValue(new Error("sem conexão"));
    const { client, observer } = watched(queryFn);
    await vi.waitFor(() => expect(client.getQueryData(KEY)).toBe("antes"));

    await client.invalidateQueries({ queryKey: KEY });

    const shown = observer.getCurrentResult();
    expect(shown.isError).toBe(true);
    expect(shown.error?.message).toBe("sem conexão");
    expect(shown.data).toBe("antes");
  });

  it("throwOnError is what would make it reject", async () => {
    const queryFn = vi.fn<() => Promise<string>>().mockResolvedValueOnce("antes").mockRejectedValue(new Error("sem conexão"));
    const { client } = watched(queryFn);
    await vi.waitFor(() => expect(client.getQueryData(KEY)).toBe("antes"));

    await expect(client.invalidateQueries({ queryKey: KEY }, { throwOnError: true })).rejects.toThrow("sem conexão");
  });

  it("and nothing in the application passes it", () => {
    const files = (function walk(dir: string): string[] {
      return readdirSync(dir).flatMap((name) => {
        const path = join(dir, name);
        return statSync(path).isDirectory() ? walk(path) : /\.tsx?$/.test(name) && !/\.(test|spec)\.tsx?$/.test(name) ? [path] : [];
      });
    })(join(process.cwd(), "src"));

    expect(files.length).toBeGreaterThan(100);
    expect(files.filter((file) => readFileSync(file, "utf8").includes("throwOnError"))).toEqual([]);
  }, 60_000);
});
