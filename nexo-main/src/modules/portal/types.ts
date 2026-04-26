// ── Public menu types ─────────────────────────────────────────────────────────

export interface PublicModifierDto {
  id:              string;
  name:            string;
  priceAdjustment: number;
}

export interface PublicModifierGroupDto {
  id:            string;
  name:          string;
  isRequired:    boolean;
  minSelections: number;
  maxSelections: number;
  options:       PublicModifierDto[];
}

export interface PublicMenuProductDto {
  id:             string;
  name:           string;
  description:    string | null;
  price:          number;
  imageUrl:       string | null;
  modifierGroups: PublicModifierGroupDto[];
}

export interface PublicMenuCategoryDto {
  id:       string | null;
  name:     string;
  sortOrder: number;
  products: PublicMenuProductDto[];
}

export interface PublicMenuDto {
  storeName:         string;
  description:       string | null;
  logoUrl:           string | null;
  coverImageUrl:     string | null;
  whatsAppPhone:     string | null;
  businessHoursJson: string | null;
  acceptingOrders:   boolean;
  deliveryEnabled:   boolean;
  takeawayEnabled:   boolean;
  categories:        PublicMenuCategoryDto[];
}

// ── Order tracking types ──────────────────────────────────────────────────────

export interface OrderTrackingDto {
  orderNumber:      number;
  status:           string;
  statusLabel:      string;
  estimatedMinutes: number | null;
  orderType:        string;
}

// ── Cart types ────────────────────────────────────────────────────────────────

export interface CartModifier {
  modifierId: string;
  label:      string;
  price:      number;
}

export interface CartItem {
  productId:      string;
  productName:    string;
  price:          number;
  quantity:       number;
  notes:          string;
  modifiers:      CartModifier[];
}
