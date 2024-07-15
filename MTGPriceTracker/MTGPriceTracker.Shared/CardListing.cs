using System;

namespace MTGPriceTracker.Shared
{
    public class CardListing
    {
        public string CardId { get; set; } // Unique identifier for the card
        public string CardName { get; set; }  // Name of the card
        public string CardSet { get; set; }  // The set the card belongs to
        public string CardRarity { get; set; }  // The rarity of the card
        public decimal Price { get; set; }  // The price of the card
        public DateTime Date { get; set; }  // Date of the price listing
        public string Vendor { get; set; }  // Vendor name or URL where the price was found
    }
}
