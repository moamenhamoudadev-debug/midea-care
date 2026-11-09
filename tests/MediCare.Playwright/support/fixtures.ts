import { test as base, expect, type Page } from '@playwright/test';
import type { TestInfo } from '@playwright/test';
import { closeSync, existsSync, mkdirSync, openSync, readFileSync, unlinkSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';

type Diagnostics = {
  consoleErrors: string[];
  pageErrors: string[];
  failedRequests: string[];
  httpErrors: string[];
};

type TestFixtures = { diagnostics: Diagnostics };

// Reserve login POST slots in a run-scoped file so test workers and pages share
// one schedule for the application's per-IP limiter. Reserve before clicking:
// delaying the network route would exceed Playwright's navigation timeout.
export async function reserveLoginPost(testInfo: TestInfo): Promise<void> {
  const runId = process.env.MEDICARE_E2E_RUN_ID ?? '';
  if (!/^[a-f0-9]{32}$/.test(runId)) throw new Error('Login throttling requires the generated Playwright run ID.');

  const windowMs = 60_250;
  const statePath = resolve(__dirname, '../test-results', `.login-throttle-${runId}.json`);
  const lockPath = `${statePath}.lock`;
  mkdirSync(resolve(__dirname, '../test-results'), { recursive: true });
  const lockStartedAt = Date.now();
  let lockHandle: number | undefined;
  while (lockHandle === undefined) {
    try {
      lockHandle = openSync(lockPath, 'wx');
    } catch (error) {
      if ((error as NodeJS.ErrnoException).code !== 'EEXIST') throw error;
      if (Date.now() - lockStartedAt > 30_000) {
        throw new Error('Could not acquire the shared Playwright login-throttle lock within 30 seconds.');
      }
      await new Promise<void>((resolve) => setTimeout(resolve, 25));
    }
  }

  let scheduledAt: number;
  try {
    let reservations: number[] = [];
    if (existsSync(statePath)) {
      const stored = JSON.parse(readFileSync(statePath, 'utf8')) as { runId?: string; reservations?: number[] };
      if (stored.runId !== runId || !Array.isArray(stored.reservations) || stored.reservations.some((time) => !Number.isFinite(time))) {
        throw new Error('Shared login-throttle state is invalid or belongs to a different Playwright run.');
      }
      reservations = stored.reservations;
    }

    const now = Date.now();
    reservations = reservations.filter((time) => time > now - windowMs);
    scheduledAt = Math.max(now, reservations.length >= 4 ? reservations[0] + windowMs : now);
    reservations = reservations.filter((time) => time > scheduledAt - windowMs);
    reservations.push(scheduledAt);
    writeFileSync(statePath, JSON.stringify({ runId, reservations }));
  } finally {
    closeSync(lockHandle);
    unlinkSync(lockPath);
  }

  const waitMs = scheduledAt - Date.now();
  if (waitMs > 0) {
    // Allow the intentional wait plus the test's normal execution budget.
    testInfo.setTimeout(Math.max(testInfo.timeout, testInfo.duration + waitMs + testInfo.timeout));
    console.log(`[E2E_RATE_LIMIT] Waiting ${Math.ceil(waitMs / 1000)}s before login POST to stay within the application's five-per-minute per-IP limit.`);
    await new Promise<void>((resolve) => setTimeout(resolve, waitMs));
  }
}

export const test = base.extend<TestFixtures>({
  diagnostics: [async ({ page, context }, use, testInfo) => {
    const diagnostics: Diagnostics = {
      consoleErrors: [],
      pageErrors: [],
      failedRequests: [],
      httpErrors: [],
    };

    await context.route('**/*', async (route) => {
      const url = new URL(route.request().url());
      if (url.hostname === 'localhost' && url.port === '5087') {
        await route.continue();
      } else {
        diagnostics.failedRequests.push(`Blocked non-local request: ${route.request().method()} ${url.origin}${url.pathname}`);
        await route.abort('blockedbyclient');
      }
    });

    const observed = new WeakSet<Page>();
    const trackPage = (observedPage: Page) => {
      if (observed.has(observedPage)) return;
      observed.add(observedPage);
      observedPage.on('console', (message) => {
        if (message.type() === 'error') diagnostics.consoleErrors.push(message.text());
      });
      observedPage.on('pageerror', (error) => diagnostics.pageErrors.push(error.stack ?? error.message));
      observedPage.on('requestfailed', (request) => {
        diagnostics.failedRequests.push(`${request.method()} ${request.url()} :: ${request.failure()?.errorText ?? 'request failed'}`);
      });
      observedPage.on('response', (response) => {
        if (response.status() >= 400) diagnostics.httpErrors.push(`${response.status()} ${response.url()}`);
      });
    };
    context.pages().forEach(trackPage);
    context.on('page', trackPage);

    await use(diagnostics);
    console.error(`[E2E_DIAGNOSTICS] ${JSON.stringify(diagnostics)}`);
    await testInfo.attach('browser-diagnostics.json', {
      body: Buffer.from(JSON.stringify(diagnostics, null, 2)),
      contentType: 'application/json',
    });
  }, { auto: true }],
});

export { expect };

export async function assertLocalPage(page: Page): Promise<void> {
  const url = new URL(page.url());
  expect(url.hostname).toBe('localhost');
  expect(url.port).toBe('5087');
  expect(url.protocol).toBe('https:');
}
