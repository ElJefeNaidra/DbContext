using System.Data;
using System.Data.Common;
using System.Reflection;

namespace DataAccess
{
    public partial class DBContext
    {
        /// <summary>
        /// Used to decorate FilterModel properties with [Skip] to enable them to be skipped
        /// during maping of FilterModel properties to SP parameter names 1:1
        /// </summary>

        [AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
        public class SkipAttribute : Attribute
        {
        }

        /// <summary>
        /// Retrieves a list of data from the database based on the given filter, along with pagination options.
        /// </summary>
        /// <typeparam name="DataModel">The type of data model to be returned.</typeparam>
        /// <param name="storedProcedureName">The name of the stored procedure to be executed.</param>
        /// <param name="filter">An object containing filtering criteria.</param>
        /// <returns>A tuple containing a list of data, the total number of rows, and response information.</returns>
        /// <exception cref="DbException">Thrown when there's a database-specific error during the operation.</exception>
        public async Task<(IEnumerable<DataModel> Data, int TotalRows, ResponseInformationModel ResponseInfo)> GridAsync<DataModel>(
            string storedProcedureName,
            object filter) where DataModel : new()
        {
            var responseInfo = new ResponseInformationModel();
            List<DataModel> resultList = new List<DataModel>();
            int totalCount = 0;

            try
            {
                using var connection = DbFactory.CreateConnection(_databaseType, _connectionString);
                await connection.OpenAsync();

                using var command = DbFactory.CreateCommand(_databaseType);
                command.Connection = connection;
                command.CommandText = storedProcedureName;
                command.CommandType = CommandType.StoredProcedure;

                string paramPrefix = _databaseType == DatabaseType.MySql ? "?" : "@";

                // Fetch required stored procedure parameters
                var spParameters = await FetchStoredProcedureParameters(storedProcedureName, connection);
                var requiredParams = new HashSet<string>(spParameters.Select(p => p.ParameterName.TrimStart('@').ToLowerInvariant()));

                // Get properties, skipping those with the `[Skip]` attribute
                var properties = filter.GetType().GetProperties()
                    .Where(p => p.GetCustomAttribute<SkipAttribute>() == null)
                    .ToList();

                // Bind properties to command parameters
                foreach (var prop in properties)
                {
                    var dbParam = command.CreateParameter();
                    dbParam.ParameterName = $"{paramPrefix}{prop.Name}";
                    dbParam.Value = prop.GetValue(filter) ?? DBNull.Value;
                    command.Parameters.Add(dbParam);
                    requiredParams.Remove(prop.Name.ToLowerInvariant());
                }

                // Add default values for missing required parameters
                foreach (var paramName in requiredParams)
                {
                    var dbParam = command.CreateParameter();
                    dbParam.ParameterName = $"{paramPrefix}{paramName}";
                    dbParam.Value = DBNull.Value;
                    command.Parameters.Add(dbParam);
                }

                // Execute command and read results
                using var reader = await command.ExecuteReaderAsync();

                if (reader.HasRows)
                {
                    var fieldNames = new HashSet<string>();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        fieldNames.Add(reader.GetName(i));
                    }

                    while (await reader.ReadAsync())
                    {
                        var item = new DataModel();
                        foreach (var prop in item.GetType().GetProperties())
                        {
                            if (fieldNames.Contains(prop.Name))
                            {
                                var value = reader[prop.Name] == DBNull.Value ? null : reader[prop.Name];
                                prop.SetValue(item, value);
                            }
                        }
                        resultList.Add(item);
                    }

                    // Read total count from the second result set
                    if (await reader.NextResultAsync() && await reader.ReadAsync())
                    {
                        totalCount = reader.GetInt32(0);
                    }
                }
            }
            catch (DbException dbEx)
            {
                await File.AppendAllTextAsync(_dbContextErrorLogsPath + "GridAsync.txt", dbEx.ToString());
                responseInfo.HasError = true;
                responseInfo.ErrorMessage = dbEx.Message;
            }
            return (resultList, totalCount, responseInfo);
        }

        /// <summary>
        /// Retrieves a DataTable from the database based on the given filter parameters.
        /// </summary>
        /// <param name="storedProcedureName">The name of the stored procedure to be executed.</param>
        /// <param name="parameters">A list of key-value pairs representing the parameters for the stored procedure.</param>
        /// <returns>A <see cref="DataTable"/> containing the query results and total row count.</returns>
        /// <exception cref="DbException">Thrown when there is a database-specific error during the operation.</exception>
        public async Task<(DataTable Data, int TotalRows)> GridDataAsync(
            string storedProcedureName,
            List<KeyValuePair<string, object>> parameters)
        {
            var dataTable = new DataTable();
            int totalCount = 0;

            try
            {
                using var connection = DbFactory.CreateConnection(_databaseType, _connectionString);
                await connection.OpenAsync();

                using var command = DbFactory.CreateCommand(_databaseType);
                command.Connection = connection;
                command.CommandText = storedProcedureName;
                command.CommandType = CommandType.StoredProcedure;

                string paramPrefix = _databaseType == DatabaseType.MySql ? "?" : "@";

                var spParameters = await FetchStoredProcedureParameters(storedProcedureName, connection);
                var requiredParams = new HashSet<string>(spParameters.Select(p => p.ParameterName.TrimStart('@').ToLowerInvariant()));

                foreach (var param in parameters)
                {
                    var dbParam = command.CreateParameter();
                    dbParam.ParameterName = $"{paramPrefix}{param.Key}";
                    dbParam.Value = param.Value ?? DBNull.Value;
                    command.Parameters.Add(dbParam);
                    requiredParams.Remove(param.Key.ToLowerInvariant());
                }

                foreach (var paramName in requiredParams)
                {
                    var dbParam = command.CreateParameter();
                    dbParam.ParameterName = $"{paramPrefix}{paramName}";
                    dbParam.Value = DBNull.Value;
                    command.Parameters.Add(dbParam);
                }

                using var reader = await command.ExecuteReaderAsync();

                if (reader.HasRows)
                {
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        dataTable.Columns.Add(reader.GetName(i), reader.GetFieldType(i));
                    }

                    while (await reader.ReadAsync())
                    {
                        var values = new object[reader.FieldCount];
                        reader.GetValues(values);
                        dataTable.Rows.Add(values);
                    }
                }

                if (await reader.NextResultAsync() && await reader.ReadAsync())
                {
                    totalCount = reader.GetInt32(0);
                }
            }
            catch (DbException dbEx)
            {
                await File.AppendAllTextAsync(_dbContextErrorLogsPath + "GridDataAsync.txt", dbEx.ToString());
                throw;
            }

            return (dataTable, totalCount);
        }

        /// <summary>
        /// Retrieves a DataTable from the database with optional column filtering.
        /// </summary>
        /// <param name="storedProcedureName">The name of the stored procedure to be executed.</param>
        /// <param name="parameters">A list of key-value pairs representing the parameters for the stored procedure.</param>
        /// <param name="columnNamesToShow">An optional list of column names to include in the DataTable.</param>
        /// <returns>A tuple containing a DataTable and the total row count.</returns>
        public async Task<(DataTable Data, int TotalRows)> GridDataTableSimpleAsync(
            string storedProcedureName,
            List<KeyValuePair<string, object>> parameters,
            List<string> columnNamesToShow = null)
        {
            var dataTable = new DataTable();
            int totalCount = 0;

            try
            {
                using var connection = DbFactory.CreateConnection(_databaseType, _connectionString);
                await connection.OpenAsync();

                using var command = DbFactory.CreateCommand(_databaseType);
                command.Connection = connection;
                command.CommandText = storedProcedureName;
                command.CommandType = CommandType.StoredProcedure;

                string paramPrefix = _databaseType == DatabaseType.MySql ? "?" : "@";

                var spParameters = await FetchStoredProcedureParameters(storedProcedureName, connection);
                var requiredParams = new HashSet<string>(spParameters.Select(p => p.ParameterName.TrimStart('@').ToLowerInvariant()));

                foreach (var param in parameters)
                {
                    var dbParam = command.CreateParameter();
                    dbParam.ParameterName = $"{paramPrefix}{param.Key}";
                    dbParam.Value = param.Value ?? DBNull.Value;
                    command.Parameters.Add(dbParam);
                    requiredParams.Remove(param.Key.ToLowerInvariant());
                }

                foreach (var paramName in requiredParams)
                {
                    var dbParam = command.CreateParameter();
                    dbParam.ParameterName = $"{paramPrefix}{paramName}";
                    dbParam.Value = DBNull.Value;
                    command.Parameters.Add(dbParam);
                }

                using var reader = await command.ExecuteReaderAsync();
                var columnFilterSet = columnNamesToShow != null ? new HashSet<string>(columnNamesToShow, StringComparer.OrdinalIgnoreCase) : null;

                if (reader.HasRows)
                {
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        string columnName = reader.GetName(i);
                        if (columnFilterSet == null || columnFilterSet.Contains(columnName))
                        {
                            dataTable.Columns.Add(columnName, reader.GetFieldType(i));
                        }
                    }

                    while (await reader.ReadAsync())
                    {
                        var values = new object[dataTable.Columns.Count];
                        for (int i = 0, j = 0; i < reader.FieldCount; i++)
                        {
                            if (columnFilterSet == null || columnFilterSet.Contains(reader.GetName(i)))
                            {
                                values[j++] = reader.IsDBNull(i) ? DBNull.Value : reader.GetValue(i);
                            }
                        }
                        dataTable.Rows.Add(values);
                    }
                }

                if (await reader.NextResultAsync() && await reader.ReadAsync())
                {
                    totalCount = reader.GetInt32(0);
                }
            }
            catch (DbException dbEx)
            {
                await File.AppendAllTextAsync(_dbContextErrorLogsPath + "GridDataTableSimpleAsync.txt", dbEx.ToString());
                throw;
            }

            return (dataTable, totalCount);
        }


    }
}
