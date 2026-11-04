import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, fireEvent, cleanup } from "@testing-library/react";
import { PortalVisibilityMatrix } from "./PortalVisibilityMatrix";
import { DEFAULT_PORTAL_VISIBILITY } from "../../api/build-portal.api";

afterEach(cleanup);

const PASSWORD_ONLY = ["changeOrders", "selections", "postConstruction", "realityCapture", "documents"] as const;

const all = {
  ...DEFAULT_PORTAL_VISIBILITY,
  changeOrders: true, selections: true, postConstruction: true, realityCapture: true, documents: true,
};

describe("PortalVisibilityMatrix — password-only sections (contract v1.5)", () => {
  it("on a public portal shows them off, disabled, with the hint", () => {
    render(<PortalVisibilityMatrix value={all} access="Public" onChange={vi.fn()} />);
    for (const flag of PASSWORD_ONLY) {
      const toggle = document.getElementById(`portal-vis-${flag}`)!;
      expect(toggle).toBeDisabled();
      expect(toggle).toHaveAttribute("aria-checked", "false");
      expect(screen.getByTestId(`portal-vis-${flag}-password`)).toHaveTextContent("exige portal com senha");
    }
    // other sections are unaffected
    expect(document.getElementById("portal-vis-photos")).not.toBeDisabled();
    expect(screen.queryByTestId("portal-vis-photos-password")).toBeNull();
  });

  it("on a password portal lets the company turn them on and off", () => {
    const onChange = vi.fn();
    render(<PortalVisibilityMatrix value={all} access="Password" onChange={onChange} />);
    const toggle = document.getElementById("portal-vis-changeOrders")!;
    expect(toggle).not.toBeDisabled();
    expect(toggle).toHaveAttribute("aria-checked", "true");
    expect(screen.queryByText("exige portal com senha")).toBeNull();
    fireEvent.click(toggle);
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ changeOrders: false }));
  });
});
