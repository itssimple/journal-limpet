using Npgsql;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace Journal_Limpet.Shared.Database
{
    public class NPGDB : IDisposable
    {
        private readonly NpgsqlConnection _connection;

        public NPGDB(NpgsqlConnection connection)
        {
            _connection = connection;
        }

        public async Task<DataTable> ExecuteDataTableAsync(string sql, params NpgsqlParameter[] parameters)
        {
            await EnsureConnected();
            var command = await GetCommandWithParams(sql, parameters);

            using (var da = new NpgsqlDataAdapter(command))
            {
                DataTable dt = new DataTable();

                da.Fill(dt);

                return dt;
            }
        }

        public async Task<List<T>> ExecuteListAsync<T>(string sql, params NpgsqlParameter[] parameters)
        {
            var dt = await ExecuteDataTableAsync(sql, parameters);

            List<T> items = new List<T>();

            foreach (DataRow row in dt.Rows)
            {
                T item = (T)Activator.CreateInstance(typeof(T), row);
                items.Add(item);
            }

            return items;
        }

        public async Task<int> ExecuteNonQueryAsync(string sql, params NpgsqlParameter[] parameters)
        {
            await EnsureConnected();
            var command = await GetCommandWithParams(sql, parameters);

            return await command.ExecuteNonQueryAsync();
        }

        public async Task<T> ExecuteScalarAsync<T>(string sql, params NpgsqlParameter[] parameters)
        {
            await EnsureConnected();
            var command = await GetCommandWithParams(sql, parameters);

            var retValue = await command.ExecuteScalarAsync();

            if (retValue is T)
                return (T)retValue;

            return default;
        }

        private async Task<NpgsqlCommand> GetCommandWithParams(string sql, NpgsqlParameter[] parameters)
        {
            var command = _connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddRange(parameters);
            if (parameters.Count() > 0)
            {
                await command.PrepareAsync();
            }

            return command;
        }

        private async Task EnsureConnected()
        {
            if (_connection.State == ConnectionState.Closed)
            {
                await _connection.OpenAsync();
            }
        }

        public void Dispose()
        {
            _connection?.Close();
            _connection?.Dispose();
        }

        public void Dispose(bool isDisposing)
        {
            if (isDisposing) Dispose();
        }
    }
}
