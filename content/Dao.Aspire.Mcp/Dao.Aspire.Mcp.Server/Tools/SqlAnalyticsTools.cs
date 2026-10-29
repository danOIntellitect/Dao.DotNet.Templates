using System.ComponentModel;
using System.Text.Json.Serialization;
using Dao.Aspire.Mcp.Data;
using Dao.Aspire.Mcp.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Dao.Aspire.Mcp.Server.Tools;

/// <summary>
/// Custom MCP tools for complex analytical queries on SQL Server.
/// These tools demonstrate best practices for:
/// - RBAC with Azure AD groups
/// - SQL injection prevention through parameterized LINQ queries
/// - Input validation with whitelist approach
/// - Structured DTOs for LLM-friendly responses
/// </summary>
[McpServerToolType]
#if IncludeAuthentication
[Authorize]
#endif
public class SqlAnalyticsTools(
    AppDbContext context,
    IHttpContextAccessor httpContextAccessor,
    ILogger<SqlAnalyticsTools> logger
)
{
    private readonly AppDbContext _context = context;
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;
    private readonly ILogger<SqlAnalyticsTools> _logger = logger;

    // Whitelist of valid regions for input validation
    private static readonly HashSet<string> ValidRegions =
    [
        "NORTH",
        "SOUTH",
        "EAST",
        "WEST",
        "CENTRAL",
    ];

    // Whitelist of valid categories for input validation
    private static readonly HashSet<string> ValidCategories =
    [
        "Electronics",
        "Clothing",
        "Food",
        "Books",
        "Toys",
        "Home",
    ];

    /// <summary>
    /// Get comprehensive sales analytics by region with date range filtering.
    /// Uses EF Core LINQ with automatic parameterization to prevent SQL injection.
    /// </summary>
    [
        McpServerTool,
        Description(
            "Retrieve sales analytics aggregated by region with date range filtering. Returns total orders, revenue, average order value, and top products per region."
        )
    ]
#if RequireAuthForRoles
    [Authorize(Policy = Policies.DataAnalyst)]
#endif
    public async Task<SalesAnalyticsResponse> GetSalesAnalyticsByRegion(
        [Description("Start date in YYYY-MM-DD format")] string startDate,
        [Description("End date in YYYY-MM-DD format")] string endDate,
        [Description(
            "Optional: Region code to filter (NORTH, SOUTH, EAST, WEST, CENTRAL). Leave empty for all regions."
        )]
            string? region = null,
        CancellationToken cancellationToken = default
    )
    {
        // Get user context for auditing
        var userId = _httpContextAccessor.HttpContext?.User.FindFirst("sub")?.Value ?? "unknown";
        var userName = _httpContextAccessor.HttpContext?.User.Identity?.Name ?? "unknown";

        _logger.LogInformation(
            "User {UserName} ({UserId}) executing GetSalesAnalyticsByRegion: StartDate={StartDate}, EndDate={EndDate}, Region={Region}",
            userName,
            userId,
            startDate,
            endDate,
            region ?? "ALL"
        );

        // Validate and sanitize date inputs
        if (
            !DateTime.TryParse(startDate, out var start) || !DateTime.TryParse(endDate, out var end)
        )
        {
            throw new McpException("Invalid date format. Use YYYY-MM-DD format.");
        }

        if (end < start)
        {
            throw new McpException("End date must be after start date.");
        }

        // Validate region input using whitelist (if provided)
        if (!string.IsNullOrEmpty(region))
        {
            var normalizedRegion = region.ToUpperInvariant();
            if (!ValidRegions.Contains(normalizedRegion))
            {
                throw new McpException(
                    $"Invalid region. Valid values: {string.Join(", ", ValidRegions)}"
                );
            }
            region = normalizedRegion; // Use normalized value
        }

        // Build parameterized query using EF Core LINQ
        // EF Core automatically parameterizes all values, preventing SQL injection
        var query = _context
            .Orders.Where(o => o.OrderDate >= start && o.OrderDate <= end)
            .Include(o => o.Items)
                .ThenInclude(i => i.Product)
            .Include(o => o.User)
            .AsQueryable();

        // Apply region filter if specified (still parameterized)
        if (!string.IsNullOrEmpty(region))
        {
            query = query.Where(o => o.User.Role == region); // Note: Using Role as region proxy for demo
        }

        // Execute complex aggregation with EF Core
        var analytics = await query
            .GroupBy(o => o.User.Role) // Using Role as region proxy
            .Select(g => new RegionStats
            {
                Region = g.Key,
                TotalOrders = g.Count(),
                TotalRevenue = g.Sum(o => o.TotalAmount),
                AvgOrderValue = g.Average(o => o.TotalAmount),
                TopProducts = g.SelectMany(o => o.Items)
                    .GroupBy(i => i.Product.Name)
                    .Select(pg => new ProductStat
                    {
                        Name = pg.Key,
                        UnitsSold = pg.Sum(i => i.Quantity),
                        Revenue = pg.Sum(i => i.Quantity * i.UnitPrice),
                    })
                    .OrderByDescending(ps => ps.Revenue)
                    .Take(5)
                    .ToList(),
            })
            .ToListAsync(cancellationToken);

        _logger.LogInformation(
            "GetSalesAnalyticsByRegion completed: Returned {RegionCount} regions",
            analytics.Count
        );

        return new SalesAnalyticsResponse
        {
            StartDate = start,
            EndDate = end,
            Regions = analytics,
            GrandTotalRevenue = analytics.Sum(r => r.TotalRevenue),
            GrandTotalOrders = analytics.Sum(r => r.TotalOrders),
        };
    }

    /// <summary>
    /// Analyze product inventory and generate reorder recommendations based on sales velocity.
    /// </summary>
    [
        McpServerTool,
        Description(
            "Analyze current inventory levels and sales trends to identify products that need reordering. Returns products with low stock relative to sales velocity."
        )
    ]
#if RequireAuthForRoles
    [Authorize(Roles = Roles.Developers)]
#endif
    public async Task<InventoryProjectionResponse> GetInventoryProjections(
        [Description("Optional: Product category to analyze. Leave empty for all categories.")]
            string? category = null,
        [Description("Number of days to look back for sales velocity calculation (default: 30)")]
            int lookbackDays = 30,
        CancellationToken cancellationToken = default
    )
    {
        var userId = _httpContextAccessor.HttpContext?.User.FindFirst("sub")?.Value ?? "unknown";

        _logger.LogInformation(
            "User {UserId} executing GetInventoryProjections: Category={Category}, LookbackDays={LookbackDays}",
            userId,
            category ?? "ALL",
            lookbackDays
        );

        // Validate category using whitelist
        if (!string.IsNullOrEmpty(category))
        {
            if (!ValidCategories.Contains(category))
            {
                throw new McpException(
                    $"Invalid category. Valid values: {string.Join(", ", ValidCategories)}"
                );
            }
        }

        // Validate lookback days
        if (lookbackDays < 1 || lookbackDays > 365)
        {
            throw new McpException("Lookback days must be between 1 and 365.");
        }

        var cutoffDate = DateTime.UtcNow.AddDays(-lookbackDays);

        // Build parameterized query
        var productsQuery = _context.Products.AsQueryable();

        if (!string.IsNullOrEmpty(category))
        {
            productsQuery = productsQuery.Where(p => p.Category == category);
        }

        var projections = await productsQuery
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Category,
                p.Stock,
                p.Price,
                p.IsAvailable,
                SalesInPeriod = p
                    .OrderItems.Where(oi => oi.Order.OrderDate >= cutoffDate)
                    .Sum(oi => oi.Quantity),
            })
            .ToListAsync(cancellationToken);

        var recommendations = projections
            .Select(p => new InventoryProjection
            {
                ProductId = p.Id,
                ProductName = p.Name,
                Category = p.Category,
                CurrentStock = p.Stock,
                SalesVelocity = (decimal)p.SalesInPeriod / lookbackDays,
                DaysOfStockRemaining =
                    p.SalesInPeriod > 0
                        ? (int)(p.Stock / ((decimal)p.SalesInPeriod / lookbackDays))
                        : int.MaxValue,
                RecommendedAction = DetermineReorderAction(
                    p.Stock,
                    p.SalesInPeriod > 0
                        ? (int)(p.Stock / ((decimal)p.SalesInPeriod / lookbackDays))
                        : int.MaxValue,
                    p.IsAvailable
                ),
                EstimatedValue = p.Stock * p.Price,
            })
            .OrderBy(p => p.DaysOfStockRemaining)
            .ToList();

        _logger.LogInformation(
            "GetInventoryProjections completed: Analyzed {ProductCount} products",
            recommendations.Count
        );

        return new InventoryProjectionResponse
        {
            AnalysisDate = DateTime.UtcNow,
            LookbackDays = lookbackDays,
            Category = category,
            Projections = recommendations,
            TotalInventoryValue = recommendations.Sum(r => r.EstimatedValue),
        };
    }

    // Helper methods for business logic
    private static string DetermineReorderAction(int stock, int daysRemaining, bool isAvailable)
    {
        if (!isAvailable)
            return "DISCONTINUED";
        if (stock == 0)
            return "OUT_OF_STOCK - ORDER IMMEDIATELY";
        if (daysRemaining < 7)
            return "CRITICAL - ORDER NOW";
        if (daysRemaining < 14)
            return "LOW - ORDER SOON";
        if (daysRemaining < 30)
            return "MODERATE - MONITOR";
        return "SUFFICIENT";
    }
}

// DTO Records for clean, serializable responses

public record SalesAnalyticsResponse
{
    public DateTime StartDate { get; init; }
    public DateTime EndDate { get; init; }
    public List<RegionStats> Regions { get; init; } = [];

    [JsonPropertyName("grand_total_revenue")]
    public decimal GrandTotalRevenue { get; init; }

    [JsonPropertyName("grand_total_orders")]
    public int GrandTotalOrders { get; init; }
}

public record RegionStats
{
    public string Region { get; init; } = string.Empty;

    [JsonPropertyName("total_orders")]
    public int TotalOrders { get; init; }

    [JsonPropertyName("total_revenue")]
    public decimal TotalRevenue { get; init; }

    [JsonPropertyName("avg_order_value")]
    public decimal AvgOrderValue { get; init; }

    [JsonPropertyName("top_products")]
    public List<ProductStat> TopProducts { get; init; } = [];
}

public record ProductStat
{
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("units_sold")]
    public int UnitsSold { get; init; }

    public decimal Revenue { get; init; }
}

public record InventoryProjectionResponse
{
    [JsonPropertyName("analysis_date")]
    public DateTime AnalysisDate { get; init; }

    [JsonPropertyName("lookback_days")]
    public int LookbackDays { get; init; }

    public string? Category { get; init; }

    public List<InventoryProjection> Projections { get; init; } = [];

    [JsonPropertyName("total_inventory_value")]
    public decimal TotalInventoryValue { get; init; }
}

public record InventoryProjection
{
    [JsonPropertyName("product_id")]
    public int ProductId { get; init; }

    [JsonPropertyName("product_name")]
    public string ProductName { get; init; } = string.Empty;

    public string Category { get; init; } = string.Empty;

    [JsonPropertyName("current_stock")]
    public int CurrentStock { get; init; }

    [JsonPropertyName("sales_velocity")]
    public decimal SalesVelocity { get; init; }

    [JsonPropertyName("days_of_stock_remaining")]
    public int DaysOfStockRemaining { get; init; }

    [JsonPropertyName("recommended_action")]
    public string RecommendedAction { get; init; } = string.Empty;

    [JsonPropertyName("estimated_value")]
    public decimal EstimatedValue { get; init; }
}

public record CustomerLifetimeValueResponse
{
    [JsonPropertyName("analysis_date")]
    public DateTime AnalysisDate { get; init; }

    [JsonPropertyName("total_customers")]
    public int TotalCustomers { get; init; }

    public List<CustomerClv> Customers { get; init; } = [];

    [JsonPropertyName("total_lifetime_value")]
    public decimal TotalLifetimeValue { get; init; }

    [JsonPropertyName("average_clv")]
    public decimal AverageClv { get; init; }
}

public record CustomerClv
{
    [JsonPropertyName("customer_id")]
    public int CustomerId { get; init; }

    [JsonPropertyName("customer_name")]
    public string CustomerName { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    [JsonPropertyName("lifetime_value")]
    public decimal LifetimeValue { get; init; }

    [JsonPropertyName("total_orders")]
    public int TotalOrders { get; init; }

    [JsonPropertyName("average_order_value")]
    public decimal AverageOrderValue { get; init; }

    [JsonPropertyName("first_purchase_date")]
    public DateTime? FirstPurchaseDate { get; init; }

    [JsonPropertyName("last_purchase_date")]
    public DateTime? LastPurchaseDate { get; init; }

    [JsonPropertyName("days_since_first_purchase")]
    public int DaysSinceFirstPurchase { get; init; }

    [JsonPropertyName("customer_segment")]
    public string CustomerSegment { get; init; } = string.Empty;
}
