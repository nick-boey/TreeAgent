import { test, expect } from '@playwright/test'
import { createMockSession } from '../utils/test-helpers'

/**
 * FI-1 / US6: exercise the stop, interrupt, and clear-context affordances on a
 * session and assert the UI reflects the resulting state.
 */
test.describe('US6 — stop a session', () => {
  test('clicking Stop transitions the session out of running state', async ({
    page,
    request,
  }, testInfo) => {
    testInfo.setTimeout(60000)

    const sessionId = await createMockSession(request)

    await page.goto(`/sessions/${sessionId}`)
    await page.waitForLoadState('networkidle')

    await expect(page.getByPlaceholder('Type a message...')).toBeEnabled({ timeout: 15000 })

    const stopButton = page.getByTestId('session-stop')
    await expect(stopButton).toBeVisible({ timeout: 10000 })
    await stopButton.click()

    // The header Stop button opens an AlertDialog confirmation; the actual
    // mutation only fires when the user confirms via the dialog's Stop button.
    const stopDialog = page.getByRole('alertdialog', { name: 'Stop Session' })
    await expect(stopDialog).toBeVisible({ timeout: 5000 })
    await stopDialog.getByRole('button', { name: 'Stop' }).click()

    // After confirming, the session detail view is either redirected to
    // /sessions (default UX) or the Stop button disappears because
    // `showStopButton` flips off. Assert one of those terminal states.
    await expect
      .poll(
        async () => {
          if (page.url().endsWith('/sessions')) {
            return 'redirected'
          }
          const stillVisible = await stopButton.isVisible().catch(() => false)
          return stillVisible ? 'visible' : 'hidden'
        },
        { timeout: 15000 }
      )
      .not.toEqual('visible')
  })
})

test.describe('US6 — interrupt a session', () => {
  test('clicking Interrupt on a plan-waiting session transitions it to waiting-for-input', async ({
    page,
    request,
  }, testInfo) => {
    testInfo.setTimeout(60000)

    // The 'plan' sentinel puts the session into waitingForPlanExecution, which
    // is one of the interruptible states that shows the Interrupt button.
    const sessionId = await createMockSession(request, { sendMessage: 'plan' })

    await page.goto(`/sessions/${sessionId}`)
    await page.waitForLoadState('networkidle')

    const interruptButton = page.getByTestId('session-interrupt')
    await expect(interruptButton).toBeVisible({ timeout: 15000 })
    await interruptButton.click()

    // After interrupt the session moves to waitingForInput; the Interrupt
    // button disappears (not an interruptible state) and the composer becomes
    // enabled so the user can send the next message.
    await expect(interruptButton).toHaveCount(0, { timeout: 15000 })
    await expect(page.getByPlaceholder('Type a message...')).toBeEnabled({ timeout: 10000 })
  })
})

test.describe('US6 — clear context', () => {
  test('clicking New Session starts a fresh session and navigates to it', async ({
    page,
    request,
  }, testInfo) => {
    testInfo.setTimeout(60000)

    const sessionId = await createMockSession(request)

    await page.goto(`/sessions/${sessionId}`)
    await page.waitForLoadState('networkidle')

    await expect(page.getByPlaceholder('Type a message...')).toBeEnabled({ timeout: 15000 })

    const newSessionButton = page.getByTestId('session-new')
    await expect(newSessionButton).toBeVisible({ timeout: 10000 })
    await newSessionButton.click()

    // After clear-context the server creates a new session and the client
    // navigates to /sessions/<newId>. The URL should change away from the
    // original session.
    await expect.poll(async () => page.url(), { timeout: 15000 }).not.toContain(sessionId)

    // The new session page should load its chat surface.
    await expect(page.getByPlaceholder('Type a message...')).toBeEnabled({ timeout: 15000 })
  })
})
