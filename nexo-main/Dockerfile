# syntax=docker/dockerfile:1
# ── Build stage ───────────────────────────────────────────────────────────────
FROM node:20-alpine AS build
WORKDIR /app

# Install deps first for better layer caching.
COPY package.json package-lock.json ./
RUN npm ci

# Build the production bundle. `vite build` runs in production mode and reads
# the committed .env.production (bakes VITE_API_BASE_URL into the bundle).
COPY . .
RUN npm run build

# ── Runtime stage — Caddy serves the static bundle ────────────────────────────
FROM caddy:2-alpine
COPY Caddyfile /etc/caddy/Caddyfile
COPY --from=build /app/dist /srv
# Railway injects $PORT at runtime; the Caddyfile listens on it.
EXPOSE 8080
