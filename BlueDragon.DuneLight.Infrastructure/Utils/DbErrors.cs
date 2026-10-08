using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BlueDragon.DuneLight.Infrastructure.Utils;

/// <summary>Prepoznavanje grešaka baze koje servisi prevode u domenske kodove (npr. unique indeks kao izvor istine za
/// jedinstvenost kod istovremenih zahtjeva).</summary>
public static class DbErrors
{
    /// <summary>Povreda unique ograničenja (bilo kojeg, ili samo zadanog kad je <paramref name="constraintName"/> zadan).</summary>
    public static bool IsUniqueViolation(DbUpdateException ex, string constraintName = null) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pgEx &&
        (constraintName == null || pgEx.ConstraintName == constraintName);
}
