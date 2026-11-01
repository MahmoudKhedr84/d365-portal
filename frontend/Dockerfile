# ---- build ----
FROM node:22-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY . .
RUN npm run build

# ---- serve ----
FROM nginx:1.27-alpine
COPY nginx.conf.template /etc/nginx/templates/default.conf.template
COPY security-headers.conf /etc/nginx/snippets/security-headers.conf
COPY docker-entrypoint.d/40-app-config.sh /docker-entrypoint.d/40-app-config.sh
COPY --from=build /app/dist /usr/share/nginx/html
RUN chmod +x /docker-entrypoint.d/40-app-config.sh

# Runtime settings (see .env.example at repo root)
ENV API_UPSTREAM=http://api:8080 \
    ENTRA_CLIENT_ID= \
    ENTRA_AUTHORITY=https://login.microsoftonline.com/organizations \
    API_SCOPE= \
    API_BASE_URL=/api

EXPOSE 8080
