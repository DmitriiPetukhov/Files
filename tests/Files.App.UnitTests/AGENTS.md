# Files.App.UnitTests Development Guidelines

- Name each test file `<TestedEntity>Tests.cs` and keep tests for one primary system under test in that file.
- Do not test interfaces through stubs only; verify interface contracts through concrete implementations.
- Keep integration unit tests separate from pure unit tests; use an `Integration` subcategory and the `<TestedEntity>IntegrationTests.cs` suffix.
- If a test file grows beyond roughly 350 lines, split the same entity's tests by scenario, using `<TestedEntity><Scenario>Tests.cs` names.
- Name test methods `<method>_<scenario>`.
- Store stubs, mocks, fakes, and other test doubles outside test files under `TestDoubles/<Domain>/`, even when currently used by one test file.
- Never hard-code filesystem paths in tests. Use `TemporaryTestDirectory` for a unique subdirectory under the system temporary directory and always clean it up after the test, including failure paths.
- Design a compact scenario tree before adding tests; cover the happy path plus important boundary inputs, failures, cancellation, and lifecycle/cleanup paths.
- After adding tests, review nearby tests for structural improvements and duplication reduction without reducing coverage.
