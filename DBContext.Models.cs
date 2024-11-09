namespace DataAccess
{
    public partial class DBContext
    {
        /// <summary>
        /// Represents the information about a response from a database operation.
        /// This model is used to encapsulate details about the outcome of a database operation,
        /// such as success or failure, error messages, and any relevant identifiers.
        /// </summary>
        public sealed class ResponseInformationModel
        {
            /// <summary>
            /// Gets or sets the ID value associated with the database operation. Default is -1.
            /// This can be used to return a primary key value or a similar identifier.
            /// </summary>
            public int? IdValue { get; set; } = -1;

            /// <summary>
            /// Indicates whether the database operation resulted in an error. Default is false.
            /// </summary>
            public bool HasError { get; set; } = false;

            /// <summary>
            /// Gets or sets the error code associated with the database operation, if any. Default is "-".
            /// </summary>
            public string? ErrorCode { get; set; } = "-";

            /// <summary>
            /// Gets or sets the error message associated with the database operation, if any. Default is "-".
            /// </summary>
            public string? ErrorMessage { get; set; } = "-";

            /// <summary>
            /// Gets or sets any additional informational message regarding the database operation. Default is "-".
            /// </summary>
            public string? InformationMessage { get; set; } = "-";

            /// <summary>
            /// Gets or sets any additional _RowGUID value regarding the database operation. Default is "-".
            /// </summary>
            public string? _RowGuid { get; set; } = "-";
        }

        /// <summary>
        /// Represents a generic response model that includes both data and response information.
        /// </summary>
        /// <typeparam name="T">The type of data included in the response.</typeparam>
        public sealed class ResponseModel<T>
        {
            /// <summary>
            /// Gets or sets the data of the response.
            /// </summary>
            public T Data { get; set; }

            /// <summary>
            /// Gets or sets the response information, which includes status and error messages.
            /// </summary>
            public ResponseInformationModel Information { get; set; }
        }
    }
}
