/** Lazy: three.js GLB export + the web-ifc worker load only when the user converts an IFC. */
export const loadIfcConverter = () => import("./viewer/convert-ifc");
