using MTGPriceTracker.Shared;
using System.Net.Http.Json;

namespace MTGPriceTracker.Client.Services
{
    public class CardPriceService
    {
        private readonly HttpClient _httpClient;

        public CardPriceService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }


        public async Task<List<CardPrice>> GetCardPricesAsync(int cardId)
        {
            return await _httpClient.GetFromJsonAsync<List<CardPrice>>($"api/cardprices/{cardId}");
        }
    }
}
