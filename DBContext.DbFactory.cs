using Microsoft.Data.SqlClient;
using MySql.Data.MySqlClient;
using System.Data.Common;

namespace DataAccess
{
    public partial class DBContext
    {
        /// <summary>
        /// Supported database types for the application.
        /// </summary>
        public enum DatabaseType
        {
            SqlServer,
            MySql
        }

        private static readonly string _dbContextErrorLogsPath = "C:\\Logs\\DbContext\\";

        /// <summary>
        /// Factory class to create database-specific connections and commands.
        /// </summary>
        public static class DbFactory
        {
            /// <summary>
            /// Creates a new database connection based on the specified database type.
            /// </summary>
            /// <param name="dbType">The type of database (SQL Server or MySQL).</param>
            /// <param name="connectionString">The connection string to use for the connection.</param>
            /// <returns>A <see cref="DbConnection"/> object for the specified database type.</returns>
            /// <exception cref="NotSupportedException">Thrown when the specified database type is not supported.</exception>
            public static DbConnection CreateConnection(DatabaseType dbType, string connectionString)
            {
                // Choose the appropriate database connection type based on dbType
                return dbType switch
                {
                    DatabaseType.SqlServer => new SqlConnection(connectionString),
                    DatabaseType.MySql => new MySqlConnection(connectionString),
                    _ => throw new NotSupportedException("Database type not supported.")
                };
            }

            /// <summary>
            /// Creates a new database command object based on the specified database type.
            /// </summary>
            /// <param name="dbType">The type of database (SQL Server or MySQL).</param>
            /// <returns>A <see cref="DbCommand"/> object for the specified database type.</returns>
            /// <exception cref="NotSupportedException">Thrown when the specified database type is not supported.</exception>
            public static DbCommand CreateCommand(DatabaseType dbType)
            {
                // Choose the appropriate database command type based on dbType
                return dbType switch
                {
                    DatabaseType.SqlServer => new SqlCommand(),
                    DatabaseType.MySql => new MySqlCommand(),
                    _ => throw new NotSupportedException("Database type not supported.")
                };
            }
        }

        /// <summary>
        /// Determines the type of database (SQL Server or MySQL) based on keywords in the connection string.
        /// </summary>
        /// <param name="connectionString">The database connection string.</param>
        /// <returns>The inferred <see cref="DatabaseType"/> (SqlServer or MySql).</returns>
        /// <exception cref="NotSupportedException">Thrown when the database type cannot be determined from the connection string.</exception>
        public static DatabaseType GetDatabaseTypeFromConnectionString(string connectionString)
        {
            // Check for SQL Server or MySQL-specific keywords in the connection string
            if (connectionString.Contains("Server=", StringComparison.OrdinalIgnoreCase) ||
                connectionString.Contains("Data Source=", StringComparison.OrdinalIgnoreCase))
            {
                // Look for MySQL-specific keywords
                if (connectionString.Contains("Uid=", StringComparison.OrdinalIgnoreCase) ||
                    connectionString.Contains("User Id=", StringComparison.OrdinalIgnoreCase) &&
                    connectionString.Contains("Port=", StringComparison.OrdinalIgnoreCase))
                {
                    return DatabaseType.MySql;
                }
                // Default to SQL Server if MySQL-specific keywords are not found
                return DatabaseType.SqlServer;
            }
            throw new NotSupportedException("Database type could not be determined from connection string.");
        }

    }
}
