# PrintOS Web

Nuxt 3 / Vue 3 / TypeScript PWA, mobile-first (iPhone look). SPA mode, installable via the web manifest and service worker.

## Stack

Nuxt 3, Pinia, `@nuxtjs/i18n` (Dutch default, English), `@vite-pwa/nuxt`, ESLint, Vitest, `openapi-fetch` typed API client.

## Commands

```powershell
npm install
npm run dev          # http://localhost:3000
npm run lint
npm run typecheck
npm test             # unit tests (Vitest)
npm run build
npm run test:e2e     # Playwright smoke tests, mobile (iPhone 14) and desktop; needs a build first
```

First time only: `npx playwright install chromium`.

## API client

`useApi()` returns an `openapi-fetch` client typed from `types/api.d.ts`. Regenerate the types with the API running on port 5000:

```powershell
npm run api:types
```

The API base URL comes from `NUXT_PUBLIC_API_BASE` (default `http://localhost:5000`).

## Authentication

`stores/auth.ts` holds the session (access and refresh token in `localStorage`, key `printos_session`), refreshes the access token a minute before it expires and clears it on sign-out. `middleware/auth.global.ts` sends signed-out visitors to `/sign-in` and signed-in visitors away from it; mark a page `public: true` in `definePageMeta` to opt out. The sign-in and create-account screen is one page (`pages/sign-in.vue`, layout `auth`). End-to-end tests mock the API (`e2e/support.ts`), so they need no backend.

## Conventions

- All user-facing text lives in `i18n/locales/nl.json` and `en.json`.
- Loading, empty and error states use `StateLoading`, `StateEmpty` and `StateError`.
- Respect safe-area insets; test at 375px width.
