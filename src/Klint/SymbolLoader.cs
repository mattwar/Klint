using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kusto.Cloud.Platform.Data;
using Kusto.Data;
using Kusto.Data.Data;
using Kusto.Data.Net.Client;
using Kusto.Language;
using Kusto.Language.Editor;
using Kusto.Language.Syntax;
using Kusto.Language.Symbols;
using Newtonsoft.Json;
using static Kushy.Helpers;

#nullable disable

namespace Kushy
{
    /// <summary>
    /// A class that retrieves schema information from a cluster as <see cref="Symbol"/> instances.
    /// </summary>
    public abstract class SymbolLoader
    {
        public abstract string DefaultCluster { get; }
        public abstract string DefaultDomain { get; }

        /// <summary>
        /// Gets a list of all the database names in the cluster associated with the connection.
        /// </summary>
        public abstract Task<string[]> GetDatabaseNamesAsync(string clusterName = null, bool throwOnError = false, CancellationToken cancellationToken = default);

        /// <summary>
        /// Loads the schema for the specified database into a <see cref="DatabaseSymbol"/>.
        /// </summary>
        public abstract Task<DatabaseSymbol> LoadDatabaseAsync(string databaseName, string clusterName = null, bool throwOnError = false, CancellationToken cancellationToken = default);

        /// <summary>
        /// Loads the schema for the specified database and returns a new <see cref="GlobalState"/> with the database added or updated.
        /// </summary>
        public Task<GlobalState> AddOrUpdateDatabaseAsync(GlobalState globals, string databaseName, string clusterName = null, bool throwOnError = false, CancellationToken cancellation = default)
        {
            return AddOrUpdateDatabaseAsync(globals, databaseName, clusterName, asDefault: false, throwOnError, cancellation);
        }

        /// <summary>
        /// Loads the schema for the specified default database and returns a new <see cref="GlobalState"/> with the database added or updated.
        /// </summary>
        public Task<GlobalState> AddOrUpdateDefaultDatabaseAsync(GlobalState globals, string databaseName, string clusterName = null, bool throwOnError = false, CancellationToken cancellation = default)
        {
            return AddOrUpdateDatabaseAsync(globals, databaseName, clusterName, asDefault: true, throwOnError, cancellation);
        }

        /// <summary>
        /// Loads the schema for the specified database and returns a new <see cref="GlobalState"/> with the database added or updated.
        /// </summary>
        private async Task<GlobalState> AddOrUpdateDatabaseAsync(GlobalState globals, string databaseName, string clusterName, bool asDefault, bool throwOnError, CancellationToken cancellation)
        {
            clusterName = string.IsNullOrEmpty(clusterName)
                ? this.DefaultCluster
                : GetFullHostName(clusterName, this.DefaultDomain);

            var db = await LoadDatabaseAsync(databaseName, clusterName, throwOnError, cancellation).ConfigureAwait(false);
            if (db == null)
                return globals;

            var cluster = globals.GetCluster(clusterName);
            if (cluster == null)
            {
                cluster = new ClusterSymbol(clusterName, new[] { db });
                globals = globals.AddOrReplaceCluster(cluster);
            }
            else
            {
                cluster = cluster.AddOrUpdateDatabase(db);
                globals = globals.AddOrReplaceCluster(cluster);
            }

            if (asDefault)
            {
                globals = globals.WithCluster(cluster).WithDatabase(db);
            }

            return globals;
        }
    }

    /// <summary>
    /// A class that retrieves schema symbols from a cluster server.
    /// </summary>
    public class ServerSymbolLoader : SymbolLoader
    {
        private readonly KustoConnectionStringBuilder _defaultConnection;
        private readonly string _defaultClusterName;
        private readonly string _defaultDomain;
        private readonly Dictionary<string, HashSet<string>> _badDatabaseNameMap = new Dictionary<string, HashSet<string>>();

        /// <summary>
        /// Creates a new <see cref="SymbolLoader"/> instance. recommended method: SymbolLoader(KustoConnectionStringBuilder clusterConnection)
        /// </summary>
        /// <param name="clusterConnection">The cluster connection string.</param>
        /// <param name="defaultDomain">The domain used to convert short cluster host names into full cluster host names.
        /// This string must start with a dot.  If not specified, the default domain is ".Kusto.Windows.Net"
        /// </param>
        public ServerSymbolLoader(string clusterConnection, string defaultDomain = null)
            : this(new KustoConnectionStringBuilder(clusterConnection), defaultDomain)
        {
        }

        /// <summary>
        /// Creates a new <see cref="SymbolLoader"/> instance.
        /// </summary>
        /// <param name="clusterConnection">The cluster connection.</param>
        /// <param name="defaultDomain">The domain used to convert short cluster host names into full cluster host names.
        /// This string must start with a dot.  If not specified, the default domain is ".Kusto.Windows.Net"
        /// </param>
        public ServerSymbolLoader(KustoConnectionStringBuilder clusterConnection, string defaultDomain = null)
        {
            _defaultConnection = clusterConnection;
            _defaultClusterName = GetHost(clusterConnection);
            _defaultDomain = String.IsNullOrEmpty(defaultDomain) 
                ? KustoFacts.KustoWindowsNet
                : defaultDomain;
        }

        public override string DefaultCluster => _defaultClusterName;
        public override string DefaultDomain => _defaultDomain;

        /// <summary>
        /// The default database specified in the connection
        /// </summary>
        public string DefaultDatabase => _defaultConnection.InitialCatalog;

        /// <summary>
        /// Gets a list of all the database names in the cluster associated with the connection.
        /// </summary>
        public override async Task<string[]> GetDatabaseNamesAsync(string clusterName = null, bool throwOnError = false, CancellationToken cancellationToken = default)
        {
            clusterName = string.IsNullOrEmpty(clusterName)
                ? _defaultClusterName
                : GetFullHostName(clusterName, _defaultDomain);

            var connection = GetClusterConnection(clusterName);
            var databases = await ExecuteControlCommandAsync<ShowDatabasesResult>(connection, "", ".show databases", throwOnError, cancellationToken);
            if (databases == null)
                return null;

            return databases.Select(d => d.DatabaseName).ToArray();
        }

        /// <summary>
        /// Loads the schema for the specified database into a <see cref="DatabaseSymbol"/>.
        /// </summary>
        public override async Task<DatabaseSymbol> LoadDatabaseAsync(string databaseName, string clusterName = null, bool throwOnError = false, CancellationToken cancellationToken = default)
        {
            clusterName = string.IsNullOrEmpty(clusterName)
                ? _defaultClusterName
                : GetFullHostName(clusterName, _defaultDomain);

            // if we've already determined this database name is bad, then bail out
            if (_badDatabaseNameMap.TryGetValue(clusterName, out var badDbNames)
                && badDbNames.Contains(databaseName))
                return null;

            var connection = GetClusterConnection(clusterName);

            var tables = await LoadTablesAsync(connection, databaseName, throwOnError, cancellationToken).ConfigureAwait(false);
            if (tables == null)
            {
                if (badDbNames == null)
                {
                    badDbNames = new HashSet<string>();
                    _badDatabaseNameMap.Add(clusterName, badDbNames);
                }
                badDbNames.Add(databaseName);
                return null;
            }

            var externalTables = await LoadExternalTablesAsync(connection, databaseName, throwOnError, cancellationToken).ConfigureAwait(false);
            var materializedViews = await LoadMaterializedViewsAsync(connection, databaseName, throwOnError, cancellationToken).ConfigureAwait(false);
            var functions = await LoadFunctionsAsync(connection, databaseName, throwOnError, cancellationToken).ConfigureAwait(false);

            var members = new List<Symbol>();
            members.AddRange(tables);
            members.AddRange(externalTables);
            members.AddRange(materializedViews);
            members.AddRange(functions);

            var databaseSymbol = new DatabaseSymbol(databaseName, members);
            return databaseSymbol;
        }

        private async Task<IReadOnlyList<TableSymbol>> LoadTablesAsync(KustoConnectionStringBuilder connection, string databaseName, bool throwOnError, CancellationToken cancellationToken)
        {
            var tables = new List<TableSymbol>();

            // get table schema from .show database xxx schema
            var databaseSchemas = await ExecuteControlCommandAsync<ShowDatabaseSchemaResult>(connection, databaseName, $".show database {databaseName} schema", throwOnError, cancellationToken).ConfigureAwait(false);
            if (databaseSchemas == null)
                return null;

            foreach (var table in databaseSchemas.Where(s => !string.IsNullOrEmpty(s.TableName)).GroupBy(s => s.TableName))
            {
                var tableDocString = table.FirstOrDefault(t => string.IsNullOrEmpty(t.ColumnName) && !string.IsNullOrEmpty(t.DocString))?.DocString;
                var columnSchemas = table.Where(t => !string.IsNullOrEmpty(t.ColumnName)).ToArray();
                var columns = columnSchemas.Select(s => new ColumnSymbol(s.ColumnName, GetKustoType(s.ColumnType), s.DocString)).ToList();
                var tableSymbol = new TableSymbol(table.Key, columns, tableDocString);
                tables.Add(tableSymbol);
            }

            return tables;
        }

        private async Task<IReadOnlyList<TableSymbol>> LoadExternalTablesAsync(KustoConnectionStringBuilder connection, string databaseName, bool throwOnError, CancellationToken cancellationToken)
        {
            var tables = new List<TableSymbol>();

            // get external tables from .show external tables and .show external table xxx cslschema
            var externalTables = await ExecuteControlCommandAsync<ShowExternalTablesResult>(connection, databaseName, ".show external tables", throwOnError, cancellationToken);
            if (externalTables != null)
            {
                foreach (var et in externalTables)
                {
                    var etSchemas = await ExecuteControlCommandAsync<ShowExternalTableSchemaResult>(connection, databaseName, $".show external table {et.TableName} cslschema", throwOnError, cancellationToken);
                    if (etSchemas != null && etSchemas.Length > 0)
                    {
                        var mvSymbol = new TableSymbol(et.TableName, "(" + etSchemas[0].Schema + ")", et.DocString).WithIsExternal(true);
                        tables.Add(mvSymbol);
                    }
                }
            }

            return tables;
        }

        private async Task<IReadOnlyList<TableSymbol>> LoadMaterializedViewsAsync(KustoConnectionStringBuilder connection, string databaseName, bool throwOnError, CancellationToken cancellationToken)
        {
            var tables = new List<TableSymbol>();

            // get materialized views from .show materialized-views and .show materialized-view xxx cslschema
            var materializedViews = await ExecuteControlCommandAsync<ShowMaterializedViewsResult>(connection, databaseName, ".show materialized-views", throwOnError, cancellationToken);
            if (materializedViews != null)
            {
                foreach (var mv in materializedViews)
                {
                    var mvSchemas = await ExecuteControlCommandAsync<ShowMaterializedViewSchemaResult>(connection, databaseName, $".show materialized-view {mv.Name} cslschema", throwOnError, cancellationToken);
                    if (mvSchemas != null && mvSchemas.Length > 0)
                    {
                        var mvSymbol = new TableSymbol(mv.Name, "(" + mvSchemas[0].Schema + ")", mv.DocString).WithIsMaterializedView(true);
                        tables.Add(mvSymbol);
                    }
                }
            }

            return tables;
        }

        private async Task<IReadOnlyList<FunctionSymbol>> LoadFunctionsAsync(KustoConnectionStringBuilder connection, string databaseName, bool throwOnError, CancellationToken cancellationToken)
        {
            var functions = new List<FunctionSymbol>();

            // get functions for .show functions
            var functionSchemas = await ExecuteControlCommandAsync<ShowFunctionsResult>(connection, databaseName, ".show functions", throwOnError, cancellationToken).ConfigureAwait(false);
            if (functionSchemas == null)
                return null;

            foreach (var fun in functionSchemas)
            {
                //var parameters = TranslateParameters(fun.Parameters);
                var functionSymbol = new FunctionSymbol(fun.Name, fun.Parameters, fun.Body, fun.DocString);
                functions.Add(functionSymbol);
            }

            return functions;
        }

        /// <summary>
        /// Executes a query or command against a kusto cluster and returns a sequence of result row instances.
        /// </summary>
        private static async Task<T[]> ExecuteControlCommandAsync<T>(KustoConnectionStringBuilder connection, string database, string command, bool throwOnError, CancellationToken cancellationToken)
        {
            try
            {
                using (var client = KustoClientFactory.CreateCslAdminProvider(connection))
                {
                    var resultReader = await client.ExecuteControlCommandAsync(database, command).ConfigureAwait(false);
                    var results = KustoDataReaderParser.ParseV1(resultReader, null);
                    var tableReader = results[WellKnownDataSet.PrimaryResult].Single().TableData.CreateDataReader();
                    var objectReader = new ObjectReader<T>(tableReader);
                    return objectReader.ToArray();
                }
            }
            catch (Exception) when (!throwOnError)
            {
                return null;
            }
        }

        private string GetClusterHost(string clusterName)
        {
            return GetHost(GetClusterConnection(clusterName));
        }

        private string GetHost(KustoConnectionStringBuilder connection)
        {
            //var csb = new KustoConnectionStringBuilder(connection);
            var uri = new Uri(connection.DataSource);
            return uri.Host;
        }

        private KustoConnectionStringBuilder GetClusterConnection(string clusterUriOrName)
        {
            if (string.IsNullOrEmpty(clusterUriOrName)
                || clusterUriOrName == _defaultClusterName)
            {
                return _defaultConnection;
            }

            // borrow most security settings from default cluster connection
            var builder = new KustoConnectionStringBuilder(_defaultConnection);

            if (string.IsNullOrWhiteSpace(clusterUriOrName))
                return null;

            var clusterUri = clusterUriOrName;

            clusterUri = KustoFacts.GetFullHostName(clusterUri, _defaultDomain);

            if (!clusterUri.Contains("://"))
                clusterUri = builder.ConnectionScheme + "://" + clusterUri;

            builder.DataSource = clusterUri;
            builder.ApplicationCertificateBlob = _defaultConnection.ApplicationCertificateBlob;
            builder.ApplicationKey = _defaultConnection.ApplicationKey;
            builder.InitialCatalog = "NetDefaultDB";

            return builder;
        }

        public class ShowDatabasesResult
        {
            public string DatabaseName;
            public string PersistentStorage;
            public string Version;
            public bool IsCurrent;
            public string DatabaseAccessMode;
            public string PrettyName;
            public bool ReservedSlot1;
            public Guid DatabaseId;
            public string InTransitionTo;
        }

        public class ShowDatabaseSchemaResult
        {
            public string DatabaseName;
            public string TableName;
            public string ColumnName;
            public string ColumnType;
            public bool IsDefaultTable;
            public bool IsDefaultColumn;
            public string PrettyName;
            public string Version;
            public string Folder;
            public string DocString;
        }

        public class ShowExternalTablesResult
        {
            public string TableName;
            public string DocString;
        }

        public class ShowExternalTableSchemaResult
        {
            public string TableName;
            public string Schema;
        }

        public class ShowMaterializedViewsResult
        {
            public string Name;
            public string DocString;
        }

        public class ShowMaterializedViewSchemaResult
        {
            public string Name;
            public string Schema;
        }

        public class ShowFunctionsResult
        {
            public string Name;
            public string Parameters;
            public string Body;
            public string Folder;
            public string DocString;
        }
    }

    /// <summary>
    /// A class that loads symbol schema from text files
    /// </summary>
    public class FileSymbolLoader : SymbolLoader
    {
        private readonly string _schemaDirectoryPath;
        private readonly string _defaultClusterName;
        private readonly string _defaultDomain;

        public FileSymbolLoader(string schemaDirectoryPath, string defaultClusterName, string defaultDomain = null)
        {
            _schemaDirectoryPath = Environment.ExpandEnvironmentVariables(schemaDirectoryPath);
            _defaultClusterName = defaultClusterName;
            _defaultDomain = defaultDomain ?? KustoFacts.KustoWindowsNet;
        }

        public override string DefaultCluster => _defaultClusterName;
        public override string DefaultDomain => _defaultDomain;

        public override Task<string[]> GetDatabaseNamesAsync(string clusterName = null, bool throwOnError = false, CancellationToken cancellationToken = default)
        {
            // TODO: load database names from cluster json file
            return null;
        }

        /// <summary>
        /// Loads the named database schema from the cache.
        /// </summary>
        public override async Task<DatabaseSymbol> LoadDatabaseAsync(string databaseName, string clusterName = null, bool throwOnError = false, CancellationToken cancellationToken = default)
        {
            var databasePath = GetDatabaseCachePath(clusterName, databaseName);

            if (databasePath != null)
            {
                try
                {
                    if (File.Exists(databasePath))
                    {
                        var jsonText = await File.ReadAllTextAsync(databasePath).ConfigureAwait(false);
                        var cachedDb = JsonConvert.DeserializeObject<DatabaseInfo>(jsonText);
                        return CreateDatabaseSymbol(cachedDb);
                    }
                }
                catch (Exception) when (!throwOnError)
                {
                }
            }

            return null;
        }

        private static readonly JsonSerializerSettings s_serializationSettings =
            new JsonSerializerSettings()
            {
                DefaultValueHandling = DefaultValueHandling.Ignore,
                Formatting = Formatting.Indented,
            };

        /// <summary>
        /// Saves the database schema to the cache.
        /// </summary>
        public async Task<bool> SaveDatabaseAsync(DatabaseSymbol database, string clusterName = null, bool throwOnError = false, CancellationToken cancellationToken = default)
        {
            var clusterPath = GetClusterCachePath(clusterName);
            var databasePath = GetDatabaseCachePath(clusterName, database.Name);

            if (clusterPath != null && databasePath != null)
            {
                try
                {
                    var cachedDb = CreateDatabaseInfo(database);
                    var jsonText = JsonConvert.SerializeObject(cachedDb, s_serializationSettings);

                    if (!Directory.Exists(_schemaDirectoryPath))
                    {
                        Directory.CreateDirectory(_schemaDirectoryPath);
                    }

                    if (!Directory.Exists(clusterPath))
                    {
                        Directory.CreateDirectory(clusterPath);
                    }

                    await File.WriteAllTextAsync(databasePath, jsonText, cancellationToken).ConfigureAwait(false);

                    return true;
                }
                catch (Exception) when (!throwOnError)
                {
                }
            }

            return false;
        }

        /// <summary>
        /// Saves all the cluster schema to the cache.
        /// </summary>
        public async Task SaveClusterAsync(ClusterSymbol cluster, CancellationToken cancellationToken = default)
        {
            foreach (var db in cluster.Databases)
            {
                var _ = await SaveDatabaseAsync(db, cluster.Name, throwOnError: false, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Saves all the schema for all the clusters to the cache.
        /// </summary>
        public async Task SaveClusters(IEnumerable<ClusterSymbol> clusters, CancellationToken cancellationToken = default)
        {
            foreach (var cluster in clusters)
            {
                await SaveClusterAsync(cluster, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Deletes all cached schemas for all clusters/databases
        /// </summary>
        public bool DeleteCache()
        {
            if (Directory.Exists(_schemaDirectoryPath))
            {
                try
                {
                    Directory.Delete(_schemaDirectoryPath, true);
                    return true;
                }
                catch (Exception)
                {
                }

                return false;
            }

            return true;
        }

        /// <summary>
        /// Deletes all cached schemas for all databases in the cluster.
        /// </summary>
        public bool DeleteClusterCache(string clusterName = null)
        {
            var clusterPath = GetClusterCachePath(clusterName);
            if (clusterPath != null)
            {
                if (Directory.Exists(clusterPath))
                {
                    try
                    {
                        Directory.Delete(clusterPath, true);
                        return true;
                    }
                    catch (Exception)
                    {
                    }

                    return false;
                }
            }

            return true;
        }

        public string GetClusterCachePath(string clusterName)
        {
            clusterName = clusterName ?? _defaultClusterName;
            if (clusterName == null)
                return null;

            var fullClusterName = GetFullHostName(clusterName, _defaultDomain);
            return Path.Combine(_schemaDirectoryPath, MakeFilePathPart(fullClusterName));
        }

        public string GetDatabaseCachePath(string clusterName, string databaseName)
        {
            clusterName = clusterName ?? _defaultClusterName;
            if (clusterName == null)
                return null;

            return Path.Combine(GetClusterCachePath(clusterName), MakeFilePathPart(databaseName) + ".json");
        }

        private static bool IsInvalidPathChar(char ch) =>
            ch == '\\' || ch == '/';

        private static string MakeFilePathPart(string name)
        {
            if (name.Any(IsInvalidPathChar))
            {
                var builder = new StringBuilder(name.Length);

                foreach (var ch in name)
                {
                    builder.Append(IsInvalidPathChar(ch) ? "_" : ch);
                }

                name = builder.ToString();
            }

            return name.ToLower();
        }

        private static DatabaseSymbol CreateDatabaseSymbol(DatabaseInfo db)
        {
            var members = new List<Symbol>();

            if (db.Tables != null)
                members.AddRange(db.Tables.Select(t => CreateTableSymbol(t)));
            if (db.ExternalTables != null)
                members.AddRange(db.ExternalTables.Select(e => CreateExternalTableSymbol(e)));
            if (db.MaterializedViews != null)
                members.AddRange(db.MaterializedViews.Select(v => CreateMaterializedViewSymbol(v)));
            if (db.Functions != null)
                members.AddRange(db.Functions.Select(f => CreateFunctionSymbol(f)));

            return new DatabaseSymbol(db.Name, members);
        }

        private static TableSymbol CreateTableSymbol(TableInfo tab)
        {
            return new TableSymbol(tab.Name, tab.Schema, tab.Description);
        }

        private static TableSymbol CreateExternalTableSymbol(ExternalTableInfo xtab)
        {
            return new TableSymbol(xtab.Name, xtab.Schema, xtab.Description).WithIsExternal(true);
        }

        private static TableSymbol CreateMaterializedViewSymbol(MaterializedViewInfo mview)
        {
            return new TableSymbol(mview.Name, mview.Schema, mview.Description).WithIsMaterializedView(true);
        }

        public static FunctionSymbol CreateFunctionSymbol(FunctionInfo fun)
        {
            return new FunctionSymbol(fun.Name, fun.Parameters, fun.Body, fun.Description);
        }

        private static DatabaseInfo CreateDatabaseInfo(DatabaseSymbol db)
        {
            return new DatabaseInfo
            {
                Name = db.Name,
                Tables = db.Tables.Count > 0 ? db.Tables.Select(t => CreateTableInfo(t)).ToList() : null,
                ExternalTables = db.ExternalTables.Count > 0 ? db.ExternalTables.Select(e => CreateExternalTableInfo(e)).ToList() : null,
                MaterializedViews = db.MaterializedViews.Count > 0 ? db.MaterializedViews.Select(m => CreateMaterializedViewInfo(m)).ToList() : null,
                Functions = db.Functions.Count > 0 ? db.Functions.Select(f => CreateFunctionInfo(f)).ToList() : null
            };
        }

        private static TableInfo CreateTableInfo(TableSymbol tab)
        {
            return new TableInfo
            {
                Name = tab.Name,
                Schema = GetSchema(tab),
                Description = string.IsNullOrEmpty(tab.Description) ? null : tab.Description
            };
        }

        private static ExternalTableInfo CreateExternalTableInfo(TableSymbol tab)
        {
            return new ExternalTableInfo
            {
                Name = tab.Name,
                Schema = GetSchema(tab),
                Description = string.IsNullOrEmpty(tab.Description) ? null : tab.Description
            };
        }

        private static MaterializedViewInfo CreateMaterializedViewInfo(TableSymbol tab)
        {
            return new MaterializedViewInfo
            {
                Name = tab.Name,
                Schema = GetSchema(tab),
                Description = string.IsNullOrEmpty(tab.Description) ? null : tab.Description
            };
        }

        private static FunctionInfo CreateFunctionInfo(FunctionSymbol fun)
        {
            return new FunctionInfo
            {
                Name = fun.Name,
                Parameters = GetParameterList(fun.Signatures[0]),
                Body = fun.Signatures[0].Body,
                Description = string.IsNullOrEmpty(fun.Description) ? null : fun.Description
            };
        }

        public class DatabaseInfo
        {
            public string Name;
            public List<TableInfo> Tables;
            public List<ExternalTableInfo> ExternalTables;
            public List<MaterializedViewInfo> MaterializedViews;
            public List<FunctionInfo> Functions;
        }

        public class TableInfo
        {
            public string Name;
            public string Schema;
            public string Description;
        }

        public class ExternalTableInfo
        {
            public string Name;
            public string Schema;
            public string Description;
        }

        public class MaterializedViewInfo
        {
            public string Name;
            public string Schema;
            public string Description;
        }

        public class FunctionInfo
        {
            public string Name;
            public string Parameters;
            public string Body;
            public string Description;
        }
    }

    /// <summary>
    /// A <see cref="SymbolLoader"/> that maintains a file cache of database schemas.
    /// </summary>
    public class CachedServerSymbolLoader : SymbolLoader
    {
        public FileSymbolLoader FileLoader { get; }
        public ServerSymbolLoader ServerLoader { get; }

        public CachedServerSymbolLoader(string connection, string cachePath, string defaultDomain = null)
        {
            this.ServerLoader = new ServerSymbolLoader(connection, defaultDomain);
            this.FileLoader = new FileSymbolLoader(cachePath, this.ServerLoader.DefaultCluster, defaultDomain);
        }

        public CachedServerSymbolLoader(ServerSymbolLoader serverLoader, FileSymbolLoader fileLoader)
        {
            this.ServerLoader = serverLoader;
            this.FileLoader = fileLoader;
        }

        public override string DefaultCluster => this.ServerLoader.DefaultCluster;
        public override string DefaultDomain => this.ServerLoader.DefaultDomain;

        public override async Task<string[]> GetDatabaseNamesAsync(string clusterName = null, bool throwOnError = false, CancellationToken cancellationToken = default)
        {
            var names = await this.FileLoader.GetDatabaseNamesAsync(clusterName, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (names == null)
            {
                names = await this.ServerLoader.GetDatabaseNamesAsync(clusterName, throwOnError, cancellationToken).ConfigureAwait(false);

                if (names != null)
                {
                    // TODO: save database names to cluster json file
                }
            }

            return names;
        }

        public override async Task<DatabaseSymbol> LoadDatabaseAsync(string databaseName, string clusterName = null, bool throwOnError = false, CancellationToken cancellationToken = default)
        {
            var db = await this.FileLoader.LoadDatabaseAsync(databaseName, clusterName, false, cancellationToken).ConfigureAwait(false);

            if (db == null)
            {
                db = await this.ServerLoader.LoadDatabaseAsync(databaseName, clusterName, throwOnError, cancellationToken).ConfigureAwait(false);

                if (db != null)
                {
                    await this.FileLoader.SaveDatabaseAsync(db, clusterName, throwOnError, cancellationToken).ConfigureAwait(false);
                }
            }

            return db;
        }
    }

    /// <summary>
    /// A class that resolves cluster/database references in kusto queries using a <see cref="SymbolLoader"/>
    /// </summary>
    public class SymbolResolver
    {
        private readonly SymbolLoader _loader;
        private readonly HashSet<string> _ignoreClusterNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public SymbolResolver(SymbolLoader loader)
        {
            _loader = loader;
        }

        /// <summary>
        /// Loads and adds the <see cref="DatabaseSymbol"/> for any database explicity referenced in the query but not already present in the <see cref="GlobalState"/>.
        /// </summary>
        public async Task<KustoCode> AddReferencedDatabasesAsync(KustoCode code, bool throwOnError = false, CancellationToken cancellationToken = default)
        {
            var service = new KustoCodeService(code);
            var globals = await AddReferencedDatabasesAsync(code.Globals, service, throwOnError, cancellationToken).ConfigureAwait(false);
            return code.WithGlobals(globals);
        }

        /// <summary>
        /// Loads and adds the <see cref="DatabaseSymbol"/> for any database explicity referenced in the <see cref="CodeScript"/ document but not already present in the <see cref="GlobalState"/>.
        /// </summary>
        public async Task<CodeScript> AddReferencedDatabasesAsync(CodeScript script, bool throwOnError = false, CancellationToken cancellationToken = default)
        {
            var globals = script.Globals;

            foreach (var block in script.Blocks)
            {
                globals = await AddReferencedDatabasesAsync(globals, block.Service, throwOnError, cancellationToken).ConfigureAwait(false);
            }

            return script.WithGlobals(globals);
        }

        /// <summary>
        /// Loads and adds the <see cref="DatabaseSymbol"/> for any database explicity referenced in the query but not already present in the <see cref="GlobalState"/>.
        /// </summary>
        private async Task<GlobalState> AddReferencedDatabasesAsync(GlobalState globals, CodeService service, bool throwOnError = false, CancellationToken cancellationToken = default)
        {
            // find all explicit cluster (xxx) references
            var clusterRefs = service.GetClusterReferences(cancellationToken);
            foreach (ClusterReference clusterRef in clusterRefs)
            {
                var clusterName = GetFullHostName(clusterRef.Cluster, _loader.DefaultDomain);

                // don't bother with cluster names that we've already shown to not exist
                if (_ignoreClusterNames.Contains(clusterName))
                    continue;

                var cluster = globals.GetCluster(clusterName);
                if (cluster == null || cluster.IsOpen)
                {
                    // check to see if this is an actual cluster and get all database names
                    var databaseNames = await _loader.GetDatabaseNamesAsync(clusterName, throwOnError).ConfigureAwait(false);
                    if (databaseNames != null)
                    {
                        // initially populate with empty 'open' databases. These will get updated to full schema if referenced
                        var databases = databaseNames.Select(db => new DatabaseSymbol(db, null, isOpen: true)).ToArray();
                        cluster = new ClusterSymbol(clusterName, databases);
                        globals = globals.WithClusterList(globals.Clusters.Concat(new[] { cluster }).ToArray());
                    }
                }

                // we already have all the known schema for this cluster
                _ignoreClusterNames.Add(clusterName);
            }

            // examine all explicit database(xxx) references
            var dbRefs = service.GetDatabaseReferences(cancellationToken);
            foreach (DatabaseReference dbRef in dbRefs)
            {
                var clusterName = string.IsNullOrEmpty(dbRef.Cluster)
                    ? null
                    : GetFullHostName(dbRef.Cluster, _loader.DefaultDomain);

                // get implicit or explicit named cluster
                var cluster = string.IsNullOrEmpty(clusterName)
                    ? globals.Cluster
                    : globals.GetCluster(clusterName);

                if (cluster != null)
                {
                    // look for existing database of this name
                    var db = cluster.Databases.FirstOrDefault(m => m.Name.Equals(dbRef.Database, StringComparison.OrdinalIgnoreCase));

                    // is this one of those not-yet-populated databases?
                    if (db == null || (db != null && db.Members.Count == 0 && db.IsOpen))
                    {
                        var newGlobals = await _loader.AddOrUpdateDatabaseAsync(globals, dbRef.Database, clusterName, throwOnError, cancellationToken).ConfigureAwait(false);
                        globals = newGlobals != null ? newGlobals : globals;
                    }
                }
            }

            return globals;
        }
    }

    internal static class Helpers
    {
        public static string GetFullHostName(string clusterNameOrUri, string defaultDomain)
        {
            return KustoFacts.GetFullHostName(KustoFacts.GetHostName(clusterNameOrUri), defaultDomain);
        }

        /// <summary>
        /// Convert CLR type name into a Kusto scalar type.
        /// </summary>
        public static ScalarSymbol GetKustoType(string clrTypeName)
        {
            switch (clrTypeName)
            {
                case "System.Byte":
                case "Byte":
                case "byte":
                case "System.SByte":
                case "SByte":
                case "sbyte":
                case "System.Int16":
                case "Int16":
                case "short":
                case "System.UInt16":
                case "UInt16":
                case "ushort":
                case "System.Int32":
                case "Int32":
                case "int":
                    return ScalarTypes.Int;
                case "System.UInt32": // unsigned ints don't fit into int, use long
                case "UInt32":
                case "uint":
                case "System.Int64":
                case "Int64":
                case "long":
                    return ScalarTypes.Long;
                case "System.Double":
                case "Double":
                case "double":
                case "float":
                case "System.single":
                case "System.Single":
                    return ScalarTypes.Real;
                case "System.UInt64": // unsigned longs do not fit into long, use decimal
                case "UInt64":
                case "ulong":
                case "System.Decimal":
                case "Decimal":
                case "decimal":
                case "System.Data.SqlTypes.SqlDecimal":
                case "SqlDecimal":
                    return ScalarTypes.Decimal;
                case "System.Guid":
                case "Guid":
                    return ScalarTypes.Guid;
                case "System.DateTime":
                case "DateTime":
                    return ScalarTypes.DateTime;
                case "System.TimeSpan":
                case "TimeSpan":
                    return ScalarTypes.TimeSpan;
                case "System.String":
                case "String":
                case "string":
                    return ScalarTypes.String;
                case "System.Boolean":
                case "Boolean":
                case "bool":
                    return ScalarTypes.Bool;
                case "System.Object":
                case "Object":
                case "object":
                    return ScalarTypes.Dynamic;
                case "System.Type":
                case "Type":
                    return ScalarTypes.Type;
                default:
                    throw new InvalidOperationException($"Unhandled clr type: {clrTypeName}");
            }
        }

        public static string GetSchema(TableSymbol table)
        {
            return "(" + string.Join(", ", table.Columns.Select(c => $"{c.Name}: {GetKustoType(c.Type)}")) + ")";
        }

        public static string GetKustoType(TypeSymbol type)
        {
            if (type is ScalarSymbol s)
            {
                return s.Name;
            }
            else if (type is TableSymbol t)
            {
                if (t.Columns.Count == 0)
                {
                    return "(*)";
                }
                else
                {
                    return GetSchema(t);
                }
            }
            else
            {
                return "unknown";
            }
        }

        public static string GetFunctionParameterType(Parameter p)
        {
            switch (p.TypeKind)
            {
                case ParameterTypeKind.Declared:
                    return GetKustoType(p.DeclaredTypes[0]);
                case ParameterTypeKind.Tabular:
                    return "(*)";
                default:
                    return "unknown";
            }
        }

        public static string GetParameterList(FunctionSymbol fun)
        {
            return GetParameterList(fun.Signatures[0]);
        }

        public static string GetParameterList(Signature sig)
        {
            return "(" + string.Join(", ", sig.Parameters.Select(p => $"{p.Name}: {GetFunctionParameterType(p)}")) + ")";
        }
    }
}