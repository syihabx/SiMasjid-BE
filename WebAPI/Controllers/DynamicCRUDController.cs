using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System.ComponentModel.DataAnnotations.Schema;
using WebAPI.Data;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;
using System.Reflection;
using WebAPI.Extensions;

namespace WebAPI.Controllers
{
    [Route("api/[controller]/{tableName}")]
    [ApiController]
    public class DynamicCRUDController : ControllerBase
    {
        private readonly DataContext _context;
        private readonly ILogger<DynamicCRUDController> _logger;

        public DynamicCRUDController(DataContext context, ILogger<DynamicCRUDController> logger)
        {
            _context = context;
            _logger = logger;
        }

        private (Type ModelType, dynamic DbSet) GetDbSet(string tableName)
        {
            // Get all DbSet properties
            var dbSetProperties = _context.GetType().GetProperties()
                .Where(p => p.PropertyType.IsGenericType && 
                           p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
                .ToList();

            // Find matching DbSet by name
            // Check both DbSet name and model name
            var property = dbSetProperties
                .FirstOrDefault(p => p.Name.Equals(tableName, StringComparison.OrdinalIgnoreCase) ||
                                   p.Name.Equals(tableName + "s", StringComparison.OrdinalIgnoreCase) ||
                                   p.PropertyType.GetGenericArguments()[0].Name.Equals(tableName, StringComparison.OrdinalIgnoreCase));
            
            if (property == null)
            {
                var availableTables = dbSetProperties
                    .Select(p => $"{p.Name} (Model: {p.PropertyType.GetGenericArguments()[0].Name})")
                    .ToList();
                    
                throw new ArgumentException(
                    $"Table '{tableName}' not found. Available tables:\n" +
                    string.Join("\n", availableTables)
                );
            }

            // Get the generic type argument (model type)
            var modelType = property.PropertyType.GetGenericArguments()[0];
            
            return (modelType, property.GetValue(_context));
        }

        // GET: api/DynamicCRUD/{tableName}
        [HttpGet]
        public async Task<ActionResult<IEnumerable<object>>> GetAll(
            string tableName,
            [FromQuery] string? filter = null,
            [FromQuery] string? sort = null,
            [FromQuery] string? sortDirection = "asc")
        {
            var (modelType, dbSet) = GetDbSet(tableName);
            
            try
            {
                var query = ((IQueryable)dbSet).Cast<object>();
                
                // Apply filtering if provided
                if (!string.IsNullOrEmpty(filter))
                {
                    var filterProperties = modelType.GetProperties()
                        .Where(p => p.PropertyType == typeof(string))
                        .ToList();
                    
                    if (filterProperties.Any())
                    {
                        var parameter = System.Linq.Expressions.Expression.Parameter(modelType, "x");
                        var filterExpressions = new List<System.Linq.Expressions.Expression>();
                        
                        foreach (var prop in filterProperties)
                        {
                            var property = System.Linq.Expressions.Expression.Property(parameter, prop);
                            var containsMethod = typeof(string).GetMethod("Contains", new[] { typeof(string) });
                            var containsExpression = System.Linq.Expressions.Expression.Call(
                                property, 
                                containsMethod, 
                                System.Linq.Expressions.Expression.Constant(filter));
                            
                            filterExpressions.Add(containsExpression);
                        }
                        
                        var combinedFilter = filterExpressions.Aggregate((prev, next) => 
                            System.Linq.Expressions.Expression.OrElse(prev, next));
                        
                        var lambda = System.Linq.Expressions.Expression.Lambda(combinedFilter, parameter);
                        query = Queryable.Where(query, (dynamic)lambda);
                    }
                }
                
                // Apply sorting if provided
                if (!string.IsNullOrEmpty(sort))
                {
                    var sortProperty = modelType.GetProperties()
                        .FirstOrDefault(p => string.Equals(p.Name, sort, StringComparison.OrdinalIgnoreCase));
                    
                    if (sortProperty != null)
                    {
                        var parameter = System.Linq.Expressions.Expression.Parameter(modelType, "x");
                        var property = System.Linq.Expressions.Expression.Property(parameter, sortProperty);
                        var lambda = System.Linq.Expressions.Expression.Lambda(property, parameter);
                        
                        if (string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase))
                        {
                            query = Queryable.OrderByDescending(query, (dynamic)lambda);
                        }
                        else
                        {
                            query = Queryable.OrderBy(query, (dynamic)lambda);
                        }
                    }
                }
                
                var dataList = await query.ToListAsync();
                
                return Ok(new {
                    status = true,
                    message = "Data retrieved successfully",
                    data = dataList,
                    totalData = dataList.Count
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new {
                    status = false,
                    message = $"Error retrieving data: {ex.Message}",
                    data = new List<object>(),
                    totalData = 0
                });
            }
        }

        // GET: api/DynamicCRUD/{tableName}/paginated?page=1&pageSize=10
        [HttpGet("paginated")]
        public async Task<ActionResult<object>> GetPaginated(string tableName, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        {
            var (modelType, dbSet) = GetDbSet(tableName);
            
            try
            {
                if (page < 1) page = 1;
                if (pageSize < 1) pageSize = 10;
                
                var totalCount = await ((IQueryable)dbSet).Cast<object>().CountAsync();
                var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
                
                var dataList = await ((IQueryable)dbSet)
                    .Cast<object>()
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();
                
                return Ok(new {
                    status = true,
                    message = "Data retrieved successfully",
                    data = dataList,
                    totalData = dataList.Count,
                    totalCount,
                    totalPages,
                    currentPage = page,
                    pageSize
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new {
                    status = false,
                    message = $"Error retrieving data: {ex.Message}",
                    data = new List<object>(),
                    totalData = 0
                });
            }
        }

        // GET: api/DynamicCRUD/{tableName}/{id}
        [HttpGet("{id}")]
        public async Task<ActionResult<object>> GetById(string tableName, int id)
        {
            var (modelType, dbSet) = GetDbSet(tableName);
            _logger.LogInformation("Getting record with ID {Id} from table {TableName}", id, tableName);
            
            var method = typeof(DbSet<>)
                .MakeGenericType(modelType)
                .GetMethod(nameof(DbSet<object>.FindAsync), new[] { typeof(object[]) });
            
            var task = (dynamic)method.Invoke(dbSet, new object[] { new object[] { id } });
            var entity = await task.ConfigureAwait(false);
            
            if (entity == null)
            {
                return NotFound(new {
                    status = false,
                    message = $"Data with id {id} not found",
                    data = new object(),
                    totalData = 0
                });
            }

            return Ok(new {
                status = true,
                message = "Data retrieved successfully",
                data = entity,
                totalData = 1
            });
        }

        // POST: api/DynamicCRUD/{tableName}
        [HttpPost]
        public async Task<ActionResult<object>> Create(string tableName, [FromBody] Dictionary<string, object> entityData)
        {
            var (modelType, dbSet) = GetDbSet(tableName);
            
            try
            {
                // Create instance of model type
                var entity = Activator.CreateInstance(modelType);
                
                // Get all properties of the model
                var properties = modelType.GetProperties()
                    .Where(p => p.CanWrite)
                    .ToList();
                
                // Validate required properties with case insensitive comparison
                var requiredProperties = properties
                    .Where(p => p.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.RequiredAttribute), true).Any())
                    .ToList();
                
                foreach (var prop in requiredProperties)
                {
                    // Validate entityData is not null
                    if (entityData == null)
                    {
                        return BadRequest(new {
                            status = false,
                            message = "Request body cannot be null",
                            data = new object()
                        });
                    }

                    // Find matching key in entityData with case insensitive comparison
                    var matchingKey = entityData.Keys
                        .FirstOrDefault(k => 
                            string.Equals(k, prop.Name, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(k, prop.GetColumnName(), StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(k, prop.Name.Replace("_", ""), StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(k, prop.GetColumnName().Replace("_", ""), StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(k, prop.Name.Replace(" ", ""), StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(k, prop.GetColumnName().Replace(" ", ""), StringComparison.OrdinalIgnoreCase));
                    
                    _logger.LogInformation("Checking required property: {PropName} (DB: {DbColumn}) - Found key: {FoundKey} - All keys: {Keys}", 
                        prop.Name, 
                        prop.GetColumnName(),
                        matchingKey,
                        string.Join(", ", entityData.Keys));
                    
                    if (string.IsNullOrEmpty(matchingKey))
                    {
                        return BadRequest(new {
                            status = false,
                            message = $"Property '{prop.Name}' is required but not found in request. Available keys: {string.Join(", ", entityData.Keys)}",
                            data = new object()
                        });
                    }

                    if (entityData[matchingKey] == null)
                    {
                        return BadRequest(new {
                            status = false,
                            message = $"Property '{prop.Name}' is required",
                            data = new object()
                        });
                    }
                }
                
                // Set properties from JSON
                foreach (var prop in entityData)
                {
                    // Find property with case insensitive comparison against model name, database field name, and JSON property name
                    var propertyInfo = properties.FirstOrDefault(p => 
                        string.Equals(p.Name, prop.Key, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(p.GetColumnName(), prop.Key, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(p.Name, prop.Key.Replace("_", ""), StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(p.GetColumnName(), prop.Key.Replace("_", ""), StringComparison.OrdinalIgnoreCase));
                    
                    _logger.LogInformation("Mapping property: {JsonProp} => {ModelProp} (DB: {DbColumn})", 
                        prop.Key, 
                        propertyInfo?.Name ?? "null", 
                        propertyInfo?.GetColumnName() ?? "null");
                    
                    if (propertyInfo != null && propertyInfo.CanWrite)
                    {
                        try
                        {
                            // Handle null values
                            if (prop.Value == null)
                            {
                                if (propertyInfo.PropertyType.IsValueType && Nullable.GetUnderlyingType(propertyInfo.PropertyType) == null)
                                {
                                    return BadRequest(new {
                                        status = false,
                                        message = $"Property '{prop.Key}' cannot be null",
                                        data = new object()
                                    });
                                }
                                propertyInfo.SetValue(entity, null);
                                continue;
                            }

                            // Skip ID property for create operation
                            if (string.Equals(propertyInfo.Name, "id", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            // Handle enum types
                            if (propertyInfo.PropertyType.IsEnum)
                            {
                                if (Enum.TryParse(propertyInfo.PropertyType, prop.Value.ToString(), out var enumValue))
                                {
                                    propertyInfo.SetValue(entity, enumValue);
                                    continue;
                                }
                                throw new Exception($"Invalid enum value for {prop.Key}. Valid values: {string.Join(", ", Enum.GetNames(propertyInfo.PropertyType))}");
                            }

                            // Handle DateTime types
                            if (propertyInfo.PropertyType == typeof(DateTime) || 
                                Nullable.GetUnderlyingType(propertyInfo.PropertyType) == typeof(DateTime))
                            {
                                if (DateTime.TryParse(prop.Value.ToString(), out var dateValue))
                                {
                                    propertyInfo.SetValue(entity, dateValue);
                                    continue;
                                }
                                throw new Exception($"Invalid date format for {prop.Key}. Expected format: yyyy-MM-ddTHH:mm:ss");
                            }

                            // Convert value to correct type
                            try
                            {
                            // Handle boolean types
                            if (propertyInfo.PropertyType == typeof(bool) || 
                                Nullable.GetUnderlyingType(propertyInfo.PropertyType) == typeof(bool))
                            {
                            var boolString = prop.Value.ToString().ToLower();
                            if (boolString == "true" || boolString == "false")
                            {
                                propertyInfo.SetValue(entity, boolString == "true");
                            }
                            else
                            {
                                throw new Exception($"Invalid boolean value for {prop.Key}. Expected true/false");
                            }
                            }
                            // Handle string types
                            else if (propertyInfo.PropertyType == typeof(string))
                            {
                                propertyInfo.SetValue(entity, prop.Value.ToString());
                            }
                            // Handle numeric types
                            else if (propertyInfo.PropertyType == typeof(decimal) || 
                                     Nullable.GetUnderlyingType(propertyInfo.PropertyType) == typeof(decimal))
                                {
                                    if (decimal.TryParse(prop.Value.ToString(), out var decimalValue))
                                    {
                                        propertyInfo.SetValue(entity, decimalValue);
                                    }
                                    else
                                    {
                                        throw new Exception($"Invalid decimal value for {prop.Key}");
                                    }
                                }
                            // Handle other numeric types
                            else if (propertyInfo.PropertyType.IsNumericType())
                            {
                                // Convert string to number if needed
                                var stringValue = prop.Value.ToString();
                                if (string.IsNullOrWhiteSpace(stringValue))
                                {
                                    propertyInfo.SetValue(entity, null);
                                }
                                else
                                {
                                    try
                                    {
                                        var value = Convert.ChangeType(stringValue, 
                                            Nullable.GetUnderlyingType(propertyInfo.PropertyType) ?? propertyInfo.PropertyType);
                                        propertyInfo.SetValue(entity, value);
                                    }
                                    catch
                                    {
                                        throw new Exception($"Invalid numeric value for {prop.Key}. Expected a number");
                                    }
                                }
                            }
                                // If types already match, use value directly
                                else if (prop.Value.GetType() == propertyInfo.PropertyType || 
                                    Nullable.GetUnderlyingType(propertyInfo.PropertyType) == prop.Value.GetType())
                                {
                                    propertyInfo.SetValue(entity, prop.Value);
                                }
                                else
                                {
                                    var value = Convert.ChangeType(prop.Value, 
                                        Nullable.GetUnderlyingType(propertyInfo.PropertyType) ?? propertyInfo.PropertyType);
                                    propertyInfo.SetValue(entity, value);
                                }
                            }
                            catch (Exception ex)
                            {
                                throw new Exception($"Failed to convert value '{prop.Value}' for property '{prop.Key}' to type {propertyInfo.PropertyType.Name}. {ex.Message}");
                            }
                        }
                        catch (Exception ex)
                        {
                            return BadRequest(new {
                                status = false,
                                message = $"Invalid value for property '{prop.Key}': {ex.Message}",
                                data = new object()
                            });
                        }
                    }
                }
                
                // Add to DbSet
                var addMethod = dbSet.GetType().GetMethod("Add");
                addMethod.Invoke(dbSet, new[] { entity });
                
                await _context.SaveChangesAsync();

                // Get ID property
                var idProperty = modelType.GetProperty("Id");
                var id = idProperty.GetValue(entity);

                return CreatedAtAction(nameof(GetById), new { tableName, id }, new {
                    status = true,
                    message = "Data created successfully",
                    data = entity,
                    totalData = 1
                });
            }
            catch (DbUpdateException ex)
            {
                return StatusCode(500, new {
                    status = false,
                    message = $"Database error: {ex.InnerException?.Message ?? ex.Message}",
                    data = new object()
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new {
                    status = false,
                    message = $"Error creating data: {ex.Message}",
                    data = new object()
                });
            }
        }

        // PUT: api/DynamicCRUD/{tableName}/{id}
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string tableName, int id, [FromBody] Dictionary<string, object> entityData)
        {
            var (modelType, dbSet) = GetDbSet(tableName);
            
            // Find existing entity
            var findMethod = typeof(DbSet<>)
                .MakeGenericType(modelType)
                .GetMethod(nameof(DbSet<object>.FindAsync), new[] { typeof(object[]) });
            
            var task = (dynamic)findMethod.Invoke(dbSet, new object[] { new object[] { id } });
            var entity = await task.ConfigureAwait(false);
            
            if (entity == null)
            {
                return NotFound();
            }

            // Update properties from JSON
            foreach (var prop in entityData)
            {
                // Skip ID property for update operation
                if (string.Equals(prop.Key, "id", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Find property with case insensitive comparison against model name, database field name, and JSON property name
                var propertyInfo = modelType.GetProperties()
                    .FirstOrDefault(p => 
                        string.Equals(p.Name, prop.Key, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(p.GetColumnName(), prop.Key, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(p.Name, prop.Key.Replace("_", ""), StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(p.GetColumnName(), prop.Key.Replace("_", ""), StringComparison.OrdinalIgnoreCase));
                    
                _logger.LogInformation("Mapping property: {JsonProp} => {ModelProp} (DB: {DbColumn})", 
                    prop.Key, 
                    propertyInfo?.Name ?? "null", 
                    propertyInfo?.GetColumnName() ?? "null");
                
                if (propertyInfo != null && propertyInfo.CanWrite)
                {
                    try
                    {
                        // Handle null values
                        if (prop.Value == null)
                        {
                            if (propertyInfo.PropertyType.IsValueType && Nullable.GetUnderlyingType(propertyInfo.PropertyType) == null)
                            {
                                return BadRequest(new {
                                    status = false,
                                    message = $"Property '{prop.Key}' cannot be null",
                                    data = new object()
                                });
                            }
                            propertyInfo.SetValue(entity, null);
                            continue;
                        }

                        // Handle enum types
                        if (propertyInfo.PropertyType.IsEnum)
                        {
                            if (Enum.TryParse(propertyInfo.PropertyType, prop.Value.ToString(), out var enumValue))
                            {
                                propertyInfo.SetValue(entity, enumValue);
                                continue;
                            }
                            throw new Exception($"Invalid enum value for {prop.Key}. Valid values: {string.Join(", ", Enum.GetNames(propertyInfo.PropertyType))}");
                        }

                        // Handle DateTime types
                        if (propertyInfo.PropertyType == typeof(DateTime) || 
                            Nullable.GetUnderlyingType(propertyInfo.PropertyType) == typeof(DateTime))
                        {
                            if (DateTime.TryParse(prop.Value.ToString(), out var dateValue))
                            {
                                propertyInfo.SetValue(entity, dateValue);
                                continue;
                            }
                            throw new Exception($"Invalid date format for {prop.Key}. Expected format: yyyy-MM-ddTHH:mm:ss");
                        }

                        // Convert value to correct type
                        try
                        {
                            // Handle boolean types
                            if (propertyInfo.PropertyType == typeof(bool) || 
                                Nullable.GetUnderlyingType(propertyInfo.PropertyType) == typeof(bool))
                            {
                                if (bool.TryParse(prop.Value.ToString(), out var boolValue))
                                {
                                    propertyInfo.SetValue(entity, boolValue);
                                }
                                else
                                {
                                    throw new Exception($"Invalid boolean value for {prop.Key}. Expected true/false");
                                }
                            }
                            // Handle string types
                            else if (propertyInfo.PropertyType == typeof(string))
                            {
                                propertyInfo.SetValue(entity, prop.Value.ToString());
                            }
                            // Handle numeric types
                            else if (propertyInfo.PropertyType == typeof(decimal) || 
                                     Nullable.GetUnderlyingType(propertyInfo.PropertyType) == typeof(decimal))
                            {
                                if (decimal.TryParse(prop.Value.ToString(), out var decimalValue))
                                {
                                    propertyInfo.SetValue(entity, decimalValue);
                                }
                                else
                                {
                                    throw new Exception($"Invalid decimal value for {prop.Key}");
                                }
                            }
                            // Handle other numeric types
                            else if (propertyInfo.PropertyType.IsNumericType())
                            {
                                // Convert string to number if needed
                                var stringValue = prop.Value.ToString();
                                if (string.IsNullOrWhiteSpace(stringValue))
                                {
                                    propertyInfo.SetValue(entity, null);
                                }
                                else
                                {
                                    try
                                    {
                                        var value = Convert.ChangeType(stringValue, 
                                            Nullable.GetUnderlyingType(propertyInfo.PropertyType) ?? propertyInfo.PropertyType);
                                        propertyInfo.SetValue(entity, value);
                                    }
                                    catch
                                    {
                                        throw new Exception($"Invalid numeric value for {prop.Key}. Expected a number");
                                    }
                                }
                            }
                            // If types already match, use value directly
                            else if (prop.Value.GetType() == propertyInfo.PropertyType || 
                                Nullable.GetUnderlyingType(propertyInfo.PropertyType) == prop.Value.GetType())
                            {
                                propertyInfo.SetValue(entity, prop.Value);
                            }
                            else
                            {
                                var value = Convert.ChangeType(prop.Value, 
                                    Nullable.GetUnderlyingType(propertyInfo.PropertyType) ?? propertyInfo.PropertyType);
                                propertyInfo.SetValue(entity, value);
                            }
                        }
                        catch (Exception ex)
                        {
                            throw new Exception($"Failed to convert value '{prop.Value}' for property '{prop.Key}' to type {propertyInfo.PropertyType.Name}. {ex.Message}");
                        }
                    }
                    catch (Exception ex)
                    {
                        return BadRequest(new {
                            status = false,
                            message = $"Invalid value for property '{prop.Key}': {ex.Message}",
                            data = new object()
                        });
                    }
                }
            }

            _context.Entry(entity).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!EntityExists(dbSet, id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return Ok(new {
                status = true,
                message = "Data updated successfully",
                data = entity,
                totalData = 1
            });
        }

        // DELETE: api/DynamicCRUD/{tableName}/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string tableName, int id)
        {
            var (modelType, dbSet) = GetDbSet(tableName);
            
            // Find entity
            var findMethod = typeof(DbSet<>)
                .MakeGenericType(modelType)
                .GetMethod(nameof(DbSet<object>.FindAsync), new[] { typeof(object[]) });
            
            var task = (dynamic)findMethod.Invoke(dbSet, new object[] { new object[] { id } });
            var entity = await task.ConfigureAwait(false);
            
            if (entity == null)
            {
                return NotFound();
            }

            // Remove entity
            var removeMethod = dbSet.GetType().GetMethod("Remove");
            removeMethod.Invoke(dbSet, new[] { entity });
            
            await _context.SaveChangesAsync();

            return Ok(new {
                status = true,
                message = "Data deleted successfully",
                data = new object(),
                totalData = 0
            });
        }

        private bool EntityExists(dynamic dbSet, int id)
        {
            var entityType = dbSet.EntityType;
            var idProperty = entityType.FindProperty("Id");
            if (idProperty == null)
            {
                return false;
            }

            var parameter = System.Linq.Expressions.Expression.Parameter(entityType.ClrType, "e");
            var idExpression = System.Linq.Expressions.Expression.Property(parameter, idProperty.PropertyInfo);
            var equalsExpression = System.Linq.Expressions.Expression.Equal(
                idExpression,
                System.Linq.Expressions.Expression.Constant(id)
            );
            
            var lambda = System.Linq.Expressions.Expression.Lambda<Func<object, bool>>(equalsExpression, parameter);
            return dbSet.Any(lambda.Compile());
        }
    }
}
