using System;
using System.ComponentModel.DataAnnotations;

namespace WebAPI.Models
{
    public class FinancialReport
    {
        [Key]
        public int Id { get; set; }
        
        [Required]
        public DateTime ReportDate { get; set; }
        
        [Required]
        [StringLength(100)]
        public string Title { get; set; }
        
        [Required]
        public string Description { get; set; }
        
        [Required]
        [Range(0, double.MaxValue)]
        public decimal Income { get; set; }
        
        [Required]
        [Range(0, double.MaxValue)]
        public decimal Expense { get; set; }
        
        public decimal Balance => Income - Expense;
    }
}
