# Working on the C# port

- Read ARCHITECTURE.md first. The Python repo is the oracle; parity with its golden vectors is the acceptance test for every reader and writer.
- One module per PR, with parity tests and negative tests.
- Never commit personal saves, Steam session data, credentials or user paths.
- No live Steam or game calls in CI; use fakes.
- Unknown fields stay read-only. No write capability without game evidence.
