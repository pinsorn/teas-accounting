// One-off dev utility: drop + recreate an EMPTY teas_test so the integration-test fixture re-applies
// all SQL seeds from scratch (needed after a seed-structure change). Reads TEAS_TEST_PG; connects to
// the 'postgres' maintenance DB to drop/create. NOT part of the solution build.
using Npgsql;

var conn = Environment.GetEnvironmentVariable("TEAS_TEST_PG")
    ?? "Host=localhost;Port=5432;Database=teas_test;Username=accounting;Password=accounting_dev_password";
var b = new NpgsqlConnectionStringBuilder(conn);
var target = b.Database ?? "teas_test";
b.Database = "postgres";

await using var c = new NpgsqlConnection(b.ConnectionString);
await c.OpenAsync();

await using (var term = new NpgsqlCommand(
    "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname=@d AND pid<>pg_backend_pid()", c))
{
    term.Parameters.AddWithValue("d", target);
    await term.ExecuteNonQueryAsync();
}
await using (var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{target}\"", c))
    await drop.ExecuteNonQueryAsync();
await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{target}\"", c))
    await create.ExecuteNonQueryAsync();

Console.WriteLine($"Reset {target}: dropped + recreated EMPTY.");
