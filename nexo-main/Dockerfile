# syntax=docker/dockerfile:1
# ── Build stage ───────────────────────────────────────────────────────────────
# Debian-slim (glibc) for the widest native-dep compatibility.
FROM node:20-slim AS build
WORKDIR /app

# Install deps first for better layer caching.
# Intentionally install WITHOUT package-lock.json: the committed lockfile is
# generated on Windows and omits the Linux Rollup native binary, so both `npm
# ci` and a lockfile-respecting `npm install` skip @rollup/rollup-linux-x64-gnu
# and `vite build` crashes (npm optional-deps bug #4828). Resolving fresh lets
# npm pick the correct platform binary.
# Tradeoff: deps resolve to latest semver-compatible versions rather than the
# exact locked ones. To restore `npm ci` reproducibility, regenerate a
# cross-platform package-lock.json (npm install on any OS with npm >= 10).
COPY package.json ./
RUN npm install --no-audit --no-fund

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
