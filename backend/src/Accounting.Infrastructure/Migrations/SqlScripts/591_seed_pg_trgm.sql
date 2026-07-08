-- pg_trgm extension (spec mcp-expansion.md sB) - enables typo-tolerant fuzzy search via
-- similarity() over customer/product/vendor name + code columns (CustomerService/
-- ProductService/VendorService trigram fallback, EF.Functions.TrigramsSimilarity).
-- Idempotent; runs once (tracked). No grants needed - once the extension is installed
-- its functions are available to every role. NB: never put curly braces here (EF
-- ExecuteSqlRawAsync treats them as string.Format placeholders).

CREATE EXTENSION IF NOT EXISTS pg_trgm;
