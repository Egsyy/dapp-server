using dengue.watch.api.infrastructure.ml;
using dengue.watch.api.common.repositories;
using Microsoft.AspNetCore.Http.HttpResults;

namespace dengue.watch.api.features.trainingdatapipeline.endpoints;

public class ManualTriggerAdvancePrediction : IEndpoint
{
    public static IEndpointRouteBuilder MapEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("training-data")
            .WithTags("Training Data Pipeline")
            .WithSummary("Manual trigger for advance prediction coordinator");

        group.MapPost("/advance/manual-trigger", Handler)
            .Produces<Results<Ok<ManualTriggerResponse>, BadRequest<string>, ProblemHttpResult>>();

        return group;
    }

    public record ManualTriggerRequest(string PsgcCode, int AggregatedYear, int AggregatedWeek);
    
    public record ManualTriggerResponse(
        bool IsSuccess,
        List<PredictionResultRecord> Results,
        List<PredictionErrorRecord> Errors,
        string Message);

    private static async Task<Results<Ok<ManualTriggerResponse>, BadRequest<string>, ProblemHttpResult>> Handler(
        [FromBody] ManualTriggerRequest request, 
        [FromServices] IPredictionCoordinator coordinator, 
        CancellationToken cancellation = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.PsgcCode))
                return TypedResults.BadRequest("PsgcCode is required");

            if (request.AggregatedWeek < 1 || request.AggregatedWeek > 53)
                return TypedResults.BadRequest("AggregatedWeek must be between 1 and 53");

            var result = await coordinator.RunForPsgcAsync(request.PsgcCode, request.AggregatedYear, request.AggregatedWeek, cancellation);
            
            var message = result.IsSuccess 
                ? $"Successfully processed {result.Results.Count} predictions for {request.PsgcCode}"
                : $"Processing completed with {result.Errors.Count} error(s) for {request.PsgcCode}";

            var response = new ManualTriggerResponse(
                result.IsSuccess,
                result.Results,
                result.Errors,
                message);
            
            return TypedResults.Ok(response);
        }
        catch (ValidationException ve)
        {
            return TypedResults.BadRequest(ve.Message);
        }
        catch (Exception e)
        {
            return TypedResults.Problem($"Unexpected error: {e.Message}");
        }
    }
}
