using System.Collections.Generic;
using System.Linq;

namespace ARKServerCreationTool.Services.Reliability
{
    /// <summary>Pure retention policy: given timestamp-named backup folders, return which to delete.</summary>
    public static class BackupRotation
    {
        public static IReadOnlyList<string> ToDelete(IEnumerable<string> folderNames, int keepCount)
        {
            if (keepCount < 0) keepCount = 0;
            return folderNames
                .OrderByDescending(n => n)   // newest (largest timestamp string) first
                .Skip(keepCount)
                .ToList();
        }
    }
}
