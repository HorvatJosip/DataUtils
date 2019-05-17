using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml.Linq;

namespace DataUtils
{
    /// <summary>
    /// Used for executing queries or procedures in the SQL Server database.
    /// </summary>
    public class DBExecutor
    {
        #region Fields

        private readonly Action<SqlConnection, SqlInfoMessageEventArgs> _errorHandler;
        private readonly string _connectionString;

        #endregion

        #region Properties

        /// <summary>
        /// Defines if transactions should be used in database queries and procedure calls.
        /// </summary>
        public bool UseTransactions { get; set; }

        #endregion

        #region Constructor

        /// <summary>
        /// Creates an instance of the <see cref="DBExecutor"/> with a connection string (can be provided
        /// by itself or via a file path to the xml containing the data) and an optional error handler that is
        /// hooked to the <see cref="SqlConnection.InfoMessage"/> event.
        /// </summary>
        /// <param name="errorHandler">Handler used for reporting database errors.</param>
        /// <param name="connectionString">String containing database connection data or path to
        /// XML file that contains connection string data.</param>
        public DBExecutor(string connectionString, Action<SqlConnection, SqlInfoMessageEventArgs> errorHandler = null)
        {
            // Set the error handler
            _errorHandler = errorHandler;

            // If the passed in string is the connection string...
            if (File.Exists(connectionString) == false)
                // Just set it as the connection string
                _connectionString = connectionString;

            // Otherwise, if it is the xml with connection string data...
            else
            {
                // Open the xml for reading
                var xml = XDocument.Load(connectionString);

                // Get the nodes that make up the connection string
                var connectionStringNodes = xml.Descendants("ConnectionString").First().Descendants().ToList();

                // Get the server name
                var server = connectionStringNodes[0].Value;

                // If there is an instance name...
                if (connectionStringNodes[1].IsEmpty == false)
                    // Add it to the server name
                    server += '\\' + connectionStringNodes[1].Value;

                // Construct the connection string using the connection string builder
                var connectionStringBuilder = new SqlConnectionStringBuilder
                {
                    // Server name
                    DataSource = server,

                    // Database name
                    InitialCatalog = connectionStringNodes[2].Value,

                    // Username
                    UserID = connectionStringNodes[3].Value,

                    // Integrated security
                    IntegratedSecurity = bool.Parse(connectionStringNodes[6].Value)
                };

                // If there is a password...
                if (connectionStringNodes[4].IsEmpty == false)
                    // Set it
                    connectionStringBuilder.Password = connectionStringNodes[4].Value;

                // Set the connection string
                _connectionString = connectionStringBuilder.ConnectionString;
            }
        }

        #endregion

        #region Methods

        #region CRUD

        /// <summary>
        /// Performs an insert of the given instance into the database.
        /// </summary>
        /// <typeparam name="T">Type of object to insert.</typeparam>
        /// <param name="instance">Instance to insert into the database.</param>
        /// <returns></returns>
        public bool Create<T>(T instance) => Create<T>(new[] { instance });

        /// <summary>
        /// Performs an insert on the collection of objects of type <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">Type of objects being inserted.</typeparam>
        /// <param name="collection">Collection of objects to insert.</param>
        /// <returns></returns>
        public bool Create<T>(IEnumerable<T> collection)
        {
            // If there are no items passed in...
            if (collection == null || collection.Count() == 0)
                // Signal invalid operation
                throw new InvalidOperationException();

            // Get the object's type
            var type = typeof(T);

            // Get the properties from the type
            var properties = GetProperties<T>(CRUD.Create, true, out _);

            // Construct parameter list
            var parameterList = string.Join(", ", properties.Select(prop => prop.Name));

            // Create a collection for the parameters
            var parameters = new List<SqlParameter>();

            // Create a format for the insert
            var insertFormat = $"INSERT INTO {type.Name}({parameterList}) VALUES({{0}}){Environment.NewLine}";

            // Initialize the string builder for the query
            var query = new StringBuilder();

            // For every element in the collection...
            for (int i = 0; i < collection.Count(); i++)
            {
                // Get the current item from the collection
                var item = collection.ElementAt(i);

                // Create parameters for the item's properties
                var itemParameters = properties.Select(
                    prop => new SqlParameter($"@{prop.Name}{i + 1}", prop.GetValue(item))
                );

                // Add the parameters to the collection
                parameters.AddRange(itemParameters);

                // Append the insert for the item to the query
                query.AppendFormat(insertFormat, string.Join(", ", itemParameters.Select(param => param.ParameterName)));
            }

            // Execute the query and return if it was successful
            return Execute(query.ToString(), parameters.ToArray()) > 0;
        }

        /// <summary>
        /// Executes the given string in the database and, using the <see cref="SqlDataReader"/>,
        /// converts the provided data into objects of type <typeparamref name="T"/>.
        /// Data is set using the properties of the object and their public set methods.
        /// Each property is set by name which means that names of the properties should
        /// match the names of the columns in the database. To skip a property, use
        /// <see cref="SkipAttribute"/>.
        /// </summary>
        /// <param name="procedureNameOrQuery">Name of the procedure (1 word) or a query.</param>
        /// <param name="parameters">Parameters used with procedure or query</param>
        /// <returns></returns>
        public List<T> Retrieve<T>(string procedureNameOrQuery, params SqlParameter[] parameters)
            // Allow creating instances of T using the default constructor
            where T : new()
            => Run(procedureNameOrQuery, parameters, command =>
            {
                // Create the collection
                var collection = new List<T>();

                // Get the properties from the type
                var properties = GetProperties<T>(CRUD.Retrieve, false, out _);

                // Create an sql data reader using the command
                using (var reader = command.ExecuteReader())
                {
                    // While the reader has something left to read...
                    while (reader.Read())
                    {
                        // Create a new instance of type T
                        var instance = new T();

                        // Foreach property on the type T
                        foreach (var property in properties)
                            // Set the value of the property on the instance
                            // using the value stored in the database
                            property.SetValue(instance, reader[property.Name]);

                        // Add the instance to the collection
                        collection.Add(instance);
                    }
                }

                // Return the collection
                return collection;
            });

        /// <summary>
        /// Retreives a collection of objects of type <typeparamref name="T"/> from
        /// the database.
        /// </summary>
        /// <typeparam name="T">Type of object to get.</typeparam>
        /// <returns></returns>
        public List<T> Retrieve<T>() where T : new()
            => Retrieve<T>($"SELECT * FROM {typeof(T).Name}");

        /// <summary>
        /// Updates an instance in the database.
        /// </summary>
        /// <typeparam name="T">Type of object to update.</typeparam>
        /// <param name="instance">Instance to update.</param>
        /// <returns></returns>
        public int Update<T>(T instance)
        {
            // Get the object's type
            var type = typeof(T);

            // Get the properties from the type and...
            var propertyParams = GetProperties<T>(CRUD.Update, false, out PropertyInfo pkProperty)
                // Create parameters based on the properties
                .Select(prop => new SqlParameter('@' + prop.Name, prop.GetValue(instance)));

            // Create a method that makes equation out of property
            string Equate(PropertyInfo prop) => $"{prop.Name} = @{prop.Name}";

            // Construct update list
            var updates = string.Join(", ", GetProperties<T>(CRUD.Update, true, out _).Select(Equate));

            // Construct the update query
            var query = $"UPDATE {type.Name} SET {updates} WHERE {Equate(pkProperty)}";

            // Execute the query and return if it was successful
            return Execute(query, propertyParams.ToArray());
        }

        /// <summary>
        /// Deletes the given instance in the database.
        /// </summary>
        /// <typeparam name="T">Type of object that needs to be deleted.</typeparam>
        /// <param name="instance">Instance of the object to delete.</param>
        /// <returns></returns>
        public bool Delete<T>(T instance)
        {
            // Get the primary key property from the type
            GetProperties<T>(CRUD.Delete, false, out PropertyInfo pkProperty);

            // Return the result of deleting by its value
            return Execute($"DELETE FROM {typeof(T).Name} WHERE {pkProperty.Name} = {pkProperty.GetValue(instance)}") > 0;
        }

        #endregion

        /// <summary>
        /// Gets values from an "enum table".
        /// </summary>
        /// <typeparam name="T">Type of the value column.</typeparam>
        /// <param name="name">Name of the "enum table".</param>
        /// <param name="nameColumn">Name of the name column.</param>
        /// <param name="valueColumn">Name of the value column.</param>
        /// <returns></returns>
        public List<DbEnum<T>> GetEnum<T>(string name, string nameColumn = "Name", string valueColumn = "Id")
            => Retrieve<DbEnum<T>>($"SELECT {valueColumn} as {nameof(DbEnum<T>.Value)}, {nameColumn} as {nameof(DbEnum<T>.Name)} FROM {name}");

        /// <summary>
        /// Executes the given string in the database.
        /// </summary>
        /// <param name="procedureNameOrQuery">Name of the procedure (1 word) or a query.</param>
        /// <param name="parameters">Parameters used with procedure or query</param>
        /// <returns></returns>
        public int Execute(string procedureNameOrQuery, params SqlParameter[] parameters)
            // Return number of rows affected by the procedure or query
            => Run(procedureNameOrQuery, parameters, command => command.ExecuteNonQuery());

        /// <summary>
        /// Executes a procedure in the database.
        /// <para>Usage: The caller (should be a method) needs to be named like the
        /// procedure in the database.</para>
        /// <para>The '<paramref name="parameters"/>' paramater that is passed
        /// in needs to be object with properties that correspond to the parameters of the
        /// procedure in the database.</para>
        /// <para>Example: the caller is a method with the following signature:
        /// DoWork(int firstParameter). That means that there is a procedure named DoWork in the
        /// database that takes in a parameter named firstParameter that is of type int.</para>
        /// <para>The way this method should then be called: ExecuteProcedure(new { firstParameter });</para>
        /// </summary>
        /// <param name="parameters">Parameters used in the procedure (can be left null if there aren't any).</param>
        /// <param name="procedureName">Name of the procedure to execute.</param>
        /// <returns></returns>
        public int ExecuteProcedure(
            object parameters = null,
            [CallerMemberName]string procedureName = null
        )
        {
            // If no parameters are passed in...
            if (parameters == null)
                // Just execute the procedure
                return Execute(procedureName);

            // Generate the sql parameters
            var sqlParameters = parameters.GetType().GetProperties().Select(
                prop => new SqlParameter('@' + prop.Name, prop.GetValue(parameters))
            );

            // Return the result of executing the procedure with given parameters
            return Execute(procedureName, sqlParameters.ToArray());
        }

        #region Helpers

        private IEnumerable<PropertyInfo> GetProperties<T>(CRUD operation, bool withoutPrimaryKey, out PropertyInfo pkProperty)
        {
            var properties = typeof(T)
                // Get the properties from the type
                .GetProperties()
                // Filter by the operation if the skip attribute is defined
                .Where(prop => prop.GetCustomAttribute<SkipAttribute>()?.Operation.HasFlag(operation) != true);

            // Try to find the primary key property
            pkProperty = properties.FirstOrDefault(prop => Attribute.IsDefined(prop, typeof(KeyAttribute)));

            // If primary key should be excluded...
            if (withoutPrimaryKey)
                // Return other properties
                return properties.Except(new[] { pkProperty });

            // Return all of the properties
            return properties;
        }

        private T Run<T>(string procedureNameOrQuery, SqlParameter[] parameters, Func<SqlCommand, T> action)
        {
            // If nothing is passed in...
            if (string.IsNullOrWhiteSpace(procedureNameOrQuery))
                // Signal invalid operation
                throw new InvalidOperationException();

            // Setup the connection using the connection string
            using (var connection = new SqlConnection(_connectionString))
            {
                // If an error handler has been provided...
                if (_errorHandler != null)
                {
                    // Use info message on errors
                    connection.FireInfoMessageEventOnUserErrors = true;

                    // Hook up the handler
                    connection.InfoMessage += (sender, e) => _errorHandler(connection, e);
                }

                // Open the connection
                connection.Open();

                // Create the command
                using (var command = new SqlCommand(procedureNameOrQuery, connection))
                {
                    // If the space character isn't found in the passed in string...
                    if (procedureNameOrQuery.IndexOf(' ') == -1)
                        // Assume the use of a procedure
                        command.CommandType = System.Data.CommandType.StoredProcedure;

                    // Add the parameters
                    command.Parameters.AddRange(parameters);

                    // If transactions should be used...
                    if (UseTransactions)
                    {
                        // Create a transaction from the connection
                        var transaction = connection.BeginTransaction();

                        // Set the command's transaction
                        command.Transaction = transaction;

                        try
                        {
                            // Get the result of the passed in method that takes in the command
                            var result = action(command);

                            // Commit the changes
                            transaction.Commit();

                            // Return the result
                            return result;
                        }
                        catch (Exception)
                        {
                            // Rollback any changes
                            transaction.Rollback();

                            // Return default value of type T
                            return default;
                        }
                    }

                    // Return the result of the passed in method that takes in the command
                    return action(command);
                }
            }
        } 

        #endregion

        #endregion
    }
}
