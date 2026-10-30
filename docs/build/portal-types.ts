// Orken Build — Portal do Cliente (API pública), contrato v1.6
// GERADO de docs/build/openapi-portal.json por BuildPortalOpenApiTests — não edite à mão.
// Regenerar (em nexo-backend): ORKEN_WRITE_PORTAL_OPENAPI=1 dotnet test tests/Nexo.IntegrationTests --filter "FullyQualifiedName~BuildPortalOpenApiTests"
// Campos com `?` podem vir null ou ausentes. Datas: "yyyy-MM-dd" (date) ou ISO 8601 (date-time).

export interface BuildProgressPointDto {
  date: string;
  percent: number;
}

export interface BuildSimplifiedBox {
  width: number;
  depth: number;
  height: number;
  elevation: number;
}

export interface CreatePublicBuildSessionRequest {
  password?: string | null;
}

export interface PublicBuildAssistanceDto {
  id: string;
  number: number;
  category: string;
  title: string;
  description: string;
  location?: string | null;
  openedVia: string;
  openedAt: string;
  status: string;
  warrantyCovered?: boolean | null;
  scheduledFor?: string | null;
  resolutionNotes?: string | null;
  rejectionReason?: string | null;
  rating?: number | null;
  feedback?: string | null;
  photoUrls: string[];
  photoUrlsExpireAt?: string | null;
  canAddPhotos: boolean;
  canGiveFeedback: boolean;
  timeline: PublicBuildAssistanceEventDto[];
}

export interface PublicBuildAssistanceEventDto {
  status: string;
  at: string;
}

export interface PublicBuildAssistanceFeedbackRequest {
  rating?: number | null;
  comment?: string | null;
}

export interface PublicBuildAssistanceListDto {
  items: PublicBuildAssistanceDto[];
  categories: string[];
  canOpen: boolean;
}

export interface PublicBuildAssistanceOpenRequest {
  category?: string | null;
  title?: string | null;
  description?: string | null;
  location?: string | null;
  contactPhone?: string | null;
  openedByName?: string | null;
}

export interface PublicBuildBootstrapDto {
  contractVersion: string;
  sections: string[];
  portal: PublicBuildPortalDto;
  company: PublicBuildCompanyDto;
  project: PublicBuildProjectDto;
  progress?: PublicBuildProgressSummaryDto | null;
  stages?: PublicBuildStageDto[] | null;
  schedule?: PublicBuildScheduleDto | null;
  weather?: PublicBuildWeatherDto | null;
  financialSummary?: PublicBuildFinancialSummaryDto | null;
  contact?: PublicBuildContactDto | null;
  latestUpdates?: PublicBuildUpdateDto[] | null;
  phase: string;
  property?: PublicBuildPropertyDto | null;
}

export interface PublicBuildChangeOrderDecisionRequest {
  decision?: string | null;
  comment?: string | null;
  signerName?: string | null;
}

export interface PublicBuildChangeOrderDto {
  id: string;
  number: number;
  title: string;
  description?: string | null;
  reason: string;
  costImpact?: number | null;
  scheduleImpactDays: number;
  stageName?: string | null;
  status: string;
  canDecide: boolean;
  sentAt?: string | null;
  decidedAt?: string | null;
  decisionChannel?: string | null;
  clientComment?: string | null;
  items: PublicBuildChangeOrderItemDto[];
}

export interface PublicBuildChangeOrderItemDto {
  name: string;
  kind: string;
  quantity: number;
  unit: string;
  unitPrice?: number | null;
  total?: number | null;
}

export interface PublicBuildChangeOrdersDto {
  items: PublicBuildChangeOrderDto[];
}

export interface PublicBuildCompanyDto {
  name: string;
  logoUrl?: string | null;
}

export interface PublicBuildContactDto {
  name?: string | null;
  phone?: string | null;
  email?: string | null;
  whatsApp?: string | null;
}

export interface PublicBuildCurvesDto {
  actual: BuildProgressPointDto[];
  planned?: BuildProgressPointDto[] | null;
  forecast?: BuildProgressPointDto[] | null;
}

export interface PublicBuildDocumentDto {
  title: string;
  category: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  validUntil?: string | null;
  url?: string | null;
  urlExpiresAt?: string | null;
  uploadedAt: string;
}

export interface PublicBuildDocumentsDto {
  items: PublicBuildDocumentDto[];
}

export interface PublicBuildErrorDto {
  error: string;
  code: string;
  portal?: PublicBuildPortalTeaserDto | null;
  retryAfterSeconds?: number | null;
}

export interface PublicBuildFinancialSummaryDto {
  approvedBudget?: number | null;
  realizedTotal: number;
  currency: string;
  asOf: string;
}

export interface PublicBuildHandoverAcceptRequest {
  signerName?: string | null;
  comment?: string | null;
}

export interface PublicBuildHandoverDocumentDto {
  name: string;
  url?: string | null;
}

export interface PublicBuildHandoverDto {
  status: string;
  scheduledFor?: string | null;
  deliveredAt?: string | null;
  keysDelivered: boolean;
  documents: PublicBuildHandoverDocumentDto[];
  documentUrlsExpireAt?: string | null;
  walkthrough?: PublicBuildWalkthroughDto | null;
  acceptedAt?: string | null;
  acceptedByName?: string | null;
  acceptanceChannel?: string | null;
  acceptanceComment?: string | null;
  canAccept: boolean;
}

export interface PublicBuildMeasurementDto {
  number: number;
  periodStart: string;
  periodEnd: string;
  approvedAt?: string | null;
  totalAmount?: number | null;
  items: PublicBuildMeasurementItemDto[];
}

export interface PublicBuildMeasurementItemDto {
  stageName: string;
  previousPercent: number;
  measuredPercent: number;
  amount?: number | null;
}

export interface PublicBuildMeasurementsDto {
  items: PublicBuildMeasurementDto[];
}

export interface PublicBuildPhotoDto {
  url?: string | null;
  caption?: string | null;
  date: string;
  urlExpiresAt: string;
}

export interface PublicBuildPhotosDto {
  items: PublicBuildPhotoDto[];
  total: number;
  skip: number;
  take: number;
}

export interface PublicBuildPortalDto {
  title?: string | null;
  welcomeMessage?: string | null;
  primaryColor?: string | null;
  accentColor?: string | null;
  logoUrl?: string | null;
  coverUrl?: string | null;
  access: string;
}

export interface PublicBuildPortalTeaserDto {
  name: string;
  logoUrl?: string | null;
  title?: string | null;
}

export interface PublicBuildProgressDto {
  asOf: string;
  actualPercent: number;
  plannedPercent?: number | null;
  forecastDelivery?: string | null;
  methodology: string;
  curves: PublicBuildCurvesDto;
}

export interface PublicBuildProgressSummaryDto {
  asOf: string;
  actualPercent: number;
  plannedPercent?: number | null;
  forecastDelivery?: string | null;
  methodology: string;
}

export interface PublicBuildProjectDto {
  name: string;
  type: string;
  status: string;
  startDate?: string | null;
  expectedEndDate?: string | null;
}

export interface PublicBuildPropertyDto {
  handoverDate?: string | null;
  handoverAccepted: boolean;
  warrantyTerms: PublicBuildWarrantyTermDto[];
  openAssistanceCount: number;
}

export interface PublicBuildRealityCaptureDto {
  type: string;
  capturedAt: string;
  title: string;
  stageName?: string | null;
  zoneName?: string | null;
  contentType: string;
  url?: string | null;
  urlExpiresAt?: string | null;
}

export interface PublicBuildRealityCapturesDto {
  items: PublicBuildRealityCaptureDto[];
  total: number;
  skip: number;
  take: number;
}

export interface PublicBuildScheduleDto {
  startDate?: string | null;
  expectedEndDate?: string | null;
  stages: PublicBuildScheduleStageDto[];
}

export interface PublicBuildScheduleStageDto {
  name: string;
  order: number;
  plannedStart?: string | null;
  plannedEnd?: string | null;
}

export interface PublicBuildSelectionChoiceRequest {
  optionId?: string | null;
  comment?: string | null;
}

export interface PublicBuildSelectionDto {
  id: string;
  title: string;
  description?: string | null;
  stageName?: string | null;
  deadline?: string | null;
  deadlinePassed: boolean;
  status: string;
  canChoose: boolean;
  chosenOptionId?: string | null;
  clientComment?: string | null;
  decidedAt?: string | null;
  options: PublicBuildSelectionOptionDto[];
  imageUrlsExpireAt?: string | null;
}

export interface PublicBuildSelectionOptionDto {
  id: string;
  name: string;
  description?: string | null;
  priceDelta: number;
  isStandard: boolean;
  imageUrl?: string | null;
  leadTimeDays?: number | null;
}

export interface PublicBuildSelectionsDto {
  items: PublicBuildSelectionDto[];
}

export interface PublicBuildSessionDto {
  token: string;
  expiresAt: string;
}

export interface PublicBuildStageDto {
  name: string;
  order: number;
  status: string;
  actualPercent: number;
}

export interface PublicBuildTimelineDto {
  items: PublicBuildTimelineItemDto[];
  nextBefore?: string | null;
}

export interface PublicBuildTimelineItemDto {
  type: string;
  occurredAt: string;
  date?: string | null;
  kind?: string | null;
  title?: string | null;
  text?: string | null;
  stageName?: string | null;
  percent?: number | null;
  previousPercent?: number | null;
  photoUrl?: string | null;
  caption?: string | null;
  photoCount?: number | null;
}

export interface PublicBuildTwinDto {
  geometrySource?: string | null;
  modelName?: string | null;
  discipline?: string | null;
  viewerUrl?: string | null;
  viewerExpiresAt?: string | null;
  zones: PublicBuildTwinZoneDto[];
  state: PublicBuildTwinStateDto;
  minDate?: string | null;
  maxDate: string;
  disclaimer: string;
  elementIndex?: PublicBuildTwinIndexDto | null;
}

export interface PublicBuildTwinGroupDto {
  name: string;
  elementIds: string[];
  order?: number | null;
}

export interface PublicBuildTwinIndexDto {
  stages: PublicBuildTwinGroupDto[];
  categories: PublicBuildTwinGroupDto[];
  zones: Record<string, string[]>;
}

export interface PublicBuildTwinStateDto {
  date: string;
  counts: Record<string, number>;
  states: Record<string, string[]>;
  zoneStates: Record<string, string[]>;
  unbound: number;
}

export interface PublicBuildTwinZoneDto {
  id: string;
  kind: string;
  name: string;
  parentId?: string | null;
  elevation?: number | null;
  order: number;
  simplifiedBox?: BuildSimplifiedBox | null;
  externalId?: string | null;
}

export interface PublicBuildUpdateDto {
  kind: string;
  title: string;
  body: string;
  publishedAt: string;
}

export interface PublicBuildWalkthroughDto {
  status: string;
  scheduledFor?: string | null;
  completedAt?: string | null;
  totalItems: number;
  okItems: number;
  pendingItems: number;
  items: PublicBuildWalkthroughItemDto[];
}

export interface PublicBuildWalkthroughItemDto {
  text: string;
  result: string;
}

export interface PublicBuildWarrantyDto {
  items: PublicBuildWarrantyTermDto[];
  notice: string;
}

export interface PublicBuildWarrantyTermDto {
  system: string;
  months: number;
  startsAt: string;
  expiresAt: string;
  isActive: boolean;
  description?: string | null;
}

export interface PublicBuildWeatherDto {
  date: string;
  temperatureMax: number;
  temperatureMin: number;
  precipitationMm: number;
  description: string;
  source: string;
  notice: string;
}
