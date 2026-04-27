export interface StoreDto {
  id: string;
  name: string;
  slug: string;
  publicSlug: string | null;
  moduleKey?: string;
  status: string;
}
