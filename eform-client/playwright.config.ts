import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: 'playwright/e2e',
  workers: 1,
  // Retries are FORBIDDEN in this suite. A retry hides the failure it is
  // covering for, and with a suite this slow it also doubles the feedback loop.
  // Flakiness is fixed at the cause instead — see CLAUDE.md at the repo root.
  retries: 0,
  use: {
    baseURL: 'http://localhost:4200',
    viewport: { width: 1920, height: 1080 },
    video: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  reporter: [
    ['html'],
    ['json', { outputFile: 'playwright-results/results.json' }],
  ],
  timeout: 120000,
});
