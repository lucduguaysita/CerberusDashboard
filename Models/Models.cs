using System;
using System.Collections.Generic;

namespace CerberusDashboard.Models
{
    /// <summary>
    /// Represents a configured SQL Server instance to monitor
    /// </summary>
    public class SqlInstance
    {
        public string Key { get; set; }           // e.g. SQLMON_DEFAULT
        public string DisplayName { get; set; }   // e.g. DEFAULT
        public string ConnectionString { get; set; }
        public bool IsReachable { get; set; }
        public string ErrorMessage { get; set; }
        public DateTime LastChecked { get; set; }
    }

    /// <summary>
    /// A complete snapshot for one SQL Server instance
    /// </summary>
    public class InstanceSnapshot
    {
        public string InstanceKey { get; set; }
        public string DisplayName { get; set; }
        public bool IsOnline { get; set; }
        public string ErrorMessage { get; set; }
        public DateTime SnapshotTime { get; set; }
        public ServerInfo ServerInfo { get; set; }
        public PerformanceMetrics Performance { get; set; }
        public List<JobSummary> Jobs { get; set; }
        public List<ActiveSession> BlockedSessions { get; set; }
        public List<TopQuery> TopQueries { get; set; }
        public List<DatabaseInfo> Databases { get; set; }
        public List<RecentQuery> RecentQueries { get; set; }
        public List<ExpensiveQuery> ExpensiveQueries { get; set; }
        public List<DataFileIO> DataFileIO { get; set; }
        public string DataFileIOError { get; set; }
        public List<HotTable> HotTables { get; set; }
        public string HotTablesError { get; set; }

        public InstanceSnapshot()
        {
            Jobs = new List<JobSummary>();
            BlockedSessions = new List<ActiveSession>();
            TopQueries = new List<TopQuery>();
            Databases = new List<DatabaseInfo>();
            RecentQueries = new List<RecentQuery>();
            ExpensiveQueries = new List<ExpensiveQuery>();
            DataFileIO = new List<DataFileIO>();
            HotTables = new List<HotTable>();
        }
    }

    /// <summary>
    /// Full dashboard payload sent via SignalR to all clients
    /// </summary>
    public class DashboardSnapshot
    {
        public DateTime GeneratedAt { get; set; }
        public List<InstanceSnapshot> Instances { get; set; }
        public int RefreshIntervalSeconds { get; set; }

        public DashboardSnapshot()
        {
            Instances = new List<InstanceSnapshot>();
            GeneratedAt = DateTime.Now;
        }
    }

    public class ServerInfo
    {
        public string ServerVersion { get; set; }
        public string Edition { get; set; }
        public Int64 CpuCount { get; set; }
        public decimal PhysicalMemoryMb { get; set; }
        public decimal SqlMemoryUsedMb { get; set; }
        public DateTime SqlStartTime { get; set; }
        public string Collation { get; set; }
    }

    public class PerformanceMetrics
    {
        public Int64 PageLifeExpectancy { get; set; }       // seconds — healthy > 300
        public decimal BatchRequestsPerSec { get; set; }
        public decimal CpuUsagePercent { get; set; }
        public decimal BufferCacheHitRatio { get; set; }  // percent — healthy > 95
        public Int64 ActiveConnections { get; set; }
        public Int64 BlockedProcesses { get; set; }
        public decimal TotalLogicalReadsPerSec { get; set; }
        public decimal TotalPhysicalReadsPerSec { get; set; }
        public decimal TotalWritesPerSec { get; set; }
        public string TopWaitType { get; set; }
        public decimal TopWaitMs { get; set; }
    }

    public class JobSummary
    {
        public Guid JobId { get; set; }
        public string JobName { get; set; }
        public bool Enabled { get; set; }
        public Int64 LastRunStatus { get; set; }  // 0=failed,1=succeeded,2=retry,3=cancelled,5=unknown
        public string LastRunStatusText => LastRunStatus == 1 ? "Succeeded"
                                         : LastRunStatus == 0 ? "Failed"
                                         : LastRunStatus == 2 ? "Retry"
                                         : LastRunStatus == 3 ? "Cancelled"
                                         : LastRunStatus == 4 ? "Running"
                                         : "Unknown";
        public DateTime? LastRunDateTime { get; set; }
        public Int64 LastRunDurationSeconds { get; set; }
        public DateTime? NextRunDateTime { get; set; }
        public string Category { get; set; }
        public string LastStepMessage { get; set; }

        // For colour coding
        public string StatusClass => LastRunStatus == 1 ? "status-ok"
                                   : LastRunStatus == 0 ? "status-fail"
                                   : LastRunStatus == 4 ? "status-running"
                                   : !Enabled ? "status-disabled"
                                   : "status-unknown";
    }

    public class ActiveSession
    {
        public Int64 SessionId { get; set; }
        public Int64 BlockingSessionId { get; set; }
        public string DatabaseName { get; set; }
        public string LoginName { get; set; }
        public string HostName { get; set; }
        public string ProgramName { get; set; }      // NEW
        public string WaitType { get; set; }
        public Int64 WaitTimeMs { get; set; }
        public string SqlText { get; set; }
        public string Status { get; set; }
        public Int64 CpuTime { get; set; }
        public Int64 LogicalReads { get; set; }
        public Int64 Writes { get; set; }             // NEW
        public Int64 ElapsedMs { get; set; }          // NEW
        public DateTime? LastRequestStart { get; set; } // NEW
        public DateTime? LoginTime { get; set; }      // NEW
        public int OpenTransactions { get; set; }     // NEW
        public Int64 MemoryKb { get; set; }           // NEW
    }

    public class TopQuery
    {
        public string QueryText { get; set; }
        public Int64 ExecutionCount { get; set; }
        public decimal AvgCpuMs { get; set; }
        public decimal AvgElapsedMs { get; set; }
        public decimal AvgLogicalReads { get; set; }
        public decimal TotalCpuMs { get; set; }
        public string DatabaseName { get; set; }
    }

    public class DatabaseInfo
    {
        public string Name { get; set; }
        public string Status { get; set; }
        public string RecoveryModel { get; set; }
        public string DataDrive { get; set; }
        public decimal SizeMb { get; set; }
        public decimal LogSizeMb { get; set; }
        public decimal LogUsedPercent { get; set; }
        public DateTime? LastBackupDate { get; set; }
        public DateTime? LastLogBackupDate { get; set; }
        public bool HasRecentBackup => LastBackupDate.HasValue && LastBackupDate.Value > DateTime.Now.AddDays(-1);
    }
    public class JobHistory
    {
        public Int64 InstanceId { get; set; }
        public Int64 StepId { get; set; }
        public string StepName { get; set; }
        public string RunStatus { get; set; }
        public DateTime RunDateTime { get; set; }
        public Int64 DurationSeconds { get; set; }
        public string Message { get; set; }
        public Int64 RetriesAttempted { get; set; }
    }
    public class QueryPlan
    {
        public string QueryText { get; set; }
        public string PlanXml { get; set; }
        public string ErrorMessage { get; set; }
    }
    public class RecentQuery
    {
        public string QueryText { get; set; }
        //public decimal TotalElapsedMs { get; set; }
        public decimal AvgElapsedMs { get; set; }
        public string DatabaseName { get; set; }
        public decimal TotalCpuMs { get; set; }
        public decimal AvgCpuMs { get; set; }
        public long ExecutionCount { get; set; }
        public DateTime LastExecuted { get; set; }
        public string PlanHandle { get; set; }
    }

    public class ExpensiveQuery
    {
        public string QueryText { get; set; }
        public long ExecutionCount { get; set; }
        public decimal TotalCpuMs { get; set; }
        public decimal AvgCpuMs { get; set; }
        //public decimal TotalElapsedMs { get; set; }
        public decimal AvgElapsedMs { get; set; }
        public decimal TotalLogicalReads { get; set; }
        public decimal AvgLogicalReads { get; set; }
        public DateTime LastExecutionTime { get; set; }
        public string DatabaseName { get; set; }
        public string PlanHandle { get; set; }
    }

    public class DataFileIO
    {
        public string DatabaseName { get; set; }
        public string FileName { get; set; }
        public string FileType { get; set; }
        public decimal SizeMb { get; set; }
        public long NumReads { get; set; }
        public long NumWrites { get; set; }
        public decimal IoStallReadMs { get; set; }
        public decimal IoStallWriteMs { get; set; }
        public decimal AvgReadLatencyMs { get; set; }
        public decimal AvgWriteLatencyMs { get; set; }
        public double ReadBytesPerSec { get; set; }
        public double WriteBytesPerSec { get; set; }
    }

    public class HotTable
    {
        public string DatabaseName { get; set; }
        public string SchemaName { get; set; }
        public string TableName { get; set; }
        public bool IsPartitioned { get; set; }
        public double InsertsPerSec { get; set; }
        public double UpdatesPerSec { get; set; }
        public double DeletesPerSec { get; set; }
        public double ReadsPerSec { get; set; }
        public double TotalDmlPerSec { get; set; }
        public double AvgRowLockWaitMs { get; set; }
        public double AvgPageLockWaitMs { get; set; }
    }

    public class PartitionInfo
    {
        public string DatabaseName { get; set; }
        public string SchemaName { get; set; }
        public string TableName { get; set; }
        public string PartitionFunction { get; set; }
        public string PartitionScheme { get; set; }
        public string PartitionColumn { get; set; }
        public string ErrorMessage { get; set; }
        public List<PartitionRange> Ranges { get; set; } = new List<PartitionRange>();
    }

    public class PartitionRange
    {
        public int PartitionNumber { get; set; }
        public long RowCount { get; set; }
        public string RangeStart { get; set; }
        public string RangeEnd { get; set; }
        public string Compression { get; set; }
    }
}

