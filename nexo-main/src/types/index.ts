/**
 * Shared application types.
 */

/** Generic paginated response from the API */
export interface PaginatedResponse<T> {
  data: T[];
  total: number;
  page: number;
  pageSize: number;
}

/** Common entity base */
export interface BaseEntity {
  id: string;
  createdAt: string;
  updatedAt: string;
}
