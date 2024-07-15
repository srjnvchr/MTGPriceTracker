using MTGPriceTracker.Shared; // Ensure this namespace is included for CardPrice class
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MTGPriceTracker.Server.Services
{
    public class CardPriceService
    {
        // Existing methods and constructor here

        // Dummy method for testing
        public async Task<List<CardPrice>> GetDummyCardPricesAsync()
        {
            // Create some dummy data
            var dummyCardPrices = new List<CardPrice>
            {
                new CardPrice { CardId = 1, CardName = "Black Lotus", Price = 5000.00m, Date = DateTime.Now },
                new CardPrice { CardId = 2, CardName = "Mox Sapphire", Price = 3000.00m, Date = DateTime.Now },
                new CardPrice { CardId = 3, CardName = "Ancestral Recall", Price = 1500.00m, Date = DateTime.Now }
            };

            // Return the dummy data
            return await Task.FromResult(dummyCardPrices);
        }
    }
}
