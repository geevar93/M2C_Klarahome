# ADR-017 — A notification channel with no provider is suppressed, not failed

- **Status:** ✅ Accepted
- **Raised at:** Step 8 (Media, file storage & Notifications module)
- **Supersedes:** —
- **Related:** ADR-014 (external identity providers; SMS and email deferred behind runtime flags)

---

## Context

`08-integrations.md` §7 is unambiguous: *"No integration work starts until its row is complete.
Missing credentials are a `⛔ BLOCKED` condition, not a reason to stub and move on."* The SMS,
WhatsApp and Email rows all read **Deferred: no budget**.

Step 8's acceptance criteria nevertheless require that "a templated transactional email and SMS are
dispatched and logged" and that "failures retry with backoff". Steps 9 through 33 all depend on
Step 8. Taken literally, the platform stops here until the client buys an SMS account.

ADR-014 already answered the same question for Identity, and the User took the same decision again
at this step: **build the pipeline, and switch off what cannot be delivered** — rather than either
blocking the programme or writing an adapter against an API nobody can call.

There is a second, sharper problem underneath. Step 7 left `LoggingOtpDispatcher` writing one-time
codes to the API log, which `07-security-compliance.md` §3 forbids outright. It was tolerable only
because it cannot be reached by a deployed host. Step 8 is where it goes — and whatever replaces it
must not simply move the secret from the log into a database column.

## Decision

**Three states, not two.** A queued notification ends in `Sent`, `Failed` **or `Suppressed`**, and
they mean different things:

- **`Sent`** — a provider accepted it.
- **`Failed`** — a provider was asked and refused, or could not be reached, after the retry budget
  was spent. This is an incident.
- **`Suppressed`** — nobody was asked, and nobody was ever going to be. The channel has no provider
  configured, or the feature flag for the channel is off, or the recipient has opted out of this
  category. The reason is recorded on the row (`NoProvider`, `ChannelDisabled`, `OptedOut`,
  `NoRecipient`). **This is not an incident**, does not retry, does not alert, and does not colour
  a dashboard red.

A deployment with no SMS account therefore produces a delivery log full of `Suppressed / NoProvider`
rows — an accurate, queryable statement of what the platform tried to tell people and could not.
It does not produce a backlog of failures retrying against nothing, which is what a two-state model
would have given, and it does not silently drop the message, which is what "just don't send" would
have given.

**Sensitive bodies are never persisted.** A template may be marked `IsSensitive`; a one-time code
is. The rendered body of a sensitive message is passed to the provider and to nothing else: the
`payload` column holds the variable *names* with their values redacted, and the log line holds the
message id. This is what actually retires `LoggingOtpDispatcher` rather than relocating it.

**Development has one sink, and it is Mailpit.** Outside Production, a channel with no provider is
routed to the SMTP sender instead of being suppressed, so an SMS lands in Mailpit addressed to its
would-be recipient. A developer can therefore complete a mobile-OTP sign-in locally, the code never
reaches a log file or a table, and the code path exercised is the real one — render, queue,
dispatch, record — rather than a shortcut that only exists in development. Production never
registers that sender: `Sms:Provider` is empty, and the message is suppressed.

## Consequences

- **Good:** the OTP-in-the-log debt from Step 7 is closed rather than moved, and closed in a way
  that leaves local development working.
- **Good:** "did the customer get told" has an honest answer for every message, including the ones
  nobody could send. An operator can count them and take the answer to whoever holds the budget.
- **Good:** the day an SMS account exists, one adapter is written, `Sms:Provider` is set, and
  everything upstream — templates, DLT ids, preferences, queue, retry, log — is already built and
  already exercised.
- **Cost:** a status that reviewers must understand. `Suppressed` looks like a failure and is not,
  and a monitoring rule that treats it as one would page somebody nightly.
- **Cost:** the DLT template ids on the seeded SMS templates are placeholders until the client
  registers with a TRAI-approved entity. They are validated for *shape*, and the registry refuses
  to send an SMS whose template carries no id — so an unregistered template cannot reach a provider
  the day one is configured.
- **Watch:** the Development SMS-to-Mailpit route must never be registered in Production. It is
  bound to `IHostEnvironment.IsProduction()` being false, asserted by a test that constructs the
  Production container and checks the sender is absent.
