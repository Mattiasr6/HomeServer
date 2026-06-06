FROM node:22-alpine AS build
WORKDIR /src

# Install dependencies first (layer caching)
COPY package.json package-lock.json ./
RUN npm ci

# Build the app
ARG NEXT_PUBLIC_API_URL
ENV NEXT_PUBLIC_API_URL=$NEXT_PUBLIC_API_URL
COPY . .
RUN npm run build

# --- Runtime stage ---
FROM node:22-alpine AS runtime
WORKDIR /app

# Standalone output includes server.js + bundled node_modules
COPY --from=build /src/.next/standalone ./
# Static assets are served separately by the standalone server
COPY --from=build /src/.next/static ./.next/static
# Public folder (favicon, etc.)
COPY --from=build /src/public ./public

# Create non-root user
RUN addgroup -S appgroup && adduser -S appuser -G appgroup && \
    chown -R appuser:appgroup /app

# Healthcheck for Coolify zero-downtime deployments
RUN apk add --no-cache curl
HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
  CMD curl -f http://localhost:3000 || exit 1

EXPOSE 3000
ENV PORT=3000
ENV NODE_ENV=production

USER appuser

CMD ["node", "server.js"]
