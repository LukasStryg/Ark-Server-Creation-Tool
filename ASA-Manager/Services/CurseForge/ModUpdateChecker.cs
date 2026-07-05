using System.Linq;

namespace ARKServerCreationTool.Services.CurseForge
{
    public static class ModUpdateChecker
    {
        /// <summary>The newest file for a mod, by fileDate (null if none).</summary>
        public static CfFile? NewestFile(CfMod mod)
            => mod.LatestFiles.OrderByDescending(f => f.FileDate).FirstOrDefault();

        /// <summary>True if the mod's newest file id differs from the recorded snapshot (or the snapshot is missing).</summary>
        public static bool HasNewerFile(CfMod mod, long? snapshotFileId)
        {
            var newest = NewestFile(mod);
            if (newest == null) return false;
            return snapshotFileId == null || newest.Id != snapshotFileId.Value;
        }
    }
}
