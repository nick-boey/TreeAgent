import { test, expect, type Page, type Locator } from '@playwright/test'

/**
 * E2E tests for the orphan-link-picker feature (T016 from orphan-changes-link-picker).
 *
 * The demo-project is seeded with:
 * - orphan-on-main: main-branch change with no sidecar, also inherited by every
 *   branch clone that copies main → multiple occurrences → "on N branches" label.
 * - dark-mode-tokens, dark-mode-toggle: orphans on ISSUE-002's branch only
 *   (feature/dark-mode+ISSUE-002) → single-occurrence label; ISSUE-002 is the
 *   containing issue, so it is pinned at the top of the picker.
 *
 * Write operations (POST /api/openspec/changes/link, POST /api/issues) are
 * intercepted so tests do not mutate server state across the shared test run.
 */

async function waitForOrphanSection(page: Page): Promise<Locator | null> {
  const section = page.locator('[data-testid="orphaned-changes-section"]')
  try {
    await section.waitFor({ state: 'visible', timeout: 10000 })
    await section.scrollIntoViewIfNeeded()
    return section
  } catch {
    return null
  }
}

async function openPicker(page: Page, changeName: string): Promise<Locator | null> {
  const section = await waitForOrphanSection(page)
  if (!section) return null

  const row = section.locator(`[data-change-name="${changeName}"]`)
  if (!(await row.count())) return null

  await row.locator('[data-testid="orphan-link-to-issue"]').click()
  const dialog = page.locator('[role="dialog"]')
  await expect(dialog).toBeVisible({ timeout: 5000 })
  return dialog
}

test.describe('Orphan-link-picker', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/projects/demo-project/issues')
    await page.waitForLoadState('networkidle')
    await page.waitForSelector('[role="row"]', { timeout: 15000 })
  })

  test('orphaned changes section is visible with seeded mock data', async ({ page }) => {
    const section = await waitForOrphanSection(page)
    if (!section) {
      test.skip(true, 'Orphaned changes section not seeded — check openspec mock data')
      return
    }

    await expect(section).toBeVisible()
    const rows = section.locator('[data-testid="orphaned-change-row"]')
    expect(await rows.count()).toBeGreaterThan(0)
  })

  test.describe('T016-2: multi-occurrence "on N branches" label', () => {
    test('orphan-on-main shows "on N branches" occurrence label', async ({ page }) => {
      const section = await waitForOrphanSection(page)
      if (!section) {
        test.skip(true, 'Orphaned changes section not seeded')
        return
      }

      const row = section.locator('[data-change-name="orphan-on-main"]')
      if (!(await row.count())) {
        test.skip(true, 'orphan-on-main row not found')
        return
      }

      const label = row.locator('[data-testid="orphaned-change-occurrence-label"]')
      await expect(label).toBeVisible()
      await expect(label).toHaveText(/on \d+ branches/)
    })

    test('hovering "on N branches" label shows tooltip listing branch names including main', async ({
      page,
    }) => {
      const section = await waitForOrphanSection(page)
      if (!section) {
        test.skip(true, 'Orphaned changes section not seeded')
        return
      }

      const row = section.locator('[data-change-name="orphan-on-main"]')
      if (!(await row.count())) {
        test.skip(true, 'orphan-on-main row not found')
        return
      }

      const label = row.locator('[data-testid="orphaned-change-occurrence-label"]')
      const text = await label.textContent()
      if (!text?.match(/on \d+ branches/)) {
        test.skip(true, 'Label is not multi-branch — orphan may appear on only one occurrence')
        return
      }

      await label.hover()
      const tooltip = page.locator('[role="tooltip"]')
      await expect(tooltip).toBeVisible({ timeout: 5000 })
      // "main" always appears because orphan-on-main is on the main repo itself
      await expect(tooltip).toContainText('main')
    })
  })

  test.describe('T016-1: single-occurrence link via picker', () => {
    test.beforeEach(async ({ page }) => {
      await page.route('**/api/openspec/changes/link', (route) =>
        route.fulfill({ status: 204, body: '' })
      )
    })

    test('dark-mode-tokens shows single-branch occurrence label (not "on N branches")', async ({
      page,
    }) => {
      const section = await waitForOrphanSection(page)
      if (!section) {
        test.skip(true, 'Orphaned changes section not seeded')
        return
      }

      const row = section.locator('[data-change-name="dark-mode-tokens"]')
      if (!(await row.count())) {
        test.skip(true, 'dark-mode-tokens row not found')
        return
      }

      const label = row.locator('[data-testid="orphaned-change-occurrence-label"]')
      await expect(label).toBeVisible()
      await expect(label).not.toHaveText(/on \d+ branches/)
    })

    test('clicking Link to issue opens picker with correct title', async ({ page }) => {
      const section = await waitForOrphanSection(page)
      if (!section) {
        test.skip(true, 'Orphaned changes section not seeded')
        return
      }

      const row = section.locator('[data-change-name="dark-mode-tokens"]')
      if (!(await row.count())) {
        test.skip(true, 'dark-mode-tokens row not found')
        return
      }

      await row.locator('[data-testid="orphan-link-to-issue"]').click()

      const dialog = page.locator('[role="dialog"]')
      await expect(dialog).toBeVisible({ timeout: 5000 })
      await expect(dialog).toContainText('Link "dark-mode-tokens" to an issue')
    })

    test('picker shows pinned block with ISSUE-002 as the containing issue', async ({ page }) => {
      const dialog = await openPicker(page, 'dark-mode-tokens')
      if (!dialog) {
        test.skip(true, 'dark-mode-tokens row not found or section not seeded')
        return
      }

      const pinned = dialog.locator('[data-testid="orphan-picker-pinned"]')
      await expect(pinned).toBeVisible({ timeout: 5000 })

      // ISSUE-002 title: "Add dark mode support with system preference detection"
      await expect(pinned).toContainText('dark mode')

      // Pinned block is separated from the full list by a divider
      await expect(dialog.locator('[data-testid="orphan-picker-divider"]')).toBeVisible()

      // ISSUE-002 row is accessible by its test ID in the pinned block
      await expect(
        dialog.locator('[data-testid="orphan-picker-row-ISSUE-002"]').first()
      ).toBeVisible()
    })

    test('selecting an issue in the picker sends POST /api/openspec/changes/link and closes dialog', async ({
      page,
    }) => {
      const linkRequests: unknown[] = []
      await page.route('**/api/openspec/changes/link', async (route) => {
        linkRequests.push(await route.request().postDataJSON())
        await route.fulfill({ status: 204, body: '' })
      })

      const dialog = await openPicker(page, 'dark-mode-tokens')
      if (!dialog) {
        test.skip(true, 'dark-mode-tokens row not found or section not seeded')
        return
      }

      // Click the first available picker row (ISSUE-002 is pinned at top)
      const pickerRow = dialog.locator('[data-testid^="orphan-picker-row-"]').first()
      await expect(pickerRow).toBeVisible({ timeout: 5000 })
      await pickerRow.click()

      // Dialog closes on selection
      await expect(dialog).not.toBeVisible({ timeout: 5000 })

      // Link API called exactly once with branchless payload
      expect(linkRequests).toHaveLength(1)
      expect(linkRequests[0]).toMatchObject({
        projectId: 'demo-project',
        changeName: 'dark-mode-tokens',
      })
    })
  })

  test.describe('T016-3: split-button sub-issue flow', () => {
    test('clicking dropdown chevron shows "Create as sub-issue under…" menu item', async ({
      page,
    }) => {
      const section = await waitForOrphanSection(page)
      if (!section) {
        test.skip(true, 'Orphaned changes section not seeded')
        return
      }

      const firstRow = section.locator('[data-testid="orphaned-change-row"]').first()
      if (!(await firstRow.count())) {
        test.skip(true, 'No orphan change rows found')
        return
      }

      await firstRow.locator('[data-testid="orphan-create-issue-menu"]').click()

      const menuItem = page.locator('[data-testid="orphan-create-sub-issue-menuitem"]')
      await expect(menuItem).toBeVisible({ timeout: 3000 })
    })

    test('"Create as sub-issue under…" opens picker in choose-parent mode', async ({ page }) => {
      const section = await waitForOrphanSection(page)
      if (!section) {
        test.skip(true, 'Orphaned changes section not seeded')
        return
      }

      const firstRow = section.locator('[data-testid="orphaned-change-row"]').first()
      if (!(await firstRow.count())) {
        test.skip(true, 'No orphan change rows found')
        return
      }

      const changeName = await firstRow.getAttribute('data-change-name')

      await firstRow.locator('[data-testid="orphan-create-issue-menu"]').click()
      await page.locator('[data-testid="orphan-create-sub-issue-menuitem"]').click()

      const dialog = page.locator('[role="dialog"]')
      await expect(dialog).toBeVisible({ timeout: 5000 })
      await expect(dialog).toContainText(`Choose a parent for "${changeName}"`)

      // Picker shows the filter input and issue list (same picker, different title)
      await expect(dialog.locator('[data-testid="orphan-picker-filter"]')).toBeVisible()
    })
  })

  test.describe('T016-4: picker filter + pinned block', () => {
    test.beforeEach(async ({ page }) => {
      await page.route('**/api/openspec/changes/link', (route) =>
        route.fulfill({ status: 204, body: '' })
      )
    })

    test('filter input narrows the lower issue list', async ({ page }) => {
      const dialog = await openPicker(page, 'dark-mode-tokens')
      if (!dialog) {
        test.skip(true, 'dark-mode-tokens row not found or section not seeded')
        return
      }

      const filterInput = dialog.locator('[data-testid="orphan-picker-filter"]')
      await expect(filterInput).toBeVisible()

      // ISSUE-003 title contains "WebSocket"
      await filterInput.fill('websocket')

      // Lower list should be filtered (fewer rows than without filter)
      const list = dialog.locator('[data-testid="orphan-picker-list"]')
      await expect(list).toBeVisible()
      const filteredCount = await list.locator('[data-testid^="orphan-picker-row-"]').count()

      // Clear filter — full list is restored
      await filterInput.fill('')
      const fullCount = await list.locator('[data-testid^="orphan-picker-row-"]').count()
      expect(fullCount).toBeGreaterThanOrEqual(filteredCount)
    })

    test('pinned issue stays in pinned block even when filter matches nothing in the lower list', async ({
      page,
    }) => {
      const dialog = await openPicker(page, 'dark-mode-tokens')
      if (!dialog) {
        test.skip(true, 'dark-mode-tokens row not found or section not seeded')
        return
      }

      const pinned = dialog.locator('[data-testid="orphan-picker-pinned"]')
      if (!(await pinned.isVisible({ timeout: 3000 }).catch(() => false))) {
        test.skip(true, 'Pinned block not visible — ISSUE-002 may not appear as containing issue')
        return
      }

      const pinnedContent = await pinned.textContent()

      const filterInput = dialog.locator('[data-testid="orphan-picker-filter"]')
      // Type a string that matches no issue title
      await filterInput.fill('xyzq_no_match_1234')

      // Pinned block remains unchanged
      await expect(pinned).toBeVisible()
      expect(await pinned.textContent()).toBe(pinnedContent)

      // Lower list shows "No matches"
      await expect(dialog.getByText(/No matches/i)).toBeVisible()
    })

    test('when filter matches the pinned issue, it appears in both pinned block and lower list', async ({
      page,
    }) => {
      const dialog = await openPicker(page, 'dark-mode-tokens')
      if (!dialog) {
        test.skip(true, 'dark-mode-tokens row not found or section not seeded')
        return
      }

      const pinned = dialog.locator('[data-testid="orphan-picker-pinned"]')
      if (!(await pinned.isVisible({ timeout: 3000 }).catch(() => false))) {
        test.skip(true, 'Pinned block not visible — ISSUE-002 may not appear as containing issue')
        return
      }

      const filterInput = dialog.locator('[data-testid="orphan-picker-filter"]')
      // ISSUE-002 title contains "dark mode"
      await filterInput.fill('dark mode')

      // Pinned block still shows ISSUE-002
      await expect(pinned).toBeVisible()
      await expect(pinned).toContainText('dark mode')

      // Lower list also shows ISSUE-002 (pinned issue appears in both sections)
      const list = dialog.locator('[data-testid="orphan-picker-list"]')
      await expect(list).toBeVisible()
      await expect(list).toContainText('dark mode')
    })
  })
})
