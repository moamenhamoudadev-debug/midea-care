# Bug and Verification Issue Log

This log records issues observed during the October 11, 2026 review. “Untriaged” means the test exposed a mismatch but the underlying cause has not yet been confirmed.

| ID | Area | Observed result | Expected result | Status | Evidence / next action |
|---|---|---|---|---|---|
| QA-INT-01 | Appointment booking | Initial test run returned HTTP 405/500 while trying to persist TempData. | Redirect to the patient's appointments after a valid booking. | Resolved | Root cause was the test host trying to read/write Data Protection keys under a protected Windows profile directory. The test host now uses ephemeral keys; all 8 portal flows pass. |
| QA-INT-02 | Rescheduling, encounter, dispensing | Initial portal flow tests failed during the same TempData/Data Protection operation. | Authorized form submissions should complete and redirect. | Resolved | Verified by the 8/8 passing `CoreClinicalFlowsIntegrationTests` run on October 11, 2026. |
| QA-INT-03 | Payment receipt | Initial payment integration flow returned HTTP 500 during TempData/Data Protection persistence. | Redirect to the payment/receipt step expected by the test. | Resolved | Verified by the 8/8 passing `CoreClinicalFlowsIntegrationTests` run on October 11, 2026. |
| QA-ENV-01 | SQL Server integration | LocalDB failed to create an automatic instance in this environment. | SQL-backed tests execute against SQL Server/LocalDB or the configured test connection string. | Environment blocked | Run the integration suite where LocalDB is installed, or set `MEDICARE_TEST_CONNECTION_STRING`. |
| QA-ENV-02 | Live AI verification | No OpenAI API key was configured during review. | Verify a live response after a project key and supported model are configured. | Awaiting environment secret | Never commit the key. Configure `OpenAI:ApiKey` in User Secrets or `OPENAI_API_KEY` in the host. |
| DEPLOY-01 | Azure deployment | The latest public Azure workflow built and published the app, but the Azure deploy action failed with “No credentials found.” The expected `medicare-depi-app.azurewebsites.net` hostname did not resolve when checked. | A configured Azure App Service serves the app and passes its `/health` endpoint. | Blocked: Azure resource/profile missing | Configure repository variable `AZURE_WEBAPP_NAME` and secret `AZURE_WEBAPP_PUBLISH_PROFILE` for an existing App Service, plus its production SQL connection and required seed secrets. Then run the manual workflow and verify the host. |

The isolated unit suite passed 503/503 tests and the in-memory portal flow suite passed 8/8. SQL Server-specific verification and live OpenAI verification remain environment-dependent.
