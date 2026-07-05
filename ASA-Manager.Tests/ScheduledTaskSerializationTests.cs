using System;
using Newtonsoft.Json;
using ARKServerCreationTool.Models;
using Xunit;

namespace ARKServerCreationTool.Tests
{
    public class ScheduledTaskSerializationTests
    {
        [Fact]
        public void ScheduledTask_round_trips_through_json()
        {
            var task = new ScheduledTask
            {
                Id = 3,
                Enabled = true,
                Type = ScheduledTaskType.Restart,
                TargetKind = ScheduleTargetKind.Cluster,
                TargetClusterKey = "west",
                Mode = ScheduleMode.DailyAtTime,
                DailyTime = new TimeSpan(5, 0, 0),
                LastRun = null,
            };

            var json = JsonConvert.SerializeObject(task);
            var back = JsonConvert.DeserializeObject<ScheduledTask>(json)!;

            Assert.Equal(ScheduledTaskType.Restart, back.Type);
            Assert.Equal(ScheduleTargetKind.Cluster, back.TargetKind);
            Assert.Equal("west", back.TargetClusterKey);
            Assert.Equal(new TimeSpan(5, 0, 0), back.DailyTime);
            Assert.True(back.Enabled);
        }

        [Fact]
        public void New_task_defaults_are_enabled_with_no_lastrun()
        {
            var task = new ScheduledTask();
            Assert.True(task.Enabled);
            Assert.Null(task.LastRun);
        }
    }
}
