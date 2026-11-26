import { defineConfig, devices } from '@playwright/test'

const port = process.env.E2E_PORT ?? '3100'

export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [['github'], ['html', { open: 'never' }]] : 'list',
  use: {
    baseURL: `http://localhost:${port}`,
    trace: 'on-first-retry',
    locale: 'nl-BE',
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
    env: { PORT: port, HOST: '127.0.0.1' },
    timeout: 60_000,
  },
})
