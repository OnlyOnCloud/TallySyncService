using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TallySyncService.Services;

namespace TallySyncService.Commands;

public class LoginCommand
{
    public static async Task ExecuteAsync(string backendUrl)
    {
        Console.WriteLine("╔═══════════════════════════════════════════════╗");
        Console.WriteLine("║   Tally Sync Service - Login                  ║");
        Console.WriteLine("╚═══════════════════════════════════════════════╝");
        Console.WriteLine();

        // Create a temporary service collection for dependency injection
        var services = new ServiceCollection();
        services.AddHttpClient();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        var serviceProvider = services.BuildServiceProvider();

        var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        var logger = serviceProvider.GetRequiredService<ILogger<AuthService>>();
        var authService = new AuthService(backendUrl, httpClientFactory, logger);

        // Get email
        Console.Write("Enter your email: ");
        var email = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(email))
        {
            Console.WriteLine("✗ Email is required");
            return;
        }

        // Send OTP
        Console.WriteLine("\nSending OTP to your email...");
        var otpSent = await authService.SendOtpEmailAsync(email);

        if (!otpSent)
        {
            Console.WriteLine("✗ Failed to send OTP. Please try again.");
            return;
        }

        // Get OTP
        Console.WriteLine();
        Console.Write("Enter the OTP from your email: ");
        var otp = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(otp))
        {
            Console.WriteLine("✗ OTP is required");
            return;
        }

        // Validate OTP
        Console.WriteLine("\nValidating OTP...");
        var token = await authService.ValidateOtpAsync(email, otp);

        if (token != null)
        {
            // Fetch user organizations
            Console.WriteLine("\nFetching your organizations...");
            var orgs = await authService.GetUserOrganisationsAsync(token);

            if (orgs == null || orgs.Count == 0)
            {
                Console.WriteLine("✗ No organizations found for your account.");
                return;
            }

            // Display organizations
            Console.WriteLine($"\nFound {orgs.Count} organization(s):");
            Console.WriteLine("─────────────────────────────────────────────────");
            for (int i = 0; i < orgs.Count; i++)
            {
                Console.WriteLine($"  {i + 1}. {orgs[i].OrganisationCode} (ID: {orgs[i].OrganisationId})");
            }
            Console.WriteLine("─────────────────────────────────────────────────");

            // Select organization
            uint selectedOrgId;
            if (orgs.Count == 1)
            {
                selectedOrgId = orgs[0].OrganisationId;
                Console.WriteLine($"\nAutomatically selected: {orgs[0].OrganisationCode}");
            }
            else
            {
                Console.Write($"\nSelect organization (1-{orgs.Count}): ");
                var selection = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(selection) || 
                    !int.TryParse(selection, out var index) || 
                    index < 1 || index > orgs.Count)
                {
                    Console.WriteLine("✗ Invalid selection");
                    return;
                }

                selectedOrgId = orgs[index - 1].OrganisationId;
                Console.WriteLine($"✓ Selected: {orgs[index - 1].OrganisationCode}");
            }

            // Save token and organization ID
            AuthService.SaveToken(token, selectedOrgId);
            Console.WriteLine($"\n✅ Login successful!");
            Console.WriteLine($"   Token and organization saved.");
            Console.WriteLine($"   You can now use the sync service.");
        }
        else
        {
            Console.WriteLine("\n✗ Login failed. Please try again.");
        }
    }
}
