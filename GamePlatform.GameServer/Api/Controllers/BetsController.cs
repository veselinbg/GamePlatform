using GamePlatform.GameServer.Application.Bets;
using Microsoft.AspNetCore.Mvc;

namespace GamePlatform.GameServer.Api.Controllers;

[ApiController]
[Route("api/bets")]
public sealed class BetsController : ControllerBase
{
    private readonly PlaceBetService _service;
    private readonly ILogger<BetsController> _logger;

    public BetsController(
        PlaceBetService service,
        ILogger<BetsController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> PlaceBet(
        [FromBody] PlaceBetRequest request,
        CancellationToken cancellationToken)
    {
        var requestId =
            Request.Headers["X-Request-Id"].FirstOrDefault()
            ?? Guid.NewGuid().ToString("N");

        var idempotencyKey =
            Request.Headers["Idempotency-Key"].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return BadRequest(new
            {
                error = "Idempotency-Key header is required.",
                requestId
            });
        }

        try
        {
            var result = await _service.ExecuteAsync(
                request,
                idempotencyKey,
                cancellationToken);

            _logger.LogInformation(
                "Bet processed. RequestId={RequestId}, BetId={BetId}, OperationId={OperationId}",
                requestId,
                result.BetId,
                result.OperationId);

            return Ok(result);
        }
        catch (InsufficientBalanceException ex)
        {
            return Conflict(new
            {
                error = ex.Message,
                requestId,
                walletId = ex.WalletId
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new
            {
                error = ex.Message,
                requestId
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                error = ex.Message,
                requestId
            });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new
            {
                error = ex.Message,
                requestId
            });
        }

    }
}
