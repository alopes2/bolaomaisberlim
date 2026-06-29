import { expect, test, type Page } from '@playwright/test'

async function signIn(page: Page) {
  await page.goto('/')
  await page.getByRole('button', { name: 'Entrar com Google' }).click()
}

async function completeProfile(page: Page) {
  await page.getByLabel('Nome', { exact: true }).fill('Ana')
  await page.getByLabel('Sobrenome').fill('Silva')
  await page.getByRole('button', { name: 'Salvar perfil' }).click()
  await page.getByRole('button', { name: 'Continuar' }).click()
}

async function choose(page: Page, label: RegExp, player: string) {
  await page.getByRole('combobox', { name: label }).click()
  await page.getByRole('combobox', { name: label }).fill(player)
  await page.getByRole('option', { name: new RegExp(player, 'i') }).click()
}

test('participant submits, edits and sees the locked public state', async ({ page, request }) => {
  await request.post('http://127.0.0.1:5080/e2e/reset')
  await signIn(page)
  await completeProfile(page)

  await page.getByRole('spinbutton', { name: 'Brasil', exact: true }).fill('2')
  await page.getByRole('spinbutton', { name: 'México', exact: true }).fill('1')
  await choose(page, /primeiro gol/i, 'Raphinha')
  await choose(page, /artilheiro brasil/i, 'Raphinha')
  await choose(page, /artilheiro méxico/i, 'Raúl Jiménez')
  await page.getByRole('button', { name: 'Salvar palpite' }).click()
  await expect(page.getByText(/último envio/i)).toBeVisible()

  await page.getByRole('spinbutton', { name: 'Brasil', exact: true }).fill('3')
  await page.getByRole('button', { name: 'Salvar palpite' }).click()
  await expect(page.getByText(/último envio/i)).toBeVisible()

  await request.post('http://127.0.0.1:5080/e2e/close')
  await page.reload()
  await expect(page.getByRole('button', { name: 'Palpites encerrados' })).toBeDisabled()
  await expect(page.getByText('Bruno B.')).toBeVisible()
})
