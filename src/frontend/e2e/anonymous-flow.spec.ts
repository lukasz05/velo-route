import { expect, test, type Download, type Page } from '@playwright/test'
import { geocodeResponse, loopRouteResponse, stubMapStyle } from './fixtures/route'

async function readDownload(download: Download): Promise<string> {
  const stream = await download.createReadStream()
  const chunks: Buffer[] = []
  for await (const chunk of stream) chunks.push(Buffer.from(chunk))
  return Buffer.concat(chunks).toString('utf8')
}

async function stubExternalHops(page: Page): Promise<void> {
  // The load-bearing block: clerk-js never loads, reproducing risk #7's mechanism.
  // The synthetic publishable key decodes to this host, so the glob is exact and does
  // not depend on Clerk's bundle filename.
  await page.route('https://clerk.example.com/**', route => route.abort())

  // Geocode and loop are the only hops that depend on ORS, so mocking them removes the
  // sole secret dependency. `/api/routes/gpx` is deliberately NOT routed — it must reach
  // the real .NET backend, which is what catches fixture-vs-backend contract drift.
  await page.route('**/api/geocode**', route =>
    route.fulfill({ json: geocodeResponse }),
  )
  await page.route('**/api/routes/loop', route =>
    route.fulfill({ json: loopRouteResponse }),
  )

  await page.route('https://tiles.openfreemap.org/**', route =>
    route.request().url().includes('/styles/liberty')
      ? route.fulfill({ json: stubMapStyle })
      : route.abort(),
  )
}

test.describe('anonymous flow', () => {
  // No `pageerror`/`console` failure hook: blocking clerk-js is designed into this spec
  // and legitimately produces a ClerkProvider console.error plus an unhandled rejection
  // from loadClerkJSScript. Failing on either would make the spec red on its own premise.
  test('a signed-out visitor searches, generates, and downloads GPX', async ({ page }) => {
    await stubExternalHops(page)

    await page.goto('/')

    // The "Start location" label has no htmlFor, so getByLabel does not reach the input.
    await page.getByRole('combobox').fill('Warszawa')
    await page.getByRole('option', { name: 'Warszawa, Mazowieckie, Poland' }).click()

    // Min/Max km stay at their 30/60 defaults. An enabled Generate button is the
    // assertion that the start point registered.
    const generate = page.getByRole('button', { name: 'Generate' })
    await expect(generate).toBeEnabled()
    await generate.click()

    await expect(page.getByText('Total distance')).toBeVisible()
    await expect(page.getByText('42.5 km')).toBeVisible()
    await expect(page.getByText('Surface quality')).toBeVisible()
    await expect(page.getByText('90% paved')).toBeVisible()

    // The route line itself is canvas and out of scope; a mounted map container is the
    // proxy for "the map rendered".
    await expect(page.locator('.maplibregl-map')).toBeVisible()

    // What makes this an *anonymous* run rather than merely an unauthenticated one: the
    // Save block is gated on Clerk reporting a session, so its absence is the assertion
    // that would catch Clerk misreporting one.
    await expect(page.getByLabel('Name')).toHaveCount(0)
    await expect(page.getByLabel('Tags')).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'Save' })).toHaveCount(0)

    const downloadPromise = page.waitForEvent('download')
    await page.getByRole('button', { name: 'Download GPX' }).click()
    const download = await downloadPromise

    expect(download.suggestedFilename()).toMatch(/^veloroute-\d{8}T\d{6}\.gpx$/)
    expect(await readDownload(download)).toContain('<trkpt')
  })
})
