using System.Runtime.CompilerServices;

// H4/M11 — lets Accounting.Api.Tests unit-test the internal McpConsentScopes helper directly
// (Accounting.Api.OAuth/McpConsentScopesTests.cs) without widening its production-facing surface.
[assembly: InternalsVisibleTo("Accounting.Api.Tests")]
