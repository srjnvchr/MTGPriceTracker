using Microsoft.AspNetCore.Mvc;
using MTGPriceTracker.Server.Services;  // Ensure this namespace is included
using MTGPriceTracker.Shared;  // Ensure this namespace is included for CardPrice class

namespace MTGPriceTracker.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CardPriceController : ControllerBase
    {
        private readonly CardPriceService _cardPriceService;

        public CardPriceController(CardPriceService cardPriceService)
        {
            _cardPriceService = cardPriceService;
        }

        // Dummy endpoint to call the dummy method
        [HttpGet("dummy")]
        public async Task<ActionResult<List<CardPrice>>> GetDummyCardPrices()
        {
            var dummyPrices = await _cardPriceService.GetDummyCardPricesAsync();
            return Ok(dummyPrices);
        }

        // Additional methods for your CardPrice API can go here
    }
}
