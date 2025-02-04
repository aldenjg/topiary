using System.Collections.Generic;
using System.Threading.Tasks;
using Topiary.Models;

namespace Topiary.Services
{
    public interface IAIAnalysisService
    {
        Task<List<DiskInsight>> AnalyzeDiskUsageAsync(FileSystemEntry rootEntry);
    }
}