import { defineConfig, devices } from '@playwright/test'

export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [['github'], ['html', { open: 'never' }]] : 'list',
  use: {
    baseURL: 'http://localhost:3100',
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
    url: 'http://localhost:3100',
    reuseExistingServer: !process.env.CI,
    env: { PORT: '3100', HOST: '127.0.0.1' },
    timeout: 60_000,
  },
})
