using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace dengue.watch.api.features.denguecases.queries;

/// <summary>
/// Get heatmap data: barangays with their coordinates and aggregated weekly dengue case counts
/// </summary>
public class GetHeatmapData : IEndpoint
{
    public static IEndpointRouteBuilder MapEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("dengue-cases")
            .WithTags("Dengue Cases")
            .WithSummary("Get Heatmap Data for all barangays with coordinates and case counts");

        group.MapGet("heatmap", Handler)
            .Produces<List<HeatmapBarangayData>>(200)
            .Produces(500);

        return group;
    }

    public record HeatmapBarangayData(
        string PsgcCode,
        string BarangayName,
        decimal Latitude,
        decimal Longitude,
        int TotalCases,
        int CurrentYearCases,
        string RiskLevel
    );

    private static async Task<Results<Ok<List<HeatmapBarangayData>>, ProblemHttpResult>> Handler(
        [FromServices] ApplicationDbContext _db,
        [FromServices] ILogger<GetHeatmapData> logger,
        int? year = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var targetYear = year ?? DateTime.Now.Year;

            var heatmapData = await _db.WeeklyDengueCases
                .Include(w => w.AdministrativeArea)
                .Where(w => w.AdministrativeArea != null
                    && w.AdministrativeArea.Latitude != null
                    && w.AdministrativeArea.Longitude != null)
                .GroupBy(w => new
                {
                    w.PsgcCode,
                    w.AdministrativeArea!.Name,
                    w.AdministrativeArea.Latitude,
                    w.AdministrativeArea.Longitude
                })
                .Select(g => new
                {
                    g.Key.PsgcCode,
                    BarangayName = g.Key.Name,
                    Latitude = g.Key.Latitude!.Value,
                    Longitude = g.Key.Longitude!.Value,
                    TotalCases = g.Sum(x => x.CaseCount),
                    CurrentYearCases = g.Where(x => x.Year == targetYear).Sum(x => x.CaseCount)
                })
                .ToListAsync(cancellationToken);

            // Calculate risk levels based on current year case distribution
            var maxCases = heatmapData.Max(x => x.CurrentYearCases);
            var result = heatmapData.Select(d =>
            {
                var riskLevel = maxCases > 0
                    ? d.CurrentYearCases switch
                    {
                        var c when c >= maxCases * 0.75 => "Critical",
                        var c when c >= maxCases * 0.50 => "High",
                        var c when c >= maxCases * 0.25 => "Medium",
                        _ => "Low"
                    }
                    : "Low";

                return new HeatmapBarangayData(
                    d.PsgcCode,
                    d.BarangayName,
                    d.Latitude,
                    d.Longitude,
                    d.TotalCases,
                    d.CurrentYearCases,
                    riskLevel
                );
            }).ToList();

            return TypedResults.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to retrieve heatmap data");
            return TypedResults.Problem(ex.Message, statusCode: 500);
        }
    }
}
