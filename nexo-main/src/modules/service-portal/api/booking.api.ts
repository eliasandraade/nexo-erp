// Public Service booking portal — plain fetch, no auth, no tenant context (mirrors modules/portal).
// Derives the backend root from VITE_API_BASE_URL (same var as apiClient); every path starts
// with /public/service/... so the store is resolved server-side by the slug in the route.
const BASE = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:5000/api";

/** Error carrying the backend's controlled message (`{ error, details }`) for friendly UI display. */
export class BookingApiError extends Error {
  constructor(public status: number, message: string) {
    super(message);
    this.name = "BookingApiError";
  }
}

async function readError(res: Response): Promise<never> {
  let message = `HTTP ${res.status}`;
  try {
    const body = await res.json();
    if (body && typeof body.error === "string" && body.error) message = body.error;
  } catch {
    /* non-JSON body — keep the status message */
  }
  throw new BookingApiError(res.status, message);
}

async function get<T>(url: string): Promise<T> {
  const res = await fetch(url);
  if (!res.ok) await readError(res);
  return res.json() as Promise<T>;
}

async function post<T>(url: string, body: unknown): Promise<T> {
  const res = await fetch(url, {
    method:  "POST",
    headers: { "Content-Type": "application/json" },
    body:    JSON.stringify(body),
  });
  if (!res.ok) await readError(res);
  return res.json() as Promise<T>;
}

// ── DTOs (mirror the backend Public* records, camelCase) ────────────────────────

export interface ServiceLabels {
  customer:     string;
  professional: string;
  catalogItem:  string;
  appointment:  string;
  order:        string;
  subject:      string;
}

export interface ServiceCapabilities {
  appointments: boolean;
  orders:       boolean;
  quotes:       boolean;
  parts:        boolean;
  packages:     boolean;
  simpleRecord: boolean;
  commissions:  boolean;
  recurrence:   boolean;
  subjectKind:  string | null;
}

export interface PublicServicePortal {
  storeName:                     string;
  presetKey:                     string;
  presetDisplayName:             string;
  labels:                        ServiceLabels;
  capabilities:                  ServiceCapabilities;
  showPrices:                    boolean;
  requiresProfessionalSelection: boolean;
  isBookingEnabled:              boolean;
  // ── Optional store branding (PR16 backend). Absent ⇒ the adaptive theme provides the identity. ──
  displayName?:                  string | null;
  description?:                  string | null;
  logoUrl?:                      string | null;
  coverImageUrl?:                string | null;
  brandColor?:                   string | null;
  whatsApp?:                     string | null;
  address?:                      string | null;
}

export interface PublicCatalogItem {
  id:              string;
  name:            string;
  description:     string | null;
  category:        string | null;
  durationMinutes: number;
  price:           number | null;
  requiresSubject: boolean;
}

export interface PublicProfessional {
  id:        string;
  name:      string;
  role:      string | null;
  specialty: string | null;
  color:     string | null;
}

export interface PublicAvailabilitySlot {
  startsAt: string; // ISO UTC
  endsAt:   string; // ISO UTC
}

export interface PublicAvailability {
  professionalId:  string;
  catalogItemId:   string;
  durationMinutes: number;
  slots:           PublicAvailabilitySlot[];
}

export interface CreatePublicAppointmentSubject {
  displayName: string;
  kind?:       string | null;
  notes?:      string | null;
}

export interface CreatePublicAppointmentRequest {
  customerName:   string;
  phone:          string;
  catalogItemId:  string;
  professionalId: string;
  startsAt:       string; // ISO UTC — echoed verbatim from an availability slot
  email?:         string | null;
  subject?:       CreatePublicAppointmentSubject | null;
  notes?:         string | null;
}

export interface PublicAppointmentCreated {
  appointmentId:    string;
  status:           string;
  startsAt:         string;
  endsAt:           string;
  serviceName:      string;
  professionalName: string;
  customerName:     string;
}

// ── Endpoints ───────────────────────────────────────────────────────────────────

export const getPortal = (slug: string) =>
  get<PublicServicePortal>(`${BASE}/public/service/${slug}`);

export const getCatalog = (slug: string) =>
  get<PublicCatalogItem[]>(`${BASE}/public/service/${slug}/catalog`);

export const getProfessionals = (slug: string) =>
  get<PublicProfessional[]>(`${BASE}/public/service/${slug}/professionals`);

export const getAvailability = (slug: string, catalogItemId: string, professionalId: string) =>
  get<PublicAvailability>(
    `${BASE}/public/service/${slug}/availability` +
    `?catalogItemId=${encodeURIComponent(catalogItemId)}` +
    `&professionalId=${encodeURIComponent(professionalId)}`);

export const createAppointment = (slug: string, req: CreatePublicAppointmentRequest) =>
  post<PublicAppointmentCreated>(`${BASE}/public/service/${slug}/appointments`, req);
