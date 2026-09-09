import { expect, test } from '@playwright/test'

test('the app shell serves', async ({ page }) => {
  await page.goto('/')

  await expect(page.getByRole('heading', { name: 'VeloRoute' })).toBeVisible()
})
