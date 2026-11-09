const { spawn, execFileSync } = require('node:child_process');
const { existsSync } = require('node:fs');
const { resolve } = require('node:path');

function stop(message) {
  console.error(`E2E safety preflight failed: ${message}`);
  process.exit(2);
}

const runId = process.env.MEDICARE_E2E_RUN_ID ?? '';
const databaseName = process.env.MEDICARE_E2E_DATABASE ?? '';
const connectionString = process.env.MEDICARE_E2E_CONNECTION_STRING ?? '';
const baseURL = process.env.MEDICARE_E2E_BASE_URL ?? '';
const webProject = process.env.MEDICARE_E2E_WEB_PROJECT ?? '';

if (!/^[a-f0-9]{32}$/.test(runId) || databaseName !== `MediCare_PlaywrightE2E_${runId}`) {
  stop('database name is not the unique generated Playwright database name.');
}

const settings = new Map(connectionString.split(';').filter(Boolean).map((entry) => {
  const separator = entry.indexOf('=');
  return [entry.slice(0, separator).trim().toLowerCase(), entry.slice(separator + 1).trim()];
}));
const server = settings.get('server') ?? settings.get('data source') ?? '';
const targetDatabase = settings.get('database') ?? settings.get('initial catalog') ?? '';
if (!/^\(localdb\)\\mssqllocaldb$/i.test(server) || targetDatabase !== databaseName) {
  stop('connection string does not target only the generated MSSQLLocalDB database.');
}

const expectedBaseURL = 'https://localhost:5087';
if (baseURL !== expectedBaseURL) stop('app URL is not the fixed localhost test URL.');
const appUrls = 'https://localhost:5087;http://localhost:5088';

const repoRoot = resolve(__dirname, '../..');
const expectedProject = resolve(repoRoot, 'src/MediCare.Web/MediCare.Web.csproj');
if (resolve(webProject) !== expectedProject) stop('app project path is outside the expected MediCare web project.');
const certificatePath = resolve(__dirname, '.local-cert/localhost.pfx');
if (!existsSync(certificatePath)) stop('the isolated localhost HTTPS certificate is missing.');

// This read-only query connects only to master. The app is not spawned unless the
// LocalDB connection succeeds and the exact generated database name is unused.
const query = `SET NOCOUNT ON; IF DB_NAME() <> N'master' RAISERROR('Safety check must use master only.', 16, 1); IF DB_ID(N'${databaseName}') IS NOT NULL RAISERROR('E2E database already exists; refusing to connect or modify it.', 16, 1); SELECT N'Playwright database name is unused.' AS SafetyCheck;`;
try {
  const result = execFileSync('sqlcmd', [
    '-S', '(localdb)\\MSSQLLocalDB',
    '-E',
    '-d', 'master',
    '-b',
    '-Q', query,
  ], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] });
  console.log(`LocalDB preflight passed for ${databaseName}.`);
  console.log(result.trim());
} catch (error) {
  const detail = error.stderr?.toString().trim() || error.message;
  stop(`read-only master preflight did not pass: ${detail}`);
}

const adminPassword = 'E2E_Admin_Test_2026!aA9';
if (process.env.MEDICARE_E2E_ADMIN_PASSWORD !== adminPassword) {
  stop('synthetic Admin password does not match the generated run identity.');
}

const appEnvironment = {
  ...process.env,
  ASPNETCORE_ENVIRONMENT: 'Production',
  ASPNETCORE_URLS: appUrls,
  ASPNETCORE_HTTPS_PORT: '5087',
  ASPNETCORE_Kestrel__Certificates__Default__Path: certificatePath,
  ASPNETCORE_Kestrel__Certificates__Default__Password: 'MediCare-Playwright-Localhost-Only',
  ConnectionStrings__DefaultConnection: connectionString,
  Database__ApplyMigrationsOnStartup: 'true',
  Seed__AdminPassword: adminPassword,
  SEED_ADMIN_PASSWORD: adminPassword,
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
delete appEnvironment.MEDICARE_E2E_ADMIN_PASSWORD;

const app = spawn('dotnet', [
  'run', '--no-launch-profile', '--project', expectedProject, '--urls', appUrls,
], { cwd: repoRoot, env: appEnvironment, stdio: 'inherit' });

for (const signal of ['SIGINT', 'SIGTERM']) {
  process.on(signal, () => app.kill(signal));
}
app.on('error', (error) => {
  console.error(`Could not start MediCare after safety preflight: ${error.message}`);
  process.exitCode = 1;
});
app.on('exit', (code, signal) => {
  if (signal) process.kill(process.pid, signal);
  else process.exit(code ?? 1);
});
