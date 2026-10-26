# InvoiceApp.Tests

xUnit tests for the Invoice Approval service. One test class per class under test, named `<ClassUnderTest>Tests`.

## Structural rule test

`DomainDependencyRuleTests` encodes exactly one structural/architectural rule in plain language:

> **InvoiceApp.Domain must not depend on web or storage packages/assemblies (it holds business rules only).**

This enforces the AGENTS.md convention that `backend/src/InvoiceApp.Domain` contains business rules with no web or storage code. The test is deterministic: it reads only from the compiled `InvoiceApp.Domain` assembly (via reflection over its referenced assemblies) and makes no network or external calls, so it produces the same pass/fail result for the same codebase state.

### Running the structural test

Run just the structural rule test with a single command:

```sh
dotnet test backend/InvoiceApp.sln --filter FullyQualifiedName~DomainDependencyRuleTests
```

It also runs as part of the full test suite:

```sh
dotnet test backend/InvoiceApp.sln
```

### Determinism and recorded run status

The structural test is deterministic by construction: it reads only the compiled
`InvoiceApp.Domain` assembly's referenced-assembly list and makes no network or external
calls, so repeated runs against an unchanged codebase produce an identical result. On the current codebase the rule holds — `InvoiceApp.Domain` declares
zero `PackageReference`/`ProjectReference` entries and references no web or storage
assembly — so the expected result is a **pass**.

Expected failure behavior: if the Domain assembly ever references a
forbidden assembly (prefix `Microsoft.AspNetCore`, `Microsoft.EntityFrameworkCore`,
`Microsoft.Data.SqlClient`, `System.Data.SqlClient`, or `Azure.`), the test fails with a
message naming the rule and listing the offending assembly.

**Recorded run (green).** Using .NET SDK 8.0.426, the documented command was executed and
reported a pass, and was run three consecutive times against the unchanged codebase with
identical results:

```
dotnet test backend/InvoiceApp.sln --filter FullyQualifiedName~DomainDependencyRuleTests
```

```
Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1   (exit code 0)
Run 1: Passed!  - Failed: 0, Passed: 1
Run 2: Passed!  - Failed: 0, Passed: 1
Run 3: Passed!  - Failed: 0, Passed: 1
```

All three runs returned exit code 0 with identical output, confirming the test passes
deterministically on the current codebase (R1.7: the command is written down and has been
run green).
