# Data Utils

**Simplifies database tasks.**

### Installing

* Package Manager Console

`Install-Package DataUtils -Version 1.0.0`

* .NET CLI

`dotnet add package DataUtils -Version 1.0.0`

## Usage

### Creating an instance

* Using the [connection string](https://www.connectionstrings.com/sql-server/)

```C#
DBExecutor executor = new DBExecutor(
				"Server=myServerAddress;Database=myDataBase;Trusted_Connection=True;",
                (conn, e) => Debug.WriteLine($"An error occurred in the database (caused by {e.Source}): {e.Message}.")
);
```

* Using the XML configuration file

```C#
DBExecutor executor = new DBExecutor(
				"C:\\Settings\\Data.xml",
                (conn, e) => Debug.WriteLine($"An error occurred in the database (caused by {e.Source}): {e.Message}.")
);
```

##### Data.xml
```XML
<Data>
	<Settings>
		<!-- Desktop || Web -->
		<AppType>Desktop</AppType>
	</Settings>
	<ConnectionString>
		<ServerName>localhost</ServerName>
		<InstanceName>SQLEXPRESS</InstanceName>
		<DatabaseName>Testing</DatabaseName>
		<Username>Merlin</Username>
		<Password/>
		<Port>1433</Port>
		<IntegratedSecurity>true</IntegratedSecurity>
	</ConnectionString>
</Data>
```

### Using the class

To configure the executor to either use transactions or not, just set the `UseTransactions` property (false by default).

#### CRUD methods

`Create`, `Retrieve`, `Update`, `Delete`

* These methods work using the properties from your class
that is defined based on the table in the database.
* The primary key is identified with the `KeyAttribute`.
* The class name should match the table name in the database.
* The names of the properties of the class should match the
column names of the table from the database.

#### Example

Model class (`Driver.cs`)

```C#
public class Driver
{
    [Key]
    public int Id { get; set; }
    public string FullName { get; set; }
    public string PhoneNumber { get; set; }
    public string DriversLicenseNumber { get; set; }
}
```

CRUD operations in the repository (`DatabaseRepository.cs`)

```C#
class DatabaseRepository
{
    private DBExecutor executor;

    public bool UseTransactions { set => executor.UseTransactions = value; }

    /// <summary>
    /// Default constructor.
    /// </summary>
    /// <param name="errorHandler">Handler used for reporting database errors.</param>
    public DatabaseRepository(Action<SqlConnection, SqlInfoMessageEventArgs> errorHandler)
    => executor = new DBExecutor(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "CS.xml"), errorHandler);

    public bool CreateDriver(Driver driver) => executor.Create(driver);
    public bool CreateDrivers(IEnumerable<Driver> drivers) => executor.Create(drivers);

    public List<Driver> RetrieveDrivers() => executor.Retrieve<Driver>();

    public bool UpdateDriver(Driver driver) => executor.Update(driver);
}
```

Delete isn't used here because it wouldn't work because of the foreign key constraints.
Instead, a procedure is used.

#### Procedures

The procedures are called using the `ExecuteProcedure` method. This method will execute a procedure based on the name of the caller.

#### Example

There are 3 procedures defined in the database:
* `InsertSampleData`
* `ClearDatabase`
* `DeleteDriver(@driverId INT)`

Here is how they are called using the executor (`DatabaseRepository.cs`)

```C#
class DatabaseRepository
{
    private DBExecutor executor;

    // Other stuff...
    
    public void InsertSampleData() => executor.ExecuteProcedure();
    public void ClearDatabase() => executor.ExecuteProcedure();
    public void DeleteDriver(int driverId) => executor.ExecuteProcedure(new { driverId });
}
```

As you can see, the procedures that are called are based on the name of the method that calls the `ExecuteProcedure` method. The last one (`DeleteDriver`) takes in a parameter that is called the same as in the database (without the '@'). The way that the parameters are passed into the executor is using the anonymous objects (e.g. new {  a = 2, b = 'c' }). That is why the DeleteDriver method calls the `ExecuteProcedure` with an anonymous object as parameter (`new { driverId }`). This will allow the parameter `driverId` to be passed in successfully into the procedure.

#### `Execute` method

This method can be used to perform custom querying. Here is the method signature:

`public int Execute(string procedureNameOrQuery, params SqlParameter[] parameters)`

* `procedureNameOrQuery` - if a single word is passed in, it is treated as a procedure name (e.g. "InsertSampleData"), otherwise it will be treated as a query (e.g. "SELECT * FROM Driver")
* `parameters` - parameters to use while executing the procedure or query.

The method returns how many rows were affected in the database.