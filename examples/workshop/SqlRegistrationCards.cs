using System.Data;
using Microsoft.Data.SqlClient;

namespace Workshop;

// Apply schema.sql through your migration process before using this writer.
public sealed class SqlRegistrationCards(string connectionString) : IRegistrationCardWriter
{
    public async Task<ApplyOutcome> ApplyAsync(RegistrationCard card, ProjectionSource source,
        CancellationToken cancellationToken)
    {
        long version = checked((long)source.Version);
        if (version <= 0 || source.Stream.Length > 256 || source.Consumer.Length > 64
            || source.DomainHash.Length != 64) throw new ArgumentException("Invalid projection source.");
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);

        using var seen = Command(connection, transaction, """
            SELECT DomainHash FROM dbo.WorkshopProjectionInbox WITH (UPDLOCK, HOLDLOCK)
            WHERE Consumer = @consumer AND SourceStream = @stream AND SourceVersion = @version;
            """, card, source, version);
        object? priorHash = await seen.ExecuteScalarAsync(cancellationToken);
        if (priorHash is string hash)
        {
            if (hash != source.DomainHash) throw new InvalidDataException("Changed event identity.");
            await transaction.CommitAsync(cancellationToken);
            return ApplyOutcome.Duplicate;
        }

        using var current = Command(connection, transaction, """
            SELECT SourceStream, SourceVersion, DomainHash FROM dbo.WorkshopRegistrationCard WITH (UPDLOCK, HOLDLOCK)
            WHERE RegistrationId = @id;
            """, card, source, version);
        long? currentVersion = null;
        await using (SqlDataReader reader = await current.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                if (reader.GetString(0) != source.Stream)
                    throw new InvalidDataException("Registration has a different source stream.");
                currentVersion = reader.GetInt64(1);
                if (currentVersion == version && reader.GetString(2) != source.DomainHash)
                    throw new InvalidDataException("Changed current event identity.");
            }
        }
        bool replace = currentVersion is null || currentVersion < version;
        if (replace)
        {
            using var write = Command(connection, transaction, currentVersion is null ? """
                INSERT dbo.WorkshopRegistrationCard
                (RegistrationId, WorkshopId, AttendeeId, DisplayName, Status, SourceStream, SourceVersion, DomainHash)
                VALUES (@id, @workshop, @attendee, @name, @status, @stream, @version, @hash);
                """ : """
                UPDATE dbo.WorkshopRegistrationCard SET WorkshopId = @workshop,
                    AttendeeId = @attendee, DisplayName = @name, Status = @status,
                    SourceVersion = @version, DomainHash = @hash
                WHERE RegistrationId = @id;
                """, card, source, version);
            await write.ExecuteNonQueryAsync(cancellationToken);
        }
        using var remember = Command(connection, transaction, """
            INSERT dbo.WorkshopProjectionInbox (Consumer, SourceStream, SourceVersion, DomainHash)
            VALUES (@consumer, @stream, @version, @hash);
            """, card, source, version);
        await remember.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return replace ? ApplyOutcome.Applied : ApplyOutcome.OlderSnapshot;
    }

    public async Task<RegistrationCard?> GetForUserAsync(Guid registrationId, Guid authenticatedUserId,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT RegistrationId, WorkshopId, AttendeeId, DisplayName, Status
            FROM dbo.WorkshopRegistrationCard WHERE RegistrationId = @id AND AttendeeId = @user;
            """;
        command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = registrationId;
        command.Parameters.Add("@user", SqlDbType.UniqueIdentifier).Value = authenticatedUserId;
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3), reader.GetString(4))
            : null;
    }

    private static SqlCommand Command(SqlConnection connection, SqlTransaction transaction, string sql,
        RegistrationCard card, ProjectionSource source, long version)
    {
        var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add("@consumer", SqlDbType.NVarChar, 64).Value = source.Consumer;
        command.Parameters.Add("@stream", SqlDbType.NVarChar, 256).Value = source.Stream;
        command.Parameters.Add("@version", SqlDbType.BigInt).Value = version;
        command.Parameters.Add("@hash", SqlDbType.Char, 64).Value = source.DomainHash;
        command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = card.RegistrationId;
        command.Parameters.Add("@workshop", SqlDbType.UniqueIdentifier).Value = card.WorkshopId;
        command.Parameters.Add("@attendee", SqlDbType.UniqueIdentifier).Value = card.AttendeeId;
        command.Parameters.Add("@name", SqlDbType.NVarChar, 120).Value = card.DisplayName;
        command.Parameters.Add("@status", SqlDbType.VarChar, 16).Value = card.Status;
        return command;
    }
}
