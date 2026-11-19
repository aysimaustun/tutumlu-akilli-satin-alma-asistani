using Dapper;
using Microsoft.Data.SqlClient;

namespace PriceAggregator.Core.Auth;

public class UserRepository
{
    private readonly string _connectionString;

    public UserRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    private const string SelectColumns = "Id, Email, PasswordHash, DisplayName, CreatedAt, Name, Surname, Phone, BirthDate, City";

    public async Task<User?> GetByEmailAsync(string email)
    {
        using var db = new SqlConnection(_connectionString);
        return await db.QuerySingleOrDefaultAsync<User>(
            $"SELECT {SelectColumns} FROM Users WHERE Email = @Email",
            new { Email = email });
    }

    public async Task<User?> GetByIdAsync(int id)
    {
        using var db = new SqlConnection(_connectionString);
        return await db.QuerySingleOrDefaultAsync<User>(
            $"SELECT {SelectColumns} FROM Users WHERE Id = @Id",
            new { Id = id });
    }

    private static string? ComputeDisplayName(string? name, string? surname)
    {
        var combined = $"{name} {surname}".Trim();
        return string.IsNullOrWhiteSpace(combined) ? null : combined;
    }

    // Returns null instead of throwing when the email is already taken (race-safe: relies on
    // the UNIQUE constraint rather than a check-then-insert) so the caller can turn it into a
    // clean 409 without needing to inspect a SqlException.
    public async Task<User?> CreateAsync(
        string email, string passwordHash, string? name, string? surname, string? phone, DateTime? birthDate, string? city)
    {
        var displayName = ComputeDisplayName(name, surname);

        using var db = new SqlConnection(_connectionString);
        try
        {
            var id = await db.QuerySingleAsync<int>(@"
                INSERT INTO Users (Email, PasswordHash, DisplayName, Name, Surname, Phone, BirthDate, City)
                OUTPUT INSERTED.Id
                VALUES (@Email, @PasswordHash, @DisplayName, @Name, @Surname, @Phone, @BirthDate, @City)",
                new { Email = email, PasswordHash = passwordHash, DisplayName = displayName, Name = name, Surname = surname, Phone = phone, BirthDate = birthDate, City = city });

            return new User(id, email, passwordHash, displayName, DateTime.UtcNow, name, surname, phone, birthDate, city);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            return null;
        }
    }

    // Returns null instead of throwing when the new email is already taken by another
    // account, same race-safe pattern as CreateAsync.
    public async Task<User?> UpdateProfileAsync(
        int id, string email, string? name, string? surname, string? phone, DateTime? birthDate, string? city)
    {
        var displayName = ComputeDisplayName(name, surname);

        using var db = new SqlConnection(_connectionString);
        try
        {
            await db.ExecuteAsync(@"
                UPDATE Users
                SET Email = @Email, DisplayName = @DisplayName, Name = @Name, Surname = @Surname,
                    Phone = @Phone, BirthDate = @BirthDate, City = @City
                WHERE Id = @Id",
                new { Id = id, Email = email, DisplayName = displayName, Name = name, Surname = surname, Phone = phone, BirthDate = birthDate, City = city });

            return await GetByIdAsync(id);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            return null;
        }
    }
}
