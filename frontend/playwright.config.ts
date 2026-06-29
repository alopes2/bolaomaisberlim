import { defineConfig, devices } from '@playwright/test'

export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  workers: 1,
  use: {
    baseURL: 'http://127.0.0.1:4173',
    trace: 'retain-on-failure',
  },
  projects: [
    {
      name: 'mobile-chromium',
      use: { ...devices['Desktop Chrome'], viewport: { width: 390, height: 844 } },
    },
  ],
  webServer: [
    {
      command: 'ASPNETCORE_ENVIRONMENT=E2E ASPNETCORE_URLS=http://127.0.0.1:5080 dotnet run --project ../backend/src/Bolao.Functions --no-launch-profile',
      url: 'http://127.0.0.1:5080/matches/current',
      reuseExistingServer: true,
      timeout: 120_000,
    },
    {
      command: 'VITE_E2E=true VITE_API_BASE_URL=http://127.0.0.1:5080 npm run dev -- --host 127.0.0.1 --port 4173',
      url: 'http://127.0.0.1:4173',
      reuseExistingServer: true,
      timeout: 120_000,
    },
  ],
})
