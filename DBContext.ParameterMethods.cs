using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Caching.Memory;
using System.Data;
using System.Data.Common;
using System.Data.SqlClient;
using System.Reflection;

namespace DataAccess
{
    public partial class DBContext
    {
        /// <summary>
        /// Fetches the parameters of a stored procedure from the database.
        /// </summary>
        /// <param name="storedProcedureName">The name of the stored procedure to fetch parameters for.</param>
        /// <param name="connection">An open <see cref="DbConnection"/> to the database.</param>
        /// <returns>A list of <see cref="DbParameter"/> representing the parameters of the stored procedure.</returns>
        private async Task<List<DbParameter>> FetchStoredProcedureParameters(string storedProcedureName, DbConnection connection)
        {
            var parameters = new List<DbParameter>();

            // Create a command to retrieve procedure parameters, adapting for SQL Server and MySQL syntax
            string query = _databaseType == DatabaseType.SqlServer
                ? @"
            SELECT 
                Parameter_name = name, 
                IsNullable = is_nullable
            FROM sys.parameters 
            WHERE object_id = OBJECT_ID(@storedProcedureName)"
                : @"SELECT 
                PARAMETER_NAME AS Parameter_name, 
                IS_NULLABLE AS IsNullable 
            FROM INFORMATION_SCHEMA.PARAMETERS 
            WHERE SPECIFIC_NAME = @storedProcedureName";

            using var command = connection.CreateCommand();
            command.CommandText = query;
            command.CommandType = CommandType.Text;

            var param = command.CreateParameter();
            param.ParameterName = "@storedProcedureName";
            param.Value = storedProcedureName;
            command.Parameters.Add(param);

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var dbParam = command.CreateParameter();
                dbParam.ParameterName = reader["Parameter_name"].ToString();
                dbParam.IsNullable = reader["IsNullable"].ToString().Equals("YES", StringComparison.OrdinalIgnoreCase);
                parameters.Add(dbParam);
            }

            return parameters;
        }

        /// <summary>
        /// Maps properties of a model, including nested complex types, to SQL parameters for a SqlCommand.
        /// </summary>
        /// <param name="model">The model object whose properties are to be mapped to SQL parameters.</param>
        /// <param name="modelType">The Type of the model object.</param>
        /// <param name="command">The SqlCommand to which the parameters will be added.</param>
        /// <param name="spParameters">A list of SqlParameter objects representing the parameters of the stored procedure.</param>
        private void MapModelToParameters(object model, Type modelType, SqlCommand command, List<SqlParameter> spParameters)
        {
            foreach (var prop in modelType.GetProperties())
            {
                if (IsComplexType(prop.PropertyType))
                {
                    // If the property is a complex type, process its properties
                    var nestedModel = prop.GetValue(model);
                    if (nestedModel != null)
                    {
                        MapModelToParameters(nestedModel, prop.PropertyType, command, spParameters);
                    }
                }
                else
                {
                    // Map simple properties as before
                    var matchingParam = spParameters.FirstOrDefault(p => p.ParameterName.Equals($"@{prop.Name}", StringComparison.OrdinalIgnoreCase));
                    if (matchingParam != null)
                    {
                        var paramValue = prop.GetValue(model);
                        paramValue = paramValue ?? (matchingParam.IsNullable ? DBNull.Value : default);
                        command.Parameters.AddWithValue(matchingParam.ParameterName, paramValue);
                    }
                }
            }
        }

        /// <summary>
        /// Determines whether a given Type is a complex type, defined as not being a primitive, an enum, or a string.
        /// </summary>
        /// <param name="type">The Type to check.</param>
        /// <returns>True if the Type is a complex type; otherwise, false.</returns>
        private bool IsComplexType(Type type)
        {
            // Determine if the type is a complex type (not a primitive, enum, or string)
            return !type.IsPrimitive && !type.IsEnum && type != typeof(string);
        }

        /// <summary>
        /// Collects the values of properties from a model, including nested complex types, into a dictionary.
        /// </summary>
        /// <param name="model">The model object from which property values are to be collected.</param>
        /// <param name="modelType">The Type of the model object.</param>
        /// <param name="propertyValues">A dictionary to store the property values, keyed by property names.</param>
        private void CollectPropertyValues(object model, Type modelType, Dictionary<string, object> propertyValues)
        {
            foreach (var prop in modelType.GetProperties())
            {
                if (IsComplexType(prop.PropertyType))
                {
                    var nestedModel = prop.GetValue(model);
                    if (nestedModel != null)
                    {
                        CollectPropertyValues(nestedModel, prop.PropertyType, propertyValues);
                    }
                }
                else
                {
                    var value = prop.GetValue(model);
                    if (value != null)
                    {
                        propertyValues[prop.Name] = value; // This will overwrite the value if it was already set by a previous model
                    }
                }
            }
        }

        /// <summary>
        /// Converts specified properties of a model into a list of key-value pairs for stored procedure parameters.
        /// </summary>
        /// <typeparam name="TModel">The type of the model.</typeparam>
        /// <param name="model">The model instance from which to extract values.</param>
        /// <param name="properties">Comma delimited string of property names to include.</param>
        /// <returns>A list of key-value pairs representing the specified properties and their values.</returns>
        public List<KeyValuePair<string, object>> ModelToSPParameterListBuilder<TModel>(TModel model, string properties,List<KeyValuePair<string, object>> existingParameters = null)
        {
            var parameterList = existingParameters ?? new List<KeyValuePair<string, object>>();
            var propertyNames = properties.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                          .Select(p => p.Trim());

            var modelType = typeof(TModel);
            var cacheKey = $"{modelType.FullName}_Properties";

            if (!_cache.TryGetValue(cacheKey, out Dictionary<string, PropertyInfo> cachedProperties))
            {
                // Cache miss, so load properties and cache them
                cachedProperties = modelType.GetProperties().ToDictionary(p => p.Name, p => p);
                _cache.Set(cacheKey, cachedProperties);
            }

            foreach (var propName in propertyNames)
            {
                if (cachedProperties.TryGetValue(propName, out PropertyInfo propertyInfo))
                {
                    var value = propertyInfo.GetValue(model);
                    parameterList.Add(new KeyValuePair<string, object>(propName, value ?? DBNull.Value));
                }
            }

            return parameterList;
        }

        /// <summary>
        /// Removes validation errors from the ModelState that correspond to the input parameters of a specified stored procedure.
        /// </summary>
        /// <remarks>
        /// This method establishes a connection to the database using the provided connection string, retrieves the parameters
        /// for the specified stored procedure, and iterates through them. If a parameter name matches a property name in the
        /// provided model, any validation errors associated with that model property in the ModelState are removed.
        /// </remarks>
        /// <param name="modelState">The ModelState dictionary from which to remove validation errors.</param>
        /// <param name="storedProcedureName">The name of the stored procedure whose input parameters are used for matching model properties.</param>
        /// <param name="model">The model instance whose properties are checked against the stored procedure's parameters.</param>
        /// <exception cref="ArgumentNullException">Thrown if any of the method arguments are null.</exception>
        /// <exception cref="ArgumentException">Thrown if the storedProcedureName is null or empty.</exception>
        public void RemoveErrorsBasedOnSpParams(ModelStateDictionary modelState, string storedProcedureName, object model)
        {
            if (modelState == null) throw new ArgumentNullException(nameof(modelState));
            if (string.IsNullOrEmpty(storedProcedureName)) throw new ArgumentException("Stored procedure name cannot be null or empty", nameof(storedProcedureName));
            if (model == null) throw new ArgumentNullException(nameof(model));

            var modelProperties = model.GetType().GetProperties();
            var spParameters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using (var connection = new SqlConnection(_connectionString))
            {
                connection.Open();
                var command = new SqlCommand(storedProcedureName, connection) { CommandType = CommandType.StoredProcedure };

                // Retrieve stored procedure parameters
                SqlCommandBuilder.DeriveParameters(command);

                foreach (SqlParameter param in command.Parameters)
                {
                    if (param.Direction == ParameterDirection.Input || param.Direction == ParameterDirection.InputOutput)
                    {
                        string paramName = param.ParameterName.Replace("@", ""); // Normalize parameter name
                        spParameters.Add(paramName);
                    }
                }
            }

            // Check for model properties that do not match any SP parameter
            foreach (var prop in modelProperties)
            {
                if (!spParameters.Contains(prop.Name) && modelState.ContainsKey(prop.Name))
                {
                    modelState.Remove(prop.Name); // Remove the ModelState errors for non-matching properties
                }
            }
        }

    }
}
