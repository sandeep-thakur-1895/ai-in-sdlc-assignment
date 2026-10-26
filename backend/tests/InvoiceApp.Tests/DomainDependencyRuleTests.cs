using System;
using System.Linq;
using Xunit;

namespace InvoiceApp.Tests;

/// <summary>
/// Structural rule (exactly one): the <c>InvoiceApp.Domain</c> project holds business rules only
/// and must not depend on web or storage packages/assemblies. This enforces the AGENTS.md
/// convention "backend/src/InvoiceApp.Domain: business rules, no web or storage code" as an
/// automated, deterministic check that reads only from the compiled codebase (no network calls).
/// </summary>
public class DomainDependencyRuleTests
{
    // Forbidden assembly name prefixes representing web or storage dependencies the Domain
    // project must never take on.
    private static readonly string[] ForbiddenAssemblyPrefixes =
    {
        "Microsoft.AspNetCore",
        "Microsoft.EntityFrameworkCore",
        "Microsoft.Data.SqlClient",
        "System.Data.SqlClient",
        "Azure."
    };

    [Fact]
    public void DomainProject_DoesNotReferenceWebOrStorageAssemblies()
    {
        var domainAssembly = typeof(InvoiceApp.Domain.ApprovalService).Assembly;

        var referencedNames = domainAssembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToList();

        var violations = referencedNames
            .Where(name => ForbiddenAssemblyPrefixes
                .Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            .ToList();

        Assert.True(
            violations.Count == 0,
            "Structural rule violated: InvoiceApp.Domain must not depend on web or storage "
            + "packages/assemblies. Offending assembly(ies): "
            + string.Join(", ", violations) + ".");
    }
}
