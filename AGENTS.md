# AI Development Instructions

Mandatory for every development task. The user defines **what**; these rules define
**how**. Apply module-specific `AGENTS.md` instructions too; they may add constraints,
but must not weaken repository-wide scope, quality, security, or validation rules.

## 1. Load only relevant context

- Start with this file and [.ai/context.md](.ai/context.md), then affected code,
  applicable module instructions and related tests before editing.
- Search narrowly by file or symbol. Read additional documentation only when the
  task requires it; do not load the entire repository or all linked guides.
- Avoid unrelated modules, dependencies, generated files, binaries, build outputs,
  IDE metadata, logs and coverage unless needed to diagnose the specific issue.
- Do not reread instructions already present in context unless they changed or
  missing detail is needed. Links below are selective references, not a preload list.

| When needed | Follow |
| --- | --- |
| Architecture, placement or dependency boundaries | [Architecture](docs/ARCHITECTURE.md), [project structure](docs/PROJECT_STRUCTURE.md), relevant [ADRs](docs/adr/) |
| Coding conventions, errors or concurrency | [Coding standards](docs/CODING_STANDARDS.md) |
| Credentials, external input, data or network access | [Security](docs/SECURITY.md) |
| API consumption or contract changes | [API guidelines](docs/API_GUIDELINES.md) |
| Tests, build or validation commands | [Testing](docs/TESTING.md) |
| Detailed process or final review | [Workflow](.ai/workflow.md), [review checklist](.ai/review-checklist.md) |

## 2. Understand, search and reuse

- Identify requested behavior, affected layers, existing implementations, reusable
  components, related tests and the smallest set of files to change before coding.
- **REUSE > EXTEND > REFACTOR > CREATE**: search for equivalent responsibilities
  before adding code, models, configuration, constants, dependencies or tests.
  Include business rules, validators, API models, UI patterns, persistence and fixtures.
- Reuse existing solutions; extend only components that own the responsibility.
  Refactor only as required for correctness, meaningful deduplication, architecture
  or maintainability of the requested change.
- Create an abstraction only when no suitable implementation exists, extending one
  would violate its responsibility and the architecture requires separation.
  No duplicate implementations without a technical reason, trivial wrappers or
  generic helpers merely to save a few lines; solve current requirements only.
- Do not invent project behavior or APIs. Infer minor details from established
  conventions; clarify material uncertainty about behavior, compatibility, security,
  data integrity or architecture, and state any assumptions or limitations.

## 3. Keep scope and architecture intact

- Make the smallest safe change; preserve behavior and public contracts unless the
  requirement explicitly changes them. Avoid unrelated cleanup, renaming, moves,
  speculative features, refactors, frameworks and dependency upgrades.
- Follow existing architecture, local patterns and dependency direction. Keep code
  in the owning layer; do not bypass boundaries, introduce cycles or silently redesign.
  If docs and code conflict, inspect surrounding implementation before choosing a pattern.
  Do not copy known bugs, vulnerabilities or dangerous practices for consistency.
- Keep business rules in one owning implementation unless architecture explicitly
  requires separate representations. Preserve API/serialization conventions and
  backward compatibility; respect existing transport/domain boundaries.
- Reuse persistence patterns. Change schemas only when required, following migrations,
  preserving existing data as required and testing the change when practical.
- Add dependencies only after checking existing code/packages and justifying the need;
  avoid dependencies for trivial work and version changes merely because newer ones exist.
- Leave unrelated pre-existing issues alone unless they block the task; report relevant ones.
  Edit generators or source configuration, not generated output, unless explicitly required.

## 4. Implementation quality

- Use clear names, focused responsibilities, simple code and minimal mutable state.
  Avoid hidden side effects and magic values; comment intent. Remove imports and code
  made obsolete by this change when safe.
- Follow existing error handling. Handle relevant invalid/missing input, network and
  persistence failures, timeouts, retries and user-visible states. Do not silently
  swallow exceptions or catch broadly without reason; keep failures observable without
  exposing sensitive information or internal implementation details to users.
- Follow existing async/concurrency mechanisms; consider cancellation, lifecycle,
  races, shared state, retries, duplicate execution and exception propagation.
  Do not block async contexts or put expensive work on the UI thread.
- Check for obvious performance regressions: redundant IO, queries, polling,
  calculations, serialization, UI updates, hot-path allocations or excessive data loads.
  Identify a real bottleneck before introducing complex optimizations.

## 5. Security essentials

- Validate external input at relevant boundaries, including API/JSON, URLs and files;
  review authentication, authorization, data access and serialization when affected.
- Never hardcode, commit, expose or log secrets/credentials; avoid sensitive personal
  data in diagnostics. Use established configuration mechanisms. Do not load user
  settings, notification history or private logs into AI context or fixtures.
- Never disable TLS validation, bypass authentication/authorization or construct unsafe
  queries from untrusted input. Use dummy credentials and local doubles for tests.

## 6. Tests, validation and completion

- Follow existing test patterns. Update tests for intentional behavior changes; add
  coverage for new business logic and regression tests for fixes when appropriate.
  Prioritize observable behavior, edge cases, validation, errors, transformations,
  persistence and critical integrations, not tests that mirror implementation details.
- Run relevant checks using existing commands/tooling: tests, build, formatting, lint,
  static analysis and security checks as applicable. Run architectural guards for
  architecture-sensitive changes; never bypass them to obtain a passing build.
  Do not install tools just to satisfy a validation category.
- Before finishing, review the change for duplication, scope, architecture, code quality,
  security, errors, performance and test coverage. Completion requires the requested
  behavior, preserved unrelated functionality and successful applicable validations.
  Never claim success after a relevant failed check; report skipped/blocked checks and why.
- Update affected docs for changes to architecture, public APIs, configuration, workflow,
  setup or important behavior. Consider an ADR only for significant architectural decisions.
- Give a concise completion report: **Changed**, **Files**, **Reuse**, **Validation**
  (actual results and reasons for checks not run), and **Notes** only for relevant
  limitations, risks, assumptions or pre-existing issues. Keep trivial reports short.