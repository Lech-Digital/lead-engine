using Coldstart.Domain;
using Microsoft.EntityFrameworkCore;

namespace Coldstart.Infrastructure;

public static class DbSeeder
{
    public static async Task SeedAsync(ColdstartDbContext db, CancellationToken ct)
    {
        if (await db.Customers.AnyAsync(ct)) return;

        var customer = Customer.Create("Coldstart");
        var now = DateTimeOffset.UtcNow;
        var a = customer.AddLead("Patronus AI", Fit.Yes, "Anand — a thought after the $50M", "…", now);
        a.Approve(new EmailAddress("anand@patronus.ai"));
        customer.AddLead("Lago", Fit.Yes, "hiring GTM at Lago", "…", now);
        customer.AddLead("Visiblie", Fit.Maybe, "congrats on the €500K", "…", now);

        db.Customers.Add(customer);
        await db.SaveChangesAsync(ct);
    }
}
