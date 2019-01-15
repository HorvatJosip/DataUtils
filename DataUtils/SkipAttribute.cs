using System;

namespace DataUtils
{
    /// <summary>
    /// Defines that this property should be skipped for the given operation(s).
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class SkipAttribute : Attribute
    {
        /// <summary>
        /// Operation(s) to skip for the property.
        /// </summary>
        public CRUD Operation { get; }

        /// <summary>
        /// Defines that this property should skip given operation(s).
        /// </summary>
        /// <param name="operation">Operation(s) to skip for the property.</param>
        public SkipAttribute(CRUD operation)
        {
            Operation = operation;
        }
    }
}
