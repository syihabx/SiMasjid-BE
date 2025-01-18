using System;
using System.ComponentModel.DataAnnotations;

namespace WebAPI.Models
{
    public class DailyTask
    {
        [Key]
        public int Id { get; set; }
        
        [Required]
        [StringLength(100)]
        public string Title { get; set; }
        
        [StringLength(500)]
        public string Description { get; set; }
        
        [Required]
        public DateTime DueDate { get; set; }
        
        [Required]
        public bool Completed { get; set; }
    }
}
