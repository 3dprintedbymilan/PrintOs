import { defineConfig, devices } from '@playwright/test'

const port = process.env.E2E_PORT ?? '3100'
const apiBase = process.env.JOURNEY_API_BASE ?? 'http://localhost:5000'

export default defineConfig({
  testDir: './journey',
  timeout: 240_000,
  expect: { timeout: 15_000 },
  fullyParallel: false,
  workers: 2,
  forbidOnly: !!process.env.CI,
  retries: 0,
  reporter: process.env.CI ? [['github'], ['html', { open: 'never', outputFolder: 'playwright-report-journey' }]] : 'list',
  use: {
    baseURL: `http://localhost:${port}`,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    locale: 'nl-BE',
    acceptDownloads: true,
  },
  projects: [
    {
      name: 'mobile',
      use: { ...devices['iPhone 14'], defaultBrowserType: 'chromium' },
    },
    {
      name: 'desktop',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
  webServer: {
    command: 'node .output/server/index.mjs',
    url: `http://localhost:${port}`,
    reuseExistingServer: !process.env.CI,
    env: { PORT: port, HOST: '127.0.0.1', NUXT_PUBLIC_API_BASE: apiBase },
    timeout: 60_000,
  },
})
