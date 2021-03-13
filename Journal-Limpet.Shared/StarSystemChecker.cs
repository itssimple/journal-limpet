using Journal_Limpet.Shared.Database;
using Microsoft.Data.SqlClient;
using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Journal_Limpet.Shared
{
    public class StarSystemChecker
    {
        private readonly MSSQLDB _db;

        public StarSystemChecker(MSSQLDB db)
        {
            _db = db;
        }

        public async Task<EDSystemData> GetSystemDataAsync(long systemAddress)
        {
            return await _db.ExecuteSingleRowAsync<EDSystemData>("SELECT * FROM EliteSystem WHERE SystemAddress = @systemAddress", new SqlParameter("systemAddress", systemAddress));
        }

        public async Task<List<EDSystemData>> GetSystemDataFromSystemNameAsync(string systemName)
        {
            return await _db.ExecuteListAsync<EDSystemData>("SELECT * FROM EliteSystem WHERE StarSystem = @starSystem", new SqlParameter("starSystem", systemName));
        }

        public async Task InsertOrUpdateSystemAsync(EDSystemData systemData)
        {
            var jsPos = JsonSerializer.Serialize(systemData.Coordinates);

            await _db.ExecuteNonQueryAsync($@"UPDATE EliteSystem WITH (UPDLOCK, SERIALIZABLE) SET StarSystem = '{systemData.Name.Replace("'", "''")}', StarPos = '{jsPos}' WHERE SystemAddress = {systemData.Id64};

IF @@ROWCOUNT = 0
BEGIN
  INSERT INTO EliteSystem (SystemAddress, StarSystem, StarPos) VALUES ({systemData.Id64}, '{systemData.Name.Replace("'", "''")}', '{jsPos}');
END;");
        }
    }

    public class EDSystemData
    {
        [JsonPropertyName("id64")]
        public long Id64 { get; set; }
        [JsonPropertyName("name")]
        public string Name { get; set; }
        [JsonPropertyName("coords")]
        public EDSystemCoordinates Coordinates { get; set; }

        public EDSystemData() { }
        public EDSystemData(DataRow datarow)
        {
            Id64 = datarow.Field<long>("SystemAddress");
            Name = datarow["StarSystem"].ToString();

            Coordinates = JsonSerializer.Deserialize<EDSystemCoordinates>(datarow["StarPos"].ToString());
        }
    }

    public struct EDSystemCoordinates
    {
        [JsonPropertyName("x")]
        public double X { get; set; }
        [JsonPropertyName("y")]
        public double Y { get; set; }
        [JsonPropertyName("z")]
        public double Z { get; set; }
    }
}
