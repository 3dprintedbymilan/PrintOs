export default defineNuxtConfig({
  compatibilityDate: '2026-11-01',
  devtools: { enabled: false },
  ssr: false,
  modules: ['@nuxt/eslint', '@pinia/nuxt', '@nuxtjs/i18n', '@vite-pwa/nuxt'],
  css: ['~/assets/css/base.css'],
  runtimeConfig: {
    public: {
      apiBase: 'http://localhost:5000',
    },
  },
  i18n: {
    defaultLocale: 'nl',
    strategy: 'no_prefix',
    locales: [
      { code: 'nl', language: 'nl-BE', name: 'Nederlands', file: 'nl.json' },
      { code: 'en', language: 'en-GB', name: 'English', file: 'en.json' },
    ],
    detectBrowserLanguage: {
      useCookie: true,
      cookieKey: 'printos_locale',
      fallbackLocale: 'nl',
    },
  },
  app: {
    head: {
      title: 'PrintOS',
      viewport: 'width=device-width, initial-scale=1, viewport-fit=cover',
      meta: [
        { name: 'theme-color', content: '#06130f' },
        { name: 'apple-mobile-web-app-capable', content: 'yes' },
        { name: 'apple-mobile-web-app-status-bar-style', content: 'black-translucent' },
        { name: 'apple-mobile-web-app-title', content: 'PrintOS' },
      ],
      link: [{ rel: 'apple-touch-icon', href: '/icons/icon-192.png' }],
    },
  },
  pwa: {
    registerType: 'autoUpdate',
    manifest: {
      name: 'PrintOS',
      short_name: 'PrintOS',
      description: 'Manage your 3D printing business, from files to profit.',
      theme_color: '#06130f',
      background_color: '#06130f',
      display: 'standalone',
      orientation: 'portrait',
      start_url: '/',
      icons: [
        { src: '/icons/icon-192.png', sizes: '192x192', type: 'image/png' },
        { src: '/icons/icon-512.png', sizes: '512x512', type: 'image/png' },
        { src: '/icons/icon-512.png', sizes: '512x512', type: 'image/png', purpose: 'maskable' },
      ],
    },
    generateSW: {
      navigateFallback: '/',
    },
    devOptions: { enabled: false },
  },
})
