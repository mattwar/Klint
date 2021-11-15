## KLINT - A Kusto Linter (static analyzer)


### Syntax

    klint [options] <files>

### options:

    -? or -help           display this help text
    -connection <string>  the connection to use to access schema from the server
    -cache <path>         overrides the default path to the local schema cache directory
    -nocache              disables use of the local schema cache
    -generate             generates cached schemas for all databases
    -delete               deletes all cached schemas
    -cluster <name>       the current cluster in scope (if no connection specified)
    -database <name>      the current database in scope (if not specified by connection)
    -disable <codes>      a comma separated list of diagnostic codes to disable

### files:

  One or more file paths or file path patterns.

### examples:

#### Run analysis on MyQueries.kql using database schemas found in local cache or server

    klint -connection "https://help.kusto.windows.net;Fed=true" -database Samples MyQueries.kql

#### Run analysis on MyQueries.kql using database schemas found in local cache only

    klint -cluster help.kusto.windows.net -database Samples MyQueries.kql

#### Run analysis on MyQueries.kql using fresh database schemas from the server only

    klint -connection "https://help.kusto.windows.net;Fed=true" -database Samples -nocache MyQueries.kql

#### Run analysis on MyQueries.kql using no schemas at all (probably not a good idea)

    klint -nocache MyQueries.kql

#### Pre-generate local schema cache 

    klint -connection "https://help.kusto.windows.net;Fed=true" -generate

#### Delete all cached schemas

    klint -delete

#### Disable diagnostic codes

    klint -cluster help -database samples -disable KS503,KS501 MyQueries.kql

