using Microsoft.Extensions.Caching.Memory;
using System.Data;
using System.Data.Common;
using System.Reflection;

namespace DataAccess
{
    public partial class DBContext
    {
        /// <summary>
        /// Executes a specified stored procedure to insert a record of type <typeparamref name="TModel"/> in the database.
        /// </summary>
        /// <typeparam name="TModel">The type of the model that represents the table structure in the database.</typeparam>
        /// <param name="storedProcedureName">The name of the stored procedure to execute for the insert operation.</param>
        /// <param name="model">The instance of <typeparamref name="TModel"/> containing the data for the insert operation.</param>
        /// <returns>
        /// A <see cref="Task"/> representing the asynchronous operation, which, upon completion,
        /// returns a tuple containing a <see cref="ResponseInformationModel"/> with details about the success or failure
        /// of the operation and the inserted model.
        /// </returns>
        /// <exception cref="DbException">Thrown if a database error occurs during the operation.</exception>
        public async Task<(ResponseInformationModel, TModel)> InsertAsync<TModel>(string storedProcedureName, TModel model)
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

                // Process the result from the stored procedure
                if (reader.Read())
                {
                    if (reader.GetOrdinal("IdValue") != -1 && !reader.IsDBNull(reader.GetOrdinal("IdValue")))
                        response.IdValue = reader.GetInt32(reader.GetOrdinal("IdValue"));

                    if (reader.GetOrdinal("HasError") != -1 && !reader.IsDBNull(reader.GetOrdinal("HasError")))
                        response.HasError = reader.GetBoolean(reader.GetOrdinal("HasError"));

                    if (reader.GetOrdinal("ErrorCode") != -1 && !reader.IsDBNull(reader.GetOrdinal("ErrorCode")))
                        response.ErrorCode = reader.GetString(reader.GetOrdinal("ErrorCode"));

                    if (reader.GetOrdinal("ErrorMessage") != -1 && !reader.IsDBNull(reader.GetOrdinal("ErrorMessage")))
                        response.ErrorMessage = reader.GetString(reader.GetOrdinal("ErrorMessage"));

                    if (reader.GetOrdinal("InformationMessage") != -1 && !reader.IsDBNull(reader.GetOrdinal("InformationMessage")))
                        response.InformationMessage = reader.GetString(reader.GetOrdinal("InformationMessage"));

                    if (reader.GetOrdinal("_RowGuid") != -1 && !reader.IsDBNull(reader.GetOrdinal("_RowGuid")))
                        response._RowGuid = reader.GetString(reader.GetOrdinal("_RowGuid"));
                }
            }
            catch (DbException ex)
            {
                // Log error and set response
                File.AppendAllText(_dbContextErrorLogsPath + "InsertAsync.txt", ex.ToString());
                response.HasError = true;
                response.ErrorMessage = "There was an error executing the stored procedure.";
            }
            finally
            {
                // Ensure the connection is closed
                connection.Close();
            }

            return (response, model);
        }


    }
}
