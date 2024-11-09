using Microsoft.Extensions.Caching.Memory;
using System.Data;
using System.Data.Common;
using System.Reflection;

namespace DataAccess
{
    public partial class DBContext
    {
        /// <summary>
        /// Executes a specified stored procedure to read and filter data, returning a record of type <typeparamref name="TModel"/> from the database.
        /// </summary>
        /// <typeparam name="TModel">The type of the model that represents the table structure in the database.</typeparam>
        /// <param name="storedProcedureName">The name of the stored procedure to execute for the read operation.</param>
        /// <param name="model">The instance of <typeparamref name="TModel"/> containing the filter criteria for the read operation.</param>
        /// <returns>
        /// A <see cref="Task"/> representing the asynchronous operation, which, upon completion,
        /// returns a tuple containing a <see cref="ResponseInformationModel"/> with details about the operation's success or failure,
        /// and an instance of <typeparamref name="TModel"/> with the data read from the database.
        /// </returns>
        public async Task<(ResponseInformationModel, TModel)> ReadFilteredAsync<TModel>(string storedProcedureName, TModel model)
        {
            var response = new ResponseInformationModel();

            // Create a database connection using the DbFactory based on _databaseType
            using var connection = DbFactory.CreateConnection(_databaseType, _connectionString);
            await connection.OpenAsync();

            // Create a command for the specified stored procedure
            using var command = DbFactory.CreateCommand(_databaseType);
            command.Connection = connection;
            command.CommandText = storedProcedureName;
            command.CommandType = CommandType.StoredProcedure;

            // Determine the parameter prefix based on the database type
            string paramPrefix = _databaseType == DatabaseType.MySql ? "?" : "@";

            // Fetch stored procedure parameters dynamically
            var spParameters = await FetchStoredProcedureParameters(storedProcedureName, connection);

            // Cache model properties for faster access
            var modelType = typeof(TModel);
            var modelKey = modelType.FullName;

            if (!_cache.TryGetValue(modelKey, out List<PropertyInfo> modelProperties))
            {
                modelProperties = modelType.GetProperties().ToList();
                _cache.Set(modelKey, modelProperties);
            }

            // Add model properties as parameters
            foreach (var prop in modelProperties)
            {
                var matchingParam = spParameters.FirstOrDefault(p => p.ParameterName.Equals($"{paramPrefix}{prop.Name}", StringComparison.OrdinalIgnoreCase));
                if (matchingParam != null)
                {
                    var paramValue = prop.GetValue(model) ?? (matchingParam.IsNullable ? DBNull.Value : null);
                    if (paramValue != null)
                    {
                        var dbParam = command.CreateParameter();
                        dbParam.ParameterName = matchingParam.ParameterName;
                        dbParam.Value = paramValue;
                        command.Parameters.Add(dbParam);
                    }
                }
            }

            try
            {
                using var reader = await command.ExecuteReaderAsync();

                if (reader.HasRows)
                {
                    if (reader.Read())
                    {
                        // Create a new instance of TModel to hold the data
                        model = Activator.CreateInstance<TModel>();
                        var properties = modelType.GetProperties().ToDictionary(p => p.Name, p => p);

                        // Map the data from the reader to the TModel instance
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            var name = reader.GetName(i);
                            if (properties.TryGetValue(name, out var propertyInfo))
                            {
                                var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                                propertyInfo.SetValue(model, value);
                            }
                        }
                    }
                }
                else
                {
                    // Set error response if no rows are returned
                    response.ErrorCode = "-2";
                    response.HasError = true;
                    response.ErrorMessage = "No rows returned.";
                    model = default;
                }
            }
            catch (DbException ex)
            {
                File.AppendAllText(_dbContextErrorLogsPath + "ReadFilteredAsync.txt", ex.ToString());
                response.HasError = true;
                response.ErrorMessage = "There was an error executing the stored procedure.";
            }
            finally
            {
                connection.Close();
            }

            return (response, model);
        }

        /// <summary>
        /// Executes a specified stored procedure to read and filter data, returning a record of type <typeparamref name="TReturnModel"/> from the database.
        /// </summary>
        /// <typeparam name="TFilterModel">The type of the model that represents the filter criteria.</typeparam>
        /// <typeparam name="TReturnModel">The type of the model that represents the table structure in the database and the data to be returned.</typeparam>
        /// <param name="storedProcedureName">The name of the stored procedure to execute for the read operation.</param>
        /// <param name="filterModel">The instance of <typeparamref name="TFilterModel"/> containing the filter criteria for the read operation.</param>
        /// <returns>
        /// A <see cref="Task"/> representing the asynchronous operation, which, upon completion,
        /// returns a tuple containing a <see cref="ResponseInformationModel"/> with details about the operation's success or failure,
        /// and an instance of <typeparamref name="TReturnModel"/> with the data read from the database.
        /// </returns>
        public async Task<(ResponseInformationModel, TReturnModel)> ReadDifferentFilterModelAsync<TFilterModel, TReturnModel>(string storedProcedureName, TFilterModel filterModel)
        {
            var response = new ResponseInformationModel();

            using var connection = DbFactory.CreateConnection(_databaseType, _connectionString);
            await connection.OpenAsync();

            using var command = DbFactory.CreateCommand(_databaseType);
            command.Connection = connection;
            command.CommandText = storedProcedureName;
            command.CommandType = CommandType.StoredProcedure;

            string paramPrefix = _databaseType == DatabaseType.MySql ? "?" : "@";

            var spParameters = await FetchStoredProcedureParameters(storedProcedureName, connection);

            var filterModelType = typeof(TFilterModel);
            var filterModelKey = filterModelType.FullName;

            if (!_cache.TryGetValue(filterModelKey, out List<PropertyInfo> filterModelProperties))
            {
                filterModelProperties = filterModelType.GetProperties().ToList();
                _cache.Set(filterModelKey, filterModelProperties);
            }

            foreach (var prop in filterModelProperties)
            {
                var matchingParam = spParameters.FirstOrDefault(p => p.ParameterName.Equals($"{paramPrefix}{prop.Name}", StringComparison.OrdinalIgnoreCase));
                if (matchingParam != null)
                {
                    var paramValue = prop.GetValue(filterModel) ?? (matchingParam.IsNullable ? DBNull.Value : null);
                    if (paramValue != null)
                    {
                        var dbParam = command.CreateParameter();
                        dbParam.ParameterName = matchingParam.ParameterName;
                        dbParam.Value = paramValue;
                        command.Parameters.Add(dbParam);
                    }
                }
            }

            TReturnModel model = default;

            try
            {
                using var reader = await command.ExecuteReaderAsync();

                if (reader.HasRows)
                {
                    if (reader.Read())
                    {
                        model = Activator.CreateInstance<TReturnModel>();
                        var properties = typeof(TReturnModel).GetProperties().ToDictionary(p => p.Name, p => p);

                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            var name = reader.GetName(i);
                            if (properties.TryGetValue(name, out var propertyInfo))
                            {
                                var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                                propertyInfo.SetValue(model, value);
                            }
                        }
                    }
                }
                else
                {
                    response.ErrorCode = "-2";
                    response.HasError = true;
                    response.ErrorMessage = "No rows returned.";
                    model = default;
                }
            }
            catch (DbException ex)
            {
                File.AppendAllText(_dbContextErrorLogsPath + "ReadDifferentFilterModelAsync.txt", ex.ToString());
                response.HasError = true;
                response.ErrorMessage = "There was an error executing the stored procedure.";
            }
            finally
            {
                connection.Close();
            }

            return (response, model);
        }

        /// <summary>
        /// Retrieves a DataTable from the database based on the given filter parameters.
        /// </summary>
        /// <param name="storedProcedureName">The name of the stored procedure to be executed.</param>
        /// <param name="parameters">A list of key-value pairs representing the parameters for the stored procedure.</param>
        /// <returns>A <see cref="DataTable"/> containing the query results.</returns>
        /// <exception cref="DbException">Thrown when there is a database-specific error during the operation.</exception>
        /// <exception cref="Exception">Thrown when there is a generic exception during the operation.</exception>
        public async Task<DataTable> ReadQueryResultToDatatable(string storedProcedureName, List<KeyValuePair<string, object>> parameters)
        {
            var dataTable = new DataTable();

            try
            {
                using var connection = DbFactory.CreateConnection(_databaseType, _connectionString);
                await connection.OpenAsync();

                // Fetch required parameters for the stored procedure
                var spParameters = await FetchStoredProcedureParameters(storedProcedureName, connection);
                var requiredParams = new HashSet<string>(spParameters.Select(p => p.ParameterName.TrimStart('@').ToLowerInvariant()));

                using (var command = DbFactory.CreateCommand(_databaseType))
                {
                    command.Connection = connection;
                    command.CommandText = storedProcedureName;
                    command.CommandType = CommandType.StoredProcedure;

                    // Determine the parameter prefix based on the database type
                    string paramPrefix = _databaseType == DatabaseType.MySql ? "?" : "@";

                    // Add provided parameters to the command and mark as added
                    foreach (var param in parameters)
                    {
                        var dbParam = command.CreateParameter();
                        dbParam.ParameterName = $"{paramPrefix}{param.Key}";
                        dbParam.Value = param.Value ?? DBNull.Value;
                        command.Parameters.Add(dbParam);
                        requiredParams.Remove(param.Key.ToLowerInvariant());
                    }

                    // Add any missing required parameters with default values
                    foreach (var paramName in requiredParams)
                    {
                        var dbParam = command.CreateParameter();
                        dbParam.ParameterName = $"{paramPrefix}{paramName}";
                        dbParam.Value = DBNull.Value;
                        command.Parameters.Add(dbParam);
                    }

                    using var reader = await command.ExecuteReaderAsync();

                    // Populate DataTable from the reader
                    if (reader.HasRows)
                    {
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            dataTable.Columns.Add(reader.GetName(i), reader.GetFieldType(i));
                        }

                        while (reader.Read())
                        {
                            var values = new object[reader.FieldCount];
                            reader.GetValues(values);
                            dataTable.Rows.Add(values);
                        }
                    }
                }

                return dataTable;
            }
            catch (DbException dbEx)
            {
                await File.AppendAllTextAsync(_dbContextErrorLogsPath + "QueryResultToDatatable.txt", dbEx.ToString());
                throw;
            }
            catch (Exception ex)
            {
                await File.AppendAllTextAsync(_dbContextErrorLogsPath + "QueryResultToDatatable.txt", ex.ToString());
                throw;
            }
        }

    }
}
