import type { BuildProjectType } from "../api/build.api";

export const PROJECT_TYPES: Array<{ value: BuildProjectType; label: string }> = [
  { value: "House",      label: "Residencial" },
  { value: "Commercial", label: "Comercial" },
  { value: "Renovation", label: "Reforma" },
  { value: "Building",   label: "Edifício" },
  { value: "Other",      label: "Outro" },
];
