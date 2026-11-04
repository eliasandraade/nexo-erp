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
# O Caddy roda sem root: a porta é alta ($PORT, 8080 por padrão), o admin está desligado e só /config e /data
# (autosave da configuração e estado do Caddy) precisam de escrita. O bundle e o Caddyfile ficam somente leitura.
RUN addgroup -S -g 10001 caddy \
    && adduser -S -D -H -u 10001 -G caddy caddy \
    && chown -R caddy:caddy /config /data
COPY Caddyfile /etc/caddy/Caddyfile
COPY --from=build /app/dist /srv
# Railway injects $PORT at runtime; the Caddyfile listens on it.
EXPOSE 8080
USER caddy
