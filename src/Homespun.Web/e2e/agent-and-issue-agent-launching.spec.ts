import { test, expect } from '@playwright/test'
import { clearIssueFilter } from './utils/test-helpers'

test.describe.serial('Agent and Issue Agent Launching', () => {
  test('selecting an issue and running a task agent with default skill', async ({
    page,
  }, testInfo) => {
    testInfo.setTimeout(60000)

    // Navigate to the issues page
    await page.goto('/projects/demo-project/issues')
    await page.waitForLoadState('networkidle')

    // Clear the default filter to show all issues
    await clearIssueFilter(page)

    // Select ISSUE-003 (not used by other E2E tests)
    const issueRow = page
      .locator('[data-testid="task-graph-issue-row"]')
      .filter({ hasText: 'WebSocket reconnection drops queued messages' })
      .first()
    await expect(issueRow).toBeVisible()
    await issueRow.click()

    // Click the toolbar Run Agent button
    await page.click('[data-testid="toolbar-run-agent"]')

    // Verify the Run Agent dialog opens
    const agentDialog = page.locator('[role="dialog"]').filter({ hasText: 'Run Agent' })
    await expect(agentDialog).toBeVisible({ timeout: 10000 })

    // Verify we're on the Task Agent tab
    const taskTab = agentDialog.locator('[data-testid="task-tab-content"]')
    await expect(taskTab).toBeVisible()

    // Verify skill picker is present and loaded
    const skillSelector = taskTab.locator('[aria-label="Select skill"]')
    await expect(skillSelector).toBeVisible({ timeout: 10000 })
    await expect(skillSelector).toBeEnabled()

    // Open and pick the first skill option (None — free-text only, always first)
    await skillSelector.click()
    const skillOption = page.getByRole('option').first()
    await skillOption.click()

    // Verify mode selector is present (added by PR #731)
    const modeSelector = taskTab.locator('[aria-label="Select mode"]')
    await expect(modeSelector).toBeVisible()

    // Verify model selector is present
    const modelSelector = taskTab.locator('[aria-label="Select model"]')
    await expect(modelSelector).toBeVisible()

    // Wait for Start Agent button to be enabled and click it
    const startButton = taskTab.getByRole('button', { name: 'Start Agent' })
    await expect(startButton).toBeEnabled({ timeout: 10000 })
    await startButton.click()

    // Dialog should close after launching (POST /api/issues/{issueId}/run returns 202)
    await expect(agentDialog).not.toBeVisible({ timeout: 15000 })

    // Should stay on the issues page
    await expect(page).toHaveURL(/\/projects\/demo-project\/issues/)
  })

  test('selecting an issue and running an issues agent', async ({ page }, testInfo) => {
    testInfo.setTimeout(60000)

    // Navigate to the issues page
    await page.goto('/projects/demo-project/issues')
    await page.waitForLoadState('networkidle')

    // Clear the default filter to show all issues
    await clearIssueFilter(page)

    // Select ISSUE-009 (not used by other E2E tests)
    const issueRow = page
      .locator('[data-testid="task-graph-issue-row"]')
      .filter({ hasText: 'Implement v2 issues endpoints' })
      .first()
    await expect(issueRow).toBeVisible()
    await issueRow.click()

    // Click the toolbar Run Agent button
    await page.click('[data-testid="toolbar-run-agent"]')

    // Verify the Run Agent dialog opens
    const agentDialog = page.locator('[role="dialog"]').filter({ hasText: 'Run Agent' })
    await expect(agentDialog).toBeVisible({ timeout: 10000 })

    // Switch to the Issues Agent tab
    await agentDialog.getByRole('tab', { name: /issues agent/i }).click()

    // Verify the Issues Agent tab is visible (now skill-less — free-text only)
    const issuesTab = agentDialog.locator('[data-testid="issues-tab-content"]')
    await expect(issuesTab).toBeVisible()

    // Verify mode selector is present (added by PR #731)
    const modeSelector = issuesTab.locator('[aria-label="Select mode"]')
    await expect(modeSelector).toBeVisible()

    // Verify model selector is present
    const modelSelector = issuesTab.locator('[aria-label="Select model"]')
    await expect(modelSelector).toBeVisible()

    // Click Start Agent
    const startButton = issuesTab.getByRole('button', { name: 'Start Agent' })
    await expect(startButton).toBeEnabled({ timeout: 10000 })
    await startButton.click()

    // Dialog should close (POST /api/issues-agent/session returns 201)
    await expect(agentDialog).not.toBeVisible({ timeout: 15000 })

    // Should navigate to either the issues page (if prompt had instructions)
    // or the session page (if no prompt/instructions selected)
    await expect(page).toHaveURL(/\/(projects\/demo-project\/issues|sessions\/)/)
  })
})
