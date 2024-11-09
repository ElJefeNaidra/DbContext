using Microsoft.Extensions.Caching.Memory;

namespace DataAccess
{
    public partial class DBContext
    {
        private readonly string _connectionString;
        private readonly DatabaseType _databaseType;
        private readonly IMemoryCache _cache;
        private readonly IHttpContextAccessor _accessor;

        public DBContext(string connectionString, IMemoryCache cache, IHttpContextAccessor accessor)
            : this(connectionString, GetDatabaseTypeFromConnectionString(connectionString), cache, accessor)
        {
        }

        public DBContext(string connectionString, DatabaseType databaseType, IMemoryCache cache, IHttpContextAccessor accessor)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _databaseType = databaseType;
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _accessor = accessor ?? throw new ArgumentNullException(nameof(accessor));
        }
    }
}
