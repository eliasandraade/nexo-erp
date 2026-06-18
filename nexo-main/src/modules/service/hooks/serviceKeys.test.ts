import { describe, it, expect } from "vitest";
import { serviceKeys } from "./useServicePreset";

/**
 * The hooks invalidate by entity prefix (e.g. `serviceKeys.professionals()`) after a mutation.
 * That only refreshes the list/detail queries if those keys are nested *under* the prefix, so
 * these tests pin the hierarchy that makes cache invalidation work.
 */
describe("serviceKeys", () => {
  it("everything nests under the 'service' root", () => {
    expect(serviceKeys.all).toEqual(["service"]);
    for (const key of [
      serviceKeys.preset(),
      serviceKeys.professionals(),
      serviceKeys.catalog(),
      serviceKeys.subjects(),
      serviceKeys.records("Subject", "x"),
    ]) {
      expect(key[0]).toBe("service");
    }
  });

  it("list + detail keys are nested under their entity prefix", () => {
    const cases = [
      { prefix: serviceKeys.professionals(), list: serviceKeys.professionalsList(true), detail: serviceKeys.professional("p1") },
      { prefix: serviceKeys.catalog(), list: serviceKeys.catalogList(false), detail: serviceKeys.catalogItem("c1") },
    ];
    for (const { prefix, list, detail } of cases) {
      expect(list.slice(0, prefix.length)).toEqual([...prefix]);
      expect(detail.slice(0, prefix.length)).toEqual([...prefix]);
    }
  });

  it("subject list key carries the filter params", () => {
    expect(serviceKeys.subjectsList({ active: true })).toEqual(["service", "subjects", { active: true }]);
  });

  it("records key is scoped by context type + id", () => {
    expect(serviceKeys.records("Subject", "abc")).toEqual(["service", "records", "Subject", "abc"]);
    expect(serviceKeys.records("Order", "abc")).not.toEqual(serviceKeys.records("Subject", "abc"));
  });
});
