using System;

namespace WebAPI.Models
{
    public class Inventory
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public int Quantity { get; set; }
        public decimal Price { get; set; }
        public string Category { get; set; }
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
        public bool IsActive { get; set; } = true;
    }
}
