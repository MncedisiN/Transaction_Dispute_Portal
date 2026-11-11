import { defineConfig, devices } from '@playwright/test'
import base from './playwright.config'

export default defineConfig(base, {
  testMatch: [
    '**/mobile-refinement.spec.ts',
    '**/banking-polish.spec.ts',
    '**/consumer-ui.spec.ts',
    '**/policy-dialogs.spec.ts',
    '**/frontend-authority.spec.ts',
    '**/screens-and-dialogs.spec.ts'
  ],
  use: {
    ...devices['iPhone 13'],
    browserName: 'webkit',
    channel: undefined,
    serviceWorkers: 'block'
  }
})
