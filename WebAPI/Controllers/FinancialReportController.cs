using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebAPI.Data;
using WebAPI.Models;

namespace WebAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class FinancialReportController : ControllerBase
    {
        private readonly DataContext _context;

        public FinancialReportController(DataContext context)
        {
            _context = context;
        }

        // GET: api/FinancialReport
        [HttpGet]
        public async Task<ActionResult<IEnumerable<FinancialReport>>> GetFinancialReports(
            [FromQuery] string? search,
            [FromQuery] string? sortField,
            [FromQuery] string? sortOrder,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10)
        {
            var query = _context.FinancialReports.AsQueryable();

            // Apply search filter
            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(r => 
                    r.Title.Contains(search) ||
                    r.Description.Contains(search) ||
                    r.Income.ToString().Contains(search) ||
                    r.Expense.ToString().Contains(search) ||
                    r.Balance.ToString().Contains(search));
            }

            // Apply sorting
            if (!string.IsNullOrEmpty(sortField))
            {
                sortOrder = sortOrder?.ToLower() == "desc" ? "desc" : "asc";
                
                query = sortField.ToLower() switch
                {
                    "title" => sortOrder == "asc" 
                        ? query.OrderBy(r => r.Title)
                        : query.OrderByDescending(r => r.Title),
                    "description" => sortOrder == "asc"
                        ? query.OrderBy(r => r.Description)
                        : query.OrderByDescending(r => r.Description),
                    "income" => sortOrder == "asc"
                        ? query.OrderBy(r => r.Income)
                        : query.OrderByDescending(r => r.Income),
                    "expense" => sortOrder == "asc"
                        ? query.OrderBy(r => r.Expense)
                        : query.OrderByDescending(r => r.Expense),
                    "balance" => sortOrder == "asc"
                        ? query.OrderBy(r => r.Balance)
                        : query.OrderByDescending(r => r.Balance),
                    "reportdate" => sortOrder == "asc"
                        ? query.OrderBy(r => r.ReportDate)
                        : query.OrderByDescending(r => r.ReportDate),
                    _ => query.OrderByDescending(r => r.ReportDate)
                };
            }
            else
            {
                query = query.OrderByDescending(r => r.ReportDate);
            }

            // Pagination
            var totalRecords = await query.CountAsync();
            var reports = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var result = new
            {
                TotalRecords = totalRecords,
                Page = page,
                PageSize = pageSize,
                Data = reports
            };

            Console.WriteLine($"API Response: {System.Text.Json.JsonSerializer.Serialize(result)}");
            return Ok(result);
        }

        // GET: api/FinancialReport/export
        [HttpGet("export")]
        public async Task<IActionResult> ExportFinancialReports()
        {
            var reports = await _context.FinancialReports.ToListAsync();
            var csv = new System.Text.StringBuilder();
            csv.AppendLine("Id,ReportDate,Title,Description,Income,Expense,Balance");

            foreach (var report in reports)
            {
                csv.AppendLine($"{report.Id},{report.ReportDate:yyyy-MM-dd},{report.Title},{report.Description},{report.Income},{report.Expense},{report.Balance}");
            }

            return File(System.Text.Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", "financial_reports.csv");
        }

        // POST: api/FinancialReport/import
        [HttpPost("import")]
        public async Task<IActionResult> ImportFinancialReports(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest("No file uploaded");
            }

            using var reader = new StreamReader(file.OpenReadStream());
            var header = await reader.ReadLineAsync();
            if (header != "Id,ReportDate,Title,Description,Income,Expense,Balance")
            {
                return BadRequest("Invalid file format");
            }

            var reports = new List<FinancialReport>();
            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync();
                var values = line.Split(',');

                var report = new FinancialReport
                {
                    ReportDate = DateTime.Parse(values[1]),
                    Title = values[2],
                    Description = values[3],
                    Income = decimal.Parse(values[4]),
                    Expense = decimal.Parse(values[5])
                };

                reports.Add(report);
            }

            await _context.FinancialReports.AddRangeAsync(reports);
            await _context.SaveChangesAsync();

            return Ok(new { Message = $"{reports.Count} records imported successfully" });
        }

        // GET: api/FinancialReport/5
        [HttpGet("{id}")]
        public async Task<ActionResult<FinancialReport>> GetFinancialReport(int id)
        {
            var financialReport = await _context.FinancialReports.FindAsync(id);

            if (financialReport == null)
            {
                return NotFound();
            }

            return financialReport;
        }

        // POST: api/FinancialReport
        [HttpPost]
        public async Task<ActionResult<FinancialReport>> CreateFinancialReport(FinancialReport financialReport)
        {
            financialReport.ReportDate = DateTime.UtcNow;
            _context.FinancialReports.Add(financialReport);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetFinancialReport), new { id = financialReport.Id }, financialReport);
        }

        // PUT: api/FinancialReport/5
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateFinancialReport(int id, FinancialReport financialReport)
        {
            if (id != financialReport.Id)
            {
                return BadRequest();
            }

            _context.Entry(financialReport).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!FinancialReportExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return NoContent();
        }

        // DELETE: api/FinancialReport/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteFinancialReport(int id)
        {
            var financialReport = await _context.FinancialReports.FindAsync(id);
            if (financialReport == null)
            {
                return NotFound();
            }

            _context.FinancialReports.Remove(financialReport);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool FinancialReportExists(int id)
        {
            return _context.FinancialReports.Any(e => e.Id == id);
        }
    }
}
