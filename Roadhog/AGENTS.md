# Roadhog Project Instructions

Use the `maplestory-agent-architecture` skill as the architecture reference for this project, adapted to C# WinForms and .NET 8.

This project is not the MapleStory Lua engine. Apply only the transferable engineering rules:

- Keep modules atomic and responsibilities explicit.
- UI code should render state and send commands only.
- Put external/client/tool APIs behind narrow adapter boundaries.
- Keep decision logic pure where possible: input context in, proposal/result out.
- Use managers/services to validate and orchestrate decisions, not to call UI or low-level APIs directly.
- Keep worker/runtime flows isolated and idempotent when background execution is added.
- Add mock-first tests for framework or runtime behavior before relying on live clients.
- Prefer structured diagnostics for runtime decisions and failures.

Safety boundary:

- Do not add memory offsets, packet logic, hooks, bypasses, anti-detection logic, or unauthorized online-game automation internals.
- If client integration is required, keep it behind a documented/local adapter and preserve a mock path.
