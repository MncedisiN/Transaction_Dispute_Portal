FROM node:24-alpine AS build
WORKDIR /src
RUN corepack enable && corepack prepare pnpm@11.25.0 --activate
COPY package.json pnpm-lock.yaml pnpm-workspace.yaml ./
RUN pnpm install --frozen-lockfile
COPY . .
RUN pnpm build

FROM build AS test
RUN pnpm typecheck && pnpm lint

FROM nginxinc/nginx-unprivileged:stable-alpine AS runtime
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY nginx-routes.conf /etc/nginx/portal-routes.conf
COPY --from=build /src/dist /usr/share/nginx/html
EXPOSE 8080 8081 8443
HEALTHCHECK --interval=20s --timeout=5s --start-period=10s --retries=3 CMD wget -qO- http://127.0.0.1:8080/health-web >/dev/null || exit 1
