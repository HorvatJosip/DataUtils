namespace DataUtils
{
    /// <summary>
    /// Model that represents enumeration stored in database.
    /// </summary>
    public class DbEnum<T>
    {
        /// <summary>
        /// Enumeration name.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Enumeration value (usually id of the row in the database).
        /// </summary>
        public T Value { get; set; }
    }
}
