using System.Data;
using System.Data.Common;

namespace DataAccess
{
    public partial class DBContext
    {
        /// <summary>
        /// Executes a specified stored procedure to update a record of type <typeparamref name="TModel"/> in the database.
        /// </summary>
        /// <typeparam name="TModel">The type of the model that represents the table structure in the database.</typeparam>
        /// <param name="storedProcedureName">The name of the stored procedure to execute for the update operation.</param>
        /// <param name="model">The instance of <typeparamref name="TModel"/> containing the data for the update operation.</param>
        /// <returns>
        /// A <see cref="Task"/> representing the asynchronous operation, which, upon completion,
        /// returns a <see cref="ResponseInformationModel"/> containing details about the success or failure of the operation,
        /// along with the updated model instance.
        /// </returns>
        public async Task<(ResponseInformationModel, TModel)> UpdateAsync<TModel>(string storedProcedureName, TModel model)
        {
            var response = new ResponseInformationModel(); // Initialize the response model to capture any errors or messages.

            // Create and open the database connection.
            using var connection = DbFactory.CreateConnection(_databaseType, _connectionString);
            await connection.OpenAsync();

            // Create the database command to execute the stored procedure.
            using var command = DbFactory.CreateCommand(_databaseType);
            command.Connection = connection;
            command.CommandText = storedProcedureName;
            command.CommandType = CommandType.StoredProcedure;

            // Determine the appropriate parameter prefix based on the database type.
            string paramPrefix = _databaseType == DatabaseType.MySql ? "?" : "@";

            // Fetch stored procedure parameters to match model properties.
            var spParameters = await FetchStoredProcedureParameters(storedProcedureName, connection);
            var modelType = typeof(TModel);
            var modelProperties = modelType.GetProperties();

            // Map model properties to stored procedure parameters, using default values where necessary.
            foreach (var prop in modelProperties)
            {
                var matchingParam = spParameters.FirstOrDefault(p => p.ParameterName.Equals($"{paramPrefix}{prop.Name}", StringComparison.OrdinalIgnoreCase));
                if (matchingParam != null)
                {
                    var paramValue = prop.GetValue(model) ?? (matchingParam.IsNullable ? DBNull.Value : null);
                    if (paramValue != null)
                    {
                        // Add each mapped parameter to the command.
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

                // Process the stored procedure result set for success/error details.
                if (reader.Read())
                {
                    response.IdValue = reader.GetInt32(reader.GetOrdinal("IdValue"));
                    response.HasError = reader.GetBoolean(reader.GetOrdinal("HasError"));
                    response.ErrorCode = reader.GetString(reader.GetOrdinal("ErrorCode"));
                    response.ErrorMessage = reader.GetString(reader.GetOrdinal("ErrorMessage"));
                    response.InformationMessage = reader.GetString(reader.GetOrdinal("InformationMessage"));
                }
            }
            catch (DbException ex)
            {
                // Log any database exceptions encountered.
                await File.AppendAllTextAsync(_dbContextErrorLogsPath + "UpdateAsync.txt", ex.ToString());
                response.HasError = true;
                response.ErrorMessage = "There was an error executing the stored procedure.";
            }
            finally
            {
                connection.Close(); // Ensure the connection is closed after the operation completes.
            }

            return (response, model);
        }

        /// <summary>
        /// Asynchronously performs an update operation using a nested model and a specified stored procedure.
        /// </summary>
        /// <typeparam name="TModel">The type of the model to be updated.</typeparam>
        /// <param name="storedProcedureName">The name of the stored procedure to be executed.</param>
        /// <param name="model">The model object containing the data to be updated.</param>
        /// <returns>
        /// A Task representing the asynchronous operation, which upon completion returns a tuple consisting of
        /// a <see cref="ResponseInformationModel"/> and the updated <typeparamref name="TModel"/>.
        /// </returns>
        public async Task<(ResponseInformationModel, TModel)> UpdateWithViewModelAsync<TModel>(string storedProcedureName, TModel model)
        {
            var response = new ResponseInformationModel(); // Initialize the response model.

            // Create and open the database connection.
            using var connection = DbFactory.CreateConnection(_databaseType, _connectionString);
            await connection.OpenAsync();

            // Create the database command to execute the stored procedure.
            using var command = DbFactory.CreateCommand(_databaseType);
            command.Connection = connection;
            command.CommandText = storedProcedureName;
            command.CommandType = CommandType.StoredProcedure;

            string paramPrefix = _databaseType == DatabaseType.MySql ? "?" : "@";

            // Fetch stored procedure parameters to match with model properties.
            var spParameters = await FetchStoredProcedureParameters(storedProcedureName, connection);
            var modelType = typeof(TModel);
            var modelProperties = modelType.GetProperties();

            // Dictionary to hold property values from the model, even nested properties.
            var propertyValues = new Dictionary<string, object>();
            CollectPropertyValues(model, typeof(TModel), propertyValues);

            // Map property values to command parameters for the stored procedure.
            foreach (var kvp in propertyValues)
            {
                var matchingParam = spParameters.FirstOrDefault(p => p.ParameterName.Equals($"{paramPrefix}{kvp.Key}", StringComparison.OrdinalIgnoreCase));
                if (matchingParam != null)
                {
                    var paramValue = kvp.Value ?? (matchingParam.IsNullable ? DBNull.Value : null);
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

                // Process the response to extract update results or error details.
                if (reader.Read())
                {
                    response.IdValue = reader.GetInt32(reader.GetOrdinal("IdValue"));
                    response.HasError = reader.GetBoolean(reader.GetOrdinal("HasError"));
                    response.ErrorCode = reader.GetString(reader.GetOrdinal("ErrorCode"));
                    response.ErrorMessage = reader.GetString(reader.GetOrdinal("ErrorMessage"));
                    response.InformationMessage = reader.GetString(reader.GetOrdinal("InformationMessage"));
                }
            }
            catch (DbException ex)
            {
                await File.AppendAllTextAsync(_dbContextErrorLogsPath + "UpdateWithViewModelAsync.txt", ex.ToString());
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
        /// Asynchronously executes a stored procedure for an update operation, using a list of parameter values.
        /// </summary>
        /// <param name="storedProcedureName">The name of the stored procedure to execute.</param>
        /// <param name="parameterValues">A list of key-value pairs representing the parameters and their values for the stored procedure.</param>
        /// <returns>
        /// A <see cref="Task"/> representing the asynchronous operation, which upon completion returns a <see cref="ResponseInformationModel"/>.
        /// This model contains details about the success or failure of the operation, including error messages if any.
        /// </returns>
        public async Task<ResponseInformationModel> UpdateFromParametersListAsync(string storedProcedureName, List<KeyValuePair<string, object>> parameterValues)
        {
            var response = new ResponseInformationModel(); // Initialize the response model.

            // Create and open the database connection.
            using var connection = DbFactory.CreateConnection(_databaseType, _connectionString);
            await connection.OpenAsync();

            using var command = DbFactory.CreateCommand(_databaseType);
            command.Connection = connection;
            command.CommandText = storedProcedureName;
            command.CommandType = CommandType.StoredProcedure;

            string paramPrefix = _databaseType == DatabaseType.MySql ? "?" : "@";

            // Fetch stored procedure parameters to check for missing parameters.
            var spParameters = await FetchStoredProcedureParameters(storedProcedureName, connection);
            var missingParameters = spParameters
                .Where(sp => !parameterValues.Any(pv => $"{paramPrefix}{pv.Key}".Equals(sp.ParameterName, StringComparison.OrdinalIgnoreCase)))
                .Select(sp => sp.ParameterName)
                .ToList();

            // Validate that all required parameters are provided; if not, set an error response.
            if (missingParameters.Any())
            {
                response.HasError = true;
                response.ErrorMessage = "Missing parameters: " + string.Join(", ", missingParameters);
                return response;
            }

            // Add provided parameters to the command.
            foreach (var param in parameterValues)
            {
                var matchingParam = spParameters.FirstOrDefault(p => $"{paramPrefix}{param.Key}".Equals(p.ParameterName, StringComparison.OrdinalIgnoreCase));
                if (matchingParam != null)
                {
                    var dbParam = command.CreateParameter();
                    dbParam.ParameterName = matchingParam.ParameterName;
                    dbParam.Value = param.Value ?? DBNull.Value;
                    command.Parameters.Add(dbParam);
                }
            }

            try
            {
                using var reader = await command.ExecuteReaderAsync();

                // Process the result set for success/error details.
                if (reader.Read())
                {
                    response.IdValue = reader.GetInt32(reader.GetOrdinal("IdValue"));
                    response.HasError = reader.GetBoolean(reader.GetOrdinal("HasError"));
                    response.ErrorCode = reader.GetString(reader.GetOrdinal("ErrorCode"));
                    response.ErrorMessage = reader.GetString(reader.GetOrdinal("ErrorMessage"));
                    response.InformationMessage = reader.GetString(reader.GetOrdinal("InformationMessage"));
                }
            }
            catch (DbException ex)
            {
                await File.AppendAllTextAsync(_dbContextErrorLogsPath + "UpdateFromParametersListAsync.txt", ex.ToString());
                response.HasError = true;
                response.ErrorMessage = "There was an error executing the stored procedure.";
            }
            finally
            {
                connection.Close();
            }

            return response;
        }

    }
}

