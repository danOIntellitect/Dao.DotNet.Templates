using System.Security.Claims;
#if IncludeDatabase
using Dao.Aspire.Mcp.Data;
using Microsoft.EntityFrameworkCore;
#endif
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Dao.Aspire.Mcp.Tests.Fixtures;

/// <summary>
/// Shared mock/fixture factories for agentic tests.
/// Provides in-memory DbContext with seeded data and configurable HttpContext mocks.
/// </summary>
public static class MockServiceFixtures
{
#if IncludeDatabase
    /// <summary>
    /// Creates an in-memory AppDbContext with test data seeded.
    /// Each call uses a unique database name for test isolation.
    /// </summary>
    public static AppDbContext CreateSeededDbContext(string? dbName = null)
    {
        dbName ??= $"TestDb_{Guid.NewGuid():N}";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        var context = new AppDbContext(options);
        SeedTestData(context);
        return context;
    }
#endif

    /// <summary>
    /// Creates a mock IHttpContextAccessor with the specified claims.
    /// </summary>
    public static IHttpContextAccessor CreateMockHttpContextAccessor(
        string? userId = "test-user-id",
        string? userName = "Test User",
        string? email = "test@example.com",
        string[]? roles = null)
    {
        var claims = new List<Claim>();

        if (userId is not null)
            claims.Add(new Claim("sub", userId));
        if (userName is not null)
            claims.Add(new Claim(ClaimTypes.Name, userName));
        if (email is not null)
            claims.Add(new Claim("email", email));
        if (userName is not null)
            claims.Add(new Claim("preferred_username", userName));

        foreach (var role in roles ?? [])
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        var httpContext = new DefaultHttpContext
        {
            User = principal
        };

        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        return accessor;
    }

    /// <summary>
    /// Creates a mock IHttpContextAccessor with NO authenticated user.
    /// </summary>
    public static IHttpContextAccessor CreateUnauthenticatedHttpContextAccessor()
    {
        var httpContext = new DefaultHttpContext();
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(httpContext);
        return accessor;
    }

    /// <summary>
    /// Creates a mock ILogger&lt;T&gt; using NSubstitute.
    /// </summary>
    public static ILogger<T> CreateMockLogger<T>()
    {
        return Substitute.For<ILogger<T>>();
    }

#if IncludeDatabase
    private static void SeedTestData(AppDbContext context)
    {
        var users = new[]
        {
            new User
            {
                Id = 1,
                Name = "North Manager",
                Email = "north@example.com",
                Role = "NORTH",
                Active = true,
                CreatedAt = DateTime.UtcNow.AddMonths(-6)
            },
            new User
            {
                Id = 2,
                Name = "South Manager",
                Email = "south@example.com",
                Role = "SOUTH",
                Active = true,
                CreatedAt = DateTime.UtcNow.AddMonths(-4)
            },
            new User
            {
                Id = 3,
                Name = "East Manager",
                Email = "east@example.com",
                Role = "EAST",
                Active = true,
                CreatedAt = DateTime.UtcNow.AddMonths(-3)
            }
        };
        context.Users.AddRange(users);

        var products = new[]
        {
            new Product
            {
                Id = 1,
                Name = "Laptop Pro",
                Description = "High-end laptop",
                Category = "Electronics",
                Price = 1500.00m,
                Stock = 20,
                IsAvailable = true
            },
            new Product
            {
                Id = 2,
                Name = "Wireless Mouse",
                Description = "Ergonomic mouse",
                Category = "Electronics",
                Price = 35.00m,
                Stock = 100,
                IsAvailable = true
            },
            new Product
            {
                Id = 3,
                Name = "Office Chair",
                Description = "Comfortable desk chair",
                Category = "Home",
                Price = 300.00m,
                Stock = 5,
                IsAvailable = true
            },
            new Product
            {
                Id = 4,
                Name = "Vintage Keyboard",
                Description = "Discontinued model",
                Category = "Electronics",
                Price = 75.00m,
                Stock = 0,
                IsAvailable = false
            }
        };
        context.Products.AddRange(products);
        context.SaveChanges();

        var orders = new[]
        {
            new Order
            {
                Id = 1,
                UserId = 1,
                OrderDate = DateTime.UtcNow.AddDays(-10),
                Status = "Completed",
                TotalAmount = 1535.00m,
                Notes = "NORTH region order"
            },
            new Order
            {
                Id = 2,
                UserId = 1,
                OrderDate = DateTime.UtcNow.AddDays(-5),
                Status = "Completed",
                TotalAmount = 70.00m,
                Notes = "NORTH region second order"
            },
            new Order
            {
                Id = 3,
                UserId = 2,
                OrderDate = DateTime.UtcNow.AddDays(-3),
                Status = "Completed",
                TotalAmount = 300.00m,
                Notes = "SOUTH region order"
            }
        };
        context.Orders.AddRange(orders);
        context.SaveChanges();

        var orderItems = new[]
        {
            new OrderItem { Id = 1, OrderId = 1, ProductId = 1, Quantity = 1, UnitPrice = 1500.00m },
            new OrderItem { Id = 2, OrderId = 1, ProductId = 2, Quantity = 1, UnitPrice = 35.00m },
            new OrderItem { Id = 3, OrderId = 2, ProductId = 2, Quantity = 2, UnitPrice = 35.00m },
            new OrderItem { Id = 4, OrderId = 3, ProductId = 3, Quantity = 1, UnitPrice = 300.00m }
        };
        context.OrderItems.AddRange(orderItems);
        context.SaveChanges();
    }
#endif
}
