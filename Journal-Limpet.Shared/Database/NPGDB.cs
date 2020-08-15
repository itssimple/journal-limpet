using Npgsql;
using System.Threading.Tasks;

namespace Journal_Limpet.Shared.Database
{
    public class NPGDB
    {
        private readonly NpgsqlConnection _connection;

        public NPGDB(NpgsqlConnection connection)
        {
            _connection = connection;
        }

        public async Task<T> ExecuteScalarAsync<T>(string sql, params NpgsqlParameter[] parameters)
        {
            await EnsureConnected();
            var command = GetCommandWithParams(sql, parameters);

            var retValue = await command.ExecuteScalarAsync();

            if (retValue is T)
                return (T)retValue;

            return default;
        }

        private NpgsqlCommand GetCommandWithParams(string sql, NpgsqlParameter[] parameters)
        {
            var command = _connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddRange(parameters);

            return command;
        }

        private async Task EnsureConnected()
        {
            if (_connection.State == System.Data.ConnectionState.Closed)
            {
                await _connection.OpenAsync();
            }
        }
    }
}
