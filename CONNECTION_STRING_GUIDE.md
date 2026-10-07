# Connection String Configuration Guide

## Current Setup

The `appsettings.json` file is configured with Windows Integrated Security:

```
"local db": "Server=(local);Database=master;Integrated Security=true;Connection Timeout=10;TrustServerCertificate=true;"
```

## Common Connection String Options

### Option 1: Local SQL Server with Windows Authentication

```
"Server=(local);Database=master;Integrated Security=true;Connection Timeout=10;TrustServerCertificate=true;"
```

### Option 2: localhost with Windows Authentication

```
"Server=localhost;Database=master;Integrated Security=true;Connection Timeout=10;TrustServerCertificate=true;"
```

### Option 3: SQL Server Express with Windows Authentication

```
"Server=.\\SQLEXPRESS;Database=master;Integrated Security=true;Connection Timeout=10;TrustServerCertificate=true;"
```

### Option 4: SQL Authentication (username/password)

```
"Server=(local);Database=master;User Id=<monitor-login>;Password=<secret>;Connection Timeout=10;TrustServerCertificate=true;"
```

### Option 5: Remote SQL Server

```
"Server=192.168.1.100;Database=master;User Id=<monitor-login>;Password=<secret>;Connection Timeout=10;TrustServerCertificate=true;"
```

## Testing Your Connection

1. Open SQL Server Management Studio (SSMS)
2. Use the same connection details to verify you can connect
3. Update the connection string in `appsettings.json`
4. Restart the application

## Troubleshooting

**Error: "Cannot connect to (local)"**

- Try `localhost` or `.` instead of `(local)`
- Try `localhost\\SQLEXPRESS` for SQL Express

**Error: "Login failed for user"**

- Check username and password
- Verify SQL Server Authentication is enabled
- Verify the user has appropriate permissions

**Error: "Certificate chain was issued by an authority that is not trusted"**

- Add `TrustServerCertificate=true;` to the connection string (already included above)

## Monitoring Multiple Instances

To monitor multiple SQL Server instances, update `appsettings.json`:

```json
{
  "MonitorRefreshSeconds": 30,
  "MonitoredInstances": "SQL01,SQL02,SQL03,SQL04",
  "InstanceDisplayNames": "Primary,Secondary,Reporting,Archive",
  "ConnectionStrings": {
    "SQL01": "Server=(local);Database=master;Integrated Security=true;Connection Timeout=10;TrustServerCertificate=true;",
    "SQL02": "Server=(local);Database=master;Integrated Security=true;Connection Timeout=10;TrustServerCertificate=true;",
    "SQL03": "Server=(local);Database=master;Integrated Security=true;Connection Timeout=10;TrustServerCertificate=true;",
    "SQL04": "Server=(local);Database=master;Integrated Security=true;Connection Timeout=10;TrustServerCertificate=true;"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "Urls": "http://0.0.0.0:5001",
  "AllowedHosts": "*"
}
```

**Important:** Each instance key in `MonitoredInstances` must have a matching entry in `ConnectionStrings`.
