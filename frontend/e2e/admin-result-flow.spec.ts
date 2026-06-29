import { expect, test } from '@playwright/test'

test('admin resolves, confirms and publishes the winner without duplicate points', async ({ page, request }) => {
  await request.post('http://127.0.0.1:5080/e2e/reset')
  await request.put('http://127.0.0.1:5080/me/profile', {
    headers: { Authorization: 'Bearer e2e-user' },
    data: { givenName: 'Ana', familyName: 'Silva' },
  })
  await request.put('http://127.0.0.1:5080/matches/match-e2e/prediction', {
    headers: { Authorization: 'Bearer e2e-user' },
    data: {
      homeGoals: 2, awayGoals: 1, firstScorerKey: 'BRA:11',
      homeTopScorerKey: 'BRA:11', awayTopScorerKey: 'MEX:9',
      homeYellowCards: 2, awayYellowCards: 3, homeRedCards: 0, awayRedCards: 1,
    },
  })

  await page.goto('/')
  await page.evaluate(() => localStorage.setItem('bolao-e2e-token', 'e2e-admin'))
  await page.goto('/admin?matchId=match-e2e')
  await expect(page.getByText('Ranking provisório')).toBeVisible()
  await page.getByRole('combobox', { name: /associar raphinha api/i }).click()
  await page.getByRole('combobox', { name: /associar raphinha api/i }).fill('Raphinha')
  await page.getByRole('option', { name: 'Raphinha' }).click()
  await page.getByRole('button', { name: 'Salvar correções' }).click()
  await expect(page.getByRole('button', { name: 'Confirmar resultado' })).toBeEnabled()
  await page.getByRole('button', { name: 'Confirmar resultado' }).click()
  await page.getByRole('button', { name: 'Confirmar', exact: true }).click()

  await page.evaluate(() => localStorage.setItem('bolao-e2e-token', 'e2e-user'))
  await page.goto('/')
  await expect(page.getByText('Vencedor da rodada')).toBeVisible()
  await expect(page.getByLabel('Primeiro lugar')).toBeVisible()
  const points = await page.getByText(/pts$/).first().textContent()
  await page.reload()
  await expect(page.getByText(points!)).toBeVisible()
})
