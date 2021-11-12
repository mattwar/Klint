## KLINT - A Kusto Linter (static analyzer)

    klint [options] <files>

### options:

    -? or -help           display this help text
    -connection <string>  the connection to use to access schema from the server
    -cache <path>         overrides the default path to the local schema cache directory
    -nocache              disables use of the local schema cache
    -generate             generates cached schemas for all databases (does not do analysis)
    -cluster <name>       the current cluster in scope (if no connection specified)
    -database <name>      the current database in scope (if not specified by connection)

### examples:

#### Run analysis on MyQueries.kql using database schemas found in local cache or server

    klint -connection ""https://help.kusto.windows.net;Fed=true"" -database Samples MyQueries.kql

#### Run analysis on MyQueries.kql using database schemas found in local cache only

    klint -cluser help.kusto.windows.net -database Samples MyQueries.kql

#### Run analysis on MyQueries.kql using fresh database schemas from the server only

    klint -connection ""https://help.kusto.windows.net;Fed=true"" -database Samples -nocache MyQueries.kql

#### Run analysis on MyQueries.kql using no schemas at all (probably not a good idea)

    klint -nocache MyQueries.kql

#### Pre-generate local schema cache (does not run analysis)

    klint -connection ""https://help.kusto.windows.net;Fed=true"" -generate
