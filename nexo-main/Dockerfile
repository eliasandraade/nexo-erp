# syntax=docker/dockerfile:1
# ── Build stage ───────────────────────────────────────────────────────────────
# Debian-slim (glibc) for the widest native-dep compatibility.
FROM node:20-slim AS build
WORKDIR /app

# Install deps first for better layer caching — exactly the versions in the committed lockfile.
# The lockfile is generated on Linux (`npm install --package-lock-only` in node:20-slim), so it
# lists the native binaries of every platform (Rollup's linux-x64-gnu included) and `npm ci` works
# here and on Windows alike (npm optional-deps bug #4828 hits locks generated on a single OS).
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund --ignore-scripts

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
