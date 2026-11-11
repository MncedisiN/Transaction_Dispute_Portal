# Dispute Portal frontend

Vue 3, TypeScript, Vue Router and Pinia. The API owns permissions, financial rules and configurable reference data. Fixed interface wording belongs in Vue. Official W3.CSS and the shared portal stylesheet provide presentation; the red shield assets are packaged locally.

Use the [root Docker instructions](../README.md) to start the complete application. Optional frontend development commands, from this folder:

~~~sh
pnpm install --frozen-lockfile
pnpm start
pnpm typecheck
pnpm lint
pnpm build
~~~

The Vite proxy configuration is in config/apiProxy.mjs. Docker uses Nginx and its internal API hostname instead. Browser checks are in e2e; install Playwright Chromium, serve a build preview on port8080, then run pnpm test or pnpm test:ui. These focused tests use API fixtures and do not prove real OTP or ledger delivery.

Vue Router preserves SPA navigation. The PWA service worker caches the public shell, not authenticated API responses. Offline financial operations require the API. Web push additionally requires browser permission and a secure context; the localhost assessment profile does not create a trusted mobile HTTPS certificate.
