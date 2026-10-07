using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;

using System.Data;
using Microsoft.Data.SqlClient;
using CerberusDashboard.Models;

namespace CerberusDashboard.Services
{
    public class SqlMonitorService
    {
        private readonly List<SqlInstance> _instances;
        private readonly int _refreshSeconds;
        private readonly IConfiguration _config;

        private class RawTableCounts
        {
            public long Inserts, Updates, Deletes, Reads;
            public long RowLockWaits, RowLockWaitMs, PageLockWaits, PageLockWaitMs;
        }

        private class RawFileIOCounts
        {
            public long BytesRead, BytesWritten;
            public long IoStallReadMs, IoStallWriteMs;
            public long NumReads, NumWrites;
        }

        private readonly Dictionary<string, (Dictionary<string, RawTableCounts> Sample, DateTime CapturedAt)> _prevTableSamples
            = new Dictionary<string, (Dictionary<string, RawTableCounts>, DateTime)>();
        private readonly object _tableSampleLock = new object();

        private readonly Dictionary<string, (Dictionary<string, RawFileIOCounts> Sample, DateTime CapturedAt)> _prevFileIOSamples
            = new Dictionary<string, (Dictionary<string, RawFileIOCounts>, DateTime)>();
        private readonly object _fileIOSampleLock = new object();

        // Partition cache — loaded once per instance at first hot-tables refresh
        private readonly Dictionary<string, HashSet<string>> _partitionedTableCache
            = new Dictionary<string, HashSet<string>>();
        private readonly HashSet<string> _partitionCacheLoaded = new HashSet<string>();
        private readonly object _partitionCacheLock = new object();

        public SqlMonitorService(IConfiguration config)
        {
            _config = config;
            _instances = LoadInstances();
            int.TryParse(_config["MonitorRefreshSeconds"], out _refreshSeconds);
            if (_refreshSeconds < 5) _refreshSeconds = 30;
        }

        private List<SqlInstance> LoadInstances()
        {
            var result = new List<SqlInstance>();
            string keys = _config["MonitoredInstances"] ?? "";
            string names = _config["InstanceDisplayNames"] ?? "";

            var keyArr = keys.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            var nameArr = names.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < keyArr.Length; i++)
            {
                string key = keyArr[i].Trim();
                string cs = _config.GetConnectionString(key);
                if (string.IsNullOrEmpty(cs)) continue;

                result.Add(new SqlInstance
                {
                    Key = key,
                    DisplayName = i < nameArr.Length ? nameArr[i].Trim() : key,
                    ConnectionString = cs
                });
            }

            return result;
        }

        // ── Public helpers used by MonitorHub for progress-driven loading ────────
        public List<SqlInstance> GetInstanceKeys() => _instances;
        public int RefreshSeconds => _refreshSeconds;

        public List<DataFileIO> GetFileIOStats(SqlInstance instance)
        {
            try
            {
                using (var conn = new SqlConnection(instance.ConnectionString))
                {
                    conn.Open();
                    return GetDataFileIO(conn, instance.Key);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetFileIOStats error [{instance.Key}]: {ex.Message}");
                return new List<DataFileIO>();
            }
        }

        public InstanceSnapshot GetInstanceSnapshotPublic(SqlInstance instance,
                                                           Action<string> progress = null)
            => GetInstanceSnapshot(instance, progress);

        public DashboardSnapshot GetAllInstanceSnapshots()
        {
            var snapshot = new DashboardSnapshot
            {
                GeneratedAt = DateTime.Now,
                RefreshIntervalSeconds = _refreshSeconds
            };

            foreach (var instance in _instances)
            {
                snapshot.Instances.Add(GetInstanceSnapshot(instance));
            }

            return snapshot;
        }

        private InstanceSnapshot GetInstanceSnapshot(SqlInstance instance,
                                                      Action<string> progress = null)
        {
            var snap = new InstanceSnapshot
            {
                InstanceKey = instance.Key,
                DisplayName = instance.DisplayName,
                SnapshotTime = DateTime.Now
            };

            try
            {
                using (var conn = new SqlConnection(instance.ConnectionString))
                {
                    progress?.Invoke("  → " + instance.DisplayName + ": opening connection...");
                    conn.Open();
                    snap.IsOnline = true;

                    progress?.Invoke("  → " + instance.DisplayName + ": server info...");
                    try { snap.ServerInfo = GetServerInfo(conn); }
                    catch (Exception ex)
                    {
                        snap.ServerInfo = new ServerInfo();
                        System.Diagnostics.Debug.WriteLine("ServerInfo error: " + ex.Message);
                    }

                    progress?.Invoke("  → " + instance.DisplayName + ": performance metrics...");
                    try { snap.Performance = GetPerformanceMetrics(conn); }
                    catch (Exception ex)
                    {
                        snap.Performance = new PerformanceMetrics();
                        System.Diagnostics.Debug.WriteLine("Performance error: " + ex.Message);
                    }

                    progress?.Invoke("  → " + instance.DisplayName + ": jobs...");
                    try { snap.Jobs = GetJobSummaries(conn); }
                    catch (Exception ex)
                    {
                        snap.Jobs = new List<JobSummary>();
                        System.Diagnostics.Debug.WriteLine("Jobs error: " + ex.Message);
                    }

                    progress?.Invoke("  → " + instance.DisplayName + ": blocked sessions...");
                    try { snap.BlockedSessions = GetBlockedSessions(conn); }
                    catch (Exception ex)
                    {
                        snap.BlockedSessions = new List<ActiveSession>();
                        System.Diagnostics.Debug.WriteLine("BlockedSessions error: " + ex.Message);
                    }

                    progress?.Invoke("  → " + instance.DisplayName + ": top queries...");
                    try { snap.TopQueries = GetTopQueries(conn); }
                    catch (Exception ex)
                    {
                        snap.TopQueries = new List<TopQuery>();
                        System.Diagnostics.Debug.WriteLine("TopQueries error: " + ex.Message);
                    }

                    progress?.Invoke("  → " + instance.DisplayName + ": databases...");
                    try { snap.Databases = GetDatabaseInfo(conn); }
                    catch (Exception ex)
                    {
                        snap.Databases = new List<DatabaseInfo>();
                        System.Diagnostics.Debug.WriteLine("Databases error: " + ex.Message);
                    }

                    progress?.Invoke("  → " + instance.DisplayName + ": active queries...");
                    try { snap.RecentQueries = GetRecentQueries(conn); }
                    catch (Exception ex)
                    {
                        snap.RecentQueries = new List<RecentQuery>();
                        System.Diagnostics.Debug.WriteLine("RecentQueries error: " + ex.Message);
                    }

                    progress?.Invoke("  → " + instance.DisplayName + ": expensive queries...");
                    try { snap.ExpensiveQueries = GetExpensiveQueries(conn); }
                    catch (Exception ex)
                    {
                        snap.ExpensiveQueries = new List<ExpensiveQuery>();
                        System.Diagnostics.Debug.WriteLine("ExpensiveQueries error: " + ex.Message);
                    }

                    progress?.Invoke("  → " + instance.DisplayName + ": data file I/O...");
                    try { snap.DataFileIO = GetDataFileIO(conn, instance.Key); }
                    catch (Exception ex)
                    {
                        snap.DataFileIO = new List<DataFileIO>();
                        snap.DataFileIOError = ex.Message;
                        System.Diagnostics.Debug.WriteLine("DataFileIO error: " + ex.Message);
                    }

                    progress?.Invoke("  → " + instance.DisplayName + ": hot tables...");
                    try { snap.HotTables = GetHotTables(conn, instance.Key); }
                    catch (Exception ex)
                    {
                        snap.HotTables = new List<HotTable>();
                        snap.HotTablesError = ex.Message;
                        System.Diagnostics.Debug.WriteLine("HotTables error: " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                snap.IsOnline = false;
                snap.ErrorMessage = ex.Message;
            }

            return snap;
        }

        // ─────────────────────────────────────────────────────────────────────
        // SERVER INFO
        // ─────────────────────────────────────────────────────────────────────
        private ServerInfo GetServerInfo(SqlConnection conn)
        {
            const string sql = @"
        SELECT 
            @@VERSION AS ServerVersion,
            SERVERPROPERTY('Edition') AS Edition,
            cpu_count AS CpuCount,
            CAST(physical_memory_kb AS bigint) / 1024.0 AS PhysicalMemoryMb,
            (SELECT CAST(cntr_value AS bigint) 
             FROM sys.dm_os_performance_counters
             WHERE counter_name = 'Total Server Memory (KB)' 
             AND object_name LIKE '%Memory Manager%') / 1024.0 AS SqlMemoryUsedMb,
            sqlserver_start_time AS SqlStartTime,
            SERVERPROPERTY('Collation') AS Collation
        FROM sys.dm_os_sys_info";

            using (var cmd = new SqlCommand(sql, conn))
            using (var reader = cmd.ExecuteReader(CommandBehavior.SingleRow))
            {
                if (reader.Read())
                {
                    return new ServerInfo
                    {
                        ServerVersion = reader["ServerVersion"]?.ToString()?.Split('\n')[0] ?? "",
                        Edition = reader["Edition"]?.ToString() ?? "",
                        CpuCount = Convert.ToInt64(reader["CpuCount"]),
                        PhysicalMemoryMb = Convert.ToDecimal(reader["PhysicalMemoryMb"]),
                        SqlMemoryUsedMb = reader["SqlMemoryUsedMb"] == DBNull.Value ? 0
                                            : Convert.ToDecimal(reader["SqlMemoryUsedMb"]),
                        SqlStartTime = Convert.ToDateTime(reader["SqlStartTime"]),
                        Collation = reader["Collation"]?.ToString() ?? ""
                    };
                }
            }
            return new ServerInfo();
        }

        // ─────────────────────────────────────────────────────────────────────
        // PERFORMANCE METRICS
        // ─────────────────────────────────────────────────────────────────────
        private PerformanceMetrics GetPerformanceMetrics(SqlConnection conn)
        {
            var metrics = new PerformanceMetrics();

            // ── Page Life Expectancy — average across all NUMA nodes ─────────
            const string pleSql = @"
        SELECT AVG(cntr_value) AS ple
        FROM sys.dm_os_performance_counters
        WHERE counter_name = 'Page life expectancy'";

            using (var cmd = new SqlCommand(pleSql, conn))
            {
                var result = cmd.ExecuteScalar();
                if (result != null && result != DBNull.Value)
                    metrics.PageLifeExpectancy = Convert.ToInt64(result);
            }

            // ── Buffer cache + batch requests ────────────────────────────────
            const string perfSql = @"
        SELECT counter_name, cntr_value
        FROM sys.dm_os_performance_counters
        WHERE counter_name IN (
            'Batch Requests/sec',
            'Buffer cache hit ratio',
            'Buffer cache hit ratio base'
        )
        AND (object_name LIKE '%Buffer Manager%' OR object_name LIKE '%SQL Statistics%')";

            decimal bcrValue = 0, bcrBase = 0;
            decimal batchSample1 = 0, batchSample2 = 0;

            using (var cmd = new SqlCommand(perfSql, conn))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    string name = reader["counter_name"].ToString().Trim();
                    decimal val = Convert.ToDecimal(reader["cntr_value"]);

                    if (name == "Batch Requests/sec") batchSample1 = val;
                    else if (name == "Buffer cache hit ratio") bcrValue = val;
                    else if (name == "Buffer cache hit ratio base") bcrBase = val;
                }
            }
            // ... rest of method stays exactly the same
            // Wait 1 second then take second sample for batch requests/sec
            System.Threading.Thread.Sleep(1000);

            const string batchSql = @"
        SELECT cntr_value
        FROM sys.dm_os_performance_counters
        WHERE counter_name = 'Batch Requests/sec'
          AND object_name LIKE '%SQL Statistics%'";

            using (var cmd = new SqlCommand(batchSql, conn))
            {
                var result = cmd.ExecuteScalar();
                if (result != null && result != DBNull.Value)
                    batchSample2 = Convert.ToDecimal(result);
            }

            // Actual rate is the difference between the two samples
            metrics.BatchRequestsPerSec = Math.Max(0, batchSample2 - batchSample1);

            if (bcrBase > 0)
                metrics.BufferCacheHitRatio = Math.Round((bcrValue / bcrBase) * 100, 1);

            // Active connections and blocking
            const string connSql = @"
    SELECT
        (SELECT COUNT(*) 
         FROM sys.dm_exec_sessions 
         WHERE session_id > 50 
           AND is_user_process = 1)                             AS ActiveConnections,
        (SELECT COUNT(*) 
         FROM sys.dm_exec_requests 
         WHERE session_id > 50 
           AND blocking_session_id > 0)                        AS BlockedProcesses";
            using (var cmd = new SqlCommand(connSql, conn))
            using (var reader = cmd.ExecuteReader(CommandBehavior.SingleRow))
            {
                if (reader.Read())
                {
                    metrics.ActiveConnections = Convert.ToInt64(reader["ActiveConnections"]);
                    metrics.BlockedProcesses = Convert.ToInt64(reader["BlockedProcesses"]);
                }
            }

            // CPU usage via ring buffer
            const string cpuSql = @"
                SELECT TOP 1
                    record.value('(./Record/SchedulerMonitorEvent/SystemHealth/ProcessUtilization)[1]', 'int') AS SqlCpuUtilization
                FROM (
                    SELECT TOP 1 CONVERT(XML, record) AS record
                    FROM sys.dm_os_ring_buffers
                    WHERE ring_buffer_type = N'RING_BUFFER_SCHEDULER_MONITOR'
                    ORDER BY timestamp DESC
                ) AS ring_data";

            try
            {
                using (var cmd = new SqlCommand(cpuSql, conn))
                {
                    var result = cmd.ExecuteScalar();
                    if (result != null && result != DBNull.Value)
                        metrics.CpuUsagePercent = Convert.ToDecimal(result);
                }
            }
            catch { /* Ring buffer may not be available on all editions */ }

            // Top wait type
            const string waitSql = @"
                SELECT TOP 1 wait_type, wait_time_ms
                FROM sys.dm_os_wait_stats
                WHERE wait_type NOT IN (
                    'SLEEP_TASK','BROKER_TO_FLUSH','BROKER_TASK_STOP','CLR_AUTO_EVENT',
                    'DISPATCHER_QUEUE_SEMAPHORE','FT_IFTS_SCHEDULER_IDLE_WAIT',
                    'HADR_FILESTREAM_IOMGR_IOCOMPLETION','HADR_WORK_QUEUE',
                    'LAZYWRITER_SLEEP','LOGMGR_QUEUE','ONDEMAND_TASK_QUEUE',
                    'REQUEST_FOR_DEADLOCK_SEARCH','RESOURCE_QUEUE','SERVER_IDLE_CHECK',
                    'SLEEP_DBSTARTUP','SLEEP_DCOMSTARTUP','SLEEP_MASTERDBREADY',
                    'SLEEP_MASTERMDREADY','SLEEP_MASTERUPGRADED','SLEEP_MSDBSTARTUP',
                    'SLEEP_SYSTEMTASK','SLEEP_TEMPDBSTARTUP','SNI_HTTP_ACCEPT',
                    'SP_SERVER_DIAGNOSTICS_SLEEP','SQLTRACE_BUFFER_FLUSH',
                    'SQLTRACE_INCREMENTAL_FLUSH_SLEEP','WAITFOR','XE_DISPATCHER_WAIT',
                    'XE_TIMER_EVENT','BROKER_EVENTHANDLER','CHECKPOINT_QUEUE',
                    'DBMIRROR_EVENTS_QUEUE','SQLTRACE_WAIT_ENTRIES','WAIT_XTP_OFFLINE_CKPT_NEW_LOG'
                )
                ORDER BY wait_time_ms DESC";

            using (var cmd = new SqlCommand(waitSql, conn))
            using (var reader = cmd.ExecuteReader(CommandBehavior.SingleRow))
            {
                if (reader.Read())
                {
                    metrics.TopWaitType = reader["wait_type"]?.ToString() ?? "";
                    metrics.TopWaitMs = Convert.ToDecimal(reader["wait_time_ms"]);
                }
            }

            return metrics;
        }

        // ─────────────────────────────────────────────────────────────────────
        // SQL AGENT JOBS
        // ─────────────────────────────────────────────────────────────────────
        private List<JobSummary> GetJobSummaries(SqlConnection conn)
        {
            var jobs = new List<JobSummary>();

            // Check if SQL Agent is accessible
            const string sql = @"
                SELECT 
                    j.job_id         AS JobId,
                    j.name           AS JobName,
                    j.enabled        AS Enabled,
                    j.category_id,
                    c.name           AS Category,
                    COALESCE(h.run_status, 5) AS LastRunStatus,
                    CASE WHEN h.run_date IS NOT NULL THEN
                        CAST(CAST(h.run_date AS VARCHAR) + ' ' +
                             STUFF(STUFF(RIGHT('000000' + CAST(h.run_time AS VARCHAR), 6), 5, 0, ':'), 3, 0, ':') AS DATETIME)
                    END              AS LastRunDateTime,
                    h.run_duration   AS LastRunDurationRaw,
                    CASE WHEN ja.next_run_date > 0 THEN
                        CAST(CAST(ja.next_run_date AS VARCHAR) + ' ' +
                             STUFF(STUFF(RIGHT('000000' + CAST(ja.next_run_time AS VARCHAR), 6), 5, 0, ':'), 3, 0, ':') AS DATETIME)
                    END              AS NextRunDateTime,
                    SUBSTRING(h.message, 1, 500) AS LastStepMessage
                FROM msdb.dbo.sysjobs j
                LEFT JOIN msdb.dbo.syscategories c ON j.category_id = c.category_id
                LEFT JOIN msdb.dbo.sysjobhistory h 
                    ON j.job_id = h.job_id 
                    AND h.instance_id = (
                        SELECT MAX(h2.instance_id) 
                        FROM msdb.dbo.sysjobhistory h2
                        WHERE h2.job_id = j.job_id AND h2.step_id = 0
                    )
                LEFT JOIN (
                    SELECT js.job_id, MIN(js.next_run_date) AS next_run_date, MIN(js.next_run_time) AS next_run_time
                    FROM msdb.dbo.sysjobschedules js
                    WHERE js.next_run_date > 0
                    GROUP BY js.job_id
                ) ja ON j.job_id = ja.job_id
                ORDER BY j.name";

            try
            {
                using (var cmd = new SqlCommand(sql, conn) { CommandTimeout = 15 })
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        Int64 durationRaw = reader["LastRunDurationRaw"] == DBNull.Value ? 0 : Convert.ToInt64(reader["LastRunDurationRaw"]);
                        // HHMMSS format — convert to total seconds
                        Int64 hours = durationRaw / 10000;
                        Int64 minutes = (durationRaw % 10000) / 100;
                        Int64 seconds = durationRaw % 100;
                        Int64 totalSeconds = hours * 3600 + minutes * 60 + seconds;

                        jobs.Add(new JobSummary
                        {
                            JobId = (Guid)reader["JobId"],
                            JobName = reader["JobName"].ToString(),
                            Enabled = Convert.ToBoolean(reader["Enabled"]),
                            LastRunStatus = Convert.ToInt64(reader["LastRunStatus"]),
                            LastRunDateTime = reader["LastRunDateTime"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(reader["LastRunDateTime"]),
                            LastRunDurationSeconds = totalSeconds,
                            NextRunDateTime = reader["NextRunDateTime"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(reader["NextRunDateTime"]),
                            Category = reader["Category"]?.ToString() ?? "Uncategorized",
                            LastStepMessage = reader["LastStepMessage"]?.ToString() ?? ""
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                // SQL Agent might not be running or accessible
                System.Diagnostics.Debug.WriteLine("Job query error: " + ex.Message);
            }

            return jobs;
        }

        // ─────────────────────────────────────────────────────────────────────
        // BLOCKED SESSIONS
        // ─────────────────────────────────────────────────────────────────────
        private List<ActiveSession> GetBlockedSessions(SqlConnection conn)
        {
            var sessions = new List<ActiveSession>();

            const string sql = @"
                SELECT TOP 25
                    r.session_id,
                    r.blocking_session_id,
                    DB_NAME(r.database_id) AS database_name,
                    s.login_name,
                    s.host_name,
                    r.wait_type,
                    r.wait_time AS wait_time_ms,
                    LEFT(t.text, 300) AS sql_text,
                    r.status,
                    r.cpu_time,
                    r.logical_reads
                FROM sys.dm_exec_requests r
                INNER JOIN sys.dm_exec_sessions s ON r.session_id = s.session_id
                CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) t
                WHERE r.session_id > 50
                  AND (r.blocking_session_id > 0 OR r.wait_time > 5000)
                ORDER BY r.wait_time DESC";

            using (var cmd = new SqlCommand(sql, conn) { CommandTimeout = 10 })
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    sessions.Add(new ActiveSession
                    {
                        SessionId = Convert.ToInt64(reader["session_id"]),
                        BlockingSessionId = Convert.ToInt64(reader["blocking_session_id"]),
                        DatabaseName = reader["database_name"]?.ToString() ?? "",
                        LoginName = reader["login_name"]?.ToString() ?? "",
                        HostName = reader["host_name"]?.ToString() ?? "",
                        WaitType = reader["wait_type"]?.ToString() ?? "",
                        WaitTimeMs = Convert.ToInt64(reader["wait_time_ms"]),
                        SqlText = reader["sql_text"]?.ToString() ?? "",
                        Status = reader["status"]?.ToString() ?? "",
                        CpuTime = Convert.ToInt64(reader["cpu_time"]),
                        LogicalReads = Convert.ToInt64(reader["logical_reads"])
                    });
                }
            }

            return sessions;
        }

        // ─────────────────────────────────────────────────────────────────────
        // TOP QUERIES BY CPU
        // ─────────────────────────────────────────────────────────────────────
        private List<TopQuery> GetTopQueries(SqlConnection conn)
        {
            var queries = new List<TopQuery>();

            const string sql = @"
    -- Currently executing queries
    SELECT TOP 10
        LEFT(t.text, 250)                                        AS query_text,
        1                                                        AS execution_count,
        r.cpu_time                                               AS avg_cpu_ms,
        r.total_elapsed_time                                     AS avg_elapsed_ms,
        CAST(r.logical_reads AS bigint)                          AS avg_logical_reads,
        CAST(r.cpu_time AS bigint)                               AS total_cpu_ms,
        DB_NAME(r.database_id)                                   AS database_name
    FROM sys.dm_exec_requests r
    CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) t
    WHERE r.session_id > 50
      AND r.status != 'background'
      AND t.text NOT LIKE '%sys.%'
      AND DB_NAME(r.database_id) NOT IN ('tempdb', 'master', 'model', 'msdb')
      AND DB_NAME(r.database_id) IS NOT NULL

    UNION ALL

    -- Top cached queries — last 24 hours only, ordered by avg cpu
    SELECT TOP 10
        LEFT(t.text, 250)                                        AS query_text,
        qs.execution_count                                       AS execution_count,
        CAST(qs.total_worker_time AS bigint) /
            CAST(qs.execution_count AS bigint) / 1000.0          AS avg_cpu_ms,
        CAST(qs.total_elapsed_time AS bigint) /
            CAST(qs.execution_count AS bigint) / 1000.0          AS avg_elapsed_ms,
        CAST(qs.total_logical_reads AS bigint) /
            CAST(qs.execution_count AS bigint)                   AS avg_logical_reads,
        CAST(qs.total_worker_time AS bigint) / 1000.0            AS total_cpu_ms,
        DB_NAME(t.dbid)                                          AS database_name
    FROM sys.dm_exec_query_stats qs
    CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) t
    WHERE qs.execution_count > 1
      AND qs.last_execution_time > DATEADD(HOUR, -24, GETUTCDATE())
      AND t.text NOT LIKE '%CREATE%'
      AND t.text NOT LIKE '%ALTER%'
      AND t.text NOT LIKE '%sys.%'
      AND t.text NOT LIKE '%INFORMATION_SCHEMA%'
      AND DB_NAME(t.dbid) NOT IN ('tempdb', 'master', 'model', 'msdb')
      AND DB_NAME(t.dbid) IS NOT NULL

    ORDER BY total_cpu_ms DESC";

            using (var cmd = new SqlCommand(sql, conn) { CommandTimeout = 10 })
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    queries.Add(new TopQuery
                    {
                        QueryText = reader["query_text"]?.ToString() ?? "",
                        ExecutionCount = Convert.ToInt64(reader["execution_count"]),
                        AvgCpuMs = Convert.ToDecimal(reader["avg_cpu_ms"]),
                        AvgElapsedMs = Convert.ToDecimal(reader["avg_elapsed_ms"]),
                        AvgLogicalReads = Convert.ToDecimal(reader["avg_logical_reads"]),
                        TotalCpuMs = Convert.ToDecimal(reader["total_cpu_ms"]),
                        DatabaseName = reader["database_name"]?.ToString() ?? ""
                    });
                }
            }

            return queries;
        }
        
        // ─────────────────────────────────────────────────────────────────────
        // DATABASE INFO
        // ─────────────────────────────────────────────────────────────────────
        private List<DatabaseInfo> GetDatabaseInfo(SqlConnection conn)
        {
            var databases = new List<DatabaseInfo>();

            const string sql = @"
    SELECT
    d.name,
    d.state_desc AS status,
    d.recovery_model_desc AS recovery_model,
    f.data_drive,
    f.size_mb,
    f.log_size_mb,
    MAX(b_full.backup_finish_date) AS last_full_backup,
    MAX(b_log.backup_finish_date)  AS last_log_backup
FROM sys.databases d
LEFT JOIN (
    SELECT
        database_id,
        SUM(CASE WHEN type = 0 THEN CAST(size AS bigint) ELSE 0 END) * 8.0 / 1024 AS size_mb,
        SUM(CASE WHEN type = 1 THEN CAST(size AS bigint) ELSE 0 END) * 8.0 / 1024 AS log_size_mb,
        MAX(CASE WHEN type = 0 THEN LEFT(physical_name, 1) END) AS data_drive
    FROM sys.master_files
    GROUP BY database_id
) f ON d.database_id = f.database_id
LEFT JOIN msdb.dbo.backupset b_full 
    ON b_full.database_name = d.name AND b_full.type = 'D'
LEFT JOIN msdb.dbo.backupset b_log  
    ON b_log.database_name  = d.name AND b_log.type  = 'L'
WHERE d.database_id > 4
GROUP BY d.name, d.state_desc, d.recovery_model_desc, f.data_drive, f.size_mb, f.log_size_mb
ORDER BY d.name";
            using (var cmd = new SqlCommand(sql, conn) { CommandTimeout = 15 })
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    decimal logSize = reader["log_size_mb"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["log_size_mb"]);
                    databases.Add(new DatabaseInfo
                    {
                        Name = reader["name"].ToString(),
                        Status = reader["status"]?.ToString() ?? "",
                        RecoveryModel = reader["recovery_model"]?.ToString() ?? "",
                        DataDrive = reader["data_drive"] == DBNull.Value ? "" : reader["data_drive"].ToString(),
                        SizeMb = reader["size_mb"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["size_mb"]),
                        LogSizeMb = logSize,
                        LastBackupDate = reader["last_full_backup"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(reader["last_full_backup"]),
                        LastLogBackupDate = reader["last_log_backup"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(reader["last_log_backup"])
                    });
                }
            }

            return databases;
        }
        public List<JobHistory> GetJobHistory(string connectionStringKey, Guid jobId, Int64 topRows = 20)
        {
            var cs = _config.GetConnectionString(connectionStringKey);
            if (string.IsNullOrEmpty(cs)) return new List<JobHistory>();

            var history = new List<JobHistory>();

            const string sql = @"
        SELECT TOP (@topRows)
            h.instance_id,
            h.step_id,
            h.step_name,
            CASE h.run_status 
                WHEN 0 THEN 'Failed'
                WHEN 1 THEN 'Succeeded'
                WHEN 2 THEN 'Retry'
                WHEN 3 THEN 'Cancelled'
                WHEN 4 THEN 'In Progress'
                ELSE 'Unknown'
            END AS run_status,
            CAST(CAST(h.run_date AS VARCHAR) + ' ' +
                 STUFF(STUFF(RIGHT('000000' + CAST(h.run_time AS VARCHAR), 6), 5, 0, ':'), 3, 0, ':') 
                 AS DATETIME) AS run_datetime,
            h.run_duration,
            LEFT(h.message, 2000) AS message,
            h.retries_attempted
        FROM msdb.dbo.sysjobhistory h
        WHERE h.job_id = @jobId
        ORDER BY h.instance_id DESC";

            using (var conn = new SqlConnection(cs))
            using (var cmd = new SqlCommand(sql, conn) { CommandTimeout = 15 })
            {
                cmd.Parameters.AddWithValue("@topRows", topRows);
                cmd.Parameters.AddWithValue("@jobId", jobId);
                conn.Open();

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        Int64 durationRaw = Convert.ToInt64(reader["run_duration"]);
                        Int64 hours = durationRaw / 10000;
                        Int64 minutes = (durationRaw % 10000) / 100;
                        Int64 seconds = durationRaw % 100;

                        history.Add(new JobHistory
                        {
                            InstanceId = Convert.ToInt64(reader["instance_id"]),
                            StepId = Convert.ToInt64(reader["step_id"]),
                            StepName = reader["step_name"]?.ToString() ?? "",
                            RunStatus = reader["run_status"]?.ToString() ?? "",
                            RunDateTime = Convert.ToDateTime(reader["run_datetime"]),
                            DurationSeconds = hours * 3600 + minutes * 60 + seconds,
                            Message = reader["message"]?.ToString() ?? "",
                            RetriesAttempted = Convert.ToInt64(reader["retries_attempted"])
                        });
                    }
                }
            }
            return history;
        }

        public QueryPlan GetQueryPlan(string connectionStringKey, string planHandle)
        {
            var cs = _config.GetConnectionString(connectionStringKey);
            if (string.IsNullOrEmpty(cs)) return new QueryPlan { ErrorMessage = "Connection not found" };

            if (string.IsNullOrEmpty(planHandle)) 
                return new QueryPlan { ErrorMessage = "No plan handle provided" };

            const string sql = @"
        SELECT 
            TRY_CAST(qp.query_plan AS nvarchar(max)) AS plan_xml,
            LEFT(t.text, 500) AS query_text
        FROM sys.dm_exec_cached_plans cp
        CROSS APPLY sys.dm_exec_query_plan(cp.plan_handle) qp
        CROSS APPLY sys.dm_exec_sql_text(cp.plan_handle) t
        WHERE CONVERT(VARCHAR(MAX), cp.plan_handle, 1) = @planHandle
          AND qp.query_plan IS NOT NULL";

            try
            {
                using (var conn = new SqlConnection(cs))
                using (var cmd = new SqlCommand(sql, conn) { CommandTimeout = 15 })
                {
                    cmd.Parameters.AddWithValue("@planHandle", planHandle);
                    conn.Open();

                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            var planXml = reader["plan_xml"] == DBNull.Value ? null : reader["plan_xml"]?.ToString();
                            var queryText = reader["query_text"]?.ToString() ?? "";

                            return new QueryPlan
                            {
                                QueryText = queryText,
                                PlanXml = planXml
                            };
                        }
                        else
                        {
                            return new QueryPlan 
                            { 
                                ErrorMessage = "Query plan not found in cache. The plan may have been evicted."
                            };
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetQueryPlan error: {ex.Message}");
                return new QueryPlan { ErrorMessage = ex.Message };
            }
        }
        // ─────────────────────────────────────────────────────────────────────
        // RECENT & ACTIVE QUERIES
        // ─────────────────────────────────────────────────────────────────────
        private List<RecentQuery> GetRecentQueries(SqlConnection conn)
        {
            var queries = new List<RecentQuery>();

            const string sql = @"
        SELECT TOP 50
            LEFT(t.text, 500)                                           AS QueryText,
            qs.total_elapsed_time / 1000                                AS TotalElapsedMs,
            qs.total_elapsed_time / 1000 / qs.execution_count           AS AvgElapsedMs,
            DB_NAME(qp.dbid)                                            AS DatabaseName,
            qs.total_worker_time / 1000                                 AS TotalCpuMs,
            qs.total_worker_time / 1000 / qs.execution_count            AS AvgCpuMs,
            qs.execution_count                                          AS ExecutionCount,
            qs.last_execution_time                                      AS LastExecuted,
            CONVERT(VARCHAR(MAX), qs.plan_handle, 1)                    AS PlanHandle
        FROM sys.dm_exec_query_stats qs
        CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) t
        CROSS APPLY sys.dm_exec_query_plan(qs.plan_handle) qp
        WHERE qs.last_execution_time > DATEADD(HOUR, -24, GETDATE())
          AND DB_NAME(qp.dbid) NOT IN ('master', 'model', 'msdb', 'tempdb')
          AND DB_NAME(qp.dbid) IS NOT NULL
          AND qs.total_elapsed_time > 300000
        ORDER BY qs.total_elapsed_time DESC";

            using (var cmd = new SqlCommand(sql, conn) { CommandTimeout = 10 })
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    queries.Add(new RecentQuery
                    {
                        QueryText = reader["QueryText"]?.ToString() ?? "",
                        //TotalElapsedMs = Convert.ToDecimal(reader["TotalElapsedMs"]),
                        AvgElapsedMs = Convert.ToDecimal(reader["AvgElapsedMs"]),
                        DatabaseName = reader["DatabaseName"]?.ToString() ?? "",
                        TotalCpuMs = Convert.ToDecimal(reader["TotalCpuMs"]),
                        AvgCpuMs = Convert.ToDecimal(reader["AvgCpuMs"]),
                        ExecutionCount = Convert.ToInt64(reader["ExecutionCount"]),
                        LastExecuted = Convert.ToDateTime(reader["LastExecuted"]),
                        PlanHandle = reader["PlanHandle"]?.ToString() ?? ""
                    });
                }
            }

            return queries;
        }
        // ─────────────────────────────────────────────────────────────────────
        // ACTIVE SESSIONS DETAIL
        // ─────────────────────────────────────────────────────────────────────
        public List<ActiveSession> GetActiveSessions(string connectionStringKey)
        {
            var cs = _config.GetConnectionString(connectionStringKey);
            if (string.IsNullOrEmpty(cs)) return new List<ActiveSession>();

            const string sql = @"
    SELECT
        s.session_id                                        AS SessionId,
        COALESCE(r.blocking_session_id, 0)                  AS BlockingSessionId,
        DB_NAME(COALESCE(r.database_id, s.database_id))     AS DatabaseName,
        s.login_name                                        AS LoginName,
        s.host_name                                         AS HostName,
        s.program_name                                      AS ProgramName,
        s.status                                            AS Status,
        COALESCE(r.wait_type, '')                           AS WaitType,
        COALESCE(r.wait_time, 0)                            AS WaitTimeMs,
        COALESCE(r.total_elapsed_time, 0)                   AS ElapsedMs,
        CASE WHEN r.session_id IS NOT NULL THEN r.cpu_time ELSE NULL END AS CpuMs,
CASE WHEN r.session_id IS NOT NULL THEN r.logical_reads ELSE NULL END AS LogicalReads,
CASE WHEN r.session_id IS NOT NULL THEN r.writes        ELSE NULL END AS Writes,
s.last_request_start_time                           AS LastRequestStart,
        s.login_time                                        AS LoginTime,
        s.open_transaction_count                            AS OpenTransactions,
        s.memory_usage * 8                                  AS MemoryKb,
        LEFT(COALESCE(t.text, ''), 300)                     AS SqlText
    FROM sys.dm_exec_sessions s
    LEFT JOIN sys.dm_exec_requests r 
        ON s.session_id = r.session_id
    OUTER APPLY sys.dm_exec_sql_text(r.sql_handle) t
    WHERE s.session_id > 50
      AND s.is_user_process = 1
      AND s.status <> 'sleeping'
    ORDER BY COALESCE(r.total_elapsed_time, 0) DESC";
            var sessions = new List<ActiveSession>();

            try
            {
                using (var conn = new SqlConnection(cs))
                using (var cmd = new SqlCommand(sql, conn) { CommandTimeout = 10 })
                {
                    conn.Open();
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            sessions.Add(new ActiveSession
                            {
                                SessionId = Convert.ToInt64(reader["SessionId"]),
                                BlockingSessionId = Convert.ToInt64(reader["BlockingSessionId"]),
                                DatabaseName = reader["DatabaseName"]?.ToString() ?? "",
                                LoginName = reader["LoginName"]?.ToString() ?? "",
                                HostName = reader["HostName"]?.ToString() ?? "",
                                ProgramName = reader["ProgramName"]?.ToString() ?? "",
                                Status = reader["Status"]?.ToString() ?? "",
                                WaitType = reader["WaitType"]?.ToString() ?? "",
                                WaitTimeMs = Convert.ToInt64(reader["WaitTimeMs"]),
                                ElapsedMs = Convert.ToInt64(reader["ElapsedMs"]),
                                CpuTime = reader["CpuMs"] == DBNull.Value ? 0 : Convert.ToInt64(reader["CpuMs"]),
                                LogicalReads = reader["LogicalReads"] == DBNull.Value ? 0 : Convert.ToInt64(reader["LogicalReads"]),
                                Writes = reader["Writes"] == DBNull.Value ? 0 : Convert.ToInt64(reader["Writes"]),
                                LastRequestStart = reader["LastRequestStart"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(reader["LastRequestStart"]),
                                LoginTime = reader["LoginTime"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(reader["LoginTime"]),
                                OpenTransactions = Convert.ToInt32(reader["OpenTransactions"]),
                                MemoryKb = Convert.ToInt64(reader["MemoryKb"]),
                                SqlText = reader["SqlText"]?.ToString() ?? ""
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("ActiveSessions error: " + ex.Message);
            }

            return sessions;
        }

        // ─────────────────────────────────────────────────────────────────────
        // EXPENSIVE QUERIES (similar to SSMS Activity Monitor)
        // ─────────────────────────────────────────────────────────────────────
        private List<ExpensiveQuery> GetExpensiveQueries(SqlConnection conn)
        {
            var queries = new List<ExpensiveQuery>();

            const string sql = @"
            SELECT TOP 20
                DB_NAME(qt.dbid) AS DatabaseName,
                SUBSTRING(qt.text, (qs.statement_start_offset/2)+1,
                    ((CASE qs.statement_end_offset
                        WHEN -1 THEN DATALENGTH(qt.text)
                        ELSE qs.statement_end_offset
                    END - qs.statement_start_offset)/2)+1) AS QueryText,
                qs.execution_count AS ExecutionCount,
                qs.total_worker_time / 1000.0 AS TotalCpuMs,
                (qs.total_worker_time / qs.execution_count) / 1000.0 AS AvgCpuMs,
                qs.total_elapsed_time / 1000.0 AS TotalElapsedMs,
                (qs.total_elapsed_time / qs.execution_count) / 1000.0 AS AvgElapsedMs,
                qs.total_logical_reads AS TotalLogicalReads,
                qs.total_logical_reads / qs.execution_count AS AvgLogicalReads,
                qs.last_execution_time AS LastExecutionTime,
                CONVERT(VARCHAR(MAX), qs.plan_handle, 1) AS PlanHandle
            FROM sys.dm_exec_query_stats AS qs
            CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) AS qt
            WHERE qs.last_execution_time > DATEADD(MINUTE, -15, GETDATE())
                AND DB_NAME(qt.dbid) NOT IN ('tempdb', 'master', 'model', 'msdb')
                AND DB_NAME(qt.dbid) IS NOT NULL
            ORDER BY (qs.total_elapsed_time / qs.execution_count) DESC";

            try
            {
                using (var cmd = new SqlCommand(sql, conn))
                {
                    cmd.CommandTimeout = 10;
                    if (conn.State != System.Data.ConnectionState.Open)
                        conn.Open();

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            queries.Add(new ExpensiveQuery
                            {
                                DatabaseName = reader["DatabaseName"]?.ToString() ?? "",
                                QueryText = reader["QueryText"]?.ToString() ?? "",
                                ExecutionCount = Convert.ToInt64(reader["ExecutionCount"]),
                                TotalCpuMs = Convert.ToDecimal(reader["TotalCpuMs"]),
                                AvgCpuMs = Convert.ToDecimal(reader["AvgCpuMs"]),
                                //TotalElapsedMs = Convert.ToDecimal(reader["TotalElapsedMs"]),
                                AvgElapsedMs = Convert.ToDecimal(reader["AvgElapsedMs"]),
                                TotalLogicalReads = Convert.ToDecimal(reader["TotalLogicalReads"]),
                                AvgLogicalReads = Convert.ToDecimal(reader["AvgLogicalReads"]),
                                LastExecutionTime = Convert.ToDateTime(reader["LastExecutionTime"]),
                                PlanHandle = reader["PlanHandle"]?.ToString() ?? ""
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("GetExpensiveQueries error: " + ex.Message);
            }

            return queries;
        }

        // ─────────────────────────────────────────────────────────────────────
        // DATA FILE I/O STATISTICS
        // ─────────────────────────────────────────────────────────────────────
        private List<DataFileIO> GetDataFileIO(SqlConnection conn, string instanceKey)
        {
            const string sql = @"
    SELECT
        vfs.database_id,
        vfs.file_id,
        DB_NAME(vfs.database_id)                            AS DatabaseName,
        mf.name                                             AS FileName,
        mf.type_desc                                        AS FileType,
        CAST(mf.size AS bigint) * 8 / 1024                 AS SizeMb,
        vfs.num_of_reads                                    AS NumReads,
        vfs.num_of_writes                                   AS NumWrites,
        vfs.num_of_bytes_read                               AS BytesRead,
        vfs.num_of_bytes_written                            AS BytesWritten,
        vfs.io_stall_read_ms                                AS IoStallReadMs,
        vfs.io_stall_write_ms                               AS IoStallWriteMs,
        CASE
            WHEN vfs.num_of_reads = 0 THEN 0
            ELSE CAST(vfs.io_stall_read_ms  AS decimal(18,2)) / vfs.num_of_reads
        END AS AvgReadLatencyMs,
        CASE
            WHEN vfs.num_of_writes = 0 THEN 0
            ELSE CAST(vfs.io_stall_write_ms AS decimal(18,2)) / vfs.num_of_writes
        END AS AvgWriteLatencyMs
    FROM sys.dm_io_virtual_file_stats(NULL, NULL) AS vfs
    INNER JOIN sys.master_files AS mf
        ON vfs.database_id = mf.database_id
        AND vfs.file_id    = mf.file_id
    WHERE DB_NAME(vfs.database_id) NOT IN ('master', 'model', 'msdb')";

            var current  = new Dictionary<string, RawFileIOCounts>();
            var fileMap  = new Dictionary<string, DataFileIO>();
            var capturedAt = DateTime.UtcNow;

            using (var cmd = new SqlCommand(sql, conn) { CommandTimeout = 30 })
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    var dbId   = Convert.ToInt32(reader["database_id"]);
                    var fileId = Convert.ToInt32(reader["file_id"]);
                    var key    = $"{dbId}:{fileId}";

                    current[key] = new RawFileIOCounts
                    {
                        BytesRead      = Convert.ToInt64(reader["BytesRead"]),
                        BytesWritten   = Convert.ToInt64(reader["BytesWritten"]),
                        IoStallReadMs  = Convert.ToInt64(reader["IoStallReadMs"]),
                        IoStallWriteMs = Convert.ToInt64(reader["IoStallWriteMs"]),
                        NumReads       = Convert.ToInt64(reader["NumReads"]),
                        NumWrites      = Convert.ToInt64(reader["NumWrites"])
                    };

                    fileMap[key] = new DataFileIO
                    {
                        DatabaseName      = reader["DatabaseName"]?.ToString()  ?? "",
                        FileName          = reader["FileName"]?.ToString()       ?? "",
                        FileType          = reader["FileType"]?.ToString()       ?? "",
                        SizeMb            = Convert.ToDecimal(reader["SizeMb"]),
                        NumReads          = Convert.ToInt64(reader["NumReads"]),
                        NumWrites         = Convert.ToInt64(reader["NumWrites"]),
                        IoStallReadMs     = Convert.ToDecimal(reader["IoStallReadMs"]),
                        IoStallWriteMs    = Convert.ToDecimal(reader["IoStallWriteMs"]),
                        AvgReadLatencyMs  = Convert.ToDecimal(reader["AvgReadLatencyMs"]),
                        AvgWriteLatencyMs = Convert.ToDecimal(reader["AvgWriteLatencyMs"])
                    };
                }
            }

            Dictionary<string, RawFileIOCounts> prev = null;
            double elapsedSec = 0;

            lock (_fileIOSampleLock)
            {
                if (_prevFileIOSamples.TryGetValue(instanceKey, out var entry))
                {
                    prev = entry.Sample;
                    elapsedSec = (capturedAt - entry.CapturedAt).TotalSeconds;
                }
                _prevFileIOSamples[instanceKey] = (current, capturedAt);
            }

            if (prev != null && elapsedSec >= 1)
            {
                foreach (var kv in fileMap)
                {
                    if (!prev.TryGetValue(kv.Key, out var p) || !current.TryGetValue(kv.Key, out var c))
                        continue;

                    kv.Value.ReadBytesPerSec  = Math.Round(Math.Max(0, c.BytesRead    - p.BytesRead)    / elapsedSec, 1);
                    kv.Value.WriteBytesPerSec = Math.Round(Math.Max(0, c.BytesWritten - p.BytesWritten) / elapsedSec, 1);

                    var dReads  = Math.Max(0, c.NumReads  - p.NumReads);
                    var dWrites = Math.Max(0, c.NumWrites - p.NumWrites);
                    var dStallR = Math.Max(0, c.IoStallReadMs  - p.IoStallReadMs);
                    var dStallW = Math.Max(0, c.IoStallWriteMs - p.IoStallWriteMs);

                    kv.Value.AvgReadLatencyMs  = dReads  > 0 ? Math.Round((decimal)dStallR / dReads,  2) : 0;
                    kv.Value.AvgWriteLatencyMs = dWrites > 0 ? Math.Round((decimal)dStallW / dWrites, 2) : 0;
                }
            }
            else
            {
                foreach (var f in fileMap.Values)
                {
                    f.ReadBytesPerSec  = -1;
                    f.WriteBytesPerSec = -1;
                }
            }

            var result = fileMap.Values.ToList();
            result.Sort((a, b) => (b.IoStallReadMs + b.IoStallWriteMs).CompareTo(a.IoStallReadMs + a.IoStallWriteMs));
            return result.Count > 20 ? result.GetRange(0, 20) : result;
        }

        // ─────────────────────────────────────────────────────────────────────
        // HOT TABLES  (DML rates via sys.dm_db_index_operational_stats)
        // ─────────────────────────────────────────────────────────────────────
        private List<HotTable> GetHotTables(SqlConnection conn, string instanceKey)
        {
            const string sql = @"
    SELECT
        ios.database_id,
        ios.object_id,
        DB_NAME(ios.database_id)                                    AS DatabaseName,
        OBJECT_SCHEMA_NAME(ios.object_id, ios.database_id)         AS SchemaName,
        OBJECT_NAME(ios.object_id, ios.database_id)                AS TableName,
        COUNT(DISTINCT ios.partition_number)                        AS PartitionCount,
        SUM(ios.leaf_insert_count)                                  AS TotalInserts,
        SUM(ios.leaf_update_count)                                  AS TotalUpdates,
        SUM(ios.leaf_delete_count)                                  AS TotalDeletes,
        SUM(CAST(ios.range_scan_count AS bigint)
          + CAST(ios.singleton_lookup_count AS bigint))             AS TotalReads,
        SUM(ios.row_lock_wait_count)                                AS RowLockWaits,
        SUM(ios.row_lock_wait_in_ms)                                AS RowLockWaitMs,
        SUM(ios.page_lock_wait_count)                               AS PageLockWaits,
        SUM(ios.page_lock_wait_in_ms)                               AS PageLockWaitMs
    FROM sys.dm_db_index_operational_stats(NULL, NULL, NULL, NULL) ios
    WHERE ios.database_id > 4
      AND OBJECT_NAME(ios.object_id, ios.database_id) IS NOT NULL
      AND OBJECT_SCHEMA_NAME(ios.object_id, ios.database_id) = 'dbo'
    GROUP BY ios.database_id, ios.object_id";

            var current = new Dictionary<string, RawTableCounts>();
            var nameMap = new Dictionary<string, (string Db, string Schema, string Table, int PartitionCount)>();
            var capturedAt = DateTime.UtcNow;

            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.CommandTimeout = 15;
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var dbId  = Convert.ToInt32(reader["database_id"]);
                        var objId = Convert.ToInt32(reader["object_id"]);
                        var key   = $"{dbId}:{objId}";

                        current[key] = new RawTableCounts
                        {
                            Inserts       = Convert.ToInt64(reader["TotalInserts"]),
                            Updates       = Convert.ToInt64(reader["TotalUpdates"]),
                            Deletes       = Convert.ToInt64(reader["TotalDeletes"]),
                            Reads         = Convert.ToInt64(reader["TotalReads"]),
                            RowLockWaits  = Convert.ToInt64(reader["RowLockWaits"]),
                            RowLockWaitMs = Convert.ToInt64(reader["RowLockWaitMs"]),
                            PageLockWaits  = Convert.ToInt64(reader["PageLockWaits"]),
                            PageLockWaitMs = Convert.ToInt64(reader["PageLockWaitMs"])
                        };
                        nameMap[key] = (
                            reader["DatabaseName"]?.ToString() ?? "",
                            reader["SchemaName"]?.ToString() ?? "",
                            reader["TableName"]?.ToString() ?? "",
                            Convert.ToInt32(reader["PartitionCount"])
                        );
                    }
                }
            }

            // Retrieve previous sample and update state atomically
            Dictionary<string, RawTableCounts> prev = null;
            double elapsedSec = 0;

            lock (_tableSampleLock)
            {
                if (_prevTableSamples.TryGetValue(instanceKey, out var entry))
                {
                    prev = entry.Sample;
                    elapsedSec = (capturedAt - entry.CapturedAt).TotalSeconds;
                }
                _prevTableSamples[instanceKey] = (current, capturedAt);
            }

            // First call for this instance — no delta available yet
            if (prev == null || elapsedSec < 1)
                return new List<HotTable>();

            // Populate partition cache once from the nameMap data already in hand (no extra query)
            lock (_partitionCacheLock)
            {
                if (!_partitionCacheLoaded.Contains(instanceKey))
                {
                    var partitioned = new HashSet<string>();
                    foreach (var kv in nameMap)
                        if (kv.Value.PartitionCount > 1)
                            partitioned.Add(kv.Key);
                    _partitionedTableCache[instanceKey] = partitioned;
                    _partitionCacheLoaded.Add(instanceKey);
                }
            }

            HashSet<string> partitionedKeys;
            lock (_partitionCacheLock)
                _partitionedTableCache.TryGetValue(instanceKey, out partitionedKeys);
            partitionedKeys ??= new HashSet<string>();

            var results = new List<HotTable>();

            foreach (var kvp in current)
            {
                if (!prev.TryGetValue(kvp.Key, out var prevCounts))
                    continue;

                var cur = kvp.Value;
                var dIns  = Math.Max(0, cur.Inserts - prevCounts.Inserts);
                var dUpd  = Math.Max(0, cur.Updates - prevCounts.Updates);
                var dDel  = Math.Max(0, cur.Deletes - prevCounts.Deletes);
                var dRead = Math.Max(0, cur.Reads   - prevCounts.Reads);
                var totalDml = dIns + dUpd + dDel;

                if (totalDml == 0 && dRead == 0) continue;

                var dRowWaits  = Math.Max(0, cur.RowLockWaits  - prevCounts.RowLockWaits);
                var dRowWaitMs = Math.Max(0, cur.RowLockWaitMs - prevCounts.RowLockWaitMs);
                var dPageWaits  = Math.Max(0, cur.PageLockWaits  - prevCounts.PageLockWaits);
                var dPageWaitMs = Math.Max(0, cur.PageLockWaitMs - prevCounts.PageLockWaitMs);

                var (db, schema, table, _) = nameMap[kvp.Key];
                var ht = new HotTable
                {
                    DatabaseName   = db,
                    SchemaName     = schema,
                    TableName      = table,
                    IsPartitioned  = partitionedKeys.Contains(kvp.Key),
                    InsertsPerSec  = Math.Round(dIns  / elapsedSec, 2),
                    UpdatesPerSec  = Math.Round(dUpd  / elapsedSec, 2),
                    DeletesPerSec  = Math.Round(dDel  / elapsedSec, 2),
                    ReadsPerSec    = Math.Round(dRead  / elapsedSec, 2),
                    TotalDmlPerSec = Math.Round(totalDml / elapsedSec, 2),
                    AvgRowLockWaitMs  = dRowWaits  > 0 ? Math.Round((double)dRowWaitMs  / dRowWaits,  2) : 0,
                    AvgPageLockWaitMs = dPageWaits > 0 ? Math.Round((double)dPageWaitMs / dPageWaits, 2) : 0
                };
                results.Add(ht);
            }

            results.Sort((a, b) => b.TotalDmlPerSec.CompareTo(a.TotalDmlPerSec));
            return results.Count > 20 ? results.GetRange(0, 20) : results;
        }

        // ─────────────────────────────────────────────────────────────────────
        // PARTITION INFO
        // ─────────────────────────────────────────────────────────────────────
        public PartitionInfo GetPartitionInfo(string instanceKey, string databaseName,
                                              string schemaName, string tableName)
        {
            var instance = _instances.FirstOrDefault(i => i.Key == instanceKey);
            if (instance == null)
                return new PartitionInfo { ErrorMessage = "Instance not found." };

            var csb = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(instance.ConnectionString)
            {
                InitialCatalog = databaseName
            };

            var result = new PartitionInfo
            {
                DatabaseName = databaseName,
                SchemaName   = schemaName,
                TableName    = tableName
            };

            const string sql = @"
    SELECT
        pf.name                                         AS PartitionFunction,
        ps.name                                         AS PartitionScheme,
        c.name                                          AS PartitionColumn,
        p.partition_number                              AS PartitionNumber,
        p.[rows]                                        AS PartitionRows,
        CAST(prv_left.value  AS nvarchar(200))          AS RangeStart,
        CAST(prv_right.value AS nvarchar(200))          AS RangeEnd,
        p.data_compression_desc                         AS Compression
    FROM sys.indexes i
    JOIN sys.partition_schemes  ps  ON i.data_space_id   = ps.data_space_id
    JOIN sys.partition_functions pf ON ps.function_id    = pf.function_id
    JOIN sys.index_columns      ic  ON i.object_id       = ic.object_id
                                    AND i.index_id       = ic.index_id
                                    AND ic.partition_ordinal > 0
    JOIN sys.columns            c   ON ic.object_id      = c.object_id
                                    AND ic.column_id     = c.column_id
    JOIN sys.partitions         p   ON i.object_id       = p.object_id
                                    AND i.index_id       = p.index_id
    LEFT JOIN sys.partition_range_values prv_left
        ON prv_left.function_id  = pf.function_id
       AND prv_left.boundary_id  = p.partition_number - 1
    LEFT JOIN sys.partition_range_values prv_right
        ON prv_right.function_id = pf.function_id
       AND prv_right.boundary_id = p.partition_number
    WHERE i.object_id = OBJECT_ID(@fqn)
      AND i.index_id IN (0, 1)
    ORDER BY p.partition_number";

            try
            {
                using (var conn = new SqlConnection(csb.ConnectionString))
                {
                    conn.Open();
                    using (var cmd = new SqlCommand(sql, conn) { CommandTimeout = 15 })
                    {
                        cmd.Parameters.AddWithValue("@fqn", $"[{schemaName}].[{tableName}]");
                        using (var reader = cmd.ExecuteReader())
                        {
                            bool first = true;
                            while (reader.Read())
                            {
                                if (first)
                                {
                                    result.PartitionFunction = reader["PartitionFunction"]?.ToString() ?? "";
                                    result.PartitionScheme   = reader["PartitionScheme"]?.ToString()   ?? "";
                                    result.PartitionColumn   = reader["PartitionColumn"]?.ToString()   ?? "";
                                    first = false;
                                }
                                result.Ranges.Add(new PartitionRange
                                {
                                    PartitionNumber = Convert.ToInt32(reader["PartitionNumber"]),
                                    RowCount        = Convert.ToInt64(reader["PartitionRows"]),
                                    RangeStart      = reader["RangeStart"]  == DBNull.Value ? null : reader["RangeStart"].ToString(),
                                    RangeEnd        = reader["RangeEnd"]    == DBNull.Value ? null : reader["RangeEnd"].ToString(),
                                    Compression     = reader["Compression"]?.ToString() ?? ""
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
            }

            return result;
        }
    }
}



