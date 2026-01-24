using System.Data;
using System.Data.Odbc;
using TallySyncService.Models;

namespace TallySyncService.Services;

/// <summary>
/// Service for connecting to Tally database via ODBC
/// </summary>
public interface IOdbcService
{
    Task<bool> TestConnectionAsync();
    Task<DataTable> ExecuteQueryAsync(string query);
    Task<int> ExecuteNonQueryAsync(string query);
    Task<object?> ExecuteScalarAsync(string query);
}

public class OdbcService : IOdbcService
{
    private readonly string _connectionString;
    private readonly ILogger<OdbcService> _logger;
    private readonly int _timeoutSeconds;

    public OdbcService(IConfiguration configuration, ILogger<OdbcService> logger)
    {
        _logger = logger;
        
        // Build ODBC connection string from configuration
        var odbcConfig = configuration.GetSection("Odbc");
        var dsnName = odbcConfig["DsnName"];
        var userName = odbcConfig["UserName"];
        var password = odbcConfig["Password"];

        if (string.IsNullOrEmpty(dsnName))
        {
            throw new InvalidOperationException("ODBC DSN Name is not configured. Check appsettings.json");
        }

        _timeoutSeconds = int.TryParse(odbcConfig["TimeoutSeconds"], out var timeout) ? timeout : 60;

        // Build connection string for DSN-based connection
        _connectionString = $"DSN={dsnName}";
        
        if (!string.IsNullOrEmpty(userName))
        {
            _connectionString += $";UID={userName}";
        }
        
        if (!string.IsNullOrEmpty(password))
        {
            _connectionString += $";PWD={password}";
        }

        _logger.LogInformation("ODBC Service initialized with DSN: {DsnName}", dsnName);
    }

    /// <summary>
    /// Tests ODBC connection to Tally database
    /// </summary>
    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            using (var connection = new OdbcConnection(_connectionString))
            {
                await Task.Run(() => connection.Open());
                _logger.LogInformation("ODBC connection test successful");
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ODBC connection test failed");
            return false;
        }
    }

    /// <summary>
    /// Executes a SELECT query and returns DataTable
    /// </summary>
    public async Task<DataTable> ExecuteQueryAsync(string query)
    {
        var dataTable = new DataTable();
        
        try
        {
            using (var connection = new OdbcConnection(_connectionString))
            {
                await Task.Run(() => connection.Open());
                
                using (var command = new OdbcCommand(query, connection))
                {
                    command.CommandTimeout = _timeoutSeconds;
                    
                    using (var adapter = new OdbcDataAdapter(command))
                    {
                        await Task.Run(() => adapter.Fill(dataTable));
                    }
                }
            }

            _logger.LogDebug("Query executed successfully. Rows: {RowCount}", dataTable.Rows.Count);
            return dataTable;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing query: {Query}", query);
            throw;
        }
    }

    /// <summary>
    /// Executes INSERT, UPDATE, or DELETE query
    /// </summary>
    public async Task<int> ExecuteNonQueryAsync(string query)
    {
        try
        {
            using (var connection = new OdbcConnection(_connectionString))
            {
                await Task.Run(() => connection.Open());
                
                using (var command = new OdbcCommand(query, connection))
                {
                    command.CommandTimeout = _timeoutSeconds;
                    var rowsAffected = await Task.Run(() => command.ExecuteNonQuery());
                    
                    _logger.LogDebug("Non-query executed. Rows affected: {RowsAffected}", rowsAffected);
                    return rowsAffected;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing non-query");
            throw;
        }
    }

    /// <summary>
    /// Executes a query and returns scalar value
    /// </summary>
    public async Task<object?> ExecuteScalarAsync(string query)
    {
        try
        {
            using (var connection = new OdbcConnection(_connectionString))
            {
                await Task.Run(() => connection.Open());
                
                using (var command = new OdbcCommand(query, connection))
                {
                    command.CommandTimeout = _timeoutSeconds;
                    var result = await Task.Run(() => command.ExecuteScalar());
                    
                    _logger.LogDebug("Scalar query executed");
                    return result;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing scalar query");
            throw;
        }
    }
}
