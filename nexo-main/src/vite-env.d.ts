/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_API_BASE_URL: string;
  /** "cookie": the refresh token in the cookie of the API and the access token in memory. Anything else: the storage of the normal mode. */
  readonly VITE_SESSION_MODE?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
