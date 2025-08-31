using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json;
using VisualSqlBuilder.Core.Models;

namespace VisualSqlBuilder.Core.Services
{
    public interface ILayoutStorageService
    {
        Task<string> SaveLayoutAsync(string connectionString, CanvasState state);
        Task<CanvasState?> LoadLayoutAsync(string connectionString, string layoutId);
        Task<List<CanvasState>> GetAllLayoutsAsync(string connectionString);
        Task<bool> DeleteLayoutAsync(string connectionString, string layoutId);
    }

    public class LayoutStorageService : ILayoutStorageService
    {
        public async Task<string> SaveLayoutAsync(string connectionString, CanvasState state)
        {
            await EnsureLayoutTableExistsAsync(connectionString);

            state.JsonData = JsonConvert.SerializeObject(state.Query, Formatting.None);
            state.ModifiedAt = DateTime.UtcNow;

            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            var sql = @"
                IF EXISTS (SELECT 1 FROM VisualSqlLayouts WHERE Id = @Id)
                BEGIN
                    UPDATE VisualSqlLayouts
                    SET Name = @Name, JsonData = @JsonData, ModifiedAt = @ModifiedAt, ModifiedBy = @ModifiedBy
                    WHERE Id = @Id
                END
                ELSE
                BEGIN
                    INSERT INTO VisualSqlLayouts (Id, Name, JsonData, CreatedAt, CreatedBy, ModifiedAt, ModifiedBy)
                    VALUES (@Id, @Name, @JsonData, @CreatedAt, @CreatedBy, @ModifiedAt, @ModifiedBy)
                END";

            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@Id", state.Id);
            command.Parameters.AddWithValue("@Name", state.Name);
            command.Parameters.AddWithValue("@JsonData", state.JsonData);
            command.Parameters.AddWithValue("@CreatedAt", state.CreatedAt);
            command.Parameters.AddWithValue("@CreatedBy", state.CreatedBy ?? "System");
            command.Parameters.AddWithValue("@ModifiedAt", state.ModifiedAt);
            command.Parameters.AddWithValue("@ModifiedBy", state.CreatedBy ?? "System");

            await command.ExecuteNonQueryAsync();

            return state.Id;
        }

        public async Task<CanvasState?> LoadLayoutAsync(string connectionString, string layoutId)
        {
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            var sql = "SELECT Id, Name, JsonData, CreatedAt, CreatedBy, ModifiedAt FROM VisualSqlLayouts WHERE Id = @Id";

            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@Id", layoutId);

            using var reader = await command.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                var state = new CanvasState
                {
                    Id = reader.GetString(0),
                    Name = reader.GetString(1),
                    JsonData = reader.GetString(2),
                    CreatedAt = reader.GetDateTime(3),
                    CreatedBy = reader.GetString(4),
                    ModifiedAt = reader.GetDateTime(5)
                };

                state.Query = JsonConvert.DeserializeObject<QueryModel>(state.JsonData) ?? new QueryModel();

                return state;
            }

            return null;
        }

        public async Task<List<CanvasState>> GetAllLayoutsAsync(string connectionString)
        {
            var layouts = new List<CanvasState>();

            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            var sql = "SELECT Id, Name, CreatedAt, CreatedBy, ModifiedAt FROM VisualSqlLayouts ORDER BY ModifiedAt DESC";

            using var command = new SqlCommand(sql, connection);
            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                layouts.Add(new CanvasState
                {
                    Id = reader.GetString(0),
                    Name = reader.GetString(1),
                    CreatedAt = reader.GetDateTime(2),
                    CreatedBy = reader.GetString(3),
                    ModifiedAt = reader.GetDateTime(4)
                });
            }

            return layouts;
        }

        public async Task<bool> DeleteLayoutAsync(string connectionString, string layoutId)
        {
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            var sql = "DELETE FROM VisualSqlLayouts WHERE Id = @Id";

            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@Id", layoutId);

            var rowsAffected = await command.ExecuteNonQueryAsync();

            return rowsAffected > 0;
        }

        private async Task EnsureLayoutTableExistsAsync(string connectionString)
        {
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            var sql = @"
                IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='VisualSqlLayouts' AND xtype='U')
                BEGIN
                    CREATE TABLE VisualSqlLayouts (
                        Id NVARCHAR(50) PRIMARY KEY,
                        Name NVARCHAR(255) NOT NULL,
                        JsonData NVARCHAR(MAX) NOT NULL,
                        CreatedAt DATETIME2 NOT NULL,
                        CreatedBy NVARCHAR(255) NOT NULL,
                        ModifiedAt DATETIME2 NOT NULL,
                        ModifiedBy NVARCHAR(255)
                    )
                END";

            using var command = new SqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}