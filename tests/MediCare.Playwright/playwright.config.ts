import { randomUUID } from 'node:crypto';
import { resolve } from 'node:path';
import { defineConfig, devices } from '@playwright/test';

const repoRoot = resolve(__dirname, '../..');
const webProject = resolve(repoRoot, 'src/MediCare.Web/MediCare.Web.csproj');
const baseURL = 'https://localhost:5087';
const appUrls = 'https://localhost:5087;http://localhost:5088';
const runId = randomUUID().replaceAll('-', '').toLowerCase();
const databaseName = `MediCare_PlaywrightE2E_${runId}`;
const connectionString = `Server=(localdb)\\MSSQLLocalDB;Database=${databaseName};Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True;Connect Timeout=15;Application Name=MediCare.Playwright.E2E`;

// The run identity is generated for each Playwright process and is never configurable.
process.env.MEDICARE_E2E_RUN_ID = runId;
process.env.MEDICARE_E2E_DATABASE = databaseName;
process.env.MEDICARE_E2E_CONNECTION_STRING = connectionString;
process.env.PLAYWRIGHT_BROWSERS_PATH = '0';
const adminPassword = 'E2E_Admin_Test_2026!aA9';
process.env.MEDICARE_E2E_ADMIN_PASSWORD = adminPassword;

const isolatedWebEnvironment: Record<string, string> = {
  ASPNETCORE_ENVIRONMENT: 'Production',
  ASPNETCORE_URLS: appUrls,
  ASPNETCORE_HTTPS_PORT: '5087',
  ASPNETCORE_Kestrel__Certificates__Default__Path: resolve(__dirname, '.local-cert/localhost.pfx'),
  ASPNETCORE_Kestrel__Certificates__Default__Password: 'MediCare-Playwright-Localhost-Only',
  ConnectionStrings__DefaultConnection: connectionString,
  MEDICARE_E2E_RUN_ID: runId,
  MEDICARE_E2E_DATABASE: databaseName,
  MEDICARE_E2E_CONNECTION_STRING: connectionString,
  MEDICARE_E2E_BASE_URL: baseURL,
  MEDICARE_E2E_WEB_PROJECT: webProject,
  Database__ApplyMigrationsOnStartup: 'true',
  Seed__AdminPassword: adminPassword,
  SendGrid__Enabled: 'false',
  SendGrid__ApiKey: '',
  Smtp__Host: '127.0.0.1',
  Smtp__Port: '1',
  Smtp__Username: '',
  Smtp__Password: '',
  Smtp__EnableSsl: 'false',
  Twilio__Enabled: 'false',
  Twilio__AccountSid: '',
  Twilio__AuthToken: '',
  PAYMOB_SECRET_KEY: '',
  PAYMOB_PUBLIC_KEY: '',
  PAYMOB_HMAC_SECRET: '',
  PAYMOB_INTEGRATION_ID: '',
  PAYMOB_PUBLIC_BASE_URL: '',
  OPENAI_API_KEY: '',
  AZURE_OPENAI_API_KEY: '',
  OPENAI_MODEL: '',
  AI_MODEL: '',
  AZURE_OPENAI_ENDPOINT: '',
  AiAssistant__ApiKey: '',
  OpenAI__ApiKey: '',
  'Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command': 'Warning',
};

export default defineConfig({
  metadata: { medicareE2ERunId: runId, medicareE2EDatabase: databaseName },
  testDir: './tests',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  timeout: 45_000,
  expect: { timeout: 10_000 },
  outputDir: './test-results',
  reporter: [['list'], ['html', { outputFolder: 'playwright-report', open: 'never' }]],
  use: {
    ...devices['Desktop Chrome'],
    baseURL,
    ignoreHTTPSErrors: true,
    screenshot: 'on',
    trace: 'on',
    video: 'on',
    serviceWorkers: 'block',
    permissions: [],
    actionTimeout: 10_000,
    navigationTimeout: 15_000,
  },
  webServer: {
    command: `node "${resolve(__dirname, 'start-isolated-app.cjs')}"`,
    cwd: repoRoot,
    stdout: 'pipe',
    stderr: 'pipe',
    url: 'http://localhost:5088/health',
    ignoreHTTPSErrors: true,
    reuseExistingServer: false,
    timeout: 180_000,
    env: isolatedWebEnvironment,
  },
});
