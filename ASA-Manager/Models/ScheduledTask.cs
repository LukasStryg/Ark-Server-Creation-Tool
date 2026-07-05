using System;

namespace ARKServerCreationTool.Models
{
    public enum ScheduledTaskType { Restart, Backup }
    public enum ScheduleTargetKind { Server, Cluster, All }
    public enum ScheduleMode { DailyAtTime, EveryNHours }

    /// <summary>A recurring reliability action (restart or backup) evaluated by the ReliabilityCoordinator.</summary>
    public class ScheduledTask
    {
        public int Id { get; set; }
        public bool Enabled { get; set; } = true;

        public ScheduledTaskType Type { get; set; }
        public ScheduleTargetKind TargetKind { get; set; }
        public int? TargetServerId { get; set; }        // when TargetKind == Server
        public string? TargetClusterKey { get; set; }   // when TargetKind == Cluster

        public ScheduleMode Mode { get; set; }
        public TimeSpan DailyTime { get; set; }         // when Mode == DailyAtTime
        public int IntervalHours { get; set; } = 6;     // when Mode == EveryNHours

        public DateTime? LastRun { get; set; }
    }
}
