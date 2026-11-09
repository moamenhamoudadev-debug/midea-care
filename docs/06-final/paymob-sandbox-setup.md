# Paymob sandbox setup

This project includes a **test-mode** Paymob Unified Checkout flow. It has not been verified with merchant credentials or a public callback endpoint. Do not treat a successful local build as proof of a completed payment integration.

## Required configuration

Set these environment variables in the local development environment or secret manager:

| Variable | Value |
|---|---|
| `PAYMOB_SECRET_KEY` | Paymob test secret key beginning with `sk_test_` |
| `PAYMOB_PUBLIC_KEY` | Paymob test public key beginning with `egy_pk_test_` |
| `PAYMOB_HMAC_SECRET` | The account webhook HMAC secret |
| `PAYMOB_INTEGRATION_ID` | Numeric test payment integration ID |
| `PAYMOB_PUBLIC_BASE_URL` | Public HTTPS origin reachable by Paymob, with no path suffix |

Never commit credentials. The code rejects live key prefixes. The application requires an HTTPS public origin so Paymob can reach `/api/payment/paymob/webhook` and return the browser to `/api/payment/paymob/return`.

## Checkout behavior

1. An authenticated patient starts checkout for their own appointment.
2. MediCare calculates the amount on the server and saves a pending payment attempt before calling Paymob.
3. The patient is redirected to Paymob's hosted Unified Checkout. Card number and CVV are not part of the MediCare checkout request.
4. The browser return URL never marks an appointment paid. MediCare verifies the transaction callback HMAC, integration ID, currency, amount in cents, the HMAC-signed Paymob order ID, matching merchant reference, and pending state before updating SQL Server.
5. A matching still-valid pending checkout is reused for repeat requests. Unique filtered database indexes prevent parallel pending attempts for one appointment and reserve a promo for one patient while its payment is pending or succeeded.

Paymob sandbox transactions do not move real money. Live key support, refunds, chargeback handling, merchant onboarding, callback reachability, and end-to-end sandbox verification remain outstanding. Network timeouts can leave a pending attempt for reconciliation; do not claim exactly-once payment processing.

## Database

The new migrations add payment-attempt persistence and a provider label to appointments. Review them, then apply them only to a dedicated development/test database before demo setup. No migration has been run against a real-user database.

## AI demonstration

The clinic guide can use OpenAI Responses or Azure OpenAI when configured, subject to explicit consent, PII redaction, emergency and medication guardrails, and a local fallback. It provides educational specialty navigation only; it is not a diagnostic or treatment service. Azure chat completions are requested in JSON mode, while the service validates specialties against the application's allow-list.
