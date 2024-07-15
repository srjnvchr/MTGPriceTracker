namespace MTGPriceTracker.Shared
{
    public class CardPrice
    {
        public int CardId { get; set; }
        public string CardName { get; set; }
        public decimal Price { get; set; }
        public DateTime Date { get; set; }
    }
}
