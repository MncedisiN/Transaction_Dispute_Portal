import portalReferenceFixture from './fixtures/portal-reference.fixture.json' with { type: 'json' }
import { platformSettings } from './fixtures/platformSettings'
import { test, expect } from '@playwright/test'
test.use({ viewport: { width: 375, height: 800 }, serviceWorkers: 'block' })
// Keep the per-action timeout bounded; allow cold browser startup on development laptops.
test.setTimeout(240000)
function samplePdf() {
  const stream = 'BT /F1 20 Tf 30 100 Td (Synthetic paid-up letter) Tj ET'
  const objects = [
    '<< /Type /Catalog /Pages 2 0 R >>',
    '<< /Type /Pages /Kids [3 0 R] /Count 1 >>',
    '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 360 200] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>',
    '<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>',
    '<< /Length ' + stream.length + ' >>\nstream\n' + stream + '\nendstream'
  ]
  let content = '%PDF-1.4\n'
  const offsets = [0]
  for (const [index, object] of objects.entries()) {
    offsets.push(content.length)
    content += index + 1 + ' 0 obj\n' + object + '\nendobj\n'
  }
  const xref = content.length
  content +=
    'xref\n0 6\n0000000000 65535 f \n' +
    offsets
      .slice(1)
      .map((offset) => String(offset).padStart(10, '0') + ' 00000 n \n')
      .join('') +
    'trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n' +
    xref +
    '\n%%EOF'
  return Buffer.from(content)
}
// Browser fixtures verify rendering under production CSP. Repository tests enforce real permissions.
test('document preview works in both themes and revoked access prevents a fresh download', async ({
  page
}, testInfo) => {
  const id = '40000000-0000-0000-0000-000000000001',
    now = '2026-10-31T08:00:00Z'
  let revoked = false,
    downloads = 0
  await page.route('**/api/v1/**', async (route) => {
    if (new URL(route.request().url()).pathname === '/api/v1/portal/reference')
      return route.fulfill({ json: portalReferenceFixture })
    if (new URL(route.request().url()).pathname === '/api/v1/platform/settings')
      return route.fulfill({ json: platformSettings() })

    const path = new URL(route.request().url()).pathname
    let data: unknown = {}
    if (path.endsWith('/auth/me'))
      data = { id: 'customer', displayName: 'Alex Morgan', roles: ['customer'], permissions: [] }
    else if (path.endsWith('/auth/csrf')) data = { requestToken: 'test' }
    else if (path.endsWith('/accounts')) data = []
    else if (path.endsWith('/notifications')) data = { items: [], total: 0 }
    else if (path.endsWith('/evidence/doc/preview'))
      return route.fulfill({ contentType: 'application/pdf', body: samplePdf() })
    else if (path.endsWith('/evidence/doc')) {
      downloads++
      return route.fulfill({
        status: revoked ? 403 : 200,
        contentType: revoked ? 'application/problem+json' : 'application/octet-stream',
        body: revoked
          ? JSON.stringify({
              detail: 'Document access has expired. Ask a supervisor to review access.'
            })
          : samplePdf()
      })
    } else if (path.endsWith('/evidence'))
      data = [
        {
          id: 'doc',
          category: 'PaidUpLetter',
          fileName: 'synthetic-letter.pdf',
          contentType: 'application/pdf',
          sizeBytes: 600,
          uploadedBy: 'Alex',
          createdAt: now,
          canView: true,
          canManageAccess: true
        }
      ]
    else if (path.includes('/operations/cases/'))
      data = {
        id,
        queueType: 'Fraud',
        status: 'UnderReview',
        dueAt: now,
        assignedName: 'Specialist'
      }
    else if (path.startsWith('/api/v1/disputes/'))
      data = {
        dispute: {
          id,
          transactionId: 'tx',
          reference: 'DP-TEST-001',
          category: 'Unrecognized',
          status: 'UnderReview',
          createdAt: now,
          amount: 100,
          currency: 'ZAR'
        },
        merchantName: 'Example Shop',
        transactionAt: now,
        transactionReference: 'TEST-TX',
        history: [],
        notes: []
      }
    else if (path.startsWith('/api/v1/transactions/'))
      data = { id: 'tx', accountId: 'account', accountNumber: '••••4821' }
    return route.fulfill({ contentType: 'application/json', body: JSON.stringify(data) })
  })
  await page.goto('/disputes/' + id)
  await page.getByRole('button', { name: 'Evidence', exact: true }).click()
  await page.getByRole('button', { name: 'View', exact: true }).click()
  const dialog = page.getByRole('dialog', { name: 'Document Preview' })
  await expect(dialog.getByRole('button', { name: 'Download Document' })).toBeEnabled({
    timeout: 45000
  })
  await expect(dialog.locator('canvas')).toBeVisible()
  expect(
    await dialog.locator('canvas').evaluate((canvas: HTMLCanvasElement) => {
      const pixels = canvas.getContext('2d')!.getImageData(0, 0, canvas.width, canvas.height).data
      let dark = 0
      for (let i = 0; i < pixels.length; i += 4) if (pixels[i]! < 100 && pixels[i + 3]! > 0) dark++
      return dark
    })
  ).toBeGreaterThan(50)
  for (const dark of [false, true]) {
    await page.evaluate((value) => {
      document.body.classList.toggle('w3-dark-grey', value)
      document.body.classList.toggle('w3-light-grey', !value)
    }, dark)
    expect(
      await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)
    ).toBeTruthy()
    expect(
      await dialog.evaluate((element) => element.scrollWidth <= element.clientWidth)
    ).toBeTruthy()
    await page.screenshot({
      path: testInfo.outputPath(dark ? 'preview-dark.png' : 'preview-light.png')
    })
  }
  revoked = true
  await dialog.getByRole('button', { name: 'Download Document' }).click()
  await expect(
    dialog
      .getByRole('alert')
      .filter({ has: page.getByRole('button', { name: 'Dismiss Notification', exact: true }) })
  ).toContainText('access has expired')
  expect(downloads).toBe(1)
  await dialog.getByRole('button', { name: 'Close Document Preview' }).click()
  await expect(dialog).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Access', exact: true })).toHaveCount(0)
})
