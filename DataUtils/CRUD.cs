namespace DataUtils
{
    /// <summary>
    /// Enumeration for CRUD operations.
    /// </summary>
    [System.Flags]
    public enum CRUD
    {
        /// <summary>
        /// None.
        /// </summary>
        None = 0,

        /// <summary>
        /// Create operation.
        /// </summary>
        Create = 1 << 0,

        /// <summary>
        /// Retrieve operation.
        /// </summary>
        Retrieve = 1 << 1,

        /// <summary>
        /// Update operation.
        /// </summary>
        Update = 1 << 2,

        /// <summary>
        /// Delete operation.
        /// </summary>
        Delete = 1 << 3
    }
}
